using System.Text.Json.Serialization;

namespace TorreClou.Core.DTOs.Storage.S3
{
    /// <summary>
    /// Represents S3-compatible storage credentials from UserStorageProfile.CredentialsJson.
    /// Supports AWS S3, Backblaze B2, and other S3-compatible providers.
    /// </summary>
    public class S3Credentials
    {
        /// <summary>
        /// AWS Access Key ID or equivalent for S3-compatible storage
        /// </summary>
        [JsonPropertyName("accessKey")]
        public string AccessKey { get; set; } = string.Empty;

        /// <summary>
        /// AWS Secret Access Key or equivalent for S3-compatible storage
        /// </summary>
        [JsonPropertyName("secretKey")]
        public string SecretKey { get; set; } = string.Empty;

        /// <summary>
        /// S3 endpoint URL (e.g., https://s3.us-west-000.backblazeb2.com for Backblaze B2)
        /// For AWS S3, this is typically region-specific (e.g., https://s3.us-east-1.amazonaws.com)
        /// </summary>
        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// Bucket name where files will be uploaded
        /// </summary>
        [JsonPropertyName("bucketName")]
        public string BucketName { get; set; } = string.Empty;

        /// <summary>
        /// Optional region identifier (e.g., us-west-000 for Backblaze, us-east-1 for AWS)
        /// </summary>
        [JsonPropertyName("region")]
        public string? Region { get; set; }
    }
}
