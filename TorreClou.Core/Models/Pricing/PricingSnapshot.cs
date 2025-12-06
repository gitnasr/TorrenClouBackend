namespace TorreClou.Core.Models.Pricing
{
    public class PricingSnapshot
    {
        public decimal BaseRatePerGb { get; set; } = 0.05m; // سعر الجيجا الأساسي (مثلاً 0.05$)

        public string UserRegion { get; set; }     // EG, US, etc.
        public double RegionMultiplier { get; set; } = 1.0; // 0.5 لمصر، 1.0 لأمريكا

        public int SeedersCount { get; set; }
        public double HealthMultiplier { get; set; } // لو Seeders قليل، السعر يزيد (Risk)

        public bool IsCacheHit { get; set; }
        public decimal CacheDiscountAmount { get; set; } // قيمة الخصم لو الملف موجود

        public decimal FinalPrice { get; set; }
        public string Currency { get; set; } = "USD";

        public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    }
}