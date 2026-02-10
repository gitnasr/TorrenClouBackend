namespace TorreClou.Core.DTOs.Storage.Google_Drive
{
    public class OAuthState
    {
        public int UserId { get; set; }
        public string Nonce { get; set; } = string.Empty;
        public string? ProfileName { get; set; }
        public DateTime ExpiresAt { get; set; }

        // User's OAuth app credentials (stored for callback)
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public bool SetAsDefault { get; set; } = false;
    }
}
