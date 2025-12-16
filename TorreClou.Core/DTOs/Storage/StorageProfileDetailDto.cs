namespace TorreClou.Core.DTOs.Storage
{
    public class StorageProfileDetailDto
    {
        public int Id { get; set; }
        public string ProfileName { get; set; } = string.Empty;
        public string ProviderType { get; set; } = string.Empty;
        public string? Email { get; set; } // Email associated with the storage account
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
