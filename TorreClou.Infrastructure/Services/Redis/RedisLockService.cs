using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TorreClou.Core.Interfaces;

namespace TorreClou.Infrastructure.Services.Redis
{
    /// <summary>
    /// Implementation of IRedisLockService using StackExchange.Redis.
    /// Provides distributed locking with automatic refresh to prevent expiration.
    /// </summary>
    public class RedisLockService : IRedisLockService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<RedisLockService> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private IDatabase Database => _redis.GetDatabase();

        public RedisLockService(IConnectionMultiplexer redis, ILogger<RedisLockService> logger, ILoggerFactory loggerFactory)
        {
            _redis = redis;
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        public async Task<IRedisLock?> AcquireLockAsync(string lockKey, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            try
            {
                var lockValue = Environment.MachineName + "_" + Guid.NewGuid().ToString();
                
                // Try to acquire lock (SET key value NX EX seconds)
                var lockAcquired = await Database.StringSetAsync(lockKey, lockValue, expiry, When.NotExists);

                if (!lockAcquired)
                {
                    _logger.LogDebug("Failed to acquire RedisLock - already held | Key: {Key}", lockKey);
                    return null;
                }

                _logger.LogInformation("Acquired RedisLock | Key: {Key} | Value: {Value} | Expiry: {Expiry}",
                    lockKey, lockValue, expiry);

                // Create lock instance with auto-refresh
                return new RedisLock(Database, lockKey, lockValue, expiry, 
                    _loggerFactory.CreateLogger<RedisLock>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error acquiring RedisLock | Key: {Key}", lockKey);
                throw;
            }
        }
    }
}

