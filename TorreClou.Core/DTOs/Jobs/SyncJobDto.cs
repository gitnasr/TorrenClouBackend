using TorreClou.Core.Enums;

namespace TorreClou.Core.DTOs.Jobs
{
    public class SyncJobDto
    {
        public int Id { get; set; }
        public int JobId { get; set; }
        public int UserId { get; set; }
        public string? UserEmail { get; set; }
        public SyncStatus Status { get; set; }
        public string? LocalFilePath { get; set; }
        public string? S3KeyPrefix { get; set; }
        public long TotalBytes { get; set; }
        public long BytesSynced { get; set; }
        public int FilesTotal { get; set; }
        public int FilesSynced { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int RetryCount { get; set; }
        public DateTime? NextRetryAt { get; set; }
        public DateTime? LastHeartbeat { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        
        // Related job information
        public string? RequestFileName { get; set; }
        public int? RequestFileId { get; set; }
        public string? StorageProfileName { get; set; }
        public int? StorageProfileId { get; set; }
        
        // Computed properties
        public double ProgressPercentage => TotalBytes > 0 ? (BytesSynced / (double)TotalBytes) * 100 : 0;
        public bool IsActive => Status == SyncStatus.SYNCING || 
                               Status == SyncStatus.SYNC_RETRY;
    }
}

