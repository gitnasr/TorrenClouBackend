namespace TorreClou.Core.DTOs.Storage.Google_Drive
{
    public class OAuthState
    {
        public int UserId { get; set; }
        public string Nonce { get; set; } = string.Empty;
        public string? ProfileName { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
