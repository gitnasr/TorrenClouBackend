using TorreClou.Core.Enums;
using TorreClou.Core.Entities;

namespace TorreClou.Core.Entities.Jobs
{
    public class S3SyncProgress : BaseEntity
    {
        public int JobId { get; set; }
        public UserJob UserJob { get; set; } = null!;
        public int SyncId { get; set; }
        public Sync Sync { get; set; } = null!;
        public string LocalFilePath { get; set; } = string.Empty;
        public string S3Key { get; set; } = string.Empty;
        public string? UploadId { get; set; } // S3 multipart upload ID
        public long PartSize { get; set; } // e.g., 10MB
        public int TotalParts { get; set; }
        public int PartsCompleted { get; set; }
        public long BytesUploaded { get; set; }
        public long TotalBytes { get; set; }
        public string PartETags { get; set; } = "[]"; // JSON array of {PartNumber, ETag}
        public SyncProgressStatus Status { get; set; }
        public int? LastPartNumber { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}

