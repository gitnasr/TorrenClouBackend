using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;
using StackExchange.Redis;
using Hangfire;
using Hangfire.PostgreSql;
using TorreClou.Infrastructure.Data;
using TorreClou.Infrastructure.Repositories;
using TorreClou.Infrastructure.Interceptors;
using TorreClou.Infrastructure.Services.Redis;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Extensions
{
    public static class SharedConfigurationExtensions
    {
        // 1. Centralized Serilog Configuration
        public static void ConfigureSharedSerilog(this IConfiguration configuration, string serviceName, string environment)
        {
            var lokiUrl = configuration["Observability:LokiUrl"] ?? Environment.GetEnvironmentVariable("LOKI_URL") ?? "http://localhost:3100";
            var lokiUser = configuration["Observability:LokiUsername"] ?? Environment.GetEnvironmentVariable("LOKI_USERNAME");
            var lokiKey = configuration["Observability:LokiApiKey"] ?? Environment.GetEnvironmentVariable("LOKI_API_KEY");

            var loggerConfig = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("service", serviceName)
                .Enrich.WithProperty("environment", environment)
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");

            if (!string.IsNullOrEmpty(lokiUrl) && !lokiUrl.Contains("localhost"))
            {
                var credentials = !string.IsNullOrEmpty(lokiUser)
                    ? new LokiCredentials { Login = lokiUser, Password = lokiKey }
                    : null;
                loggerConfig.WriteTo.GrafanaLoki(lokiUrl, credentials: credentials);
            }

            Log.Logger = loggerConfig.CreateLogger();
        }

        // 2. Database & Repositories
        public static IServiceCollection AddSharedDatabase(this IServiceCollection services, IConfiguration config)
        {
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                var interceptor = new UpdateAuditableEntitiesInterceptor();
                options.UseNpgsql(config.GetConnectionString("DefaultConnection"))
                       .AddInterceptors(interceptor);
            });

            services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            return services;
        }

        // 3. Redis Setup
        public static IServiceCollection AddSharedRedis(this IServiceCollection services, IConfiguration config)
        {
            var redisConn = config.GetSection("Redis:ConnectionString").Value ?? "localhost:6379";
            services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConn));

            services.AddSingleton<IRedisCacheService, RedisCacheService>();
            services.AddScoped<IRedisLockService, RedisLockService>();
            services.AddSingleton<IRedisStreamService, RedisStreamService>();

            return services;
        }

        // 4. Hangfire Base (Storage Only)
        public static IServiceCollection AddSharedHangfireBase(this IServiceCollection services, IConfiguration config, Action<IGlobalConfiguration>? extraConfig = null)
        {
            var connString = config.GetConnectionString("DefaultConnection");

            services.AddHangfire((provider, hfConfig) =>
            {
                hfConfig.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                        .UseSimpleAssemblyNameTypeSerializer()
                        .UseRecommendedSerializerSettings()
                        .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connString));

                // Allow passing extra filters (like JobStateSyncFilter)
                extraConfig?.Invoke(hfConfig);
            });

            return services;
        }

        // 5. Hangfire Server (For Workers)
        public static IServiceCollection AddSharedHangfireServer(this IServiceCollection services, string[] queues)
        {
            services.AddHangfireServer(options =>
            {
                options.WorkerCount = Environment.ProcessorCount * 5;
                options.ServerTimeout = TimeSpan.FromSeconds(45);
                options.HeartbeatInterval = TimeSpan.FromSeconds(15);
                options.SchedulePollingInterval = TimeSpan.FromSeconds(10);
                options.Queues = queues;
            });
            return services;
        }
    }
}