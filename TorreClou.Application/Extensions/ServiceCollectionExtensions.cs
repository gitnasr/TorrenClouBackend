using Microsoft.Extensions.DependencyInjection;
using TorreClou.Application.Services;

using TorreClou.Application.Services.Storage;
using TorreClou.Application.Services.Torrent;
using TorreClou.Core.Interfaces;

namespace TorreClou.Application.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<ITorrentQuoteService, TorrentQuoteService>();
            services.AddScoped<ITrackerScraper, UdpTrackerScraper>();
            services.AddScoped<ITorrentService, TorrentService>();
            services.AddScoped<IVoucherService, VoucherService>();
            services.AddScoped<IPricingEngine, PricingEngine>();
            services.AddScoped<ITorrentHealthService, TorrentHealthService>();

            services.AddScoped<IJobService, JobService>();
            services.AddScoped<ISyncJobsService, SyncJobsService>();
            services.AddScoped<IGoogleDriveAuthService, GoogleDriveAuthService>();

            services.AddScoped<IStorageProfilesService, StorageProfilesService>();

            return services;
        }
    }
}
