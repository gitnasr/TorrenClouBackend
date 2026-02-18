using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TorreClou.Core.DTOs.Storage.S3;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Exceptions;
using TorreClou.Core.Interfaces;

namespace TorreClou.S3.Worker.Services
{
    /// <summary>
    /// Service for S3-specific job operations including credential verification and lock management.
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

        public async Task<(string AccessKey, string SecretKey, string Endpoint, string BucketName)>
            VerifyAndGetCredentialsAsync(UserStorageProfile profile, CancellationToken cancellationToken = default)
        {
            const string logPrefix = "[S3_JOB_SERVICE]";

            if (profile == null)
            {
                _logger.LogError("{LogPrefix} UserStorageProfile is null", logPrefix);
                throw new ValidationException("InvalidProfile", "Storage profile does not exist");
            }

            if (!profile.IsActive)
            {
                _logger.LogWarning("{LogPrefix} Profile {ProfileId} is inactive", logPrefix, profile.Id);
                throw new BusinessRuleException("InactiveProfile", "Storage profile is not active");
            }

            if (string.IsNullOrWhiteSpace(profile.CredentialsJson))
            {
                _logger.LogError("{LogPrefix} Profile {ProfileId} has no credentials", logPrefix, profile.Id);
                throw new BusinessRuleException("NoCredentials", "User has not configured S3 credentials in their storage profile");
            }

            S3Credentials? credentials;
            try
            {
                credentials = JsonSerializer.Deserialize<S3Credentials>(profile.CredentialsJson);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "{LogPrefix} Failed to parse CredentialsJson for Profile {ProfileId}", logPrefix, profile.Id);
                throw new ValidationException("InvalidCredentialsJson", "Credentials JSON is malformed or corrupted");
            }

            if (credentials == null)
            {
                _logger.LogError("{LogPrefix} Deserialized credentials are null for Profile {ProfileId}", logPrefix, profile.Id);
                throw new ValidationException("InvalidCredentialsJson", "Credentials JSON is malformed or corrupted");
            }

            var missingFields = new List<string>();
            if (string.IsNullOrWhiteSpace(credentials.AccessKey)) missingFields.Add("AccessKey");
            if (string.IsNullOrWhiteSpace(credentials.SecretKey)) missingFields.Add("SecretKey");
            if (string.IsNullOrWhiteSpace(credentials.Endpoint)) missingFields.Add("Endpoint");
            if (string.IsNullOrWhiteSpace(credentials.BucketName)) missingFields.Add("BucketName");

            if (missingFields.Any())
            {
                _logger.LogError("{LogPrefix} Profile {ProfileId} missing required fields: {Fields}", logPrefix, profile.Id, string.Join(", ", missingFields));
                throw new ValidationException("MissingRequiredFields", $"Required credentials fields missing: {string.Join(", ", missingFields)}");
            }

            _logger.LogInformation("{LogPrefix} Testing bucket access | Profile: {ProfileId} | Bucket: {Bucket}", logPrefix, profile.Id, credentials.BucketName);

            await TestBucketAccessAsync(credentials.Endpoint, credentials.AccessKey, credentials.SecretKey, credentials.BucketName, cancellationToken);

            _logger.LogInformation("{LogPrefix} Credentials verified successfully | Profile: {ProfileId}", logPrefix, profile.Id);

            return (credentials.AccessKey, credentials.SecretKey, credentials.Endpoint, credentials.BucketName);
        }

        public async Task TestBucketAccessAsync(
            string endpoint,
            string accessKey,
            string secretKey,
            string bucketName,
            CancellationToken cancellationToken = default)
        {
            const string logPrefix = "[S3_JOB_SERVICE]";

            var config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true,
                Timeout = TimeSpan.FromSeconds(10),
                MaxErrorRetry = 1
            };

            using var s3Client = new AmazonS3Client(accessKey, secretKey, config);

            try
            {
                var listRequest = new ListObjectsV2Request
                {
                    BucketName = bucketName,
                    MaxKeys = 1
                };

                var response = await s3Client.ListObjectsV2Async(listRequest, cancellationToken);

                _logger.LogDebug("{LogPrefix} Bucket access test successful | Bucket: {Bucket} | Objects: {Count}",
                    logPrefix, bucketName, response.KeyCount);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("{LogPrefix} Bucket access denied | Bucket: {Bucket} | StatusCode: {Status}", logPrefix, bucketName, ex.StatusCode);
                throw new ForbiddenException("AccessDenied", $"Access denied to bucket '{bucketName}'. Check credentials and bucket permissions.");
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("{LogPrefix} Bucket not found | Bucket: {Bucket}", logPrefix, bucketName);
                throw new NotFoundException("BucketNotFound", $"Bucket '{bucketName}' does not exist or is not accessible.");
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(ex, "{LogPrefix} S3 error testing bucket access | Bucket: {Bucket} | StatusCode: {Status}", logPrefix, bucketName, ex.StatusCode);
                throw new ExternalServiceException("S3Error", $"S3 error: {ex.Message}");
            }
        }

        public async Task<bool> DeleteUploadLockAsync(int jobId)
        {
            const string logPrefix = "[S3_JOB_SERVICE]";
            var lockKey = $"s3:lock:{jobId}";

            try
            {
                var deleted = await _redisLockService.DeleteLockAsync(lockKey);
                if (deleted)
                    _logger.LogInformation("{LogPrefix} Deleted upload lock | JobId: {JobId} | Key: {Key}", logPrefix, jobId, lockKey);
                else
                    _logger.LogDebug("{LogPrefix} Lock did not exist | JobId: {JobId} | Key: {Key}", logPrefix, jobId, lockKey);
                return deleted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{LogPrefix} Error deleting upload lock | JobId: {JobId} | Key: {Key}", logPrefix, jobId, lockKey);
                return false;
            }
        }
    }
}
