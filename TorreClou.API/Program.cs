using Hangfire;
using Scalar.AspNetCore;
using Serilog;
using TorreClou.API.Extensions;
using TorreClou.API.Filters;
using TorreClou.Application.Extensions;
using TorreClou.Infrastructure.Extensions;
using TorreClou.Infrastructure.Services;

const string ServiceName = "torreclou-api";

// Bootstrap logger for startup errors only
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog (replaces bootstrap logger)
    builder.Configuration.ConfigureSharedSerilog(ServiceName, builder.Environment.EnvironmentName);
    builder.Host.UseSerilog();

    Log.Information("Starting {ServiceName}", ServiceName);

    // Infrastructure
    builder.Services.AddSharedDatabase(builder.Configuration);
    builder.Services.AddSharedRedis(builder.Configuration);
    builder.Services.AddTorreClouOpenTelemetry(ServiceName, builder.Configuration, builder.Environment, true);
    builder.Services.AddSharedHangfireBase(builder.Configuration);

    // Application Services
    builder.Services.AddApplicationServices();
    builder.Services.AddInfrastructureServices(builder.Configuration);
    builder.Services.AddApiServices(builder.Configuration);
    builder.Services.AddIdentityServices(builder.Configuration);
    builder.Services.AddHttpClient();
    
    // Prometheus Remote Write to Grafana Cloud
    builder.Services.AddHostedService<PrometheusRemoteWriteService>();

    // CORS
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    var app = builder.Build();

    // Middleware
    app.UseExceptionHandler();
    app.UseCors("AllowAll");

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }
    else
    {
        app.UseHttpsRedirection();
        app.UseHsts();
    }

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new HangfireAuthorizationFilter()],
        DashboardTitle = "TorreClou Jobs"
    });

    app.UseOpenTelemetryPrometheusScrapingEndpoint();
    app.MapControllers();

    Log.Information("{ServiceName} started successfully", ServiceName);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
