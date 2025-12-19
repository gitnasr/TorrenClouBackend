using StackExchange.Redis;
using TorreClou.Core.Entities.Jobs;
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
                var downloadPath = entry["downloadPath"].ToString();

                if (!int.TryParse(jobIdStr, out var jobId))
                {
                    Logger.LogError("[SYNC_WORKER] Invalid jobId in stream entry: {JobId}", jobIdStr);
                    return false;
                }

                // Fetch job from database for validation
                var job = await unitOfWork.Repository<UserJob>().GetByIdAsync(jobId);
                if (job == null)
                {
                    Logger.LogError("[SYNC_WORKER] Job not found in database | JobId: {JobId}", jobId);
                    return false;
                }

                // Note: Lease acquisition is handled by BaseJob.ExecuteAsync in Hangfire
                // We just enqueue the job here - Hangfire will handle duplicate prevention via leases

                Logger.LogInformation("[SYNC_WORKER] Enqueuing S3 sync job to Hangfire | JobId: {JobId}", jobId);

                // Enqueue to Hangfire for reliable execution with retry capability
                var hangfireJobId = backgroundJobClient.Enqueue<S3SyncJob>(
                    service => service.ExecuteAsync(jobId, CancellationToken.None));

                Logger.LogInformation(
                    "[SYNC_WORKER] S3 sync job enqueued | JobId: {JobId} | HangfireJobId: {HangfireJobId}",
                    jobId, hangfireJobId);

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
                
                var jobIdStr = entry["jobId"].ToString();
                if (int.TryParse(jobIdStr, out var jobId))
                {
                    var job = await errorUnitOfWork.Repository<UserJob>().GetByIdAsync(jobId);
                    if (job != null)
                    {
                        job.Status = TorreClou.Core.Enums.JobStatus.FAILED;
                        job.ErrorMessage = $"Failed to enqueue sync job: {errorMessage}";
                        job.CompletedAt = DateTime.UtcNow;
                        await errorUnitOfWork.Complete();
                        
                        Logger.LogInformation("[SYNC_WORKER] Job marked as failed | JobId: {JobId}", jobId);
                    }
                }
            }
            catch (Exception updateEx)
            {
                Logger.LogError(updateEx, "[SYNC_WORKER] Failed to update job status to FAILED");
            }
        }
    }
}

