namespace TorreClou.Core.DTOs.Storage
{
    public record ConfigureS3RequestDto
    {
        public string ProfileName { get; init; } = string.Empty;
        public string S3Endpoint { get; init; } = string.Empty;
        public string S3AccessKey { get; init; } = string.Empty;
        public string S3SecretKey { get; init; } = string.Empty;
        public string S3BucketName { get; init; } = string.Empty;
        public string S3Region { get; init; } = string.Empty;
        public bool SetAsDefault { get; init; } = false;
    }

    public record StorageProfileResultDto
    {
        public bool Success { get; init; }
        public int StorageProfileId { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public record StorageProviderDto
    {
        public int Id { get; init; }
        public string ProfileName { get; init; } = string.Empty;
        public string ProviderType { get; init; } = string.Empty;
        public string? S3Endpoint { get; init; }
        public string? S3BucketName { get; init; }
        public bool IsDefault { get; init; }
        public bool IsActive { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    public record StorageProvidersListDto
    {
        public List<StorageProviderDto> Providers { get; init; } = new();
    }
}
