using StackExchange.Redis;
using TorreClou.Worker;
using TorreClou.Worker.Services;
using Microsoft.EntityFrameworkCore;
using TorreClou.Infrastructure.Data;
using TorreClou.Infrastructure.Repositories;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Interceptors;
using Hangfire;
using Hangfire.PostgreSql;

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
builder.Services.AddScoped<IUnitOfWork, TorreClou.Infrastructure.Data.UnitOfWork>();

// HttpClient for downloading torrent files
builder.Services.AddHttpClient();

// Hangfire configuration
var hangfireConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddHangfire((provider, config) => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(hangfireConnectionString, new PostgreSqlStorageOptions
    {
        SchemaName = "hangfire"
    })
    .UseFilter(new TorreClou.Worker.Filters.JobStateSyncFilter(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<ILogger<TorreClou.Worker.Filters.JobStateSyncFilter>>()
    ))
);

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Environment.ProcessorCount * 5;

    // Fast recovery settings
    options.ServerTimeout = TimeSpan.FromSeconds(45);
    options.HeartbeatInterval = TimeSpan.FromSeconds(15);
    options.SchedulePollingInterval = TimeSpan.FromSeconds(10);

    options.Queues = new[] { "torrents", "default" };
});

// Register Job Services (Hangfire jobs)
builder.Services.AddScoped<TorrentDownloadJob>();
builder.Services.AddScoped<TorrentUploadJob>();

// Redis configuration
var redisConnectionString = builder.Configuration.GetSection("Redis:ConnectionString").Value ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnectionString)
);

// Background Services

// 1. Job Health Monitor - Continuous monitoring and recovery of orphaned jobs
builder.Services.AddHostedService<JobHealthMonitor>();

// 2. Torrent Worker - Redis stream consumer for new job events
builder.Services.AddHostedService<TorrentWorker>();

var host = builder.Build();
host.Run();
