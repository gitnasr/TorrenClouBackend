namespace TorreClou.Core.DTOs.Storage.Google_Drive
{
    public class OAuthState
    {
        public int UserId { get; set; }
        public string Nonce { get; set; } = string.Empty;
        public string? ProfileName { get; set; }
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// When set, the callback updates the existing profile instead of creating a new one.
        /// Used for re-authentication flows.
        /// </summary>
        public int? ProfileId { get; set; }

        /// <summary>
        /// FK to UserOAuthCredential — the callback loads app credentials from this.
        /// </summary>
        public int OAuthCredentialId { get; set; }

        public bool SetAsDefault { get; set; } = false;
    }
}
