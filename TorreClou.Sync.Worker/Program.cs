using Hangfire;
using Serilog;
using TorreClou.API.Extensions;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.Core.Options;
using TorreClou.Infrastructure.Extensions;
using TorreClou.Infrastructure.Filters;
using TorreClou.Infrastructure.Services;
using TorreClou.Infrastructure.Services.S3;
using TorreClou.Infrastructure.Settings;
using TorreClou.Sync.Worker;
using TorreClou.Sync.Worker.Services;

const string ServiceName = "torreclou-sync-worker";

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    Log.Information("Starting Sync Worker");
    var builder = Host.CreateApplicationBuilder(args);

    // 1. Shared Setup (Logging, DB, Redis, OpenTelemetry)
    builder.Configuration.ConfigureSharedSerilog(ServiceName, builder.Environment.EnvironmentName);
    builder.Services.AddSerilog();

    builder.Services.AddSharedDatabase(builder.Configuration);
    builder.Services.AddSharedRedis(builder.Configuration);
    builder.Services.AddTorreClouOpenTelemetry(ServiceName, builder.Configuration, builder.Environment);

    // 2. Hangfire Setup (Server Mode)
    // Register Base Hangfire (Storage) + Custom Filter logic if needed
    builder.Services.AddSharedHangfireBase(builder.Configuration, (config) =>
    {
        // Ideally, move complex filter registration to a standardized IConfigureOptions
        // For now, this callback pattern works if the extension supports it, 
        // otherwise register the filter globally after build or use the manual add below.
    });

    // Manually add the filter to Global Filters (safest approach for DI filters)
    GlobalJobFilters.Filters.Add(new JobStateSyncFilter(
        builder.Services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        new LoggerFactory().CreateLogger<JobStateSyncFilter>()
    ));

    // Register Server for specific queues
    builder.Services.AddSharedHangfireServer(queues: ["sync", "default"]);

    // 3. Worker Specific Services
    builder.Services.Configure<BackblazeSettings>(builder.Configuration.GetSection("Backblaze"));

    // S3 & Job Services
    builder.Services.AddScoped<IS3ResumableUploadService, S3ResumableUploadService>();
    builder.Services.AddScoped<IS3SyncJob, S3SyncJob>(); // Use Interface for Hangfire Dashboard support

    // 4. Hosted Services
    builder.Services.Configure<JobHealthMonitorOptions>(opts =>
    {
        opts.CheckInterval = TimeSpan.FromMinutes(2);
        opts.StaleJobThreshold = TimeSpan.FromMinutes(5);
    });

    builder.Services.AddHostedService<JobHealthMonitor<UserJob>>();
    builder.Services.AddHostedService<SyncWorker>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Sync Worker terminated");
}
finally
{
    Log.CloseAndFlush();
}