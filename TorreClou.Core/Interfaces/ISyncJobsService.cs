using TorreClou.Core.DTOs.Common;
using TorreClou.Core.DTOs.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface ISyncJobsService
    {
        Task<Result<PaginatedResult<SyncJobDto>>> GetAllSyncJobsAsync(int pageNumber, int pageSize, SyncStatus? status = null);
        Task<Result<SyncJobDto>> GetSyncJobByIdAsync(int syncId);
        Task<Result<SyncJobStatisticsDto>> GetSyncJobStatisticsAsync();
    }
}

