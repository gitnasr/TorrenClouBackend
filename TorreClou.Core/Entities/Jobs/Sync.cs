using TorreClou.Core.Enums;
using TorreClou.Core.Entities;
using TorreClou.Core.Interfaces;

namespace TorreClou.Core.Entities.Jobs
{
    public class Sync : BaseEntity, IRecoverableJob
    {
        public int JobId { get; set; }
        public UserJob UserJob { get; set; } = null!;
        public SyncStatus Status { get; set; }
        public string? LocalFilePath { get; set; } // Block storage path
        public string? S3KeyPrefix { get; set; } // e.g., "torrents/{jobId}"
        public long TotalBytes { get; set; }
        public long BytesSynced { get; set; }
        public int FilesTotal { get; set; }
        public int FilesSynced { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int RetryCount { get; set; }
        public DateTime? NextRetryAt { get; set; }
        
        // Navigation property for file-level progress
        public ICollection<S3SyncProgress> FileProgress { get; set; } = new List<S3SyncProgress>();

        public JobType Type => JobType.Sync;

        public DateTime? LastHeartbeat { get; set; }
        public string? HangfireJobId { get; set; }
        JobStatus IRecoverableJob.Status { get; set; }
    }
}

