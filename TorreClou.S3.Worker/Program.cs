using Serilog;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.Core.Options;
using TorreClou.Infrastructure.Extensions;
using TorreClou.Infrastructure.Services;
using TorreClou.S3.Worker;
using TorreClou.S3.Worker.Interfaces;
using TorreClou.S3.Worker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

const string ServiceName = "torreclou-s3-worker";

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

    // Infrastructure (Database, Redis, OpenTelemetry)
    builder.Services.AddSharedDatabase(builder.Configuration);
    builder.Services.AddSharedRedis(builder.Configuration);
    builder.Services.AddTorreClouOpenTelemetry(ServiceName, builder.Configuration, builder.Environment);

    // Hangfire with S3 queue
    builder.Services.AddSharedHangfireBase(builder.Configuration);
    builder.Services.AddSharedHangfireServer(queues: ["s3", "default"]);

    // S3-Specific Services (NO BackblazeSettings - all credentials from UserStorageProfile)
    builder.Services.AddScoped<IS3JobService, S3JobService>();
    builder.Services.AddSingleton<IS3ResumableUploadServiceFactory, S3ResumableUploadServiceFactory>();
    builder.Services.AddScoped<IS3UploadJob, S3UploadJob>();

    // Shared Infrastructure Services
    builder.Services.AddScoped<IJobStatusService, TorreClou.Infrastructure.Services.JobStatusService>();
    builder.Services.AddScoped<ITransferSpeedMetrics, TransferSpeedMetrics>();

    // Hosted Services
    builder.Services.Configure<JobHealthMonitorOptions>(opts =>
    {
        opts.CheckInterval = TimeSpan.FromMinutes(2);
        opts.StaleJobThreshold = TimeSpan.FromMinutes(5);
    });
    builder.Services.AddHostedService<JobHealthMonitor<UserJob>>();
    builder.Services.AddHostedService<S3Worker>();

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
    Log.Fatal(ex, "S3 Worker terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
