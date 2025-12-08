using TorreClou.Core.Entities.Financals;
using TorreClou.Core.Specifications;

public class ActiveInvoiceByTorrentAndUserSpec : BaseSpecification<Invoice>
{
    public ActiveInvoiceByTorrentAndUserSpec(string infoHash, int userId)
        : base(i =>
            i.TorrentFile.InfoHash == infoHash &&
            i.UserId == userId &&
            i.PaidAt != null &&
            i.RefundedAt != null 
        )
    {
        AddOrderByDescending(i => i.CreatedAt);
        AddInclude(i => i.TorrentFile);
    }
}
