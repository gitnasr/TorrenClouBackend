using StackExchange.Redis;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Workers;
using TorreClou.GoogleDrive.Worker.Services; 
using Hangfire;

namespace TorreClou.GoogleDrive.Worker
{
    public class GoogleDriveWorker(
        ILogger<GoogleDriveWorker> logger,
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory) : BaseStreamWorker(logger, redis, scopeFactory)
    {
        protected override string StreamKey => "uploads:googledrive:stream";
        protected override string ConsumerGroupName => "googledrive-workers";

        protected override async Task<bool> ProcessJobAsync(StreamEntry entry, IServiceProvider services, CancellationToken token)
        {
            // 1. Use Base Helper for Safe Parsing
            var jobId = ParseJobId(entry);
            if (!jobId.HasValue)
            {
                Logger.LogWarning("[GD_WORKER] Invalid/Missing JobId. Acking to remove.");
                return true;
            }

            // 2. Resolve Services (Scoped is handled by Base Class)
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var backgroundJobClient = services.GetRequiredService<IBackgroundJobClient>();

            // 3. Load Job & Idempotency Check
            var job = await unitOfWork.Repository<UserJob>().GetByIdAsync(jobId.Value);

            if (job == null)
            {
                Logger.LogError("[GD_WORKER] Job {Id} not found in DB. Acking.", jobId);
                return true;
            }

            // CRITICAL: Prevent duplicate uploads if worker restarts before ACK
            if (!string.IsNullOrEmpty(job.HangfireJobId))
            {
                Logger.LogInformation("[GD_WORKER] Job {Id} already enqueued (HF: {HfId}). Skipping.",
                    jobId, job.HangfireJobId);
                return true;
            }

            // 4. Enqueue to Hangfire
            // We don't need to pass downloadPath/profileId manually; 
            // the Job itself (GoogleDriveUploadJob) should load the entity from DB to get those details.
            Logger.LogInformation("[GD_WORKER] Enqueuing Job {Id}...", jobId);

            var hangfireJobId = backgroundJobClient.Enqueue<GoogleDriveUploadJob>(
                service => service.ExecuteAsync(jobId.Value, CancellationToken.None));

            // 5. Update State
            job.HangfireJobId = hangfireJobId;
            job.Status = JobStatus.PENDING_UPLOAD;
            job.CurrentState = "Queued for Google Drive Upload";
            job.LastHeartbeat = DateTime.UtcNow;

            await unitOfWork.Complete();

            Logger.LogInformation("[GD_WORKER] Success | JobId: {JobId} -> HF: {HfId}", jobId, hangfireJobId);

            return true; // Base class handles the XACK
        }
    }
}