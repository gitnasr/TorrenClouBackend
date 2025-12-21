using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Extensions; // Ensure you have this for IsFailed()
using SyncEntity = TorreClou.Core.Entities.Jobs.Sync;

namespace TorreClou.Infrastructure.Filters
{
    public class JobStateSyncFilter(IServiceScopeFactory scopeFactory, ILogger<JobStateSyncFilter> logger) : IElectStateFilter
    {
        public void OnStateElection(ElectStateContext context)
        {
            // Only act if the job is moving to the FAILED state (all retries exhausted)
            if (context.CandidateState is not FailedState failedState) return;

            try
            {
                // 1. Extract the ID (It could be JobId OR SyncId depending on the job type)
                if (context.BackgroundJob.Job.Args.Count == 0 || context.BackgroundJob.Job.Args[0] is not int id)
                    return;

                var jobType = context.BackgroundJob.Job.Type;
                var errorMessage = failedState.Exception.Message;

                // 2. Route based on Job Type name to avoid hard dependencies on Worker DLLs
                // "S3SyncJob" takes a SyncId. All others (TorrentDownloadJob, GoogleDriveUploadJob) take a JobId.
                if (jobType.Name.Contains("S3SyncJob"))
                {
                    UpdateSyncStatusToFailed(id, errorMessage).GetAwaiter().GetResult();
                }
                else
                {
                    UpdateUserJobStatusToFailed(id, errorMessage).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Filter] Critical error syncing job state.");
            }
        }

        private async Task UpdateUserJobStatusToFailed(int jobId, string error)
        {
            using var scope = scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var job = await unitOfWork.Repository<UserJob>().GetByIdAsync(jobId);

            if (job == null || job.Status.IsFailed()) return;

            // Map current status to specific failure status
            JobStatus failureStatus = job.Status switch
            {
                JobStatus.QUEUED or JobStatus.DOWNLOADING or JobStatus.TORRENT_DOWNLOAD_RETRY => JobStatus.TORRENT_FAILED,
                JobStatus.PENDING_UPLOAD or JobStatus.UPLOADING or JobStatus.UPLOAD_RETRY => JobStatus.UPLOAD_FAILED,
                JobStatus.SYNCING or JobStatus.SYNC_RETRY => JobStatus.UPLOAD_FAILED,
                _ => JobStatus.FAILED
            };

            logger.LogError("[Filter] Marking UserJob {JobId} as {Status} (Exhausted). Error: {Error}", jobId, failureStatus, error);

            job.Status = failureStatus;
            job.ErrorMessage = $"System Failure: {error}";
            job.CompletedAt = DateTime.UtcNow;
            job.NextRetryAt = null;

            await unitOfWork.Complete();
        }

        private async Task UpdateSyncStatusToFailed(int syncId, string error)
        {
            using var scope = scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // Correctly using SyncEntity repository
            var sync = await unitOfWork.Repository<SyncEntity>().GetByIdAsync(syncId);

            if (sync == null || sync.Status == SyncStatus.Failed || sync.Status == SyncStatus.Completed) return;

            logger.LogError("[Filter] Marking Sync {SyncId} as Failed (Exhausted). Error: {Error}", syncId, error);

            sync.Status = SyncStatus.Failed;
            sync.ErrorMessage = $"System Failure: {error}";
            sync.NextRetryAt = null;

            await unitOfWork.Complete();
        }
    }
}