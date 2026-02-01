using Amazon.S3;
using Amazon.S3.Model;
using System.Text.Json;
using TorreClou.Core.DTOs.Storage.S3;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using Microsoft.Extensions.Logging;

namespace TorreClou.S3.Worker.Services
{
    /// <summary>
    /// Service for S3-specific job operations including credential verification and lock management.
    /// Implements NO FALLBACK policy - all credentials must come from UserStorageProfile.CredentialsJson.
    /// </summary>
    public class S3JobService : IS3JobService
    {
        private readonly IRedisLockService _redisLockService;
        private readonly ILogger<S3JobService> _logger;

        public S3JobService(
            IRedisLockService redisLockService,
            ILogger<S3JobService> logger)
        {
            _redisLockService = redisLockService;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<Result<(string AccessKey, string SecretKey, string Endpoint, string BucketName)>>
            VerifyAndGetCredentialsAsync(UserStorageProfile profile, CancellationToken cancellationToken = default)
        {
            const string logPrefix = "[S3_JOB_SERVICE]";

            // 1. Validate profile exists and is active
            if (profile == null)
            {
                _logger.LogError("{LogPrefix} UserStorageProfile is null", logPrefix);
                return Result<(string, string, string, string)>.Failure(
                    "INVALID_PROFILE",
                    "Storage profile does not exist");
            }

            if (!profile.IsActive)
            {
                _logger.LogWarning("{LogPrefix} Profile {ProfileId} is inactive", logPrefix, profile.Id);
                return Result<(string, string, string, string)>.Failure(
                    "INACTIVE_PROFILE",
                    "Storage profile is not active");
            }

            // 2. Check if CredentialsJson exists
            if (string.IsNullOrWhiteSpace(profile.CredentialsJson))
            {
                _logger.LogError("{LogPrefix} Profile {ProfileId} has no credentials", logPrefix, profile.Id);
                return Result<(string, string, string, string)>.Failure(
                    "NO_CREDENTIALS",
                    "User has not configured S3 credentials in their storage profile");
            }

            // 3. Parse CredentialsJson
            S3Credentials? credentials;
            try
            {
                credentials = JsonSerializer.Deserialize<S3Credentials>(profile.CredentialsJson);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "{LogPrefix} Failed to parse CredentialsJson for Profile {ProfileId}",
                    logPrefix, profile.Id);
                return Result<(string, string, string, string)>.Failure(
                    "INVALID_CREDENTIALS_JSON",
                    "Credentials JSON is malformed or corrupted");
            }

            if (credentials == null)
            {
                _logger.LogError("{LogPrefix} Deserialized credentials are null for Profile {ProfileId}",
                    logPrefix, profile.Id);
                return Result<(string, string, string, string)>.Failure(
                    "INVALID_CREDENTIALS_JSON",
                    "Credentials JSON is malformed or corrupted");
            }

            // 4. Validate required fields
            var missingFields = new List<string>();
            if (string.IsNullOrWhiteSpace(credentials.AccessKey)) missingFields.Add("AccessKey");
            if (string.IsNullOrWhiteSpace(credentials.SecretKey)) missingFields.Add("SecretKey");
            if (string.IsNullOrWhiteSpace(credentials.Endpoint)) missingFields.Add("Endpoint");
            if (string.IsNullOrWhiteSpace(credentials.BucketName)) missingFields.Add("BucketName");

            if (missingFields.Any())
            {
                _logger.LogError("{LogPrefix} Profile {ProfileId} missing required fields: {Fields}",
                    logPrefix, profile.Id, string.Join(", ", missingFields));
                return Result<(string, string, string, string)>.Failure(
                    "MISSING_REQUIRED_FIELDS",
                    $"Required credentials fields missing: {string.Join(", ", missingFields)}");
            }

            // 5. Test bucket access
            _logger.LogInformation("{LogPrefix} Testing bucket access | Profile: {ProfileId} | Bucket: {Bucket}",
                logPrefix, profile.Id, credentials.BucketName);

            var accessTestResult = await TestBucketAccessAsync(
                credentials.Endpoint,
                credentials.AccessKey,
                credentials.SecretKey,
                credentials.BucketName,
                cancellationToken);

            if (accessTestResult.IsFailure)
            {
                _logger.LogError("{LogPrefix} Bucket access test failed | Profile: {ProfileId} | Error: {Error}",
                    logPrefix, profile.Id, accessTestResult.Error.Message);
                return Result<(string, string, string, string)>.Failure(
                    "BUCKET_ACCESS_DENIED",
                    $"Cannot access S3 bucket with provided credentials: {accessTestResult.Error.Message}");
            }

            _logger.LogInformation("{LogPrefix} Credentials verified successfully | Profile: {ProfileId}",
                logPrefix, profile.Id);

            return Result.Success((
                credentials.AccessKey,
                credentials.SecretKey,
                credentials.Endpoint,
                credentials.BucketName));
        }

