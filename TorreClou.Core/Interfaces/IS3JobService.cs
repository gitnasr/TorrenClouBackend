using TorreClou.Core.Shared;
using TorreClou.Core.Entities.Jobs;

namespace TorreClou.Core.Interfaces
{
    /// <summary>
    /// Service for S3-specific job operations including credential verification and lock management.
    /// </summary>
    public interface IS3JobService
    {
        /// <summary>
        /// Verifies S3 credentials from UserStorageProfile and tests bucket access.
        /// NO fallback - if credentials are missing or invalid, the operation fails.
        /// </summary>
        /// <param name="profile">User storage profile containing S3 credentials in CredentialsJson</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>
        /// Success: Tuple containing (AccessKey, SecretKey, Endpoint, BucketName)
        /// Failure: Error with codes like NO_CREDENTIALS, INVALID_CREDENTIALS_JSON, MISSING_REQUIRED_FIELDS, BUCKET_ACCESS_DENIED
        /// </returns>
        Task<Result<(string AccessKey, string SecretKey, string Endpoint, string BucketName)>>
            VerifyAndGetCredentialsAsync(UserStorageProfile profile, CancellationToken cancellationToken = default);

        /// <summary>
        /// Tests if the provided S3 credentials can access the specified bucket.
        /// Creates a temporary S3 client and attempts to list objects (MaxKeys=1).
        /// </summary>
        /// <param name="endpoint">S3 endpoint URL (e.g., https://s3.us-west-000.backblazeb2.com)</param>
        /// <param name="accessKey">AWS Access Key ID</param>
        /// <param name="secretKey">AWS Secret Access Key</param>
        /// <param name="bucketName">Bucket name to test access</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>
        /// Success: true if bucket is accessible
        /// Failure: Error with details about access failure
        /// </returns>
        Task<Result<bool>> TestBucketAccessAsync(
            string endpoint,
            string accessKey,
            string secretKey,
            string bucketName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the distributed upload lock for an S3 job.
        /// Lock key pattern: s3:lock:{jobId}
        /// </summary>
        /// <param name="jobId">The job ID</param>
        /// <returns>True if the lock was deleted, false if it didn't exist</returns>
        Task<bool> DeleteUploadLockAsync(int jobId);
    }
}
