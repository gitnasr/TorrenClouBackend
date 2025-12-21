using TorreClou.Core.Entities.Torrents;
using TorreClou.Core.Enums;

namespace TorreClou.Core.Models.Pricing
{
    public class QuotePricingRequest
    {
        public int UserId { get; set; }
        public RegionCode Region { get; set; }

        public long SizeInBytes { get; set; }
        public double HealthMultiplier { get; set; }
        public bool IsCacheHit { get; set; }

        public List<string> SelectedFilesPath { get; set; } = new();

        public string? VoucherCode { get; set; }

        public RequestedFile TorrentFile { get; set; } = null!;

        public string InfoHash { get; set; } = string.Empty;
    }

    public class QuotePricingResult
    {
        public Invoice Invoice { get; set; } = null!;
        public PricingSnapshot Snapshot { get; set; } = null!;
        public bool IsReused { get; set; }
    }
}
