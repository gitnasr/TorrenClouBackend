
using TorreClou.Core.Enums;
using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Torrents;
using TorreClou.Core.Entities.Financals;

namespace TorreClou.Core.Entities.Jobs
{
    public class UserJob : BaseEntity
    {
        public int UserId { get; set; }
        public User User { get; set; } = null!;


        public int StorageProfileId { get; set; }
        public UserStorageProfile StorageProfile { get; set; } = null!;

        public JobStatus Status { get; set; } = JobStatus.QUEUED;

        public JobType Type { get; set; } = JobType.Torrent;

        public int RequestFileId { get; set; }

        public RequestedFile RequestFile { get; set; } = null!;

        public string? ErrorMessage { get; set; }
        public string? CurrentState { get; set; }

        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        
        /// <summary>
        /// Updated periodically by the worker to indicate the job is still being processed.
        /// Used by recovery service to detect crashed/orphaned jobs.
        /// </summary>
        public DateTime? LastHeartbeat { get; set; }

        /// <summary>
        /// Hangfire job ID for state reconciliation and recovery.
        /// </summary>
        public string? HangfireJobId { get; set; }

        /// <summary>
        /// Explicit download path for reliable resumption after crashes.
        /// </summary>
        public string? DownloadPath { get; set; }

        /// <summary>
        /// Bytes downloaded so far. Used for progress tracking and resumption.
        /// </summary>
        public long BytesDownloaded { get; set; }

        /// <summary>
        /// Total bytes expected to download.
        /// </summary>
        public long TotalBytes { get; set; }

        public Invoice? Invoice { get; set; }

        public int[] SelectedFileIndices { get; set; } = [];
    }
}