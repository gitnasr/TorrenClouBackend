using TorreClou.Core.Entities.Jobs;

namespace TorreClou.Core.Specifications
{
    public class JobTimelineSpecification : BaseSpecification<JobStatusHistory>
    {
        public JobTimelineSpecification(int jobId, int pageNumber, int pageSize)
            : base(h => h.JobId == jobId)
        {
            AddOrderBy(h => h.ChangedAt);
            ApplyPaging((pageNumber - 1) * pageSize, pageSize);
        }
    }
}
