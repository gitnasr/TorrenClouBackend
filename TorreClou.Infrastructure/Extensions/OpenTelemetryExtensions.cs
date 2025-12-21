using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace TorreClou.Infrastructure.Extensions
{
    public static class OpenTelemetryExtensions
    {
        public static IServiceCollection AddTorreClouOpenTelemetry(
            this IServiceCollection services,
            string serviceName,
            IConfiguration configuration,
            IHostEnvironment environment,
            bool includeAspNetCoreInstrumentation = false)
        {
            var observabilityConfig = configuration.GetSection("Observability");
            var enablePrometheus = observabilityConfig.GetValue<bool>("EnablePrometheus", includeAspNetCoreInstrumentation);
            var enableTracing = observabilityConfig.GetValue<bool>("EnableTracing", true);
            var enableLogging = observabilityConfig.GetValue<bool>("EnableLogging", true);

            var otlpEndpoint = observabilityConfig["OtlpEndpoint"];
            var otlpHeaders = observabilityConfig["OtlpHeaders"];

            // 1. Define Resource ONCE (Shared by Traces, Metrics, Logs)
            var resourceBuilder = ResourceBuilder.CreateDefault()
                .AddService(serviceName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = environment.EnvironmentName,
                    ["service.instance.id"] = Environment.MachineName
                });

            // 2. Configure OpenTelemetry Pipeline
            var otelBuilder = services.AddOpenTelemetry()
                .WithMetrics(metrics =>
                {
                    metrics.SetResourceBuilder(resourceBuilder)
                           .AddHttpClientInstrumentation()
                           .AddRuntimeInstrumentation()
                           .AddProcessInstrumentation();

                    if (includeAspNetCoreInstrumentation)
                        metrics.AddAspNetCoreInstrumentation();

                    if (enablePrometheus)
                        metrics.AddPrometheusExporter();

                    if (!string.IsNullOrEmpty(otlpEndpoint))
                    {
                        metrics.AddOtlpExporter(opts =>
                        {
                            opts.Endpoint = new Uri(otlpEndpoint);
                            if (!string.IsNullOrEmpty(otlpHeaders)) opts.Headers = otlpHeaders;
                        });
                    }
                });

            if (enableTracing)
            {
                otelBuilder.WithTracing(tracing =>
                {
                    tracing.SetResourceBuilder(resourceBuilder)
                           .AddHttpClientInstrumentation()
                           .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true)
                           .AddRedisInstrumentation();

                    if (includeAspNetCoreInstrumentation)
                        tracing.AddAspNetCoreInstrumentation();

                    if (!string.IsNullOrEmpty(otlpEndpoint))
                    {
                        tracing.AddOtlpExporter(opts =>
                        {
                            opts.Endpoint = new Uri(otlpEndpoint);
                            if (!string.IsNullOrEmpty(otlpHeaders)) opts.Headers = otlpHeaders;
                        });
                    }
                });
            }

            // 3. Configure Logging (Optional but recommended)
            if (enableLogging && !string.IsNullOrEmpty(otlpEndpoint))
            {
                services.AddLogging(logging =>
                {
                    logging.AddOpenTelemetry(options =>
                    {
                        options.SetResourceBuilder(resourceBuilder);
                        options.IncludeScopes = true;
                        options.AddOtlpExporter(opts =>
                        {
                            opts.Endpoint = new Uri(otlpEndpoint);
                            if (!string.IsNullOrEmpty(otlpHeaders)) opts.Headers = otlpHeaders;
                        });
                    });
                });
            }

            return services;
        }
    }
}