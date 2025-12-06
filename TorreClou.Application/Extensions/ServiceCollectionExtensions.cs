using Microsoft.Extensions.DependencyInjection;
using TorreClou.Application.Services;
using TorreClou.Core.Interfaces;

namespace TorreClou.Application.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IPaymentBusinessService, PaymentBusinessService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IWalletService, WalletService>();
            services.AddScoped<IQuoteService, QuoteService>();
            services.AddScoped<ITrackerScraper, UdpTrackerScraper>();
            services.AddScoped<ITorrentService, TorrentService>();

            services.AddScoped<IPricingEngine, PricingEngine>();

            return services;
        }
    }
}
