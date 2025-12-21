using Hangfire;
using Scalar.AspNetCore;
using Serilog;
using TorreClou.API.Extensions;
using TorreClou.API.Filters;
using TorreClou.Application.Extensions;
using TorreClou.Infrastructure.Extensions; // Import Shared Extensions

const string ServiceName = "torreclou-api";

// Bootstrap Logger
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    Log.Information("Starting API");
    var builder = WebApplication.CreateBuilder(args);

    // 1. Shared Serilog Setup
    builder.Configuration.ConfigureSharedSerilog(ServiceName, builder.Environment.EnvironmentName);
    builder.Host.UseSerilog();

    // 2. Shared Infrastructure (DB, Redis, Repos, OpenTelemetry)
    builder.Services.AddSharedDatabase(builder.Configuration);
    builder.Services.AddSharedRedis(builder.Configuration);
    builder.Services.AddTorreClouOpenTelemetry(ServiceName, builder.Configuration, builder.Environment, true);

    // 3. Hangfire (Client Mode - No Server)
    builder.Services.AddSharedHangfireBase(builder.Configuration);

    // 4. API Specifics
    builder.Services.AddApplicationServices();
    builder.Services.AddInfrastructureServices(builder.Configuration);
    builder.Services.AddApiServices(builder.Configuration);
    builder.Services.AddIdentityServices(builder.Configuration);
    builder.Services.AddHttpClient();
    // CORS
    builder.Services.AddCors(options => {

    });

    var app = builder.Build();

    // --- Middleware ---
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
        app.UseCors("Development");
    }
    else
    {
        app.UseCors("Production");
        app.UseHsts();
    }

    app.UseExceptionHandler();
    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();

    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new HangfireAuthorizationFilter()],
        DashboardTitle = "TorreClou Jobs"
    });

    app.UseOpenTelemetryPrometheusScrapingEndpoint();
    app.MapControllers();
    app.Run();
}
catch (Exception ex) { Log.Fatal(ex, "API terminated unexpectedly"); }
finally { Log.CloseAndFlush(); }