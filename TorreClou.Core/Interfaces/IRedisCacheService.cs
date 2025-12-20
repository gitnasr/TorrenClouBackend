namespace TorreClou.Core.Interfaces
{
    /// <summary>
    /// Service for Redis key-value cache operations.
    /// Provides abstraction over Redis string operations with TTL support.
    /// </summary>
    public interface IRedisCacheService
    {
        /// <summary>
        /// Sets a key-value pair in Redis with optional expiration.
        /// </summary>
        /// <param name="key">The Redis key</param>
        /// <param name="value">The value to store</param>
        /// <param name="expiry">Optional expiration time. If null, key never expires.</param>
        /// <returns>True if the key was set successfully</returns>
        Task<bool> SetAsync(string key, string value, TimeSpan? expiry = null);

        /// <summary>
        /// Gets a value from Redis by key.
        /// </summary>
        /// <param name="key">The Redis key</param>
        /// <returns>The value if found, null otherwise</returns>
        Task<string?> GetAsync(string key);

        /// <summary>
        /// Deletes a key from Redis.
        /// </summary>
        /// <param name="key">The Redis key</param>
        /// <returns>True if the key was deleted, false if it didn't exist</returns>
        Task<bool> DeleteAsync(string key);

        /// <summary>
        /// Atomically gets and deletes a key from Redis.
        /// </summary>
        /// <param name="key">The Redis key</param>
        /// <returns>The value if found before deletion, null otherwise</returns>
        Task<string?> GetAndDeleteAsync(string key);

        /// <summary>
        /// Checks if a key exists in Redis.
        /// </summary>
        /// <param name="key">The Redis key</param>
        /// <returns>True if the key exists, false otherwise</returns>
        Task<bool> ExistsAsync(string key);
    }
}

