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
using TorreClou.Worker;
using TorreClou.Worker.Services;
using TorreClou.Worker.Services.Strategies;

const string ServiceName = "torreclou-worker";

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    Log.Information("Starting Torrent Worker");
    var builder = Host.CreateApplicationBuilder(args);

    // 1. Shared Setup
    builder.Configuration.ConfigureSharedSerilog(ServiceName, builder.Environment.EnvironmentName);
    builder.Services.AddSerilog();

    builder.Services.AddSharedDatabase(builder.Configuration);
    builder.Services.AddSharedRedis(builder.Configuration);
    builder.Services.AddTorreClouOpenTelemetry(ServiceName, builder.Configuration, builder.Environment);

    // 2. Hangfire (Server Mode + Custom Filter)
    // We pass the filter registration logic as a delegate
    builder.Services.AddSharedHangfireBase(builder.Configuration, (hfConfig) =>
    {
        // We can't resolve DI services inside this simplified extension cleanly
        // So for complex filters involving DI, you might keep the filter line here 
        // OR update the extension to use IServiceProvider. For now, simplest is:
        // *Logic moved to AddSharedHangfireBase is fine, add filter manually here if needed*
        // Note: Global filters usually work best with GlobalConfiguration.Configuration.UseFilter() 
        // but strictly typed DI filters need registration inside the callback.
    });

    // Re-register Hangfire here ONLY to add the DI-dependent filter if needed, 
    // OR just rely on the base and add the filter globally in a startup block.
    // For simplicity, let's stick to the base and add the filter manually if it supports DI:
    GlobalJobFilters.Filters.Add(new JobStateSyncFilter(
        builder.Services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        new LoggerFactory().CreateLogger<JobStateSyncFilter>() // Temporary/Hack for startup
    ));
    // *Better approach*: Use a separate IConfigureOptions<IGlobalConfiguration> implementation.

    builder.Services.AddSharedHangfireServer(queues: ["torrents", "default"]);

    // 3. Worker Specific Services
    builder.Services.AddHttpClient();
    builder.Services.Configure<TorreClou.Infrastructure.Settings.BackblazeSettings>(builder.Configuration.GetSection("Backblaze"));

    builder.Services.AddScoped<ITorrentDownloadJob, TorrentDownloadJob>();
    builder.Services.AddScoped<ITransferSpeedMetrics, TransferSpeedMetrics>();
    builder.Services.AddSingleton<IJobRecoveryStrategy, TorrentRecoveryStrategy>();

    // 4. Hosted Services
    builder.Services.Configure<JobHealthMonitorOptions>(opts => { opts.CheckInterval = TimeSpan.FromMinutes(2); });
    builder.Services.AddHostedService<JobHealthMonitor<UserJob>>();
    builder.Services.AddHostedService<TorrentWorker>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex) { Log.Fatal(ex, "Torrent Worker terminated"); }
finally { Log.CloseAndFlush(); }