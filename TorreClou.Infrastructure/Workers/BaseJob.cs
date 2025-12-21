using Hangfire.Common;
using Microsoft.Extensions.Logging;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Specifications;

namespace TorreClou.Infrastructure.Workers
{
    /// <summary>
    /// Abstract base class for Hangfire jobs that process UserJob entities.
    /// Implements Template Method pattern for consistent job lifecycle management.
    /// </summary>
    /// <typeparam name="TJob">The concrete job type for logger categorization.</typeparam>
    public abstract class BaseJob<TJob>(
        IUnitOfWork unitOfWork,
        ILogger<TJob> logger) where TJob : class
    {
        protected readonly IUnitOfWork UnitOfWork = unitOfWork;
        protected readonly ILogger<TJob> Logger = logger;

        /// <summary>
        /// Log prefix for consistent logging (e.g., "[DOWNLOAD]", "[UPLOAD]").
        /// </summary>
        protected abstract string LogPrefix { get; }

        /// <summary>
        /// Template method that orchestrates the job execution lifecycle.
        /// Subclasses should override ExecuteCoreAsync for specific job logic.
        /// </summary>
        public async Task ExecuteAsync(int jobId, CancellationToken cancellationToken = default)
        {
            var operationName = $"job.{LogPrefix.Trim('[', ']').ToLowerInvariant()}.execute";
            

            Logger.LogInformation("{LogPrefix} Starting job | JobId: {JobId}", LogPrefix, jobId);
            UserJob? job = null; 

            try
            {
                // 1. Load job from database
                 job = await LoadJobAsync(jobId);


                if (job == null)
                {
                    return;
                }

                // 2. Check if job is FAILED - never reprocess failed jobs
                if (job.Status == JobStatus.FAILED)
                {
                    Logger.LogWarning("{LogPrefix} Job is FAILED and will not be reprocessed | JobId: {JobId}", 
                        LogPrefix, jobId);
                    return;
                }

                // 3. Check if already completed or cancelled
                if (IsJobTerminated(job))
                {
                    Logger.LogInformation("{LogPrefix} Job already finished | JobId: {JobId} | Status: {Status}", 
                        LogPrefix, jobId, job.Status);
                    return;
                }

                // 4. Execute the core job logic
                await ExecuteCoreAsync(job, cancellationToken);


            }
            catch (OperationCanceledException)
            {
                Logger.LogError("{LogPrefix} Job cancelled | JobId: {JobId}", LogPrefix, jobId);
                if (job != null)
                    await OnJobCancelledAsync(job);

                throw; // Let Hangfire handle the cancellation
            }
            catch (Exception ex)
            {
                Logger.LogCritical(ex, "{LogPrefix} Fatal error | JobId: {JobId}", LogPrefix, jobId);
                

                if (job != null)
                {
                    // Check if retries are available before marking as failed
                    bool hasRetries = HasRetriesAvailable(job);
                    // MarkJobFailedAsync will determine the correct status, so we don't need to set it here
                    await OnJobErrorAsync(job, ex);
                    await MarkJobFailedAsync(job, ex.Message, hasRetries);
                    
                }

                throw; // Let Hangfire retry if attempts remain
            }
        }

        /// <summary>
        /// Core job execution logic. Override in derived classes.
        /// </summary>
        protected abstract Task ExecuteCoreAsync(UserJob job, CancellationToken cancellationToken);

        /// <summary>
        /// Configure the specification with job-specific includes.
        /// Override in derived classes to add related entities.
        /// </summary>
        protected abstract void ConfigureSpecification(BaseSpecification<UserJob> spec);

        /// <summary>
        /// Hook for cleanup when job is cancelled. Override for specific cleanup logic.
        /// </summary>
        protected virtual Task OnJobCancelledAsync(UserJob job)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Hook for cleanup when job encounters an error. Override for specific cleanup logic.
        /// </summary>
        protected virtual Task OnJobErrorAsync(UserJob job, Exception exception)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Loads the job from the database with configured includes.
        /// </summary>
        protected async Task<UserJob?> LoadJobAsync(int jobId)
        {
            var spec = new BaseSpecification<UserJob>(j => j.Id == jobId);
            ConfigureSpecification(spec);

            var job = await UnitOfWork.Repository<UserJob>().GetEntityWithSpec(spec);

            if (job == null)
            {
                Logger.LogError("{LogPrefix} Job not found | JobId: {JobId}", LogPrefix, jobId);
            }

            return job;
        }

        /// <summary>
        /// Checks if the job is in a terminal state (COMPLETED or CANCELLED).
        /// Note: FAILED is checked separately and never reprocessed.
        /// </summary>
        protected bool IsJobTerminated(UserJob job)
        {
            return job.Status == JobStatus.COMPLETED || job.Status == JobStatus.CANCELLED;
        }


        /// <summary>
        /// Updates the job's heartbeat and current state.
        /// </summary>
        protected async Task UpdateHeartbeatAsync(UserJob job, string? state = null)
        {
            try
            {
                job.LastHeartbeat = DateTime.UtcNow;
                if (state != null)
                {
                    job.CurrentState = state;
                }
                
                await UnitOfWork.Complete();
            }
            catch (Exception ex)
            {
                Logger.LogCritical(ex, "{LogPrefix} Failed to update heartbeat | JobId: {JobId}", LogPrefix, job.Id);
            }
        }

        /// <summary>
        /// Marks the job as failed with an error message.
        /// If retries are available (determined by Hangfire), sets status to specific retry state based on current phase.
        /// Otherwise, sets appropriate failure state.
        /// </summary>
        protected async Task MarkJobFailedAsync(UserJob job, string errorMessage, bool hasRetries = false)
        {
            try
            {
                if (hasRetries)
                {
                    // Determine retry state based on current job phase
                    JobStatus retryStatus = job.Status switch
                    {
                        JobStatus.QUEUED or JobStatus.DOWNLOADING or JobStatus.TORRENT_FAILED or JobStatus.TORRENT_DOWNLOAD_RETRY => JobStatus.TORRENT_DOWNLOAD_RETRY,
                        JobStatus.SYNCING or JobStatus.SYNC_RETRY => JobStatus.SYNC_RETRY,
                        JobStatus.UPLOADING or JobStatus.UPLOAD_RETRY => JobStatus.UPLOAD_RETRY,
                    };
                    
                    job.Status = retryStatus;
                    job.ErrorMessage = errorMessage;
                    // NextRetryAt will be set by Hangfire's retry mechanism
                    // We estimate it based on typical retry delays: 60s, 300s, 900s
                    // This is approximate - Hangfire will handle actual scheduling
                    job.NextRetryAt = DateTime.UtcNow.AddMinutes(1); // Conservative estimate
                    
                    Logger.LogWarning("{LogPrefix} Job marked as {RetryStatus} | JobId: {JobId} | Error: {Error} | NextRetryAt: {NextRetry}", 
                        LogPrefix, retryStatus, job.Id, errorMessage, job.NextRetryAt);
                }
                else
                {
                    // Determine failure state based on current job phase
                    JobStatus failureStatus = job.Status switch
                    {
                        JobStatus.DOWNLOADING or JobStatus.TORRENT_DOWNLOAD_RETRY or JobStatus.TORRENT_FAILED => JobStatus.TORRENT_FAILED,
                        JobStatus.SYNCING or JobStatus.SYNC_RETRY => JobStatus.UPLOAD_FAILED, // Sync failures are upload-related
                        JobStatus.UPLOADING or JobStatus.UPLOAD_RETRY => JobStatus.UPLOAD_FAILED,
                        _ => JobStatus.FAILED // Fallback to generic failure state
                    };
                    
                    job.Status = failureStatus;
                    job.ErrorMessage = errorMessage;
                    job.CompletedAt = DateTime.UtcNow;
                    job.NextRetryAt = null; // Clear retry time
                    
                    Logger.LogError("{LogPrefix} Job marked as {FailureStatus} (no retries remaining) | JobId: {JobId} | Error: {Error}", 
                        LogPrefix, failureStatus, job.Id, errorMessage);
                }
                
                await UnitOfWork.Complete();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{LogPrefix} Failed to mark job status | JobId: {JobId}", LogPrefix, job.Id);
            }
        }

        /// <summary>
        /// Determines if Hangfire retries are likely available for this job.
        /// This is a heuristic - Hangfire's AutomaticRetry attribute controls actual retries.
        /// </summary>
        protected bool HasRetriesAvailable(UserJob job)
        {
            // If job is in a terminal failure state, no retries
            if (job.Status == JobStatus.FAILED ||
                job.Status == JobStatus.TORRENT_FAILED ||
                job.Status == JobStatus.UPLOAD_FAILED ||
                job.Status == JobStatus.GOOGLE_DRIVE_FAILED ||
                job.Status == JobStatus.COMPLETED ||
                job.Status == JobStatus.CANCELLED)
                return false;

            // If job is in any retry state, assume retries might still be available
            // (Hangfire will eventually mark as FAILED when exhausted)
            if (
                job.Status == JobStatus.TORRENT_DOWNLOAD_RETRY ||
                job.Status == JobStatus.UPLOAD_RETRY ||
                job.Status == JobStatus.SYNC_RETRY)
                return true;

            // For other active states, assume retries might be available
            // Hangfire's AutomaticRetry will handle actual retry logic
            return true;
        }
    }
}

