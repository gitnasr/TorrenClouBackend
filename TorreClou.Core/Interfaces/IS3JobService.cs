using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    /// <summary>
    /// Service for S3-specific job operations including credential verification and lock management.
    /// </summary>
    public interface IS3JobService
    {
        Task<Result<(string AccessKey, string SecretKey, string Endpoint, string BucketName)>> VerifyAndGetCredentialsAsync(UserStorageProfile profile, CancellationToken cancellationToken = default);


        Task<Result<bool>> TestBucketAccessAsync(
            string endpoint,
            string accessKey,
            string secretKey,
            string bucketName,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteUploadLockAsync(int jobId);
    }
}
