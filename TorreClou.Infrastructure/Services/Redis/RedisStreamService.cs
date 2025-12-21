using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TorreClou.Core.Interfaces;

namespace TorreClou.Infrastructure.Services.Redis
{
    /// <summary>
    /// Implementation of IRedisStreamService using StackExchange.Redis.
    /// Provides stream publishing operations.
    /// </summary>
    public class RedisStreamService : IRedisStreamService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<RedisStreamService> _logger;
        private IDatabase Database => _redis.GetDatabase();

        public RedisStreamService(IConnectionMultiplexer redis, ILogger<RedisStreamService> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public async Task<string> PublishAsync(string streamKey, Dictionary<string, string> fields)
        {
            try
            {
                var nameValueEntries = fields.Select(kvp => new NameValueEntry(kvp.Key, kvp.Value)).ToArray();
                var messageId = await Database.StreamAddAsync(streamKey, nameValueEntries);
                
                _logger.LogDebug("Published to Redis stream | Stream: {Stream} | MessageId: {MessageId} | Fields: {FieldCount}",
                    streamKey, messageId, fields.Count);
                
                return messageId.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing to Redis stream | Stream: {Stream}", streamKey);
                throw;
            }
        }
    }
}

