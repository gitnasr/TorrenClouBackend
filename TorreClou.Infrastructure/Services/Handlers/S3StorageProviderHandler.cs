using Hangfire;
using Microsoft.Extensions.Logging;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;

namespace TorreClou.Infrastructure.Services.Handlers
{
    /// <summary>
    /// Storage provider handler for S3-compatible storage (AWS S3, Backblaze B2, etc.)
    /// </summary>
    public class S3StorageProviderHandler : IStorageProviderHandler
    {
        private readonly IRedisLockService _redisLockService;
        private readonly ILogger<S3StorageProviderHandler> _logger;

        public S3StorageProviderHandler(
            IRedisLockService redisLockService,
            ILogger<S3StorageProviderHandler> logger)
        {
            _redisLockService = redisLockService;
            _logger = logger;
        }

        public StorageProviderType ProviderType => StorageProviderType.AwsS3;

        public async Task<bool> DeleteUploadLockAsync(int jobId)
        {
            try
            {
                var lockKey = $"s3:lock:{jobId}";
                var result = await _redisLockService.DeleteLockAsync(lockKey);
                
                if (result)
                {
                    _logger.LogDebug("Deleted S3 upload lock | JobId: {JobId}", jobId);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete S3 upload lock | JobId: {JobId}", jobId);
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
