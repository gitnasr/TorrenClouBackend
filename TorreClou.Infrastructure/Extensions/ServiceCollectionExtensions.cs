using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TorreClou.Application.Services.Torrent;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Data;
using TorreClou.Infrastructure.Interceptors;
using TorreClou.Infrastructure.Repositories;
using TorreClou.Infrastructure.Services;
using TorreClou.Infrastructure.Settings;
using TorreClou.Core.Options;

namespace TorreClou.Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddSingleton<UpdateAuditableEntitiesInterceptor>();

            // Coinremitter payment gateway
            services.AddHttpClient<IPaymentGateway, CoinremitterService>();
            services.Configure<CoinremitterSettings>(configuration.GetSection("Coinremitter"));

            // Backblaze B2 Storage
            services.Configure<BackblazeSettings>(configuration.GetSection("Backblaze"));
            services.AddSingleton<IBlobStorageService, BackblazeStorageService>();

            // Redis
            var redisSettings = configuration.GetSection("Redis").Get<RedisSettings>() ?? new RedisSettings();
            services.AddSingleton<IConnectionMultiplexer>(
                ConnectionMultiplexer.Connect(redisSettings.ConnectionString)
            );

            services.AddScoped<ITokenService, TokenService>();

            // Google Drive Services
            services.Configure<GoogleDriveSettings>(configuration.GetSection("GoogleDrive"));
            services.AddScoped<IGoogleDriveService, GoogleDriveService>();
            
            // Upload Progress Context (scoped per Hangfire job)
            services.AddScoped<IUploadProgressContext, UploadProgressContext>();

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
