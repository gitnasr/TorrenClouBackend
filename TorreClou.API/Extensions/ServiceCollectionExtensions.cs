using System.Text.Json.Serialization;
using TorreClou.API.Middleware;
using TorreClou.API.Services;

namespace TorreClou.API.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    // Serialize enums as strings instead of integers
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                });
            services.AddEndpointsApiExplorer();
            services.AddOpenApi();

            services.AddExceptionHandler<GlobalExceptionHandler>();
            services.AddProblemDetails();

            // Health Check Service with caching
            services.AddMemoryCache();
            services.AddScoped<IHealthCheckService, HealthCheckService>();

            return services;
        }
    }
}
