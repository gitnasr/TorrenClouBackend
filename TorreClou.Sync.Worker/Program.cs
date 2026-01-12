using Hangfire;
using Microsoft.Extensions.Hosting;
using Serilog;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.Core.Options;
using TorreClou.Infrastructure.Extensions;
using TorreClou.Infrastructure.Filters;
using TorreClou.Infrastructure.Services.S3;
using TorreClou.Infrastructure.Settings;
using TorreClou.Sync.Worker;
using TorreClou.Sync.Worker.Services;

const string ServiceName = "torreclou-sync-worker";

// Bootstrap logger
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Configure Serilog
    builder.Configuration.ConfigureSharedSerilog(ServiceName, builder.Environment.EnvironmentName);
    builder.Services.AddSerilog();

    Log.Information("Starting {ServiceName}", ServiceName);

    // Infrastructure
    builder.Services.AddSharedDatabase(builder.Configuration);
    builder.Services.AddSharedRedis(builder.Configuration);
    builder.Services.AddTorreClouOpenTelemetry(ServiceName, builder.Configuration, builder.Environment);

    // Hangfire
    builder.Services.AddSharedHangfireBase(builder.Configuration);
    GlobalJobFilters.Filters.Add(new JobStateSyncFilter(
        builder.Services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        new LoggerFactory().CreateLogger<JobStateSyncFilter>()
    ));
    builder.Services.AddSharedHangfireServer(queues: ["sync", "default"]);

    // Worker Services
    builder.Services.Configure<BackblazeSettings>(builder.Configuration.GetSection("Backblaze"));
    builder.Services.AddScoped<IS3ResumableUploadService, S3ResumableUploadService>();
    builder.Services.AddScoped<IS3SyncJob, S3SyncJob>();
    builder.Services.AddScoped<IJobStatusService, TorreClou.Infrastructure.Services.JobStatusService>();

    // Hosted Services
    builder.Services.Configure<JobHealthMonitorOptions>(opts =>
    {
        opts.CheckInterval = TimeSpan.FromMinutes(2);
        opts.StaleJobThreshold = TimeSpan.FromMinutes(5);
    });
    builder.Services.AddHostedService<SyncRecoveryService>();
    builder.Services.AddHostedService<SyncWorker>();

    // Configure host shutdown timeout to allow Hangfire graceful shutdown
    builder.Services.Configure<HostOptions>(opts => 
    {
        opts.ShutdownTimeout = TimeSpan.FromMinutes(6); // Longer than Hangfire's ServerTimeout
    });

    var host = builder.Build();
    
    Log.Information("{ServiceName} started successfully", ServiceName);
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Sync Worker terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
