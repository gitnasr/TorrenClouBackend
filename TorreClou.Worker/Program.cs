using StackExchange.Redis;
using TorreClou.Worker;
using TorreClou.Worker.Services;
using TorreClou.Worker.Services.Strategies;
using Microsoft.EntityFrameworkCore;
using TorreClou.Infrastructure.Data;
using TorreClou.Infrastructure.Repositories;
using TorreClou.Infrastructure.Services;
using TorreClou.Infrastructure.Extensions;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Options;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Infrastructure.Interceptors;
using Hangfire;
using Hangfire.PostgreSql;
using TorreClou.Infrastructure.Filters;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;

const string ServiceName = "torreclou-worker";

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting worker application");

var builder = Host.CreateApplicationBuilder(args);

    // Configure Serilog with Loki
    var lokiUrl = builder.Configuration["Observability:LokiUrl"] ?? 
                  Environment.GetEnvironmentVariable("LOKI_URL") ?? 
                  "http://localhost:3100";
    var lokiUsername = builder.Configuration["Observability:LokiUsername"] ?? 
                       Environment.GetEnvironmentVariable("LOKI_USERNAME");
    var lokiApiKey = builder.Configuration["Observability:LokiApiKey"] ?? 
                     Environment.GetEnvironmentVariable("LOKI_API_KEY");
    
    var loggerConfig = new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithProperty("service", ServiceName)
        .Enrich.WithProperty("environment", builder.Environment.EnvironmentName)
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
    
    // Add Loki sink if URL is configured
    if (!string.IsNullOrEmpty(lokiUrl) && !lokiUrl.Equals("http://localhost:3100", StringComparison.OrdinalIgnoreCase))
    {
        if (!string.IsNullOrEmpty(lokiUsername) && !string.IsNullOrEmpty(lokiApiKey))
        {
            var credentials = new LokiCredentials
            {
                Login = lokiUsername,
                Password = lokiApiKey
            };
            loggerConfig = loggerConfig.WriteTo.GrafanaLoki(lokiUrl, credentials: credentials);
        }
        else
        {
            loggerConfig = loggerConfig.WriteTo.GrafanaLoki(lokiUrl);
        }
    }
    
    Log.Logger = loggerConfig.CreateLogger();
    
    builder.Services.AddSerilog();

    // Add OpenTelemetry
    builder.Services.AddTorreClouOpenTelemetry(ServiceName, builder.Configuration, builder.Environment);

// Database configuration
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var interceptor = new UpdateAuditableEntitiesInterceptor();
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
           .AddInterceptors(interceptor);
});

// Repository and UnitOfWork
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Backblaze Settings
builder.Services.Configure<TorreClou.Infrastructure.Settings.BackblazeSettings>(
    builder.Configuration.GetSection("Backblaze"));

// HttpClient for downloading torrent files
builder.Services.AddHttpClient();

// Hangfire configuration
var hangfireConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddHangfire((provider, config) => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(hangfireConnectionString))
    .UseFilter(new JobStateSyncFilter(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<ILogger<JobStateSyncFilter>>()
    ))
);

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Environment.ProcessorCount * 5;

    // Fast recovery settings
    options.ServerTimeout = TimeSpan.FromSeconds(45);
    options.HeartbeatInterval = TimeSpan.FromSeconds(15);
    options.SchedulePollingInterval = TimeSpan.FromSeconds(10);

    options.Queues = ["torrents", "default"];
});

// Register Job Services (Hangfire jobs)
builder.Services.AddScoped<TorrentDownloadJob>();
    builder.Services.AddScoped<ITransferSpeedMetrics,TransferSpeedMetrics>();

// Redis configuration
var redisConnectionString = builder.Configuration.GetSection("Redis:ConnectionString").Value ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnectionString)
);

// Job Health Monitor Configuration
builder.Services.Configure<JobHealthMonitorOptions>(options =>
{
    options.CheckInterval = TimeSpan.FromMinutes(2);
    options.StaleJobThreshold = TimeSpan.FromMinutes(5);
});

// Recovery Strategies (for JobHealthMonitor)
builder.Services.AddSingleton<IJobRecoveryStrategy, TorrentRecoveryStrategy>();

// Background Services

// 1. Job Health Monitor - Generic monitoring and recovery of orphaned jobs (from Infrastructure)
builder.Services.AddHostedService<JobHealthMonitor<UserJob>>();

// 2. Torrent Worker - Redis stream consumer for new job events
builder.Services.AddHostedService<TorrentWorker>();

var host = builder.Build();
host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
