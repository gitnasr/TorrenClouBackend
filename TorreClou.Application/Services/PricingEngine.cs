using TorreClou.Core.Enums;
using TorreClou.Core.Models.Pricing;

namespace TorreClou.Application.Services
{
    public interface IPricingEngine
    {
        PricingSnapshot CalculatePrice(long sizeBytes, RegionCode region, int seeders, bool isCached = false);
    }

    public class PricingEngine : IPricingEngine
    {
        private const decimal BASE_RATE_PER_GB = 0.20m;
        private const decimal MINIMUM_CHARGE = 0.20m;

        public PricingSnapshot CalculatePrice(long sizeBytes, RegionCode region, int seeders, bool isCached = false)
        {
            var snapshot = new PricingSnapshot
            {
                BaseRatePerGb = BASE_RATE_PER_GB,
                UserRegion = region.ToString(),
                SeedersCount = seeders,
                IsCacheHit = isCached
            };

            // 1. حساب الحجم بالجيجا
            double sizeInGb = sizeBytes / (1024.0 * 1024.0 * 1024.0);
            if (sizeInGb < 0.1) sizeInGb = 0.1;

            // 2. معامل المنطقة (Region Multiplier)
            snapshot.RegionMultiplier = GetRegionMultiplier(region);

            // 3. معامل صحة التورنت (Health Multiplier)
            // كل ما الـ Seeders قلوا، السعر يزيد لأننا هنستهلك موارد ووقت أطول
            snapshot.HealthMultiplier = GetHealthMultiplier(seeders);

            // --- المعادلة الأساسية ---
            // Price = (Size * Rate * Region) * Health
            decimal rawPrice = (decimal)sizeInGb * BASE_RATE_PER_GB * (decimal)snapshot.RegionMultiplier;

            // تطبيق معامل الصحة
            rawPrice *= (decimal)snapshot.HealthMultiplier;

            // 4. خصم الكاش (Cache Hit)
            if (isCached)
            {
                // لو موجود، خصم 50% (أو يدفع تكلفة الـ Upload فقط)
                decimal discount = rawPrice * 0.50m;
                snapshot.CacheDiscountAmount = discount;
                rawPrice -= discount;
            }

            // التأكد من الحد الأدنى
            snapshot.FinalPrice = Math.Max(rawPrice, MINIMUM_CHARGE);

            // تقريب لأقرب رقمين عشريين
            snapshot.FinalPrice = Math.Round(snapshot.FinalPrice, 2);

            return snapshot;
        }

        private double GetRegionMultiplier(RegionCode region)
        {
            return region switch
            {
                RegionCode.EG => 0.4, // مصر خصم 60%
                RegionCode.IN => 0.4, // الهند
                RegionCode.SA => 0.8, // السعودية
                RegionCode.US or RegionCode.EU => 1.0, // أمريكا وأوروبا سعر كامل
                _ => 1.0
            };
        }

        private double GetHealthMultiplier(int seeders)
        {
            if (seeders >= 50) return 1.0;   // ممتاز
            if (seeders >= 20) return 1.1;   // جيد
            if (seeders >= 5) return 1.3;   // بطيء قليلاً
            if (seeders >= 2) return 1.8;   // بطيء جداً (High Risk)
            return 2.5;                      // شبه ميت (هياخد وقت طويل جداً)
        }
    }
}