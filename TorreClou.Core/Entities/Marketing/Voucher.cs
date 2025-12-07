using TorreClou.Core.Enums;

namespace TorreClou.Core.Entities.Marketing
{
    public class Voucher : BaseEntity
    {
        public string Code { get; set; } = string.Empty;

        public DiscountType Type { get; set; } = DiscountType.Percentage;

        public decimal Value { get; set; }

        public int? MaxUsesTotal { get; set; }
        public int MaxUsesPerUser { get; set; } = 1;

        public DateTime? ExpiresAt { get; set; }
        public bool IsActive { get; set; } = true;

        public ICollection<UserVoucherUsage> Usages { get; set; } = [];
    }
}