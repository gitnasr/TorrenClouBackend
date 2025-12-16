using StackExchange.Redis;
using TorreClou.GoogleDrive.Worker;
using TorreClou.GoogleDrive.Worker.Services;
using Microsoft.EntityFrameworkCore;
using TorreClou.Infrastructure.Data;
using TorreClou.Infrastructure.Repositories;
using TorreClou.Infrastructure.Extensions;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Interceptors;
using TorreClou.Infrastructure.Services;
using TorreClou.Core.Options;
using Hangfire;
using Hangfire.PostgreSql;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;

const string ServiceName = "torreclou-googledrive-worker";

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Google Drive worker application");

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

// HttpClient for Google Drive API
builder.Services.AddHttpClient();

// Hangfire configuration
var hangfireConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddHangfire((provider, config) => config
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(hangfireConnectionString))
);

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Environment.ProcessorCount * 5;

    // Fast recovery settings
    options.ServerTimeout = TimeSpan.FromSeconds(45);
    options.HeartbeatInterval = TimeSpan.FromSeconds(15);
    options.SchedulePollingInterval = TimeSpan.FromSeconds(10);

    options.Queues = ["googledrive", "default"];
});

// Register Job Services (Hangfire jobs)
builder.Services.AddScoped<GoogleDriveUploadJob>();

// Redis configuration
var redisConnectionString = builder.Configuration.GetSection("Redis:ConnectionString").Value ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnectionString)
);

// Google Drive Services
builder.Services.Configure<GoogleDriveSettings>(builder.Configuration.GetSection("GoogleDrive"));
builder.Services.AddScoped<IGoogleDriveJob, GoogleDriveJobService>();

// Upload Progress Context (scoped per job for progress tracking and resume support)
builder.Services.AddScoped<IUploadProgressContext, UploadProgressContext>();

// Background Services
// Google Drive Worker - Redis stream consumer for upload jobs
builder.Services.AddHostedService<GoogleDriveWorker>();

var host = builder.Build();
host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Google Drive worker application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

