using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Infrastructure.Settings;

namespace TorreClou.Infrastructure.Services
{
    public class BackblazeStorageService : IBlobStorageService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly BackblazeSettings _settings;

        public BackblazeStorageService(IOptions<BackblazeSettings> settings)
        {
            _settings = settings.Value;

            var config = new AmazonS3Config
            {
                ServiceURL = _settings.Endpoint,
                ForcePathStyle = true
            };

            _s3Client = new AmazonS3Client(
                _settings.KeyId,
                _settings.ApplicationKey,
                config
            );
        }

        public async Task<Result<string>> UploadAsync(Stream stream, string fileName, string contentType)
        {
            try
            {
                // Generate unique key with timestamp to avoid collisions
                var key = $"torrents/{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid()}_{fileName}";

                var request = new PutObjectRequest
                {
                    BucketName = _settings.BucketName,
                    Key = key,
                    InputStream = stream,
                    ContentType = contentType
                };

                await _s3Client.PutObjectAsync(request);

                // Build the public URL
                var publicUrl = $"{_settings.Endpoint.TrimEnd('/')}/{_settings.BucketName}/{key}";

                return Result.Success(publicUrl);
            }
            catch (AmazonS3Exception ex)
            {
                return Result<string>.Failure("UPLOAD_FAILED", $"Failed to upload file to storage: {ex.Message}");
            }
            catch (Exception ex)
            {
                return Result<string>.Failure("UPLOAD_ERROR", $"Unexpected error during upload: {ex.Message}");
            }
        }
    }
}




