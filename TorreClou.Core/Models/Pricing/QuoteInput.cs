using TorreClou.Core.Enums;

namespace TorreClou.Core.Models.Pricing
{
    public class QuoteInput
    {
        public int UserId { get; set; }
        public RegionCode Region { get; set; }

        public long SizeInBytes { get; set; }
        public double HealthMultiplier { get; set; }
        public bool IsCacheHit { get; set; }

        public List<int> SelectedFiles { get; set; } = new();

        public string? VoucherCode { get; set; }

        // product-specific reference
        public object ProductRef { get; set; } = default!; // TorrentFile, StoragePlan, ...
        public string ProductKey { get; set; } = string.Empty; // مثلا "torrent" or "storage"
        public string DedupKey { get; set; } = string.Empty;   // مثلاً InfoHash+UserId
    }

}
