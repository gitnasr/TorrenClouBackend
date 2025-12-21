using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreClou.Core.Shared;
using TorreClou.Infrastructure.Settings;

namespace TorreClou.Infrastructure.Services.S3
{
    public interface IS3FileDownloadService
    {
        Task<Result<string>> DownloadToTempAsync(string s3Key, string tempDirectory, CancellationToken cancellationToken = default);
        Task<Result<Stream>> GetStreamAsync(string s3Key, CancellationToken cancellationToken = default);
        Task<Result<int>> DownloadAllWithPrefixAsync(string s3KeyPrefix, string tempDirectory, CancellationToken cancellationToken = default);
    }

    public class S3FileDownloadService : IS3FileDownloadService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly BackblazeSettings _settings;
        private readonly ILogger<S3FileDownloadService> _logger;

        public S3FileDownloadService(
            IOptions<BackblazeSettings> settings,
            ILogger<S3FileDownloadService> logger)
        {
            _settings = settings.Value;
            _logger = logger;

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

        public async Task<Result<string>> DownloadToTempAsync(string s3Key, string tempDirectory, CancellationToken cancellationToken = default)
        {
            try
            {
                var fileName = Path.GetFileName(s3Key);
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = Guid.NewGuid().ToString();
                }

                var tempFilePath = Path.Combine(tempDirectory, fileName);

                // Ensure temp directory exists
                Directory.CreateDirectory(tempDirectory);

                var request = new GetObjectRequest
                {
                    BucketName = _settings.BucketName,
                    Key = s3Key
                };

                using var response = await _s3Client.GetObjectAsync(request, cancellationToken);
                await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.ResponseStream.CopyToAsync(fileStream, cancellationToken);

                _logger.LogDebug("Downloaded file from S3 | Key: {Key} | TempPath: {TempPath}",
                    s3Key, tempFilePath);

                return Result.Success(tempFilePath);
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex, "Failed to download file from S3 | Key: {Key}",
                    s3Key);
                return Result<string>.Failure("DOWNLOAD_FAILED", $"Failed to download file: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error downloading file from S3 | Key: {Key}",
                    s3Key);
                return Result<string>.Failure("DOWNLOAD_ERROR", $"Unexpected error: {ex.Message}");
            }
        }

        public async Task<Result<Stream>> GetStreamAsync(string s3Key, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new GetObjectRequest
                {
                    BucketName = _settings.BucketName,
                    Key = s3Key
                };

                var response = await _s3Client.GetObjectAsync(request, cancellationToken);
                
                // Return a stream that will be disposed by the caller
                var memoryStream = new MemoryStream();
                await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken);
                memoryStream.Position = 0;

                _logger.LogDebug("Retrieved stream from S3 | Key: {Key}",
                    s3Key);

                return Result.Success<Stream>(memoryStream);
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex, "Failed to get stream from S3 | Key: {Key}",
                    s3Key);
                return Result<Stream>.Failure("GET_STREAM_FAILED", $"Failed to get stream: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error getting stream from S3 | Key: {Key}",
                    s3Key);
                return Result<Stream>.Failure("GET_STREAM_ERROR", $"Unexpected error: {ex.Message}");
            }
        }

        public async Task<Result<int>> DownloadAllWithPrefixAsync(string s3KeyPrefix, string tempDirectory, CancellationToken cancellationToken = default)
        {
            try
            {
                Directory.CreateDirectory(tempDirectory);

                var request = new ListObjectsV2Request
                {
                    BucketName = _settings.BucketName,
                    Prefix = s3KeyPrefix
                };

                var fileCount = 0;
                do
                {
                    var response = await _s3Client.ListObjectsV2Async(request, cancellationToken);
                    
                    foreach (var s3Object in response.S3Objects)
                    {
                        // Skip directories (keys ending with /)
                        if (s3Object.Key.EndsWith("/"))
                            continue;

                        // Calculate relative path from prefix
                        var relativePath = s3Object.Key.Substring(s3KeyPrefix.Length).TrimStart('/');
                        var localFilePath = Path.Combine(tempDirectory, relativePath);
                        
                        // Create directory structure
                        var localDir = Path.GetDirectoryName(localFilePath);
                        if (!string.IsNullOrEmpty(localDir))
                        {
                            Directory.CreateDirectory(localDir);
                        }

                        // Download file
                        var getRequest = new GetObjectRequest
                        {
                            BucketName = _settings.BucketName,
                            Key = s3Object.Key
                        };

                        using var getResponse = await _s3Client.GetObjectAsync(getRequest, cancellationToken);
                        await using var fileStream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                        await getResponse.ResponseStream.CopyToAsync(fileStream, cancellationToken);

                        fileCount++;
                        _logger.LogDebug("Downloaded file from S3 | Key: {Key} | LocalPath: {LocalPath}",
                            s3Object.Key, localFilePath);
                    }

                    request.ContinuationToken = response.NextContinuationToken;
                } while (request.ContinuationToken != null);

                _logger.LogInformation("Downloaded {FileCount} files from S3 | Prefix: {Prefix} | TempPath: {TempPath}",
                    fileCount, s3KeyPrefix, tempDirectory);

                return Result.Success(fileCount);
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex, "Failed to download files from S3 | Prefix: {Prefix}",
                    s3KeyPrefix);
                return Result<int>.Failure("DOWNLOAD_ALL_FAILED", $"Failed to download files: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error downloading files from S3 | Prefix: {Prefix}",
                    s3KeyPrefix);
                return Result<int>.Failure("DOWNLOAD_ALL_ERROR", $"Unexpected error: {ex.Message}");
            }
        }
    }
}

