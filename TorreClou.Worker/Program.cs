using StackExchange.Redis;
using TorreClou.Worker;
using TorreClou.Worker.Services;
using TorreClou.Worker.Services.Strategies;
using Microsoft.EntityFrameworkCore;
using TorreClou.Infrastructure.Data;
using TorreClou.Infrastructure.Repositories;
using TorreClou.Infrastructure.Services;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Options;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Infrastructure.Interceptors;
using Hangfire;
using TorreClou.Infrastructure.Filters;

var builder = Host.CreateApplicationBuilder(args);

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

// HttpClient for downloading torrent files
builder.Services.AddHttpClient();

// Hangfire configuration
var hangfireConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddHangfire((provider, config) => config .UseSimpleAssemblyNameTypeSerializer()  .UseRecommendedSerializerSettings()
  
    .UseFilter(new JobStateSyncFilter(
        provider.GetRequiredService<IUnitOfWork>(),
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
builder.Services.AddScoped<TorrentUploadJob>();

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
