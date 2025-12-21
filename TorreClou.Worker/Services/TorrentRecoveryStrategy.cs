using Hangfire;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.GoogleDrive.Worker.Services;
using TorreClou.Worker.Services;

namespace TorreClou.Worker.Services.Strategies
{
    public class TorrentRecoveryStrategy : IJobRecoveryStrategy
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

        public string RecoverJob(IRecoverableJob job, IBackgroundJobClient client)
        {
            var userJob = (UserJob)job;

            // 1. UPLOAD PHASE RECOVERY
            if (IsUploadPhase(userJob.Status))
            {
                // Route based on the Storage Profile (e.g., Google Drive, S3, etc.)
                var provider = userJob.StorageProfile?.ProviderType ?? StorageProviderType.GoogleDrive;

                userJob.CurrentState = $"Recovering upload to {provider}...";

                return provider switch
                {
                    StorageProviderType.GoogleDrive =>
                        client.Enqueue<GoogleDriveUploadJob>(x => x.ExecuteAsync(job.Id, CancellationToken.None)),

                    // Future: StorageProviderType.S3 => client.Enqueue<S3UploadJob>(...)

                    _ => throw new NotSupportedException($"No upload worker for provider: {provider}")
                };
            }

            // 2. DOWNLOAD PHASE RECOVERY
            userJob.CurrentState = "Recovering download phase...";
            userJob.Status = JobStatus.QUEUED;

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