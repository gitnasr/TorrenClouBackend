using Microsoft.Extensions.Options;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Models.Pricing;

namespace TorreClou.Application.Services
{
    public class PricingEngine() : IPricingEngine
    {
        private const decimal BASE_RATE_PER_GB = 0.05m;

        private const decimal MINIMUM_CHARGE = 0.20m;


        public PricingSnapshot CalculatePrice(long sizeBytes, RegionCode region, double healthMultiplier, bool isCached = false)
        {
            // 1. Convert Bytes to GB (Using Decimal for precision)
            // We set a logical minimum of 0.1 GB to prevent micro-transactions for 1KB files
            decimal sizeInGb = (decimal)sizeBytes / (1024m * 1024m * 1024m);
            if (sizeInGb < 0.1m) sizeInGb = 0.1m;

            // 2. Get Modifiers
            decimal regionMultiplier = GetRegionMultiplier(region);
            decimal healthFactor = (decimal)healthMultiplier; // Cast double input to decimal immediately

            // 3. Base Calculation
            // Formula: (GB * Rate * Region) * Health
            decimal basePrice = sizeInGb * BASE_RATE_PER_GB * regionMultiplier;
            decimal priceWithHealth = basePrice * healthFactor;

            // 4. Create Snapshot (Populate before final adjustments for transparency)
            var snapshot = new PricingSnapshot
            {
                BaseRatePerGb = BASE_RATE_PER_GB,
                UserRegion = region.ToString(),
                RegionMultiplier = (double)regionMultiplier, // Store as double for display/JSON
                HealthMultiplier = healthMultiplier,
                IsCacheHit = isCached,
                TotalSizeInBytes = sizeBytes,
                CalculatedSizeInGb = (double)Math.Round(sizeInGb, 4)
            };

            // 5. Apply Cache Discount
            if (isCached)
            {
                // If cached, they only pay for the "Storage/Bandwidth" slot, not the "Compute" of downloading
                //decimal discount = priceWithHealth * _settings.CacheDiscountPercentage;
                //snapshot.CacheDiscountAmount = Math.Round(discount, 4);
                //priceWithHealth -= discount;
            }

            // 6. Enforce Minimum Charge (Floor)
            // This covers transaction fees (Stripe/PayPal usually charge fixed $0.30 + %)
            snapshot.FinalPrice = Math.Max(priceWithHealth, MINIMUM_CHARGE);

            // 7. Final Rounding (Bankers Rounding)
            snapshot.FinalPrice = Math.Round(snapshot.FinalPrice, 2, MidpointRounding.AwayFromZero);

            return snapshot;
        }

        private decimal GetRegionMultiplier(RegionCode region)
        {
            // Purchasing Power Parity (PPP) Logic
            return region switch
            {
                RegionCode.EG => 0.4m, // Egypt (60% Off)
                RegionCode.IN => 0.4m, // India (60% Off)
                RegionCode.SA => 0.8m, // Saudi Arabia (20% Off)
                RegionCode.US => 1.0m, // USA (Base)
                RegionCode.EU => 1.0m, // Europe (Base)
                _ => 1.0m
            };
        }
    }
}