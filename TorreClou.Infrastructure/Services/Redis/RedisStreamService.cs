using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TorreClou.Core.Interfaces;

namespace TorreClou.Infrastructure.Services.Redis
{
    /// <summary>
    /// Implementation of IRedisStreamService using StackExchange.Redis.
    /// Provides stream publishing operations.
    /// </summary>
    public class RedisStreamService(IConnectionMultiplexer redis, ILogger<RedisStreamService> logger) : IRedisStreamService
    {
        private IDatabase Database => redis.GetDatabase();

        public async Task<string> PublishAsync(string streamKey, Dictionary<string, string> fields)
        {
            try
            {
                var nameValueEntries = fields.Select(kvp => new NameValueEntry(kvp.Key, kvp.Value)).ToArray();
                var messageId = await Database.StreamAddAsync(streamKey, nameValueEntries);

                logger.LogDebug("Published to Redis stream | Stream: {Stream} | MessageId: {MessageId} | Fields: {FieldCount}",
                    streamKey, messageId, fields.Count);

                return messageId.ToString();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error publishing to Redis stream | Stream: {Stream}", streamKey);
                throw;
            }
        }
    }
}

