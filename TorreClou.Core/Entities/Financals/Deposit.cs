using TorreClou.Core.Enums;

namespace TorreClou.Core.Entities.Financals
{
    public class Deposit : BaseEntity
    {
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public decimal Amount { get; set; }
        public string Currency { get; set; } = "USD";

        public string PaymentProvider { get; set; } = "Stripe";

        public string GatewayTransactionId { get; set; } = string.Empty;

        public string? PaymentUrl { get; set; }

        public DepositStatus Status { get; set; } = DepositStatus.Pending;

        public int? WalletTransactionId { get; set; }
    }
}