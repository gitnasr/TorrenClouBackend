using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TorreClou.Core.Interfaces;

namespace TorreClou.Infrastructure.Services.Redis
{
    /// <summary>
    /// Implementation of IRedisCacheService using StackExchange.Redis.
    /// Provides key-value cache operations with TTL support.
    /// </summary>
    public class RedisCacheService : IRedisCacheService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<RedisCacheService> _logger;
        private IDatabase Database => _redis.GetDatabase();

        public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public async Task<bool> SetAsync(string key, string value, TimeSpan? expiry = null)
        {
            try
            {
                var result = expiry.HasValue
                    ? await Database.StringSetAsync(key, value, expiry.Value)
                    : await Database.StringSetAsync(key, value);

                _logger.LogDebug("Redis SET | Key: {Key} | Expiry: {Expiry} | Success: {Success}", 
                    key, expiry?.ToString() ?? "none", result);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting Redis key | Key: {Key}", key);
                throw;
            }
        }

        public async Task<string?> GetAsync(string key)
        {
            try
            {
                var value = await Database.StringGetAsync(key);
                var result = value.HasValue ? value.ToString() : null;
                
                _logger.LogDebug("Redis GET | Key: {Key} | Found: {Found}", key, result != null);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Redis key | Key: {Key}", key);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string key)
        {
            try
            {
                var result = await Database.KeyDeleteAsync(key);
                
                _logger.LogDebug("Redis DELETE | Key: {Key} | Deleted: {Deleted}", key, result);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting Redis key | Key: {Key}", key);
                throw;
            }
        }

        public async Task<string?> GetAndDeleteAsync(string key)
        {
            try
            {
                var value = await Database.StringGetDeleteAsync(key);
                var result = value.HasValue ? value.ToString() : null;
                
                _logger.LogDebug("Redis GETDEL | Key: {Key} | Found: {Found}", key, result != null);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting and deleting Redis key | Key: {Key}", key);
                throw;
            }
        }

        public async Task<bool> ExistsAsync(string key)
        {
            try
            {
                var result = await Database.KeyExistsAsync(key);
                
                _logger.LogDebug("Redis EXISTS | Key: {Key} | Exists: {Exists}", key, result);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking Redis key existence | Key: {Key}", key);
                throw;
            }
        }
    }
}

