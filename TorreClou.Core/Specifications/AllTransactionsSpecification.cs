using TorreClou.Core.Entities.Financals;

namespace TorreClou.Core.Specifications
{
    public class AllTransactionsSpecification : BaseSpecification<WalletTransaction>
    {
        public AllTransactionsSpecification(int pageNumber, int pageSize)
        {
            AddOrderByDescending(x => x.CreatedAt);
            ApplyPaging((pageNumber - 1) * pageSize, pageSize);
        }
    }
}