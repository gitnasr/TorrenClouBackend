using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Specifications;

namespace TorreClou.Worker.Services
{
    /// <summary>
    /// Continuous background service that monitors job health and recovers orphaned jobs.
    /// Runs every 2 minutes to detect jobs that:
    /// - Are stuck in PROCESSING/UPLOADING with stale heartbeat
    /// - Have Hangfire jobs that failed/succeeded but DB wasn't updated
    /// </summary>
    public class JobHealthMonitor : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<JobHealthMonitor> _logger;

        // How often to check for orphaned jobs
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(2);

        // Jobs not updated in this duration are considered orphaned
        private static readonly TimeSpan StaleJobThreshold = TimeSpan.FromMinutes(5);

        public JobHealthMonitor(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<JobHealthMonitor> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[HEALTH] Job health monitor started");

            // Initial recovery on startup
            await RecoverOrphanedJobsAsync(stoppingToken);

            // Continuous monitoring loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(CheckInterval, stoppingToken);
                    await RecoverOrphanedJobsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[HEALTH] Error during health check. Will retry in {Interval}", CheckInterval);
                }
            }

            _logger.LogInformation("[HEALTH] Job health monitor stopped");
        }

        private async Task RecoverOrphanedJobsAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
            var monitoringApi = JobStorage.Current?.GetMonitoringApi();

            var staleTime = DateTime.UtcNow - StaleJobThreshold;

            // Find jobs stuck in PROCESSING or UPLOADING with stale heartbeat
            var stuckJobsSpec = new BaseSpecification<UserJob>(j =>
                (j.Status == JobStatus.PROCESSING || j.Status == JobStatus.UPLOADING) &&
                (
                    (j.LastHeartbeat != null && j.LastHeartbeat < staleTime) ||
                    (j.LastHeartbeat == null && j.StartedAt != null && j.StartedAt < staleTime)
                ));

            var stuckJobs = await unitOfWork.Repository<UserJob>().ListAsync(stuckJobsSpec);

            if (!stuckJobs.Any())
            {
                _logger.LogDebug("[HEALTH] No orphaned jobs found");
                return;
            }

            _logger.LogWarning("[HEALTH] Found {Count} potentially orphaned jobs", stuckJobs.Count);

            foreach (var job in stuckJobs)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    // Check Hangfire state if we have the job ID
                    var shouldRecover = await ShouldRecoverJobAsync(job, monitoringApi);
                    
                    if (!shouldRecover)
                    {
                        _logger.LogDebug("[HEALTH] Job still processing in Hangfire | JobId: {JobId}", job.Id);
                        continue;
                    }

                    await RecoverJobAsync(job, unitOfWork, backgroundJobClient);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[HEALTH] Failed to recover job | JobId: {JobId}", job.Id);
                }
            }
        }

        private Task<bool> ShouldRecoverJobAsync(UserJob job, IMonitoringApi? monitoringApi)
        {
            // If we don't have a Hangfire job ID, assume we should recover
            if (string.IsNullOrEmpty(job.HangfireJobId) || monitoringApi == null)
            {
                return Task.FromResult(true);
            }

            try
            {
                var hangfireJob = monitoringApi.JobDetails(job.HangfireJobId);
                
                if (hangfireJob == null)
                {
                    // Job doesn't exist in Hangfire - recover it
                    _logger.LogWarning("[HEALTH] Hangfire job not found | JobId: {JobId} | HangfireJobId: {HangfireJobId}",
                        job.Id, job.HangfireJobId);
                    return Task.FromResult(true);
                }

                var currentState = hangfireJob.History.FirstOrDefault()?.StateName;
                
                // If job is still processing in Hangfire, don't recover
                if (currentState == "Processing" || currentState == "Enqueued" || currentState == "Scheduled")
                {
                    return Task.FromResult(false);
                }

                // If Hangfire shows succeeded but DB shows processing - need to sync
                if (currentState == "Succeeded" && job.Status == JobStatus.PROCESSING)
                {
                    _logger.LogWarning("[HEALTH] Hangfire succeeded but DB shows processing | JobId: {JobId}", job.Id);
                    return Task.FromResult(true);
                }

                // If Hangfire shows failed, we should recover (re-enqueue)
                if (currentState == "Failed" || currentState == "Deleted")
                {
                    return Task.FromResult(true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[HEALTH] Failed to check Hangfire state | JobId: {JobId}", job.Id);
            }

            return Task.FromResult(true);
        }

        private async Task RecoverJobAsync(UserJob job, IUnitOfWork unitOfWork, IBackgroundJobClient backgroundJobClient)
        {
            _logger.LogInformation(
                "[HEALTH] Recovering orphaned job | JobId: {JobId} | Status: {Status} | LastHeartbeat: {LastHeartbeat}",
                job.Id, job.Status, job.LastHeartbeat);

            // Determine what to re-enqueue based on current status
            string hangfireJobId;
            
            if (job.Status == JobStatus.UPLOADING)
            {
                // Resume upload phase
                job.CurrentState = "Recovering upload from interrupted state...";
                hangfireJobId = backgroundJobClient.Enqueue<TorrentUploadJob>(
                    service => service.ExecuteAsync(job.Id, CancellationToken.None));
            }
            else
            {
                // Resume download phase (PROCESSING or unknown state)
                job.Status = JobStatus.QUEUED;
                job.CurrentState = "Recovering download from interrupted state...";
                hangfireJobId = backgroundJobClient.Enqueue<TorrentDownloadJob>(
                    service => service.ExecuteAsync(job.Id, CancellationToken.None));
            }

            // Update job with new Hangfire job ID
            job.HangfireJobId = hangfireJobId;
            job.ErrorMessage = null; // Clear previous error
            await unitOfWork.Complete();

            _logger.LogInformation(
                "[HEALTH] Job recovered and re-enqueued | JobId: {JobId} | HangfireJobId: {HangfireJobId}",
                job.Id, hangfireJobId);
        }
    }
}

