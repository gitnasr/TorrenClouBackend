using TorreClou.Core.DTOs.Common;
using TorreClou.Core.DTOs.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IJobService
    {
        Task<Result<JobCreationResult>> CreateAndDispatchJobAsync(int invoiceId, int userId);
        Task<Result<PaginatedResult<JobDto>>> GetUserJobsAsync(int userId, int pageNumber, int pageSize, JobStatus? status = null);
        Task<Result<JobDto>> GetJobByIdAsync(int userId, int jobId, UserRole? userRole = null);
        Task<Result<JobStatisticsDto>> GetUserJobStatisticsAsync(int userId);
    }
}

