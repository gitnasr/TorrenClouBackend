namespace TorreClou.Core.DTOs.Storage.Google_Drive
{

    public class UserInfoResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string? Email { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("verified_email")]
        public bool VerifiedEmail { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
