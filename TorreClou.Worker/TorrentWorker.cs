using StackExchange.Redis;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Workers;
using TorreClou.Worker.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Hangfire;

namespace TorreClou.Worker
{
    /// <summary>
    /// Redis stream consumer that listens for new torrent jobs and enqueues them to Hangfire.
    /// This provides decoupled event-driven job dispatch from the API layer.
    /// </summary>
    public class TorrentWorker : BaseStreamWorker
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        protected override string StreamKey => "jobs:stream";
        protected override string ConsumerGroupName => "torrent-workers";

        public TorrentWorker(
            ILogger<TorrentWorker> logger,
            IConnectionMultiplexer redis,
            IServiceScopeFactory serviceScopeFactory) : base(logger, redis)
        {
            _serviceScopeFactory = serviceScopeFactory;
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
                var jobTypeStr = entry["jobType"].ToString();

                if (!int.TryParse(jobIdStr, out var jobId))
                {
                    Logger.LogError("[WORKER] Invalid jobId in stream entry: {JobId}", jobIdStr);
                    return false;
                }

                // Check if this is a torrent job
                if (!Enum.TryParse<JobType>(jobTypeStr, out var jobType) || jobType != JobType.Torrent)
                {
                    Logger.LogDebug("[WORKER] Skipping non-torrent job | JobId: {JobId} | JobType: {JobType}", jobId, jobTypeStr);
                    return true; // Not an error, just not our job type
                }

                // Fetch job from database to update with Hangfire job ID
                var job = await unitOfWork.Repository<UserJob>().GetByIdAsync(jobId);
                if (job == null)
                {
                    Logger.LogError("[WORKER] Job not found in database | JobId: {JobId}", jobId);
                    return false;
                }

                // Check if job is already being processed or completed
                if (job.Status != JobStatus.QUEUED)
                {
                    Logger.LogInformation("[WORKER] Job already in progress or completed | JobId: {JobId} | Status: {Status}", 
                        jobId, job.Status);
                    return true;
                }

                Logger.LogInformation("[WORKER] Enqueuing torrent download job to Hangfire | JobId: {JobId}", jobId);

                // Enqueue to Hangfire for reliable execution with retry capability
                var hangfireJobId = backgroundJobClient.Enqueue<TorrentDownloadJob>(
                    service => service.ExecuteAsync(jobId, CancellationToken.None));

                // Store Hangfire job ID in database for state reconciliation
                job.HangfireJobId = hangfireJobId;
                job.CurrentState = "Queued for processing";
                await unitOfWork.Complete();

                Logger.LogInformation(
                    "[WORKER] Torrent job enqueued | JobId: {JobId} | HangfireJobId: {HangfireJobId}",
                    jobId, hangfireJobId);

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[WORKER] Error enqueuing torrent job to Hangfire");
                
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
                        job.Status = JobStatus.FAILED;
                        job.ErrorMessage = $"Failed to enqueue job: {errorMessage}";
                        job.CompletedAt = DateTime.UtcNow;
                        await errorUnitOfWork.Complete();
                        
                        Logger.LogInformation("[WORKER] Job marked as failed | JobId: {JobId}", jobId);
                    }
                }
            }
            catch (Exception updateEx)
            {
                Logger.LogError(updateEx, "[WORKER] Failed to update job status to FAILED");
            }
        }
    }
}
