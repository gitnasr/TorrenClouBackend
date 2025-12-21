using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;

namespace TorreClou.Core.Specifications
{
    public class SyncJobsSpecification : BaseSpecification<Sync>
    {
        public SyncJobsSpecification(int pageNumber, int pageSize, SyncStatus? status = null)
            : base(sync => status == null || sync.Status == status)
        {
            AddInclude(sync => sync.UserJob);
            AddInclude(sync => sync.UserJob.User);
            AddInclude(sync => sync.UserJob.StorageProfile);
            AddInclude(sync => sync.UserJob.RequestFile);
            AddOrderByDescending(sync => sync.CreatedAt);
            ApplyPaging((pageNumber - 1) * pageSize, pageSize);
        }
    }
}

