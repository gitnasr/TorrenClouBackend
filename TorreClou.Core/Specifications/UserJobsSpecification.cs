using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;

namespace TorreClou.Core.Specifications
{
    public class UserJobsSpecification : BaseSpecification<UserJob>
    {
        public UserJobsSpecification(int userId, int pageNumber, int pageSize, JobStatus? status = null )
            : base(job => 
                job.UserId == userId && (status == null || job.Status == status))
              
        {
            AddInclude(job => job.StorageProfile);
            AddInclude(job => job.RequestFile);
            AddOrderByDescending(job => job.CreatedAt);
            ApplyPaging((pageNumber - 1) * pageSize, pageSize);
        }
    }
}
