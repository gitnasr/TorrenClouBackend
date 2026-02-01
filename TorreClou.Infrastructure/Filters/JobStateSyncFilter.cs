using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Extensions; // Ensure you have this for IsFailed()


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
                // 1. Extract the ID (JobId)
                if (context.BackgroundJob.Job.Args.Count == 0 || context.BackgroundJob.Job.Args[0] is not int id)
                    return;

                var errorMessage = failedState.Exception.Message;

                // 2. Update UserJob status to failed
                UpdateUserJobStatusToFailed(id, errorMessage).GetAwaiter().GetResult();
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
            var jobStatusService = scope.ServiceProvider.GetRequiredService<IJobStatusService>();
            var job = await unitOfWork.Repository<UserJob>().GetByIdAsync(jobId);

            if (job == null || job.Status.IsFailed()) return;

            // Map current status to specific failure status
            JobStatus failureStatus = job.Status switch
            {
                JobStatus.QUEUED or JobStatus.DOWNLOADING or JobStatus.TORRENT_DOWNLOAD_RETRY => JobStatus.TORRENT_FAILED,
                JobStatus.PENDING_UPLOAD or JobStatus.UPLOADING or JobStatus.UPLOAD_RETRY => JobStatus.UPLOAD_FAILED,
                _ => JobStatus.FAILED
            };

            logger.LogError("[Filter] Marking UserJob {JobId} as {Status} (Exhausted). Error: {Error}", jobId, failureStatus, error);

            job.CompletedAt = DateTime.UtcNow;
            job.NextRetryAt = null;

            await jobStatusService.TransitionJobStatusAsync(
                job,
                failureStatus,
                StatusChangeSource.System,
                $"System Failure: {error}",
                new { exhaustedRetries = true, hangfireJobId = jobId });
        }
    }
}
