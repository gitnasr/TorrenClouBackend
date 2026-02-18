using Hangfire;
using Microsoft.Extensions.Logging;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;

namespace TorreClou.Infrastructure.Services.Handlers
{

    public class S3StorageProviderHandler(
        IRedisLockService redisLockService,
        ILogger<S3StorageProviderHandler> logger) : IStorageProviderHandler
    {
        public StorageProviderType ProviderType => StorageProviderType.S3;

        public async Task<bool> DeleteUploadLockAsync(int jobId)
        {
            try
            {
                var lockKey = $"s3:lock:{jobId}";
                var result = await redisLockService.DeleteLockAsync(lockKey);

                if (result)
                {
                    logger.LogDebug("Deleted S3 upload lock | JobId: {JobId}", jobId);
                }

                return result;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete S3 upload lock | JobId: {JobId}", jobId);
                return false;
            }
        }

        public Type GetUploadJobInterfaceType()
        {
            return typeof(IS3UploadJob);
        }

        public string EnqueueUploadJob(int jobId, IBackgroundJobClient client)
        {
            return client.Enqueue<IS3UploadJob>(x => x.ExecuteAsync(jobId, CancellationToken.None));
        }
    }
}
