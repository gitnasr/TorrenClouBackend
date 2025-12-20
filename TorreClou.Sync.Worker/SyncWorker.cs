using StackExchange.Redis;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Workers;
using TorreClou.Sync.Worker.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Hangfire;

namespace TorreClou.Sync.Worker
{
    /// <summary>
    /// Redis stream consumer that listens for sync jobs and enqueues them to Hangfire.
    /// This provides decoupled event-driven sync dispatch from the Torrent Worker.
    /// </summary>
    public class SyncWorker : BaseStreamWorker
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        protected override string StreamKey => "sync:stream";
        protected override string ConsumerGroupName => "sync-workers";

        public SyncWorker(
            ILogger<SyncWorker> logger,
            IConnectionMultiplexer redis,
            IServiceScopeFactory serviceScopeFactory) : base(logger, redis)
        {
            _serviceScopeFactory = serviceScopeFactory;
            Logger.LogInformation("[SYNC_WORKER] SyncWorker initialized | Stream: {Stream} | Group: {Group}", 
                StreamKey, ConsumerGroupName);
        }

        protected override async Task<bool> ProcessJobAsync(StreamEntry entry, CancellationToken cancellationToken)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            try
            {
                // Parse job data from stream entry
                var jobIdStr = entry["jobId"].ToString();
                var syncIdStr = entry["syncId"].ToString();

                if (!int.TryParse(jobIdStr, out var jobId))
                {
                    Logger.LogError("[SYNC_WORKER] Invalid jobId in stream entry: {JobId}", jobIdStr);
                    return false;
                }

                int? syncId = null;
                if (!string.IsNullOrEmpty(syncIdStr) && int.TryParse(syncIdStr, out var parsedSyncId))
                {
                    syncId = parsedSyncId;
                }

                // Fetch Sync entity from database for validation
                Sync? sync = null;
                if (syncId.HasValue)
                {
                    sync = await unitOfWork.Repository<Sync>().GetByIdAsync(syncId.Value);
                }
                else
                {
                    // If syncId not provided, find by JobId
                    var syncRepository = unitOfWork.Repository<Sync>();
                    var syncs = await syncRepository.GetAllAsync();
                    sync = syncs.FirstOrDefault(s => s.JobId == jobId);
                }

                if (sync == null)
                {
                    Logger.LogError("[SYNC_WORKER] Sync entity not found in database | JobId: {JobId} | SyncId: {SyncId}", 
                        jobId, syncId);
                    return false;
                }

                // Validate sync status - only process Pending or Retrying
                if (sync.Status != SyncStatus.Pending && sync.Status != SyncStatus.Retrying)
                {
                    Logger.LogWarning("[SYNC_WORKER] Sync is not in valid state for processing | JobId: {JobId} | SyncId: {SyncId} | Status: {Status}",
                        jobId, sync.Id, sync.Status);
                    return true; // Not an error, just skip
                }

                // Note: Lease acquisition is handled by BaseJob.ExecuteAsync in Hangfire
                // We just enqueue the job here - Hangfire will handle duplicate prevention via leases

                Logger.LogInformation("[SYNC_WORKER] Enqueuing S3 sync job to Hangfire | JobId: {JobId} | SyncId: {SyncId}", 
                    jobId, sync.Id);

                // Enqueue to Hangfire for reliable execution with retry capability
                // Pass syncId to the job
                var hangfireJobId = backgroundJobClient.Enqueue<S3SyncJob>(
                    service => service.ExecuteAsync(sync.Id, CancellationToken.None));

                Logger.LogInformation(
                    "[SYNC_WORKER] S3 sync job enqueued | JobId: {JobId} | SyncId: {SyncId} | HangfireJobId: {HangfireJobId}",
                    jobId, sync.Id, hangfireJobId);

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SYNC_WORKER] Error enqueuing S3 sync job to Hangfire");
                
                // Try to update job status to FAILED
                await TryMarkJobFailedAsync(entry, ex.Message);
                
                return false;
            }
        }

        private async Task TryMarkJobFailedAsync(StreamEntry entry, string errorMessage)
        {
            try
            {
                using var errorScope = _serviceScopeFactory.CreateScope();
                var errorUnitOfWork = errorScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                
                var syncIdStr = entry["syncId"].ToString();
                if (!string.IsNullOrEmpty(syncIdStr) && int.TryParse(syncIdStr, out var syncId))
                {
                    var sync = await errorUnitOfWork.Repository<Sync>().GetByIdAsync(syncId);
                    if (sync != null)
                    {
                        sync.Status = SyncStatus.Failed;
                        sync.ErrorMessage = $"Failed to enqueue sync job: {errorMessage}";
                        await errorUnitOfWork.Complete();
                        
                        Logger.LogInformation("[SYNC_WORKER] Sync marked as failed | SyncId: {SyncId}", syncId);
                    }
                }
            }
            catch (Exception updateEx)
            {
                Logger.LogError(updateEx, "[SYNC_WORKER] Failed to update sync status to FAILED");
            }
        }
    }
}

