namespace TorreClou.Core.DTOs.Storage.S3
{
    public class PartETag
    {
        public int PartNumber { get; set; }
        public string ETag { get; set; } = string.Empty;
    }
}

