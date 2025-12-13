using System.Text.Json.Serialization;

namespace TorreClou.Infrastructure.Services
{
    public partial class GoogleDriveService
    {
        public class TokenRefreshResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = string.Empty;

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }
    }
}
