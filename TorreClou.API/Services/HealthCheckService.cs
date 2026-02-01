using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;
using TorreClou.Infrastructure.Data;

namespace TorreClou.API.Services
{
    public interface IHealthCheckService
    {
        Task<HealthStatus> GetCachedHealthStatusAsync();
        Task<DetailedHealthStatus> GetDetailedHealthStatusAsync(CancellationToken ct = default);
    }

    public class HealthCheckService : IHealthCheckService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ApplicationDbContext _dbContext;
        private readonly IMemoryCache _cache;
        private readonly ILogger<HealthCheckService> _logger;

        private const string HEALTH_CACHE_KEY = "system_health_status";
        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan HEALTH_CHECK_TIMEOUT = TimeSpan.FromSeconds(5);

        public HealthCheckService(
            IConnectionMultiplexer redis,
            ApplicationDbContext dbContext,
            IMemoryCache cache,
            ILogger<HealthCheckService> logger)
        {
            _redis = redis;
            _dbContext = dbContext;
            _cache = cache;
            _logger = logger;
        }

        public async Task<HealthStatus> GetCachedHealthStatusAsync()
        {
            return await _cache.GetOrCreateAsync(HEALTH_CACHE_KEY, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CACHE_DURATION;
                return await CheckHealthAsync(CancellationToken.None);
            }) ?? new HealthStatus();
        }

        private async Task<HealthStatus> CheckHealthAsync(CancellationToken ct)
        {
            var status = new HealthStatus
            {
                Timestamp = DateTime.UtcNow,
                Version = GetVersion()
            };

            // Database check with timeout
            using var dbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            dbCts.CancelAfter(HEALTH_CHECK_TIMEOUT);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                await _dbContext.Database.CanConnectAsync(dbCts.Token);
                stopwatch.Stop();

                status.Database = "healthy";
                status.DatabaseResponseTimeMs = (int)stopwatch.ElapsedMilliseconds;
            }
            catch (OperationCanceledException)
            {
                status.Database = "timeout";
                status.IsHealthy = false;
                _logger.LogWarning("Database health check timed out after {Timeout}ms", HEALTH_CHECK_TIMEOUT.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                status.Database = "unhealthy";
                status.IsHealthy = false;
                _logger.LogError(ex, "Database health check failed");
            }

            // Redis check with timeout
            using var redisCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            redisCts.CancelAfter(HEALTH_CHECK_TIMEOUT);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                await _redis.GetDatabase().PingAsync();
                stopwatch.Stop();

                status.Redis = "healthy";
                status.RedisResponseTimeMs = (int)stopwatch.ElapsedMilliseconds;
            }
            catch (OperationCanceledException)
            {
                status.Redis = "timeout";
                status.IsHealthy = false;
                _logger.LogWarning("Redis health check timed out after {Timeout}ms", HEALTH_CHECK_TIMEOUT.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                status.Redis = "unhealthy";
                status.IsHealthy = false;
                _logger.LogError(ex, "Redis health check failed");
            }

            return status;
        }

        public async Task<DetailedHealthStatus> GetDetailedHealthStatusAsync(CancellationToken ct = default)
        {
            // Detailed checks for debugging (not cached) - includes all the expensive operations
            var detailed = new DetailedHealthStatus
            {
                Timestamp = DateTime.UtcNow,
                Version = GetVersion()
            };

            // Basic health checks
            using var dbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            dbCts.CancelAfter(HEALTH_CHECK_TIMEOUT);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var canConnect = await _dbContext.Database.CanConnectAsync(dbCts.Token);
                stopwatch.Stop();

                detailed.Database = canConnect ? "healthy" : "unhealthy";
                detailed.DatabaseResponseTimeMs = (int)stopwatch.ElapsedMilliseconds;
                detailed.IsHealthy = canConnect;

                // Check for pending migrations (expensive operation)
                if (canConnect)
                {
                    try
                    {
                        var pendingMigrations = await _dbContext.Database.GetPendingMigrationsAsync(ct);
                        detailed.PendingMigrations = pendingMigrations.Count();
                        if (detailed.PendingMigrations > 0)
                        {
                            detailed.Warnings.Add($"{detailed.PendingMigrations} pending database migrations");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to check pending migrations");
                        detailed.Warnings.Add("Could not check pending migrations");
                    }
                }
            }
            catch (Exception ex)
            {
                detailed.Database = "unhealthy";
                detailed.IsHealthy = false;
                _logger.LogError(ex, "Detailed database health check failed");
            }

            // Redis check
            using var redisCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            redisCts.CancelAfter(HEALTH_CHECK_TIMEOUT);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                await _redis.GetDatabase().PingAsync();
                stopwatch.Stop();

                detailed.Redis = "healthy";
                detailed.RedisResponseTimeMs = (int)stopwatch.ElapsedMilliseconds;
            }
            catch (Exception ex)
            {
                detailed.Redis = "unhealthy";
                detailed.IsHealthy = false;
                _logger.LogError(ex, "Detailed Redis health check failed");
            }

            // Storage info
            try
            {
                var downloadPath = "/app/downloads";
                if (!Directory.Exists(downloadPath))
                {
                    downloadPath = Directory.GetCurrentDirectory();
                }

                var driveInfo = new DriveInfo(Path.GetPathRoot(downloadPath) ?? downloadPath);
                detailed.Storage = new StorageInfo
                {
                    TotalBytes = driveInfo.TotalSize,
                    UsedBytes = driveInfo.TotalSize - driveInfo.AvailableFreeSpace,
                    AvailableBytes = driveInfo.AvailableFreeSpace,
                    UsagePercent = (double)(driveInfo.TotalSize - driveInfo.AvailableFreeSpace) / driveInfo.TotalSize * 100
                };

                if (detailed.Storage.UsagePercent >= 90)
                {
                    detailed.Warnings.Add($"Storage usage is high: {detailed.Storage.UsagePercent:F1}%");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get storage info");
                detailed.Warnings.Add("Could not retrieve storage information");
            }

            return detailed;
        }

        private string GetVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "1.0.0";
            }
            catch
            {
                return "1.0.0";
            }
        }
    }

    public class HealthStatus
    {
        public DateTime Timestamp { get; set; }
        public bool IsHealthy { get; set; } = true;
        public string Database { get; set; } = "unknown";
        public string Redis { get; set; } = "unknown";
        public string Version { get; set; } = "1.0.0";
        public int? DatabaseResponseTimeMs { get; set; }
        public int? RedisResponseTimeMs { get; set; }
    }

    public class DetailedHealthStatus : HealthStatus
    {
        public int PendingMigrations { get; set; }
        public StorageInfo Storage { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class StorageInfo
    {
        public long TotalBytes { get; set; }
        public long UsedBytes { get; set; }
        public long AvailableBytes { get; set; }
        public double UsagePercent { get; set; }
    }
}
