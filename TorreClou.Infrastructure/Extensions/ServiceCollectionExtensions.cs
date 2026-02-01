using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Data;
using TorreClou.Infrastructure.Interceptors;
using TorreClou.Infrastructure.Repositories;
using TorreClou.Infrastructure.Services;
using TorreClou.Infrastructure.Settings;
using TorreClou.Core.Options;
using TorreClou.Application.Services.Google_Drive;
using TorreClou.Infrastructure.Services.Redis;
using TorreClou.Infrastructure.Services.Drive;
using TorreClou.Infrastructure.Services.Handlers;

namespace TorreClou.Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddSingleton<UpdateAuditableEntitiesInterceptor>();

            // Redis
            var redisSettings = configuration.GetSection("Redis").Get<RedisSettings>() ?? new RedisSettings();
            services.AddSingleton<IConnectionMultiplexer>(
                ConnectionMultiplexer.Connect(redisSettings.ConnectionString)
            );

            // Redis Services
            services.AddSingleton<IRedisCacheService, RedisCacheService>();
            services.AddScoped<IRedisLockService, RedisLockService>();
            services.AddSingleton<IRedisStreamService, RedisStreamService>();

            services.AddScoped<ITokenService, TokenService>();

            // Google Drive Services
            services.Configure<GoogleDriveSettings>(configuration.GetSection("GoogleDrive"));
            services.AddScoped<IGoogleDriveJobService, GoogleDriveJobService>();
            services.AddScoped<IGoogleDriveService, GoogleDriveService>();

            // Upload Progress Context (scoped per Hangfire job)
            services.AddScoped<IUploadProgressContext, UploadProgressContext>();

            // Transfer Speed Metrics (singleton for metrics collection)
            services.AddSingleton<ITransferSpeedMetrics, TransferSpeedMetrics>();

            // Job Status Service (timeline tracking)
            services.AddScoped<IJobStatusService, JobStatusService>();

            // Job Handlers (Strategy Pattern for decoupled job processing)
            // Storage Provider Handlers
            services.AddScoped<IStorageProviderHandler, GoogleDriveStorageProviderHandler>();
            services.AddScoped<IStorageProviderHandler, S3StorageProviderHandler>();
            
            // Job Type Handlers
            services.AddScoped<IJobTypeHandler, TorrentJobTypeHandler>();
            
            // Job Cancellation Handlers
            services.AddScoped<IJobCancellationHandler, TorrentCancellationHandler>();
            
            // Job Handler Factory
            services.AddScoped<IJobHandlerFactory, JobHandlerFactory>();

            services.AddDbContext<ApplicationDbContext>((sp, options) =>
            {
                var interceptor = sp.GetRequiredService<UpdateAuditableEntitiesInterceptor>();
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"))
                       .AddInterceptors(interceptor);
            });

            return services;
        }
    }
}
