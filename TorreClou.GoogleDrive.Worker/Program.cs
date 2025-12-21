using Serilog;
using TorreClou.Infrastructure.Extensions;
using TorreClou.GoogleDrive.Worker;
using TorreClou.GoogleDrive.Worker.Services;
using TorreClou.Infrastructure.Services;
using TorreClou.Infrastructure.Settings;
using TorreClou.Core.Options;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Interfaces;
using TorreClou.API.Extensions;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.Infrastructure.Services.Drive;

const string ServiceName = "torreclou-googledrive-worker";

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    Log.Information("Starting Google Drive Worker");
    var builder = Host.CreateApplicationBuilder(args);

    // 1. Shared Setup
    builder.Configuration.ConfigureSharedSerilog(ServiceName, builder.Environment.EnvironmentName);
    builder.Services.AddSerilog(); // Attach to Host

    builder.Services.AddSharedDatabase(builder.Configuration);
    builder.Services.AddSharedRedis(builder.Configuration);
    builder.Services.AddTorreClouOpenTelemetry(ServiceName, builder.Configuration, builder.Environment);

    // 2. Hangfire (Server Mode)
    builder.Services.AddSharedHangfireBase(builder.Configuration);
    builder.Services.AddSharedHangfireServer(queues: ["googledrive", "default"]);

    // 3. Worker Specific Services
    builder.Services.AddHttpClient();
    builder.Services.AddScoped<IGoogleDriveUploadJob,GoogleDriveUploadJob>(); 
    builder.Services.Configure<GoogleDriveSettings>(builder.Configuration.GetSection("GoogleDrive"));
    builder.Services.Configure<BackblazeSettings>(builder.Configuration.GetSection("Backblaze"));
    builder.Services.AddScoped<IGoogleDriveJobService, GoogleDriveJobService>();
    builder.Services.AddScoped<IUploadProgressContext, UploadProgressContext>();
    builder.Services.AddScoped<ITransferSpeedMetrics, TransferSpeedMetrics>();

    // 4. Hosted Services
    builder.Services.Configure<JobHealthMonitorOptions>(opts => { opts.CheckInterval = TimeSpan.FromMinutes(2); });
    builder.Services.AddHostedService<JobHealthMonitor<UserJob>>();
    builder.Services.AddHostedService<GoogleDriveWorker>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex) { Log.Fatal(ex, "GDrive Worker terminated"); }
finally { Log.CloseAndFlush(); }