        /// <inheritdoc/>
        public async Task<Result<bool>> TestBucketAccessAsync(
            string endpoint,
            string accessKey,
            string secretKey,
            string bucketName,
            CancellationToken cancellationToken = default)
        {
            const string logPrefix = "[S3_JOB_SERVICE]";

            try
            {
                // Create temporary S3 client with user credentials
                var config = new AmazonS3Config
                {
                    ServiceURL = endpoint,
                    ForcePathStyle = true, // Required for S3-compatible services like Backblaze B2
                    Timeout = TimeSpan.FromSeconds(10),
                    MaxErrorRetry = 1
                };

                using var s3Client = new AmazonS3Client(accessKey, secretKey, config);

                // Test bucket access by listing objects (MaxKeys=1 for minimal overhead)
                var listRequest = new ListObjectsV2Request
                {
                    BucketName = bucketName,
                    MaxKeys = 1
                };

                var response = await s3Client.ListObjectsV2Async(listRequest, cancellationToken);

                _logger.LogDebug("{LogPrefix} Bucket access test successful | Bucket: {Bucket} | Objects: {Count}",
                    logPrefix, bucketName, response.KeyCount);

                return Result.Success(true);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("{LogPrefix} Bucket access denied | Bucket: {Bucket} | StatusCode: {Status}",
                    logPrefix, bucketName, ex.StatusCode);
                return Result<bool>.Failure(
                    "ACCESS_DENIED",
                    $"Access denied to bucket '{bucketName}'. Check credentials and bucket permissions.");
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("{LogPrefix} Bucket not found | Bucket: {Bucket}",
                    logPrefix, bucketName);
                return Result<bool>.Failure(
                    "BUCKET_NOT_FOUND",
                    $"Bucket '{bucketName}' does not exist or is not accessible.");
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex, "{LogPrefix} S3 error testing bucket access | Bucket: {Bucket} | StatusCode: {Status}",
                    logPrefix, bucketName, ex.StatusCode);
                return Result<bool>.Failure(
                    "S3_ERROR",
                    $"S3 error: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{LogPrefix} Unexpected error testing bucket access | Bucket: {Bucket}",
                    logPrefix, bucketName);
                return Result<bool>.Failure(
                    "UNEXPECTED_ERROR",
                    $"Unexpected error testing bucket access: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteUploadLockAsync(int jobId)
        {
            const string logPrefix = "[S3_JOB_SERVICE]";
            var lockKey = $"s3:lock:{jobId}";

            try
            {
                var deleted = await _redisLockService.DeleteLockAsync(lockKey);
                if (deleted)
                {
                    _logger.LogInformation("{LogPrefix} Deleted upload lock | JobId: {JobId} | Key: {Key}",
                        logPrefix, jobId, lockKey);
                }
                else
                {
                    _logger.LogDebug("{LogPrefix} Lock did not exist | JobId: {JobId} | Key: {Key}",
                        logPrefix, jobId, lockKey);
                }
                return deleted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{LogPrefix} Error deleting upload lock | JobId: {JobId} | Key: {Key}",
                    logPrefix, jobId, lockKey);
                return false;
            }
        }
    }
}
