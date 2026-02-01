using System.Text.Json.Serialization;

namespace TorreClou.Core.DTOs.Storage.GoogleDrive
{
    public class GoogleDriveCredentials
    {
        // OAuth App Credentials (user-provided)
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("client_secret")]
        public string ClientSecret { get; set; } = string.Empty;

        [JsonPropertyName("redirect_uri")]
        public string RedirectUri { get; set; } = string.Empty;

        // OAuth Tokens (from Google)
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_at")]
        public string? ExpiresAt { get; set; }
    }
}
