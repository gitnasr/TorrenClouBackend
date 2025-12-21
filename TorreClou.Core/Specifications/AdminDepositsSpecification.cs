using TorreClou.Core.Entities.Financals;
using TorreClou.Core.Enums;

namespace TorreClou.Core.Specifications
{
    public class AdminDepositsSpecification : BaseSpecification<Deposit>
    {
        public AdminDepositsSpecification(int pageNumber, int pageSize, DepositStatus? status = null)
            : base(status.HasValue ? x => x.Status == status.Value : x => true)
        {
            AddInclude(x => x.User);
            AddOrderByDescending(x => x.CreatedAt);
            ApplyPaging((pageNumber - 1) * pageSize, pageSize);
        }
    }
}
