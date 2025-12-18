using TorreClou.Core.Entities.Financals;
using TorreClou.Core.Enums;

namespace TorreClou.Core.Specifications
{
    public class ActiveInvoiceByTorrentAndUserSpec : BaseSpecification<Invoice>
    {
        public ActiveInvoiceByTorrentAndUserSpec(string infoHash, int userId)
            : base(i =>
                i.TorrentFile.InfoHash == infoHash &&
                i.UserId == userId &&
                i.CancelledAt == null &&  // Not cancelled
                i.RefundedAt == null &&   // Not refunded
                // Exclude invoices with FAILED jobs - allow user to retry with new invoice
                (i.Job == null || i.Job.Status != JobStatus.FAILED)
            )
        {
            AddOrderByDescending(i => i.CreatedAt);
            AddInclude(i => i.TorrentFile);
            AddInclude(i => i.Job);  // Include Job to check status
        }
    }
}