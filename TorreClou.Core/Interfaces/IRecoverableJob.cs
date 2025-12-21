using TorreClou.Core.Enums;

namespace TorreClou.Core.Interfaces
{
    
    public interface IRecoverableJob
    {
        /// <summary>
        /// Unique identifier for the job.
        /// </summary>
        int Id { get; }

        /// <summary>
        /// Current status of the job. Active states: QUEUED, DOWNLOADING, SYNCING, PENDING_UPLOAD, UPLOADING.
        /// Retry states: TORRENT_DOWNLOAD_RETRY, UPLOAD_RETRY, SYNC_RETRY.
        /// Failure states: TORRENT_FAILED, UPLOAD_FAILED, GOOGLE_DRIVE_FAILED, FAILED.
        /// Terminal states: COMPLETED, CANCELLED.
        /// </summary>
        JobStatus Status { get; set; }

        /// <summary>
        /// Type of job for strategy selection during recovery.
        /// </summary>
        JobType Type { get; }

        /// <summary>
        /// Last heartbeat timestamp from the worker processing this job.
        /// Used to detect stale/orphaned jobs.
        /// </summary>
        DateTime? LastHeartbeat { get; set; }

        /// <summary>
        /// When the job started processing.
        /// Used as fallback when LastHeartbeat is null.
        /// </summary>
        DateTime? StartedAt { get; }

        /// <summary>
        /// Hangfire job ID for state reconciliation.
        /// </summary>
        string? HangfireJobId { get; set; }

        /// <summary>
        /// Error message from previous failure (cleared on recovery).
        /// </summary>
        string? ErrorMessage { get; set; }
    }
}

