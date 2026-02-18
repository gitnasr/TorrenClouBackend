using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using TorreClou.Core.Exceptions;
using TorreClou.Core.Interfaces;
using PartETag = TorreClou.Core.DTOs.Storage.S3.PartETag;

namespace TorreClou.S3.Worker.Services
{
    /// <summary>
    /// S3 resumable upload service with user-specific credentials (NO FALLBACK)
    /// Accepts pre-configured AmazonS3 client with user credentials
    /// </summary>
    public class S3ResumableUploadService(
        IAmazonS3 s3Client,
        ILogger<S3ResumableUploadService> logger) : IS3ResumableUploadService
    {
        private readonly IAmazonS3 _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
        private readonly ILogger<S3ResumableUploadService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public async Task<string> InitiateUploadAsync(string bucketName, string s3Key, long fileSize, string? contentType = null, CancellationToken cancellationToken = default)
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

                return response.UploadId;
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex, "Failed to initiate multipart upload | Bucket: {Bucket} | Key: {Key}", bucketName, s3Key);
                throw new ExternalServiceException("InitUploadFailed", $"Failed to initiate upload: {ex.Message}");
            }
        }

        public async Task<PartETag> UploadPartAsync(string bucketName, string s3Key, string uploadId, int partNumber, Stream partData, CancellationToken cancellationToken = default)
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

                return new PartETag
                {
                    PartNumber = partNumber,
                    ETag = response.ETag
                };
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex, "Failed to upload part | Bucket: {Bucket} | Key: {Key} | PartNumber: {PartNumber}", bucketName, s3Key, partNumber);
                throw new ExternalServiceException("UploadPartFailed", $"Failed to upload part: {ex.Message}");
            }
        }

        public async Task CompleteUploadAsync(string bucketName, string s3Key, string uploadId, List<PartETag> parts, CancellationToken cancellationToken = default)
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
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex, "Failed to complete multipart upload | Bucket: {Bucket} | Key: {Key} | UploadId: {UploadId}", bucketName, s3Key, uploadId);
                throw new ExternalServiceException("CompleteUploadFailed", $"Failed to complete upload: {ex.Message}");
            }
        }

        public async Task AbortUploadAsync(string bucketName, string s3Key, string uploadId, CancellationToken cancellationToken = default)
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
            }
            catch (AmazonS3Exception ex)
            {
                // Don't fail if abort fails — upload may already be aborted or completed
                _logger.LogWarning(ex, "Failed to abort multipart upload (may already be aborted) | Bucket: {Bucket} | Key: {Key} | UploadId: {UploadId}",
                    bucketName, s3Key, uploadId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error aborting multipart upload | Bucket: {Bucket} | Key: {Key} | UploadId: {UploadId}",
                    bucketName, s3Key, uploadId);
            }
        }

        public async Task<List<PartETag>> ListPartsAsync(string bucketName, string s3Key, string uploadId, CancellationToken cancellationToken = default)
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

                return parts;
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex, "Failed to list parts | Bucket: {Bucket} | Key: {Key} | UploadId: {UploadId}", bucketName, s3Key, uploadId);
                throw new ExternalServiceException("ListPartsFailed", $"Failed to list parts: {ex.Message}");
            }
        }

        public async Task<bool> CheckObjectExistsAsync(string bucketName, string s3Key, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new GetObjectMetadataRequest
                {
                    BucketName = bucketName,
                    Key = s3Key
                };

                await _s3Client.GetObjectMetadataAsync(request, cancellationToken);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogWarning(ex, "Error checking object existence | Bucket: {Bucket} | Key: {Key}", bucketName, s3Key);
                return false; // Assume doesn't exist on error
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error checking object existence | Bucket: {Bucket} | Key: {Key}", bucketName, s3Key);
                return false;
            }
        }
    }
}
