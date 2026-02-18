using Hangfire;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.Core.Specifications;

namespace TorreClou.Worker.Services
{
    public class TorrentRecoveryStrategy(IServiceScopeFactory serviceScopeFactory) : IJobRecoveryStrategy
    {
        public JobType SupportedJobType => JobType.Torrent;

        // Monitor BOTH download and upload statuses
        public IReadOnlyList<JobStatus> MonitoredStatuses => new[]
        {
            // Download Phase
            JobStatus.DOWNLOADING,
            JobStatus.TORRENT_DOWNLOAD_RETRY,

            // Upload Phase
            JobStatus.PENDING_UPLOAD,
            JobStatus.UPLOADING,
            JobStatus.UPLOAD_RETRY,
            JobStatus.UPLOAD_FAILED,
            JobStatus.GOOGLE_DRIVE_FAILED
        };

        public async Task<string?> RecoverJobAsync(IRecoverableJob job, IBackgroundJobClient client)
        {
            var userJob = (UserJob)job;

            // 1. UPLOAD PHASE RECOVERY
            if (IsUploadPhase(userJob.Status))
            {
                // Reload the job with StorageProfile to get the correct provider type
                using var uploadScope = serviceScopeFactory.CreateScope();
                var uploadUnitOfWork = uploadScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                var jobSpec = new BaseSpecification<UserJob>(j => j.Id == userJob.Id);
                jobSpec.AddInclude(j => j.StorageProfile);
                var reloadedJob = await uploadUnitOfWork.Repository<UserJob>().GetEntityWithSpec(jobSpec);

                if (reloadedJob == null)
                {
                    throw new InvalidOperationException($"Job {userJob.Id} not found when attempting upload recovery");
                }

                // Route based on the Storage Profile (e.g., Google Drive, S3, etc.)
                var provider = reloadedJob.StorageProfile?.ProviderType;

                if (provider == null)
                {
                    throw new InvalidOperationException($"Job {userJob.Id} has no storage profile configured for upload recovery");
                }

                reloadedJob.CurrentState = $"Recovering upload to {provider}...";
                await uploadUnitOfWork.Complete();

                return provider switch
                {
                    StorageProviderType.GoogleDrive =>
                        client.Enqueue<IGoogleDriveUploadJob>(x => x.ExecuteAsync(job.Id, CancellationToken.None)),

                    StorageProviderType.S3 =>
                        client.Enqueue<IS3UploadJob>(x => x.ExecuteAsync(job.Id, CancellationToken.None)),

                    _ => throw new NotSupportedException($"No upload worker for provider: {provider}")
                };
            }

            // 2. DOWNLOAD PHASE RECOVERY
            userJob.CurrentState = "Recovering download phase...";

            // Resolve IJobStatusService from a new scope and reload the job to ensure it's tracked
            using var scope = serviceScopeFactory.CreateScope();
            var jobStatusService = scope.ServiceProvider.GetRequiredService<IJobStatusService>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // Reload the job in the new scope to ensure it's tracked by the correct DbContext
            var trackedJob = await unitOfWork.Repository<UserJob>().GetByIdAsync(userJob.Id);
            if (trackedJob == null)
            {
                throw new InvalidOperationException($"Job {userJob.Id} not found when attempting recovery");
            }

            await jobStatusService.TransitionJobStatusAsync(
                trackedJob,
                JobStatus.QUEUED,
                StatusChangeSource.Recovery,
                metadata: new { recoveredFrom = userJob.Status.ToString(), recoveryTime = DateTime.UtcNow });

            return client.Enqueue<ITorrentDownloadJob>(
                service => service.ExecuteAsync(job.Id, CancellationToken.None));
        }

        private static bool IsUploadPhase(JobStatus status)
        {
            return status == JobStatus.PENDING_UPLOAD ||
                   status == JobStatus.UPLOADING ||
                   status == JobStatus.UPLOAD_RETRY ||
                   status == JobStatus.UPLOAD_FAILED ||
                   status == JobStatus.GOOGLE_DRIVE_FAILED;
        }
    }
}