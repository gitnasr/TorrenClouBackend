namespace TorreClou.Core.Interfaces
{
    /// <summary>
    /// Service for distributed locking using Redis.
    /// Provides automatic lock refresh to prevent expiration during long-running operations.
    /// </summary>
    public interface IRedisLockService
    {
        /// <summary>
        /// Attempts to acquire a distributed lock.
        /// </summary>
        /// <param name="lockKey">The unique key for the lock</param>
        /// <param name="expiry">The lock expiration time. Lock will auto-refresh at half this interval.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An IRedisLock instance if acquired successfully, null if lock is already held by another instance</returns>
        Task<IRedisLock?> AcquireLockAsync(string lockKey, TimeSpan expiry, CancellationToken cancellationToken = default);
    }
}

