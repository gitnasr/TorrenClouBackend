using TorreClou.Core.Enums;
using TorreClou.Core.Extensions;

namespace TorreClou.Core.DTOs.Jobs
{
    public class JobDto
    {
        public int Id { get; set; }
        public int StorageProfileId { get; set; }
        public string? StorageProfileName { get; set; }
        public JobStatus Status { get; set; }
        public string Type { get; set; } = string.Empty;
        public int RequestFileId { get; set; }
        public string? RequestFileName { get; set; }
        public string? ErrorMessage { get; set; }
        public string? CurrentState { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? LastHeartbeat { get; set; }
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public string[]? SelectedFilePaths { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        
        // Computed properties
        public double ProgressPercentage => TotalBytes > 0 ? (BytesDownloaded / (double)TotalBytes) * 100 : 0;
        public bool IsActive => Status == JobStatus.QUEUED || 
                               Status == JobStatus.DOWNLOADING || 
                               Status == JobStatus.PENDING_UPLOAD || 
                               Status == JobStatus.UPLOADING || 
                               Status == JobStatus.TORRENT_DOWNLOAD_RETRY || 
                               Status == JobStatus.UPLOAD_RETRY;
        public bool CanRetry => Status.IsFailed() && Status != JobStatus.CANCELLED;
        public bool CanCancel => Status.IsCancellable();

        /// <summary>
        /// Status change timeline for this job.
        /// </summary>
        public List<JobTimelineEntryDto> Timeline { get; set; } = [];
    }
}
