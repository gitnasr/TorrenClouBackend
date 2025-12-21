using TorreClou.Core.Enums;

namespace TorreClou.Core.Extensions
{
    public static class JobStatusExtensions
    {
        public static bool IsActive(this JobStatus status)
        {
            return status == JobStatus.QUEUED ||
                   status == JobStatus.DOWNLOADING ||
                   status == JobStatus.PENDING_UPLOAD ||
                   status == JobStatus.UPLOADING ||
                   status == JobStatus.TORRENT_DOWNLOAD_RETRY ||
                   status == JobStatus.UPLOAD_RETRY;
        }

        public static bool IsRetrying(this JobStatus status)
        {
            return status == JobStatus.TORRENT_DOWNLOAD_RETRY ||
                   status == JobStatus.UPLOAD_RETRY;
        }

        public static bool IsFailed(this JobStatus status)
        {
            return status == JobStatus.FAILED ||
                   status == JobStatus.TORRENT_FAILED ||
                   status == JobStatus.UPLOAD_FAILED ||
                   status == JobStatus.GOOGLE_DRIVE_FAILED;
        }

        public static bool IsCompleted(this JobStatus status) => status == JobStatus.COMPLETED;
        public static bool IsCancelled(this JobStatus status) => status == JobStatus.CANCELLED;
    }
}