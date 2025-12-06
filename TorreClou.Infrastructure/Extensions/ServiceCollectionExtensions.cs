using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TorreClou.Application.Services;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Data;
using TorreClou.Infrastructure.Interceptors;
using TorreClou.Infrastructure.Repositories;
using TorreClou.Infrastructure.Services;
using TorreClou.Infrastructure.Settings;

namespace TorreClou.Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddSingleton<UpdateAuditableEntitiesInterceptor>();

            // Coinremitter payment gateway
            services.AddHttpClient<IPaymentGateway, CoinremitterService>();
            services.Configure<CoinremitterSettings>(configuration.GetSection("Coinremitter"));

            services.AddScoped<ITokenService, TokenService>();
            services.AddScoped<ITorrentParser, TorrentParserService>();
            services.AddDbContext<ApplicationDbContext>((sp, options) =>
            {
                var interceptor = sp.GetRequiredService<UpdateAuditableEntitiesInterceptor>();
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"))
                       .AddInterceptors(interceptor);
            });

            return services;
        }
    }
}
