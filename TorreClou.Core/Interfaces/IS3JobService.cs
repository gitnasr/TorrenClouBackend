using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;

namespace TorreClou.Core.Interfaces
{
    /// <summary>
    /// Service for S3-specific job operations including credential verification, lock management, and upload progress tracking.
    /// </summary>
    public interface IS3JobService
    {
        Task<(string AccessKey, string SecretKey, string Endpoint, string BucketName)> VerifyAndGetCredentialsAsync(UserStorageProfile profile, CancellationToken cancellationToken = default);

        Task TestBucketAccessAsync(
            string endpoint,
            string accessKey,
            string secretKey,
            string bucketName,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteUploadLockAsync(int jobId);

        // Upload progress tracking
        Task<IReadOnlyList<S3SyncProgress>> GetInProgressUploadsAsync(int jobId);
        Task<S3SyncProgress?> GetUploadProgressAsync(int jobId, string s3Key);
        Task CreateUploadProgressAsync(S3SyncProgress progress);
        Task SaveUploadProgressAsync(S3SyncProgress progress);
        Task DeleteUploadProgressAsync(S3SyncProgress progress);
    }
}
