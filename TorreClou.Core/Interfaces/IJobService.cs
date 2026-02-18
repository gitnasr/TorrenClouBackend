using TorreClou.Core.DTOs.Common;
using TorreClou.Core.DTOs.Jobs;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;

namespace TorreClou.Core.Interfaces
{
    public interface IJobService
    {
        Task<JobCreationResult> CreateAndDispatchJobAsync(int torrentFileId, int userId, string[]? selectedFiles, int storageProfileId);
        Task<PaginatedResult<JobDto>> GetUserJobsAsync(int userId, int pageNumber, int pageSize, JobStatus? status = null);
        Task<JobDto> GetJobByIdAsync(int userId, int jobId, UserRole? userRole = null);
        Task<JobStatisticsDto> GetUserJobStatisticsAsync(int userId);

        Task<IReadOnlyList<UserJob>> GetActiveJobsByStorageProfileIdAsync(int storageProfileId);

        Task RetryJobAsync(int jobId, int userId, UserRole? userRole = null);

        Task CancelJobAsync(int jobId, int userId, UserRole? userRole = null);

        // Worker-facing job state updates
        Task UpdateJobStartedAtAsync(UserJob job);
        Task UpdateJobProgressAsync(UserJob job, long bytesUploaded);
        Task UpdateHeartbeatAsync(int jobId);
    }
}
