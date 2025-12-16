using TorreClou.Core.Enums;

namespace TorreClou.Core.Entities.Jobs
{
    public class UserStorageProfile : BaseEntity
    {
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public string ProfileName { get; set; } = string.Empty;

        public StorageProviderType ProviderType { get; set; }
        public string? Email { get; set; } // Email associated with the storage account (nullable for non-email providers)
        public string CredentialsJson { get; set; } = "{}";

        public bool IsDefault { get; set; } = false;
        public bool IsActive { get; set; } = true;
    }
}