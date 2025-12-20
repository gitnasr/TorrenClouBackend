namespace TorreClou.Core.Interfaces
{
    /// <summary>
    /// Represents a distributed lock acquired from Redis.
    /// Automatically refreshes the lock to prevent expiration during long-running operations.
    /// Must be disposed to release the lock.
    /// </summary>
    public interface IRedisLock : IDisposable
    {
        /// <summary>
        /// Gets whether this instance still owns the lock.
        /// </summary>
        bool IsOwned { get; }

        /// <summary>
        /// Manually refreshes the lock expiration time.
        /// </summary>
        /// <returns>True if the lock was refreshed successfully, false if ownership was lost</returns>
        Task<bool> RefreshAsync();

        /// <summary>
        /// Manually releases the lock.
        /// </summary>
        /// <returns>True if the lock was released successfully</returns>
        Task<bool> ReleaseAsync();
    }
}

