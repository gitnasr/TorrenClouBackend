using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Services;
using TorreClou.Application.Services.Google_Drive;
using TorreClou.Infrastructure.Services.Drive;
using TorreClou.Infrastructure.Services.Handlers;

namespace TorreClou.Infrastructure.Extensions
{
    /// <summary>
    /// Registers API-specific infrastructure services.
    /// Database, Redis, and Repository registrations are handled by
    /// AddSharedDatabase() and AddSharedRedis() in SharedConfigurationExtensions.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<ITokenService, TokenService>();

            // Google Drive Services (credentials configured per-user via API)
            services.AddScoped<IGoogleDriveJobService, GoogleDriveJobService>();
            services.AddScoped<IGoogleDriveService, GoogleDriveService>();

            // Upload Progress Context (scoped per Hangfire job)
            services.AddScoped<IUploadProgressContext, UploadProgressContext>();

            // Transfer Speed Metrics (singleton for metrics collection)
            services.AddSingleton<ITransferSpeedMetrics, TransferSpeedMetrics>();

            // Job Status Service (timeline tracking)
            services.AddScoped<IJobStatusService, JobStatusService>();

            // Health Check Service
            services.AddScoped<IHealthCheckService, HealthCheckService>();

            // Google API Client (for OAuth token exchange and user info)
            services.AddScoped<IGoogleApiClient, GoogleApiClient>();

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

            return services;
        }
    }
}
