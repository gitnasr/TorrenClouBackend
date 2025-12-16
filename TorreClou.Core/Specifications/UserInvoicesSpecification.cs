namespace TorreClou.Core.Specifications
{
    public class UserInvoicesSpecification : BaseSpecification<Invoice>
    {
        public UserInvoicesSpecification(
            int userId, 
            int pageNumber, 
            int pageSize, 
            DateTime? dateFrom = null, 
            DateTime? dateTo = null)
            : base(invoice => 
                invoice.UserId == userId &&
                (dateFrom == null || invoice.CreatedAt >= dateFrom.Value) &&
                (dateTo == null || invoice.CreatedAt <= dateTo.Value))
        {
            AddInclude(invoice => invoice.TorrentFile);
            AddInclude(invoice => invoice.Job);
            AddOrderByDescending(invoice => invoice.CreatedAt);
            ApplyPaging((pageNumber - 1) * pageSize, pageSize);
        }
    }
}

