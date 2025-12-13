using StackExchange.Redis;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Workers;
using TorreClou.GoogleDrive.Worker.Services;
using Hangfire;
using TorreClou.Core.Enums;

namespace TorreClou.GoogleDrive.Worker
{

    public class GoogleDriveWorker(
        ILogger<GoogleDriveWorker> logger,
        IConnectionMultiplexer redis,
        IServiceScopeFactory serviceScopeFactory) : BaseStreamWorker(logger, redis)
    {
        protected override string StreamKey => "uploads:googledrive:stream";
        protected override string ConsumerGroupName => "googledrive-workers";

        protected override async Task<bool> ProcessJobAsync(StreamEntry entry, CancellationToken cancellationToken)
        {
            using var scope = serviceScopeFactory.CreateScope();
            var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            try
            {
                // Parse job data from stream entry
                var jobIdStr = entry["jobId"].ToString();
                var downloadPath = entry["downloadPath"].ToString();
                var storageProfileIdStr = entry["storageProfileId"].ToString();

                if (!int.TryParse(jobIdStr, out var jobId))
                {
                    Logger.LogError("[GOOGLE_DRIVE_WORKER] Invalid jobId in stream entry: {JobId}", jobIdStr);
                    return false;
                }

                if (!int.TryParse(storageProfileIdStr, out var storageProfileId))
                {
                    Logger.LogError("[GOOGLE_DRIVE_WORKER] Invalid storageProfileId in stream entry: {StorageProfileId}", storageProfileIdStr);
                    return false;
                }

                // Fetch job from database
                var job = await unitOfWork.Repository<UserJob>().GetByIdAsync(jobId);
                if (job == null)
                {
                    Logger.LogError("[GOOGLE_DRIVE_WORKER] Job not found in database | JobId: {JobId}", jobId);
                    return false;
                }

                // Check if job is already being processed or completed
                if (job.Status != JobStatus.UPLOADING)
                {
                    Logger.LogInformation("[GOOGLE_DRIVE_WORKER] Job not in UPLOADING status | JobId: {JobId} | Status: {Status}", 
                        jobId, job.Status);
                    return true;
                }

                Logger.LogInformation("[GOOGLE_DRIVE_WORKER] Enqueuing Google Drive upload job to Hangfire | JobId: {JobId}", jobId);

                // Enqueue to Hangfire for reliable execution with retry capability
                var hangfireJobId = backgroundJobClient.Enqueue<GoogleDriveUploadJob>(
                    service => service.ExecuteAsync(jobId, CancellationToken.None));

                Logger.LogInformation(
                    "[GOOGLE_DRIVE_WORKER] Google Drive upload job enqueued | JobId: {JobId} | HangfireJobId: {HangfireJobId}",
                    jobId, hangfireJobId);

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[GOOGLE_DRIVE_WORKER] Error enqueuing Google Drive upload job to Hangfire");
                
                // Try to update job status to FAILED
                await TryMarkJobFailedAsync(entry, ex.Message);
                
                return false;
            }
        }

        private async Task TryMarkJobFailedAsync(StreamEntry entry, string errorMessage)
        {
            try
            {
                using var errorScope = serviceScopeFactory.CreateScope();
                var errorUnitOfWork = errorScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                
                var jobIdStr = entry["jobId"].ToString();
                if (int.TryParse(jobIdStr, out var jobId))
                {
                    var job = await errorUnitOfWork.Repository<UserJob>().GetByIdAsync(jobId);
                    if (job != null)
                    {
                        job.Status = TorreClou.Core.Enums.JobStatus.FAILED;
                        job.ErrorMessage = $"Failed to enqueue upload job: {errorMessage}";
                        job.CompletedAt = DateTime.UtcNow;
                        await errorUnitOfWork.Complete();
                        
                        Logger.LogInformation("[GOOGLE_DRIVE_WORKER] Job marked as failed | JobId: {JobId}", jobId);
                    }
                }
            }
            catch (Exception updateEx)
            {
                Logger.LogError(updateEx, "[GOOGLE_DRIVE_WORKER] Failed to update job status to FAILED");
            }
        }
    }
}

