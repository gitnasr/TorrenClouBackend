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
    /// Runs on startup and periodically.
    /// </summary>
    public class SyncRecoveryService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<SyncRecoveryService> logger,
        IOptions<JobHealthMonitorOptions> options) : BackgroundService
    {
        private readonly JobHealthMonitorOptions _options = options.Value;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation(
                "[SYNC_RECOVERY] Starting | CheckInterval: {Interval} | StaleThreshold: {Threshold}",
                _options.CheckInterval, _options.StaleJobThreshold);

            await RecoverStaleSyncsAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.CheckInterval, stoppingToken);
                    await RecoverStaleSyncsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // graceful shutdown
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[SYNC_RECOVERY] Loop error");
                }
            }
        }

        private async Task RecoverStaleSyncsAsync(CancellationToken cancellationToken)
        {
            using var scope = serviceScopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
            var monitoringApi = JobStorage.Current?.GetMonitoringApi();

            var now = DateTime.UtcNow;
            var staleCutoff = now - _options.StaleJobThreshold;

            // Candidates rules using ONLY fields available in Sync entity:
            // - SYNC_RETRY: only if NextRetryAt is null OR due
            // - SYNCING: stale heartbeat OR stale startedAt when heartbeat null
            // - PENDING: do not "mass recover" all pending (no CreatedAt available),
            //            recover only if (NextRetryAt due) OR (StartedAt stale) AND no HangfireJobId.
            var spec = new BaseSpecification<SyncEntity>(s =>
                (s.Status == SyncStatus.SYNC_RETRY &&
                    (s.NextRetryAt == null || s.NextRetryAt <= now)) ||

                (s.Status == SyncStatus.SYNCING &&
                    (
                        (s.LastHeartbeat != null && s.LastHeartbeat < staleCutoff) ||
                        (s.LastHeartbeat == null && s.StartedAt != null && s.StartedAt < staleCutoff)
                    )) ||

                (s.Status == SyncStatus.PENDING &&
                    string.IsNullOrEmpty(s.HangfireJobId) &&
                    (
                        (s.NextRetryAt != null && s.NextRetryAt <= now) ||
                        (s.StartedAt != null && s.StartedAt < staleCutoff)
                    ))
            );

            var candidates = await unitOfWork.Repository<SyncEntity>().ListAsync(spec);

            if (!candidates.Any())
                return;

            logger.LogWarning("[SYNC_RECOVERY] Found {Count} candidate syncs to inspect", candidates.Count);

            foreach (var sync in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!ShouldRecoverSync(sync, monitoringApi, staleCutoff))
                    {
                        logger.LogDebug("[SYNC_RECOVERY] Skip SyncId {SyncId} - Hangfire job active/pending", sync.Id);
                        continue;
                    }

                    var prevStatus = sync.Status;
                    var prevHangfireId = sync.HangfireJobId;

                    // Increment retry + compute next retry (simple backoff, tweak as you like)
                    // If you prefer "immediate re-enqueue" set NextRetryAt = null
                    sync.RetryCount = Math.Max(0, sync.RetryCount) + 1;
                    sync.NextRetryAt = ComputeNextRetryAt(sync.RetryCount, now);

                    // Ensure job will be accepted by your S3SyncJob status gate
                    sync.Status = SyncStatus.SYNC_RETRY;
                    sync.ErrorMessage = null;

                    // Heartbeat/start markers
                    sync.StartedAt ??= now;
                    sync.LastHeartbeat = now;

                    // Enqueue new Hangfire job
                    var newHangfireId = backgroundJobClient.Enqueue<IS3SyncJob>(
                        x => x.ExecuteAsync(sync.Id, CancellationToken.None));

                    sync.HangfireJobId = newHangfireId;

                    await unitOfWork.Complete();

                    logger.LogInformation(
                        "[SYNC_RECOVERY] Recovered SyncId {SyncId} (was {PrevStatus}) | PrevHF={PrevHF} -> NewHF={NewHF} | RetryCount={Retry}",
                        sync.Id, prevStatus, prevHangfireId, newHangfireId, sync.RetryCount);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[SYNC_RECOVERY] Failed to recover SyncId {SyncId}", sync.Id);
                }
            }
        }

        private bool ShouldRecoverSync(SyncEntity sync, IMonitoringApi? monitoringApi, DateTime staleCutoff)
        {
            // If we have no Hangfire context, recover (DB is source of truth)
            if (string.IsNullOrWhiteSpace(sync.HangfireJobId) || monitoringApi == null)
                return true;

            try
            {
                var jobDetails = monitoringApi.JobDetails(sync.HangfireJobId);
                if (jobDetails == null)
                {
                    logger.LogWarning("[SYNC_RECOVERY] Hangfire job not found | SyncId={SyncId} | HF={HF}",
                        sync.Id, sync.HangfireJobId);
                    return true;
                }

                // Take the latest state from history (LAST is most recent)
                var currentState = jobDetails.History?.LastOrDefault()?.StateName;

                if (string.IsNullOrWhiteSpace(currentState))
                    return true;

                // If HF job is queued, don't duplicate
                if (currentState is "Enqueued" or "Scheduled")
                    return false;

                // If HF job is processing, only recover if DB is stale
                if (currentState == "Processing")
                {
                    var dbStale =
                        (sync.LastHeartbeat != null && sync.LastHeartbeat < staleCutoff) ||
                        (sync.LastHeartbeat == null && sync.StartedAt != null && sync.StartedAt < staleCutoff);

                    if (dbStale)
                    {
                        logger.LogWarning("[SYNC_RECOVERY] HF=Processing but DB stale => recover | SyncId={SyncId} | HF={HF}",
                            sync.Id, sync.HangfireJobId);
                        return true;
                    }

                    return false;
                }

                // If failed/deleted => recover
                if (currentState is "Failed" or "Deleted")
                    return true;

                // If succeeded but DB not completed => mismatch => recover
                if (currentState == "Succeeded" && sync.Status != SyncStatus.COMPLETED)
                {
                    logger.LogWarning("[SYNC_RECOVERY] HF=Succeeded but DB={Status} => recover | SyncId={SyncId}",
                        sync.Status, sync.Id);
                    return true;
                }

                // Unknown/other states => safe recover
                return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[SYNC_RECOVERY] Hangfire state check failed | SyncId={SyncId}", sync.Id);
                return true;
            }
        }

        private static DateTime? ComputeNextRetryAt(int retryCount, DateTime nowUtc)
        {
            // Simple exponential-ish backoff (cap at 30 minutes)
            // 1 => 30s, 2 => 60s, 3 => 120s, 4 => 240s...
            var seconds = Math.Min(1800, 30 * (int)Math.Pow(2, Math.Min(10, retryCount - 1)));
            return nowUtc.AddSeconds(seconds);
        }
    }
}
