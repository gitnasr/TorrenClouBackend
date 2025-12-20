namespace TorreClou.Core.Interfaces
{
    /// <summary>
    /// Service for Redis stream operations.
    /// Provides abstraction for publishing messages to Redis streams.
    /// </summary>
    public interface IRedisStreamService
    {
        /// <summary>
        /// Publishes a message to a Redis stream.
        /// </summary>
        /// <param name="streamKey">The stream key</param>
        /// <param name="fields">Dictionary of field names and values</param>
        /// <returns>The message ID assigned by Redis</returns>
        Task<string> PublishAsync(string streamKey, Dictionary<string, string> fields);
    }
}

