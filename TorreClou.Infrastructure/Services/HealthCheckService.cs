using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TorreClou.Core.DTOs.Common;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Data;

namespace TorreClou.Infrastructure.Services
{
    public class HealthCheckService(
        IUnitOfWork unitOfWork,
        IRedisCacheService redisCache,
        ApplicationDbContext dbContext,
        IMemoryCache cache,
        ILogger<HealthCheckService> logger) : IHealthCheckService
    {
        private const string HealthCacheKey = "system_health_status";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);

        public async Task<HealthStatus> GetCachedHealthStatusAsync()
        {
            return await cache.GetOrCreateAsync(HealthCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
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

            // Database check via IUnitOfWork
            using var dbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            dbCts.CancelAfter(HealthCheckTimeout);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var canConnect = await unitOfWork.CanConnectAsync(dbCts.Token);
                stopwatch.Stop();

                status.DatabaseResponseTimeMs = (int)stopwatch.ElapsedMilliseconds;

                if (canConnect)
                {
                    status.Database = "healthy";
                }
                else
                {
                    status.Database = "unhealthy";
                    status.IsHealthy = false;
                    logger.LogWarning("Database health check returned false (unable to connect). Response time: {ResponseTimeMs}ms", stopwatch.ElapsedMilliseconds);
                }
            }
            catch (OperationCanceledException)
            {
                status.Database = "timeout";
                status.IsHealthy = false;
                logger.LogWarning("Database health check timed out after {Timeout}ms", HealthCheckTimeout.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                status.Database = "unhealthy";
                status.IsHealthy = false;
                logger.LogError(ex, "Database health check failed");
            }

            // Redis check via IRedisCacheService (with timeout enforced via Task.WhenAny)
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var redisTask = redisCache.ExistsAsync("health:ping");
                var delayTask = Task.Delay(HealthCheckTimeout, ct);

                var completedTask = await Task.WhenAny(redisTask, delayTask);
                stopwatch.Stop();

                if (completedTask == delayTask)
                {
                    status.Redis = "timeout";
                    status.IsHealthy = false;
                    logger.LogWarning("Redis health check timed out after {Timeout}ms", HealthCheckTimeout.TotalMilliseconds);
                }
                else
                {
                    await redisTask;
                    status.Redis = "healthy";
                    status.RedisResponseTimeMs = (int)stopwatch.ElapsedMilliseconds;
                }
            }
            catch (OperationCanceledException)
            {
                status.Redis = "timeout";
                status.IsHealthy = false;
                logger.LogWarning("Redis health check cancelled");
            }
            catch (Exception ex)
            {
                status.Redis = "unhealthy";
                status.IsHealthy = false;
                logger.LogError(ex, "Redis health check failed");
            }

            return status;
        }

        public async Task<DetailedHealthStatus> GetDetailedHealthStatusAsync(CancellationToken ct = default)
        {
            var detailed = new DetailedHealthStatus
            {
                Timestamp = DateTime.UtcNow,
                Version = GetVersion()
            };

            // Database check via IUnitOfWork
            using var dbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            dbCts.CancelAfter(HealthCheckTimeout);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var canConnect = await unitOfWork.CanConnectAsync(dbCts.Token);
                stopwatch.Stop();

                detailed.Database = canConnect ? "healthy" : "unhealthy";
                detailed.DatabaseResponseTimeMs = (int)stopwatch.ElapsedMilliseconds;
                detailed.IsHealthy = canConnect;

                // Check for pending migrations (acceptable — Infra layer owns DbContext)
                if (canConnect)
                {
                    try
                    {
                        var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(ct);
                        detailed.PendingMigrations = pendingMigrations.Count();
                        if (detailed.PendingMigrations > 0)
                        {
                            detailed.Warnings.Add($"{detailed.PendingMigrations} pending database migrations");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to check pending migrations");
                        detailed.Warnings.Add("Could not check pending migrations");
                    }
                }
            }
            catch (Exception ex)
            {
                detailed.Database = "unhealthy";
                detailed.IsHealthy = false;
                logger.LogError(ex, "Detailed database health check failed");
            }

            // Redis check via IRedisCacheService (with timeout enforced via Task.WhenAny)
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var redisTask = redisCache.ExistsAsync("health:ping");
                var delayTask = Task.Delay(HealthCheckTimeout, ct);

                var completedTask = await Task.WhenAny(redisTask, delayTask);
                stopwatch.Stop();

                if (completedTask == delayTask)
                {
                    detailed.Redis = "timeout";
                    detailed.IsHealthy = false;
                    logger.LogWarning("Detailed Redis health check timed out after {Timeout}ms", HealthCheckTimeout.TotalMilliseconds);
                }
                else
                {
                    await redisTask;
                    detailed.Redis = "healthy";
                    detailed.RedisResponseTimeMs = (int)stopwatch.ElapsedMilliseconds;
                }
            }
            catch (Exception ex)
            {
                detailed.Redis = "unhealthy";
                detailed.IsHealthy = false;
                logger.LogError(ex, "Detailed Redis health check failed");
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
                logger.LogWarning(ex, "Failed to get storage info");
                detailed.Warnings.Add("Could not retrieve storage information");
            }

            return detailed;
        }

        private static string GetVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetEntryAssembly();
                var version = assembly?.GetName().Version;
                return version?.ToString() ?? "1.0.0";
            }
            catch
            {
                return "1.0.0";
            }
        }
    }
}
