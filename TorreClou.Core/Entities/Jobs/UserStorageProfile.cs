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

        /// <summary>
        /// Set to true when Google refresh token is expired/revoked.
        /// Cleared after successful re-authentication.
        /// </summary>
        public bool NeedsReauth { get; set; } = false;

        /// <summary>
        /// FK to the reusable OAuth app credentials (ClientId/Secret/RedirectUri).
        /// Null for non-Google-Drive providers or legacy profiles.
        /// </summary>
        public int? OAuthCredentialId { get; set; }
        public UserOAuthCredential? OAuthCredential { get; set; }
    }
}