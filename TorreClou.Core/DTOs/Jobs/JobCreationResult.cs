namespace TorreClou.Core.DTOs.Jobs
{
    public class JobCreationResult
    {
        public int JobId { get; set; }
        public int? StorageProfileId { get; set; }
        public bool HasStorageProfileWarning { get; set; }
        public string? StorageProfileWarningMessage { get; set; }
    }
}

