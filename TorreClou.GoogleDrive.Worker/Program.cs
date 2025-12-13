using StackExchange.Redis;
using TorreClou.GoogleDrive.Worker;
using TorreClou.GoogleDrive.Worker.Services;
using Microsoft.EntityFrameworkCore;
using TorreClou.Infrastructure.Data;
using TorreClou.Infrastructure.Repositories;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Interceptors;
using TorreClou.Infrastructure.Services;
using TorreClou.Core.Options;
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
builder.Services.AddScoped<IGoogleDriveService, GoogleDriveService>();

// Upload Progress Context (scoped per job for progress tracking and resume support)
builder.Services.AddScoped<IUploadProgressContext, UploadProgressContext>();

// Background Services
// Google Drive Worker - Redis stream consumer for upload jobs
builder.Services.AddHostedService<GoogleDriveWorker>();

var host = builder.Build();
host.Run();

