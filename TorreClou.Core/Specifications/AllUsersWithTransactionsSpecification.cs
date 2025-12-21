using TorreClou.Core.Entities;

namespace TorreClou.Core.Specifications
{
    public class AllUsersWithTransactionsSpecification : BaseSpecification<User>
    {
        public AllUsersWithTransactionsSpecification(int pageNumber, int pageSize)
        {
            AddInclude(u => u.WalletTransactions);
            AddOrderByDescending(u => u.CreatedAt);
            ApplyPaging((pageNumber - 1) * pageSize, pageSize);
        }
    }
}