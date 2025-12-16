namespace TorreClou.Core.DTOs.Jobs
{
    public class JobDto
    {
        public int Id { get; set; }
        public int StorageProfileId { get; set; }
        public string? StorageProfileName { get; set; }
        public string Status { get; set; } = string.Empty;
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
        public int[] SelectedFileIndices { get; set; } = [];
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        
        // Computed properties
        public double ProgressPercentage => TotalBytes > 0 ? (BytesDownloaded / (double)TotalBytes) * 100 : 0;
        public bool IsActive => Status == "PROCESSING" || Status == "UPLOADING" || Status == "QUEUED";
    }
}
