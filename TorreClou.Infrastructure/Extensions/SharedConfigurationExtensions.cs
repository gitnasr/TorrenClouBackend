using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Debugging;
using Serilog.Sinks.Grafana.Loki;
using StackExchange.Redis;
using Hangfire;
using Hangfire.PostgreSql;
using TorreClou.Infrastructure.Data;
using TorreClou.Infrastructure.Repositories;
using TorreClou.Infrastructure.Interceptors;
using TorreClou.Infrastructure.Services.Redis;
using TorreClou.Core.Interfaces;
using System.Net.Http.Headers;
using System.Text;

namespace TorreClou.Infrastructure.Extensions
{
    public static class SharedConfigurationExtensions
    {
        /// <summary>
        /// Configures Serilog with Console and Grafana Loki sinks
        /// </summary>
        public static void ConfigureSharedSerilog(this IConfiguration configuration, string serviceName, string environment)
        {
            // Enable Serilog internal debugging
            SelfLog.Enable(msg => Console.WriteLine($"[SERILOG ERROR] {msg}"));
            
            var isDevelopment = environment?.Equals("Development", StringComparison.OrdinalIgnoreCase) == true;
            
            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Is(isDevelopment ? LogEventLevel.Debug : LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("Hangfire", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("service", serviceName)
                .Enrich.WithProperty("env", environment ?? "unknown")
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                );

            // Configure Loki sink
            var lokiUrl = configuration["Observability:LokiUrl"];
            var lokiUser = configuration["Observability:LokiUsername"];
            var lokiKey = configuration["Observability:LokiApiKey"];

            if (!string.IsNullOrEmpty(lokiUrl) && 
                !string.IsNullOrEmpty(lokiUser) && 
                !string.IsNullOrEmpty(lokiKey) &&
                !lokiUrl.Contains("localhost"))
            {
                // Test Loki connection first
                TestLokiConnection(lokiUrl, lokiUser, lokiKey, serviceName, environment).Wait();
                
                loggerConfig.WriteTo.GrafanaLoki(
                    lokiUrl,
                    credentials: new LokiCredentials { Login = lokiUser, Password = lokiKey },
                    labels: new[]
                    {
                        new LokiLabel { Key = "app", Value = "torreclou" },
                        new LokiLabel { Key = "service", Value = serviceName },
                        new LokiLabel { Key = "env", Value = environment ?? "unknown" }
                    },
                    propertiesAsLabels: new[] { "level" },
                    batchPostingLimit: 5,
                    period: TimeSpan.FromSeconds(1)
                );
            }

            Log.Logger = loggerConfig.CreateLogger();
        }
        
        /// <summary>
        /// Tests Loki connection by sending a test log directly via HTTP
        /// </summary>
        private static async Task TestLokiConnection(string lokiUrl, string lokiUser, string lokiKey, string serviceName, string? environment)
        {
            try
            {
                using var client = new HttpClient();
                
                // Set Basic Auth
                var authBytes = Encoding.UTF8.GetBytes($"{lokiUser}:{lokiKey}");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
                
                // Build the full Loki push URL (the sink adds this automatically)
                var pushUrl = lokiUrl.TrimEnd('/') + "/loki/api/v1/push";
                
                // Create test log payload
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000; // nanoseconds
                var payload = $$"""
                {
                    "streams": [{
                        "stream": {
                            "app": "torreclou",
                            "service": "{{serviceName}}",
                            "env": "{{environment ?? "unknown"}}",
                            "level": "Information"
                        },
                        "values": [
                            ["{{timestamp}}", "Loki connection test from {{serviceName}}"]
                        ]
                    }]
                }
                """;
                
                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(pushUrl, content);
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[LOKI] ✅ Test log sent successfully to {pushUrl}");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[LOKI] ❌ Failed: {response.StatusCode} - {error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOKI] ❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Configures Database and Repositories
        /// </summary>
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

        /// <summary>
        /// Configures Redis connection and services
        /// </summary>
        public static IServiceCollection AddSharedRedis(this IServiceCollection services, IConfiguration config)
        {
            var redisConn = config["Redis:ConnectionString"] ?? "localhost:6379";

            // Parse connection string and configure timeouts for cloud Redis
            var configurationOptions = ConfigurationOptions.Parse(redisConn);
            
            // Set timeout values suitable for cloud Redis (RedisLabs)
            configurationOptions.SyncTimeout = 15000; // 15 seconds for synchronous operations
            configurationOptions.AsyncTimeout = 15000; // 15 seconds for async operations like XREADGROUP
            configurationOptions.ConnectTimeout = 10000; // 10 seconds for initial connection
            
            // Enable keep-alive for better connection stability
            configurationOptions.KeepAlive = 60; // Send keep-alive every 60 seconds
            
            // Configure retry policy for transient failures
            configurationOptions.ReconnectRetryPolicy = new ExponentialRetry(1000); // Retry with exponential backoff starting at 1 second
            
            // Abort on connect fail to prevent hanging connections
            configurationOptions.AbortOnConnectFail = false;

            services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(configurationOptions));
            services.AddSingleton<IRedisCacheService, RedisCacheService>();
            services.AddScoped<IRedisLockService, RedisLockService>();
            services.AddSingleton<IRedisStreamService, RedisStreamService>();

            return services;
        }

        /// <summary>
        /// Configures Hangfire with PostgreSQL storage
        /// </summary>
        public static IServiceCollection AddSharedHangfireBase(this IServiceCollection services, IConfiguration config, Action<IGlobalConfiguration>? extraConfig = null)
        {
            var connString = config.GetConnectionString("DefaultConnection");

            services.AddHangfire((provider, hfConfig) =>
            {
                hfConfig.UseSimpleAssemblyNameTypeSerializer()
                        .UseRecommendedSerializerSettings()
                        .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connString), new()
                        {
                            InvisibilityTimeout = TimeSpan.FromHours(24),
                            QueuePollInterval = TimeSpan.FromSeconds(1)
                        });

                extraConfig?.Invoke(hfConfig);
            });

            return services;
        }

        /// <summary>
        /// Configures Hangfire Server for Workers
        /// </summary>
        public static IServiceCollection AddSharedHangfireServer(this IServiceCollection services, string[] queues)
        {
            services.AddHangfireServer(options =>
            {
                options.WorkerCount = 50; // Reduced from 100 to reduce Redis connection contention
                options.ServerTimeout = TimeSpan.FromMinutes(2); // Reduced from 5 minutes
                options.HeartbeatInterval = TimeSpan.FromSeconds(30);
                options.SchedulePollingInterval = TimeSpan.FromSeconds(10);
                options.Queues = queues;
            });
            return services;
        }
    }
}
