using System.Text.Json.Serialization;

namespace TorreClou.Core.DTOs.Storage.GoogleDrive
{
    /// <summary>
    /// OAuth tokens stored in UserStorageProfile.CredentialsJson (JSONB).
    /// App credentials (ClientId/Secret) are now in UserOAuthCredential entity.
    /// </summary>
    public class GoogleDriveCredentials
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_at")]
        public string? ExpiresAt { get; set; }
    }
}
