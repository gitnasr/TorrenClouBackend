using Hangfire;
using Microsoft.Extensions.Hosting;
using Serilog;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.Core.Options;
using TorreClou.Infrastructure.Extensions;
using TorreClou.Infrastructure.Filters;
using TorreClou.Infrastructure.Services;
using TorreClou.Worker;
using TorreClou.Worker.Services;

const string ServiceName = "torreclou-worker";

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
    builder.Services.AddSharedHangfireServer(queues: ["torrents", "default"]);

    // Worker Services
    builder.Services.AddHttpClient();
    builder.Services.AddScoped<ITorrentDownloadJob, TorrentDownloadJob>();
    builder.Services.AddScoped<ITransferSpeedMetrics, TransferSpeedMetrics>();
    builder.Services.AddScoped<IJobStatusService, JobStatusService>();
    builder.Services.AddSingleton<IJobRecoveryStrategy, TorrentRecoveryStrategy>();

    // Hosted Services
    builder.Services.Configure<JobHealthMonitorOptions>(opts => opts.CheckInterval = TimeSpan.FromMinutes(2));
    builder.Services.AddHostedService<JobHealthMonitor<UserJob>>();
    builder.Services.AddHostedService<TorrentWorker>();

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
    Log.Fatal(ex, "Worker terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
