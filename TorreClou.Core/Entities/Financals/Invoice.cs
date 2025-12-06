using TorreClou.Core.Entities.Jobs;

namespace TorreClou.Core.Entities.Financals
{
    public class Invoice : BaseEntity
    {
        public int UserId { get; set; }

        public int JobId { get; set; }
        public UserJob Job { get; set; } = null!;

        public decimal Amount { get; set; }
        public string Currency { get; set; } = "USD";

        public string PricingSnapshotJson { get; set; } = "{}";

        public bool IsPaid { get; set; } = false;
        public bool IsRefunded { get; set; } = false;

        public string? WalletTransactionId { get; set; }
        public WalletTransaction WalletTransaction { get; set; } = null!;

        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(15);
    }
}