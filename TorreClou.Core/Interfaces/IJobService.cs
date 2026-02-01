using TorreClou.Core.DTOs.Common;
using TorreClou.Core.DTOs.Jobs;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IJobService
    {
        Task<Result<JobCreationResult>> CreateAndDispatchJobAsync(int torrentFileId, int userId, string[] selectedFiles, int? storageProfileId = null);
        Task<Result<PaginatedResult<JobDto>>> GetUserJobsAsync(int userId, int pageNumber, int pageSize, JobStatus? status = null);
        Task<Result<JobDto>> GetJobByIdAsync(int userId, int jobId, UserRole? userRole = null);
        Task<Result<JobStatisticsDto>> GetUserJobStatisticsAsync(int userId);

        Task<Result<IReadOnlyList<UserJob>>> GetActiveJobsByStorageProfileIdAsync(int storageProfileId);

        Task<Result<bool>> RetryJobAsync(int jobId, int userId, UserRole? userRole = null);

        Task<Result<bool>> CancelJobAsync(int jobId, int userId, UserRole? userRole = null);

    }
}

