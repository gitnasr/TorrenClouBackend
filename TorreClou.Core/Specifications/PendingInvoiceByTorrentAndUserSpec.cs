namespace TorreClou.Core.Specifications
{
    public class PendingInvoiceByTorrentAndUserSpec : BaseSpecification<Invoice>
    {
        public PendingInvoiceByTorrentAndUserSpec(string infoHash, int userId)
            : base(i =>
                i.TorrentFile.InfoHash == infoHash &&
                i.UserId == userId &&
                i.PaidAt == null &&           // Not paid
                i.CancelledAt == null &&      // Not cancelled
                i.RefundedAt == null &&       // Not refunded
                i.ExpiresAt > DateTime.UtcNow // Not expired
            )
        {
            AddOrderByDescending(i => i.CreatedAt);
            AddInclude(i => i.TorrentFile);
        }
    }
}