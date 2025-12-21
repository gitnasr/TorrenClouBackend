using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreClou.Core.Entities;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Options;
using TorreClou.Core.Specifications;

namespace TorreClou.Infrastructure.Services
{
     public class JobHealthMonitor<TJob> : BackgroundService
        where TJob : BaseEntity, IRecoverableJob
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<JobHealthMonitor<TJob>> _logger;
        private readonly Dictionary<JobType, IJobRecoveryStrategy> _strategies;
        private readonly JobHealthMonitorOptions _options;
        private readonly HashSet<JobStatus> _allMonitoredStatuses;
        public JobHealthMonitor(
              IServiceScopeFactory serviceScopeFactory,
              ILogger<JobHealthMonitor<TJob>> logger,
              IEnumerable<IJobRecoveryStrategy> strategies,
              IOptions<JobHealthMonitorOptions> options)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _options = options.Value;

            // Map strategies
            _strategies = strategies.ToDictionary(s => s.SupportedJobType);

            // Aggregate all statuses from all strategies
            _allMonitoredStatuses = strategies
                .SelectMany(s => s.MonitoredStatuses)
                .ToHashSet();

            _logger.LogInformation(
                "[HEALTH] Initialized for {JobType} | Watching Statuses: {Statuses}",
                typeof(TJob).Name,
                string.Join(", ", _allMonitoredStatuses));
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[HEALTH] Job health monitor started for {JobType}", typeof(TJob).Name);

            // Initial recovery on startup
            await RecoverOrphanedJobsAsync(stoppingToken);

            // Continuous monitoring loop
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_options.CheckInterval, stoppingToken);
                await RecoverOrphanedJobsAsync(stoppingToken);
            }

        }

        private async Task RecoverOrphanedJobsAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
            var monitoringApi = JobStorage.Current?.GetMonitoringApi();

            var staleTime = DateTime.UtcNow - _options.StaleJobThreshold;

            // DYNAMIC SPECIFICATION
            // We now query based on the aggregated list of statuses from our strategies
            var stuckJobsSpec = new BaseSpecification<TJob>(j =>
                _allMonitoredStatuses.Contains(j.Status) && 
                (
                    (j.LastHeartbeat != null && j.LastHeartbeat < staleTime) ||
                    (j.LastHeartbeat == null && j.StartedAt != null && j.StartedAt < staleTime)
                ));

            var stuckJobs = await unitOfWork.Repository<TJob>().ListAsync(stuckJobsSpec);

            if (!stuckJobs.Any()) return;

            _logger.LogWarning("[HEALTH] Found {Count} stalled jobs", stuckJobs.Count);

            foreach (var job in stuckJobs)
            {
                if (!_strategies.TryGetValue(job.Type, out var strategy))
                {
                    _logger.LogError("No strategy for JobType: {Type}", job.Type);
                    continue;
                }

                // Double check: Is this status actually monitored by this specific strategy?
                // (Prevents edge cases where statuses overlap uniquely)
                if (!strategy.MonitoredStatuses.Contains(job.Status))
                    continue;

                await RecoverSingleJobAsync(job, strategy, unitOfWork, backgroundJobClient, monitoringApi);
            }
        }
        private async Task RecoverSingleJobAsync(
            TJob job,
            IJobRecoveryStrategy strategy,
            IUnitOfWork unitOfWork,
            IBackgroundJobClient client,
            IMonitoringApi? monitoringApi)
        {
            try
            {
                if (!ShouldRecoverJob(job, monitoringApi)) return;

                var newHangfireId = strategy.RecoverJob(job, client);

                if (!string.IsNullOrEmpty(newHangfireId))
                {
                    job.HangfireJobId = newHangfireId;
                    job.ErrorMessage = null;
                    job.LastHeartbeat = DateTime.UtcNow; // Bump heartbeat immediately

                    // IMPORTANT: Save immediately to prevent loop processing same job if crash happens
                    await unitOfWork.Complete();

                    _logger.LogInformation("Recovered Job {Id} via {Strategy}", job.Id, strategy.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to recover Job {Id}", job.Id);
            }
        }
        private bool ShouldRecoverJob(IRecoverableJob job, IMonitoringApi? monitoringApi)
        {
            // If we don't have a Hangfire job ID, assume we should recover
            if (string.IsNullOrEmpty(job.HangfireJobId) || monitoringApi == null)
            {
                return true;
            }

            try
            {
                var hangfireJob = monitoringApi.JobDetails(job.HangfireJobId);

                if (hangfireJob == null)
                {
                    _logger.LogWarning("[HEALTH] Hangfire job not found | JobId: {JobId} | HangfireJobId: {HangfireJobId}",
                        job.Id, job.HangfireJobId);
                    return true;
                }

                var currentState = hangfireJob.History.FirstOrDefault()?.StateName;

                // If job is still "Processing" in Hangfire (Hangfire's state name) but our DB heartbeat is stale,
                // Hangfire hasn't detected the dead worker yet - force recovery
                if (currentState == "Processing")
                {
                    _logger.LogWarning(
                        "[HEALTH] Hangfire shows Processing but heartbeat is stale - forcing recovery | JobId: {JobId} | HangfireJobId: {HangfireJobId}",
                        job.Id, job.HangfireJobId);
                    return true;
                }

                // Enqueued/Scheduled means job is pending in Hangfire - don't duplicate
                if (currentState == "Enqueued" || currentState == "Scheduled")
                {
                    return false;
                }

                // If Hangfire shows succeeded but DB shows job is still in active state - need to sync
                if (currentState == "Succeeded" && job.Status == JobStatus.DOWNLOADING)
                {
                    _logger.LogWarning("[HEALTH] Hangfire succeeded but DB shows job still active | JobId: {JobId} | Status: {Status}", job.Id, job.Status);
                    return true;
                }

                // If Hangfire shows failed, we should recover (re-enqueue)
                if (currentState == "Failed" || currentState == "Deleted")
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[HEALTH] Failed to check Hangfire state | JobId: {JobId}", job.Id);
            }

            return true;
        }

       
    }
}

