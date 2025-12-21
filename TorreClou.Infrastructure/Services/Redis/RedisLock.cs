using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TorreClou.Core.Interfaces;

namespace TorreClou.Infrastructure.Services.Redis
{
    /// <summary>
    /// Internal implementation of IRedisLock with automatic refresh mechanism.
    /// </summary>
    internal class RedisLock : IRedisLock
    {
        private readonly IDatabase _database;
        private readonly string _lockKey;
        private readonly string _lockValue;
        private readonly TimeSpan _expiry;
        private readonly TimeSpan _refreshInterval;
        private readonly ILogger<RedisLock> _logger;
        private readonly CancellationTokenSource _refreshCts;
        private readonly Task _refreshTask;
        private bool _isOwned;
        private bool _disposed;

        public bool IsOwned => _isOwned && !_disposed;

        public RedisLock(
            IDatabase database,
            string lockKey,
            string lockValue,
            TimeSpan expiry,
            ILogger<RedisLock> logger)
        {
            _database = database;
            _lockKey = lockKey;
            _lockValue = lockValue;
            _expiry = expiry;
            _refreshInterval = TimeSpan.FromMilliseconds(expiry.TotalMilliseconds / 2); // Refresh at half of expiry
            _logger = logger;
            _isOwned = true;
            _refreshCts = new CancellationTokenSource();

            // Start background task to automatically refresh the lock
            _refreshTask = Task.Run(RefreshLoopAsync, _refreshCts.Token);

            _logger.LogDebug("RedisLock created | Key: {Key} | Expiry: {Expiry} | RefreshInterval: {RefreshInterval}",
                _lockKey, _expiry, _refreshInterval);
        }

        public async Task<bool> RefreshAsync()
        {
            if (_disposed || !_isOwned)
            {
                return false;
            }

            try
            {
                // Check if we still own the lock before refreshing
                var currentValue = await _database.StringGetAsync(_lockKey);
                if (currentValue.HasValue && currentValue.ToString() == _lockValue)
                {
                    // Refresh the lock by setting it again with the same value
                    await _database.StringSetAsync(_lockKey, _lockValue, _expiry);
                    _logger.LogDebug("RedisLock refreshed | Key: {Key}", _lockKey);
                    return true;
                }
                else
                {
                    _logger.LogWarning("RedisLock refresh failed - ownership lost | Key: {Key}", _lockKey);
                    _isOwned = false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing RedisLock | Key: {Key}", _lockKey);
                _isOwned = false;
                return false;
            }
        }

        public async Task<bool> ReleaseAsync()
        {
            if (_disposed)
            {
                return false;
            }

            try
            {
                // Stop the refresh task
                _refreshCts.Cancel();
                try
                {
                    await _refreshTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }

                // Release the lock (only if we still own it)
                var currentValue = await _database.StringGetAsync(_lockKey);
                if (currentValue.HasValue && currentValue.ToString() == _lockValue)
                {
                    await _database.KeyDeleteAsync(_lockKey);
                    _isOwned = false;
                    _logger.LogDebug("RedisLock released | Key: {Key}", _lockKey);
                    return true;
                }
                else
                {
                    _logger.LogDebug("RedisLock already expired or released | Key: {Key}", _lockKey);
                    _isOwned = false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error releasing RedisLock | Key: {Key}", _lockKey);
                _isOwned = false;
                return false;
            }
        }

        private async Task RefreshLoopAsync()
        {
            try
            {
                while (!_refreshCts.Token.IsCancellationRequested && _isOwned)
                {
                    await Task.Delay(_refreshInterval, _refreshCts.Token);
                    
                    if (!_refreshCts.Token.IsCancellationRequested)
                    {
                        await RefreshAsync();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when lock is released
                _logger.LogDebug("RedisLock refresh loop cancelled | Key: {Key}", _lockKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RedisLock refresh loop | Key: {Key}", _lockKey);
                _isOwned = false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseAsync().GetAwaiter().GetResult();
            _refreshCts.Dispose();
        }
    }
}

