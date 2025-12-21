namespace TorreClou.Core.Entities.Torrents
{
    public class RequestedFile : BaseEntity
    {
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string[] Files { get; set; } = [];
        public string InfoHash { get; set; } = string.Empty;

        public string FileType { get; set; } = string.Empty;

        public int UploadedByUserId { get; set; }
        public User UploadedByUser { get; set; } = null!;

        /// <summary>
        /// Direct URL to the file (e.g., Backblaze B2 or external URL)
        /// </summary>
        public string? DirectUrl { get; set; }
    }
}
