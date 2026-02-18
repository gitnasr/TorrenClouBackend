using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TorreClou.Core.DTOs.Storage.S3;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Exceptions;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Specifications;

namespace TorreClou.S3.Worker.Services
{
    /// <summary>
    /// Service for S3-specific job operations including credential verification, lock management, and upload progress tracking.
    /// </summary>
    public class S3JobService(
        IRedisLockService redisLockService,
        IUnitOfWork unitOfWork,
        ILogger<S3JobService> logger) : IS3JobService
    {
        public async Task<(string AccessKey, string SecretKey, string Endpoint, string BucketName)>
            VerifyAndGetCredentialsAsync(UserStorageProfile profile, CancellationToken cancellationToken = default)
        {
            const string logPrefix = "[S3_JOB_SERVICE]";

            if (profile == null)
            {
                logger.LogError("{LogPrefix} UserStorageProfile is null", logPrefix);
                throw new ValidationException("InvalidProfile", "Storage profile does not exist");
            }

            if (!profile.IsActive)
            {
                logger.LogWarning("{LogPrefix} Profile {ProfileId} is inactive", logPrefix, profile.Id);
                throw new BusinessRuleException("InactiveProfile", "Storage profile is not active");
            }

            if (string.IsNullOrWhiteSpace(profile.CredentialsJson))
            {
                logger.LogError("{LogPrefix} Profile {ProfileId} has no credentials", logPrefix, profile.Id);
                throw new BusinessRuleException("NoCredentials", "User has not configured S3 credentials in their storage profile");
            }

            S3Credentials? credentials;
            try
            {
                credentials = JsonSerializer.Deserialize<S3Credentials>(profile.CredentialsJson);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "{LogPrefix} Failed to parse CredentialsJson for Profile {ProfileId}", logPrefix, profile.Id);
                throw new ValidationException("InvalidCredentialsJson", "Credentials JSON is malformed or corrupted");
            }

            if (credentials == null)
            {
                logger.LogError("{LogPrefix} Deserialized credentials are null for Profile {ProfileId}", logPrefix, profile.Id);
                throw new ValidationException("InvalidCredentialsJson", "Credentials JSON is malformed or corrupted");
            }

            var missingFields = new List<string>();
            if (string.IsNullOrWhiteSpace(credentials.AccessKey)) missingFields.Add("AccessKey");
            if (string.IsNullOrWhiteSpace(credentials.SecretKey)) missingFields.Add("SecretKey");
            if (string.IsNullOrWhiteSpace(credentials.Endpoint)) missingFields.Add("Endpoint");
            if (string.IsNullOrWhiteSpace(credentials.BucketName)) missingFields.Add("BucketName");

            if (missingFields.Count != 0)
            {
                logger.LogError("{LogPrefix} Profile {ProfileId} missing required fields: {Fields}", logPrefix, profile.Id, string.Join(", ", missingFields));
                throw new ValidationException("MissingRequiredFields", $"Required credentials fields missing: {string.Join(", ", missingFields)}");
            }

            logger.LogInformation("{LogPrefix} Testing bucket access | Profile: {ProfileId} | Bucket: {Bucket}", logPrefix, profile.Id, credentials.BucketName);

            await TestBucketAccessAsync(credentials.Endpoint, credentials.AccessKey, credentials.SecretKey, credentials.BucketName, cancellationToken);

            logger.LogInformation("{LogPrefix} Credentials verified successfully | Profile: {ProfileId}", logPrefix, profile.Id);

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

                logger.LogDebug("{LogPrefix} Bucket access test successful | Bucket: {Bucket} | Objects: {Count}",
                    logPrefix, bucketName, response.KeyCount);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                logger.LogWarning("{LogPrefix} Bucket access denied | Bucket: {Bucket} | StatusCode: {Status}", logPrefix, bucketName, ex.StatusCode);
                throw new ForbiddenException("AccessDenied", $"Access denied to bucket '{bucketName}'. Check credentials and bucket permissions.");
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                logger.LogWarning("{LogPrefix} Bucket not found | Bucket: {Bucket}", logPrefix, bucketName);
                throw new NotFoundException("BucketNotFound", $"Bucket '{bucketName}' does not exist or is not accessible.");
            }
            catch (AmazonS3Exception ex)
            {
                logger.LogError(ex, "{LogPrefix} S3 error testing bucket access | Bucket: {Bucket} | StatusCode: {Status}", logPrefix, bucketName, ex.StatusCode);
                throw new ExternalServiceException("S3Error", $"S3 error: {ex.Message}");
            }
        }

        public async Task<bool> DeleteUploadLockAsync(int jobId)
        {
            const string logPrefix = "[S3_JOB_SERVICE]";
            var lockKey = $"s3:lock:{jobId}";

            try
            {
                var deleted = await redisLockService.DeleteLockAsync(lockKey);
                if (deleted)
                    logger.LogInformation("{LogPrefix} Deleted upload lock | JobId: {JobId} | Key: {Key}", logPrefix, jobId, lockKey);
                else
                    logger.LogDebug("{LogPrefix} Lock did not exist | JobId: {JobId} | Key: {Key}", logPrefix, jobId, lockKey);
                return deleted;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{LogPrefix} Error deleting upload lock | JobId: {JobId} | Key: {Key}", logPrefix, jobId, lockKey);
                return false;
            }
        }

        public async Task<IReadOnlyList<S3SyncProgress>> GetInProgressUploadsAsync(int jobId)
        {
            var spec = new BaseSpecification<S3SyncProgress>(
                p => p.JobId == jobId && p.Status == S3UploadProgressStatus.InProgress);
            return await unitOfWork.Repository<S3SyncProgress>().ListAsync(spec);
        }

        public async Task<S3SyncProgress?> GetUploadProgressAsync(int jobId, string s3Key)
        {
            var spec = new BaseSpecification<S3SyncProgress>(
                p => p.JobId == jobId && p.S3Key == s3Key);
            return await unitOfWork.Repository<S3SyncProgress>().GetEntityWithSpec(spec);
        }

        public async Task CreateUploadProgressAsync(S3SyncProgress progress)
        {
            unitOfWork.Repository<S3SyncProgress>().Add(progress);
            await unitOfWork.Complete();
        }

        public async Task SaveUploadProgressAsync(S3SyncProgress progress)
        {
            await unitOfWork.Complete();
        }

        public async Task DeleteUploadProgressAsync(S3SyncProgress progress)
        {
            unitOfWork.Repository<S3SyncProgress>().Delete(progress);
            await unitOfWork.Complete();
        }
    }
}
