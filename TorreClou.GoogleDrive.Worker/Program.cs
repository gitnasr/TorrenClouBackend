using Serilog;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.Core.Options;
using TorreClou.Infrastructure.Extensions;
using TorreClou.Infrastructure.Services;
using TorreClou.Infrastructure.Services.Drive;
using TorreClou.Infrastructure.Settings;
using TorreClou.GoogleDrive.Worker;
using TorreClou.GoogleDrive.Worker.Services;

const string ServiceName = "torreclou-googledrive-worker";

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
    builder.Services.AddSharedHangfireServer(queues: ["googledrive", "default"]);

    // Worker Services
    builder.Services.AddHttpClient();
    builder.Services.AddScoped<IGoogleDriveUploadJob, GoogleDriveUploadJob>();
    builder.Services.Configure<GoogleDriveSettings>(builder.Configuration.GetSection("GoogleDrive"));
    builder.Services.Configure<BackblazeSettings>(builder.Configuration.GetSection("Backblaze"));
    builder.Services.AddScoped<IGoogleDriveJobService, GoogleDriveJobService>();
    builder.Services.AddScoped<IUploadProgressContext, UploadProgressContext>();
    builder.Services.AddScoped<ITransferSpeedMetrics, TransferSpeedMetrics>();

    // Hosted Services
    builder.Services.Configure<JobHealthMonitorOptions>(opts => opts.CheckInterval = TimeSpan.FromMinutes(2));
    builder.Services.AddHostedService<JobHealthMonitor<UserJob>>();
    builder.Services.AddHostedService<GoogleDriveWorker>();

    var host = builder.Build();
    
    Log.Information("{ServiceName} started successfully", ServiceName);
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Google Drive Worker terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
