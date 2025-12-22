using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Options;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.Core.Options;
using TorreClou.Core.Specifications;
using SyncEntity = TorreClou.Core.Entities.Jobs.Sync;

namespace TorreClou.Sync.Worker.Services
{
    /// <summary>
    /// Background service that recovers orphaned or stuck sync jobs.
    /// Runs on startup and periodically to detect:
    /// - Jobs in SYNC_RETRY status that need to be re-enqueued
    /// - Jobs in SYNCING status with stale heartbeats (worker crashed)
    /// </summary>
    public class SyncRecoveryService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<SyncRecoveryService> logger,
        IOptions<JobHealthMonitorOptions> options) : BackgroundService
    {
        private readonly JobHealthMonitorOptions _options = options.Value;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("[SYNC_RECOVERY] Starting sync recovery service | CheckInterval: {Interval} | StaleThreshold: {Threshold}",
                _options.CheckInterval, _options.StaleJobThreshold);

            // Initial recovery on startup
            await RecoverStaleSyncsAsync(stoppingToken);

            // Continuous monitoring loop
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_options.CheckInterval, stoppingToken);
                await RecoverStaleSyncsAsync(stoppingToken);
            }
        }

        private async Task RecoverStaleSyncsAsync(CancellationToken cancellationToken)
        {
            using var scope = serviceScopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
            var monitoringApi = JobStorage.Current?.GetMonitoringApi();

            var staleTime = DateTime.UtcNow - _options.StaleJobThreshold;

            // Query for:
            // 1. SYNC_RETRY - explicitly marked for retry
            // 2. SYNCING with stale heartbeat - worker crashed mid-sync
            // 3. PENDING with stale start time - never got picked up
            var stuckSyncsSpec = new BaseSpecification<SyncEntity>(s =>
                s.Status == SyncStatus.SYNC_RETRY ||
                (s.Status == SyncStatus.SYNCING &&
                    ((s.LastHeartbeat != null && s.LastHeartbeat < staleTime) ||
                     (s.LastHeartbeat == null && s.StartedAt != null && s.StartedAt < staleTime))) ||
                (s.Status == SyncStatus.PENDING &&
                    s.StartedAt == null && s.LastHeartbeat == null &&
                    s.Id > 0) // Ensure it's a valid entity
            );

            var stuckSyncs = await unitOfWork.Repository<SyncEntity>().ListAsync(stuckSyncsSpec);

            if (!stuckSyncs.Any()) return;

            logger.LogWarning("[SYNC_RECOVERY] Found {Count} stale/retry syncs to recover", stuckSyncs.Count);

            foreach (var sync in stuckSyncs)
            {
                try
                {
                    if (!ShouldRecoverSync(sync, monitoringApi))
                    {
                        logger.LogDebug("[SYNC_RECOVERY] Skipping sync {SyncId} - Hangfire job still active", sync.Id);
                        continue;
                    }

                    // Enqueue new Hangfire job
                    var newHangfireId = backgroundJobClient.Enqueue<IS3SyncJob>(
                        x => x.ExecuteAsync(sync.Id, CancellationToken.None));

                    // Update sync state
                    sync.HangfireJobId = newHangfireId;
                    sync.Status = SyncStatus.SYNCING;
                    sync.ErrorMessage = null;
                    sync.LastHeartbeat = DateTime.UtcNow;

                    await unitOfWork.Complete();

                    logger.LogInformation("[SYNC_RECOVERY] Recovered sync {SyncId} -> HF {HangfireId}",
                        sync.Id, newHangfireId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[SYNC_RECOVERY] Failed to recover sync {SyncId}", sync.Id);
                }
            }
        }

        private bool ShouldRecoverSync(SyncEntity sync, IMonitoringApi? monitoringApi)
        {
            // If no Hangfire job ID, definitely recover
            if (string.IsNullOrEmpty(sync.HangfireJobId) || monitoringApi == null)
            {
                return true;
            }

            try
            {
                var hangfireJob = monitoringApi.JobDetails(sync.HangfireJobId);

                if (hangfireJob == null)
                {
                    logger.LogWarning("[SYNC_RECOVERY] Hangfire job not found | SyncId: {SyncId} | HangfireJobId: {HangfireJobId}",
                        sync.Id, sync.HangfireJobId);
                    return true;
                }

                var currentState = hangfireJob.History.FirstOrDefault()?.StateName;

                // If Hangfire shows Processing but heartbeat is stale, force recovery
                if (currentState == "Processing")
                {
                    logger.LogWarning("[SYNC_RECOVERY] Hangfire shows Processing but heartbeat is stale - forcing recovery | SyncId: {SyncId}",
                        sync.Id);
                    return true;
                }

                // Enqueued/Scheduled means job is pending in Hangfire - don't duplicate
                if (currentState == "Enqueued" || currentState == "Scheduled")
                {
                    return false;
                }

                // If Hangfire shows Failed or Deleted, we should recover
                if (currentState == "Failed" || currentState == "Deleted")
                {
                    return true;
                }

                // Succeeded in Hangfire but still marked as SYNCING/SYNC_RETRY in DB is a state mismatch
                if (currentState == "Succeeded" && sync.Status != SyncStatus.COMPLETED)
                {
                    logger.LogWarning("[SYNC_RECOVERY] Hangfire succeeded but DB shows {Status} | SyncId: {SyncId}",
                        sync.Status, sync.Id);
                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[SYNC_RECOVERY] Failed to check Hangfire state | SyncId: {SyncId}", sync.Id);
            }

            return true;
        }
    }
}

