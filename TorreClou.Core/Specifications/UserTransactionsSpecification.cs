using TorreClou.Core.Entities.Financals;

namespace TorreClou.Core.Specifications
{
    // Specification classes for wallet queries
    public class UserTransactionsSpecification : BaseSpecification<WalletTransaction>
    {
        public UserTransactionsSpecification(int userId, int pageNumber, int pageSize)
            : base(x => x.UserId == userId)
        {
            AddOrderByDescending(x => x.CreatedAt);
            ApplyPaging((pageNumber - 1) * pageSize, pageSize);
        }
    }
}