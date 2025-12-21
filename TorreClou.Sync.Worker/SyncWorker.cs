using StackExchange.Redis;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Workers;
using TorreClou.Sync.Worker.Services;
using Hangfire;
using SyncEntity = TorreClou.Core.Entities.Jobs.Sync;

namespace TorreClou.Sync.Worker
{
    public class SyncWorker(
        ILogger<SyncWorker> logger,
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory) : BaseStreamWorker(logger, redis, scopeFactory)
    {
        protected override string StreamKey => "sync:stream";
        protected override string ConsumerGroupName => "sync-workers";

        protected override async Task<bool> ProcessJobAsync(StreamEntry entry, IServiceProvider services, CancellationToken token)
        {
            // 1. Parse IDs (Using Base Helper for JobId)
            var jobId = ParseJobId(entry);
            var syncIdStr = GetStreamValue(entry, "syncId");

            if (!jobId.HasValue || string.IsNullOrEmpty(syncIdStr) || !int.TryParse(syncIdStr, out var syncId))
            {
                Logger.LogWarning("[SYNC_WORKER] Invalid IDs in stream. Acking to remove.");
                return true;
            }

            // 2. Resolve Services
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var backgroundJobClient = services.GetRequiredService<IBackgroundJobClient>();

            // 3. Load Sync Entity
            var syncRepo = unitOfWork.Repository<SyncEntity>();
            var sync = await syncRepo.GetByIdAsync(syncId);

            if (sync == null)
            {
                Logger.LogError("[SYNC_WORKER] Sync entity {SyncId} not found. Acking.", syncId);
                return true;
            }

            // 4. Validate State
            if (sync.Status != SyncStatus.Pending && sync.Status != SyncStatus.Retrying)
            {
                Logger.LogWarning("[SYNC_WORKER] Sync {Id} is {Status}, skipping.", syncId, sync.Status);
                return true;
            }

            // CRITICAL: Idempotency Check
            // Prevents duplicate Hangfire jobs if worker crashes before ACK
            // Note: Ensure SyncEntity has a 'HangfireJobId' property (like UserJob does)
            if (!string.IsNullOrEmpty(sync.HangfireJobId))
            {
                Logger.LogInformation("[SYNC_WORKER] Sync {Id} already enqueued (HF: {HfId}). Skipping.",
                    syncId, sync.HangfireJobId);
                return true;
            }

            // 5. Enqueue to Hangfire
            Logger.LogInformation("[SYNC_WORKER] Enqueuing Sync {Id}...", syncId);

            var hangfireJobId = backgroundJobClient.Enqueue<S3SyncJob>(
                service => service.ExecuteAsync(syncId, CancellationToken.None));

            // 6. Update State & Persist ID (Missing in your original code!)
            sync.HangfireJobId = hangfireJobId;
            sync.Status = SyncStatus.Pending; // Update status to reflect it's in Hangfire

            await unitOfWork.Complete();

            Logger.LogInformation("[SYNC_WORKER] Success | SyncId: {Id} -> HF: {HfId}", syncId, hangfireJobId);

            return true; // Base class handles XACK
        }
    }
}