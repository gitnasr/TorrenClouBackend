using StackExchange.Redis;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Workers;
using Hangfire;
using TorreClou.Core.Interfaces.Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace TorreClou.S3.Worker
{
    public class S3Worker(
        ILogger<S3Worker> logger,
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory) : BaseStreamWorker(logger, redis, scopeFactory)
    {
        protected override string StreamKey => "uploads:awss3:stream";
        protected override string ConsumerGroupName => "s3-workers";

        protected override async Task<bool> ProcessJobAsync(StreamEntry entry, IServiceProvider services, CancellationToken token)
        {
            // 1. Use Base Helper for Safe Parsing
            var jobId = ParseJobId(entry);
            if (!jobId.HasValue)
            {
                Logger.LogWarning("[S3_WORKER] Invalid/Missing JobId. Acking to remove.");
                return true;
            }

            // 2. Resolve Services (Scoped is handled by Base Class)
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var backgroundJobClient = services.GetRequiredService<IBackgroundJobClient>();

            // 3. Load Job & Idempotency Check
            var job = await unitOfWork.Repository<UserJob>().GetByIdAsync(jobId.Value);

            if (job == null)
            {
                Logger.LogError("[S3_WORKER] Job {Id} not found in DB. Acking.", jobId);
                return true;
            }

            // CRITICAL: Prevent duplicate uploads if worker restarts before ACK
            if (!string.IsNullOrEmpty(job.HangfireUploadJobId))
            {
                Logger.LogInformation("[S3_WORKER] Job {Id} already enqueued (HF: {HfId}). Skipping.",
                    jobId, job.HangfireUploadJobId);
                return true;
            }

            // 4. Enqueue to Hangfire
            Logger.LogInformation("[S3_WORKER] Enqueuing Job {Id}...", jobId);

            var hangfireJobId = backgroundJobClient.Enqueue<IS3UploadJob>(
                service => service.ExecuteAsync(jobId.Value, CancellationToken.None));

            // 5. Persist Hangfire job reference to DB (with rollback on failure)
            try
            {
                job.HangfireUploadJobId = hangfireJobId;
                job.Status = JobStatus.PENDING_UPLOAD;
                job.CurrentState = "Queued for S3 Upload";
                job.LastHeartbeat = DateTime.UtcNow;

                await unitOfWork.Complete();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[S3_WORKER] DB persist failed after enqueue | JobId: {JobId} | HfId: {HfId}. Deleting Hangfire job.",
                    jobId, hangfireJobId);

                try
                {
                    backgroundJobClient.Delete(hangfireJobId);
                    Logger.LogInformation("[S3_WORKER] Deleted orphaned Hangfire job | HfId: {HfId}", hangfireJobId);
                }
                catch (Exception deleteEx)
                {
                    Logger.LogError(deleteEx, "[S3_WORKER] Failed to delete orphaned Hangfire job | HfId: {HfId}", hangfireJobId);
                }

                throw;
            }

            Logger.LogInformation("[S3_WORKER] Success | JobId: {JobId} -> HF: {HfId}", jobId, hangfireJobId);

            return true; // Base class handles the XACK
        }
    }
}
