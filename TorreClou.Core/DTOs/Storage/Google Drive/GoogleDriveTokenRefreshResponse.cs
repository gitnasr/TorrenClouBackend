using System.Text.Json.Serialization;

namespace TorreClou.Infrastructure.Services
{
   
        public class GoogleDriveTokenRefreshResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = string.Empty;

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }
    
}
