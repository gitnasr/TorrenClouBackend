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

namespace TorreClou.Infrastructure.Extensions
{
    public static class SharedConfigurationExtensions
    {
        // 1. Centralized Serilog Configuration
        public static void ConfigureSharedSerilog(this IConfiguration configuration, string serviceName, string environment)
        {
            var lokiUrl = configuration["Observability:LokiUrl"] ?? Environment.GetEnvironmentVariable("LOKI_URL") ?? "http://localhost:3100";
            var lokiUser = configuration["Observability:LokiUsername"] ?? Environment.GetEnvironmentVariable("LOKI_USERNAME");
            var lokiKey = configuration["Observability:LokiApiKey"] ?? Environment.GetEnvironmentVariable("LOKI_API_KEY");

            var isDevelopment = environment?.Equals("Development", StringComparison.OrdinalIgnoreCase) == true;
            
            // Start with configuration (which includes Console sink), then override levels for development
            var loggerConfig = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .MinimumLevel.Is(isDevelopment ? LogEventLevel.Debug : LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft", isDevelopment ? LogEventLevel.Information : LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", isDevelopment ? LogEventLevel.Information : LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", isDevelopment ? LogEventLevel.Information : LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore.Routing", isDevelopment ? LogEventLevel.Information : LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("service", serviceName)
                .Enrich.WithProperty("environment", environment);
            
            // Note: Console sink is already configured in appsettings, so we don't add it again here

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
          
            Console.WriteLine($"[Redis] Connecting to: {redisConn}");

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
                hfConfig  .UseSimpleAssemblyNameTypeSerializer()
                        .UseRecommendedSerializerSettings()
                        .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connString), new()
                        {
                            InvisibilityTimeout = TimeSpan.FromHours(24),
                            QueuePollInterval = TimeSpan.FromSeconds(1)
                        });

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
                options.ServerTimeout = TimeSpan.FromMinutes(5);
                options.HeartbeatInterval = TimeSpan.FromSeconds(30);
                options.SchedulePollingInterval = TimeSpan.FromSeconds(10);
                options.Queues = queues;
            });
            return services;
        }
    }
}