using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Infrastructure.Settings;
using PartETag = TorreClou.Core.Interfaces.PartETag;

namespace TorreClou.Infrastructure.Services.S3
{
    public class S3ResumableUploadService : IS3ResumableUploadService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly BackblazeSettings _settings;
        private readonly ILogger<S3ResumableUploadService> _logger;

        public S3ResumableUploadService(
            IOptions<BackblazeSettings> settings,
            ILogger<S3ResumableUploadService> logger)
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

        public async Task<Result<string>> InitiateUploadAsync(string bucketName, string s3Key, long fileSize, string? contentType = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new InitiateMultipartUploadRequest
                {
                    BucketName = bucketName,
                    Key = s3Key,
                    ContentType = contentType ?? "application/octet-stream"
                };

                var response = await _s3Client.InitiateMultipartUploadAsync(request, cancellationToken);
                
                _logger.LogDebug("Initiated multipart upload | Bucket: {Bucket} | Key: {Key} | UploadId: {UploadId}",
                    bucketName, s3Key, response.UploadId);

                return Result.Success(response.UploadId);
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex, "Failed to initiate multipart upload | Bucket: {Bucket} | Key: {Key}",
                    bucketName, s3Key);
                return Result<string>.Failure("INIT_UPLOAD_FAILED", $"Failed to initiate upload: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error initiating multipart upload | Bucket: {Bucket} | Key: {Key}",
                    bucketName, s3Key);
                return Result<string>.Failure("INIT_UPLOAD_ERROR", $"Unexpected error: {ex.Message}");
            }
        }

        public async Task<Result<Core.Interfaces.PartETag>> UploadPartAsync(string bucketName, string s3Key, string uploadId, int partNumber, Stream partData, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new UploadPartRequest
                {
                    BucketName = bucketName,
                    Key = s3Key,
                    UploadId = uploadId,
                    PartNumber = partNumber,
                    InputStream = partData
                };

                var response = await _s3Client.UploadPartAsync(request, cancellationToken);

                _logger.LogDebug("Uploaded part | Bucket: {Bucket} | Key: {Key} | PartNumber: {PartNumber} | ETag: {ETag}",
                    bucketName, s3Key, partNumber, response.ETag);

                return Result.Success(new PartETag
                {
                    PartNumber = partNumber,
                    ETag = response.ETag
                });
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex, "Failed to upload part | Bucket: {Bucket} | Key: {Key} | PartNumber: {PartNumber}",
                    bucketName, s3Key, partNumber);
                return Result<PartETag>.Failure("UPLOAD_PART_FAILED", $"Failed to upload part: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error uploading part | Bucket: {Bucket} | Key: {Key} | PartNumber: {PartNumber}",
                    bucketName, s3Key, partNumber);
                return Result<PartETag>.Failure("UPLOAD_PART_ERROR", $"Unexpected error: {ex.Message}");
            }
        }

        public async Task<Result> CompleteUploadAsync(string bucketName, string s3Key, string uploadId, List<PartETag> parts, CancellationToken cancellationToken = default)
        {
            try
            {
                var partETags = parts.Select(p => new Amazon.S3.Model.PartETag
                {
                    PartNumber = p.PartNumber,
                    ETag = p.ETag
                }).ToList();

                var request = new CompleteMultipartUploadRequest
                {
                    BucketName = bucketName,
                    Key = s3Key,
                    UploadId = uploadId,
                    PartETags = partETags
                };

                await _s3Client.CompleteMultipartUploadAsync(request, cancellationToken);

                _logger.LogInformation("Completed multipart upload | Bucket: {Bucket} | Key: {Key} | UploadId: {UploadId} | Parts: {PartCount}",
                    bucketName, s3Key, uploadId, parts.Count);

                return Result.Success();
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex, "Failed to complete multipart upload | Bucket: {Bucket} | Key: {Key} | UploadId: {UploadId}",
                    bucketName, s3Key, uploadId);
                return Result.Failure("COMPLETE_UPLOAD_FAILED", $"Failed to complete upload: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error completing multipart upload | Bucket: {Bucket} | Key: {Key} | UploadId: {UploadId}",
                    bucketName, s3Key, uploadId);
                return Result.Failure("COMPLETE_UPLOAD_ERROR", $"Unexpected error: {ex.Message}");
            }
        }

        public async Task<Result> AbortUploadAsync(string bucketName, string s3Key, string uploadId, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new AbortMultipartUploadRequest
                {
                    BucketName = bucketName,
                    Key = s3Key,
                    UploadId = uploadId
                };

                await _s3Client.AbortMultipartUploadAsync(request, cancellationToken);

                _logger.LogInformation("Aborted multipart upload | Bucket: {Bucket} | Key: {Key} | UploadId: {UploadId}",
                    bucketName, s3Key, uploadId);

                return Result.Success();
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogWarning(ex, "Failed to abort multipart upload (may already be aborted) | Bucket: {Bucket} | Key: {Key} | UploadId: {UploadId}",
                    bucketName, s3Key, uploadId);
                // Don't fail if abort fails - upload may already be aborted or completed
                return Result.Success();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error aborting multipart upload | Bucket: {Bucket} | Key: {Key} | UploadId: {UploadId}",
                    bucketName, s3Key, uploadId);
                return Result.Success(); // Don't fail on abort errors
            }
        }

        public async Task<Result<List<PartETag>>> ListPartsAsync(string bucketName, string s3Key, string uploadId, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new ListPartsRequest
                {
                    BucketName = bucketName,
                    Key = s3Key,
                    UploadId = uploadId
                };

                var response = await _s3Client.ListPartsAsync(request, cancellationToken);

                var parts = response.Parts.Select(p => new PartETag
                {
                    PartNumber = p.PartNumber ?? 0,
                    ETag = p.ETag
                }).ToList();

                _logger.LogDebug("Listed parts for multipart upload | Bucket: {Bucket} | Key: {Key} | UploadId: {UploadId} | Parts: {PartCount}",
                    bucketName, s3Key, uploadId, parts.Count);

                return Result.Success(parts);
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex, "Failed to list parts | Bucket: {Bucket} | Key: {Key} | UploadId: {UploadId}",
                    bucketName, s3Key, uploadId);
                return Result<List<PartETag>>.Failure("LIST_PARTS_FAILED", $"Failed to list parts: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error listing parts | Bucket: {Bucket} | Key: {Key} | UploadId: {UploadId}",
                    bucketName, s3Key, uploadId);
                return Result<List<PartETag>>.Failure("LIST_PARTS_ERROR", $"Unexpected error: {ex.Message}");
            }
        }

        public async Task<Result<bool>> CheckObjectExistsAsync(string bucketName, string s3Key, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new GetObjectMetadataRequest
                {
                    BucketName = bucketName,
                    Key = s3Key
                };

                await _s3Client.GetObjectMetadataAsync(request, cancellationToken);
                return Result.Success(true);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return Result.Success(false);
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogWarning(ex, "Error checking object existence | Bucket: {Bucket} | Key: {Key}",
                    bucketName, s3Key);
                return Result.Success(false); // Assume doesn't exist on error
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error checking object existence | Bucket: {Bucket} | Key: {Key}",
                    bucketName, s3Key);
                return Result.Success(false);
            }
        }
    }
}

