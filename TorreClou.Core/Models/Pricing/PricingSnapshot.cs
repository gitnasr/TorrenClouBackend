namespace TorreClou.Core.Models.Pricing
{
    public class PricingSnapshot
    {
        public long TotalSizeInBytes { get; set; }
        public double TotalSizeInGb => TotalSizeInBytes / 1_073_741_824.0;


        public List<int> SelectedFiles { get; set; } = new();

        public decimal BaseRatePerGb { get; set; }
        public string UserRegion { get; set; } = string.Empty;
        public double RegionMultiplier { get; set; }
        public double HealthMultiplier { get; set; }

        public bool IsCacheHit { get; set; }
        public decimal CacheDiscountAmount { get; set; }

        public decimal FinalPrice { get; set; }

        public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
        public double CalculatedSizeInGb { get; set; }
    }
}
