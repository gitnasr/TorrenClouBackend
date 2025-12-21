using TorreClou.Core.DTOs.Common;
using TorreClou.Core.DTOs.Jobs;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services
{
    public class SyncJobsService(IUnitOfWork unitOfWork) : ISyncJobsService
    {
        public async Task<Result<PaginatedResult<SyncJobDto>>> GetAllSyncJobsAsync(int pageNumber, int pageSize, SyncStatus? status = null)
        {
            var spec = new SyncJobsSpecification(pageNumber, pageSize, status);
            var countSpec = new BaseSpecification<Sync>(sync => 
                status == null || sync.Status == status);

            var syncJobs = await unitOfWork.Repository<Sync>().ListAsync(spec);
            var totalCount = await unitOfWork.Repository<Sync>().CountAsync(countSpec);

            var items = syncJobs.Select(sync => new SyncJobDto
            {
                Id = sync.Id,
                JobId = sync.JobId,
                UserId = sync.UserJob?.UserId ?? 0,
                UserEmail = sync.UserJob?.User?.Email,
                Status = sync.Status,
                LocalFilePath = sync.LocalFilePath,
                S3KeyPrefix = sync.S3KeyPrefix,
                TotalBytes = sync.TotalBytes,
                BytesSynced = sync.BytesSynced,
                FilesTotal = sync.FilesTotal,
                FilesSynced = sync.FilesSynced,
                ErrorMessage = sync.ErrorMessage,
                StartedAt = sync.StartedAt,
                CompletedAt = sync.CompletedAt,
                RetryCount = sync.RetryCount,
                NextRetryAt = sync.NextRetryAt,
                LastHeartbeat = sync.LastHeartbeat,
                CreatedAt = sync.CreatedAt,
                UpdatedAt = sync.UpdatedAt,
                RequestFileName = sync.UserJob?.RequestFile?.FileName,
                RequestFileId = sync.UserJob?.RequestFileId,
                StorageProfileName = sync.UserJob?.StorageProfile?.ProfileName,
                StorageProfileId = sync.UserJob?.StorageProfileId
            }).ToList();

            return Result.Success(new PaginatedResult<SyncJobDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        public async Task<Result<SyncJobDto>> GetSyncJobByIdAsync(int syncId)
        {
            var spec = new BaseSpecification<Sync>(sync => sync.Id == syncId);
            spec.AddInclude(sync => sync.UserJob);
            spec.AddInclude(sync => sync.UserJob.User);
            spec.AddInclude(sync => sync.UserJob.StorageProfile);
            spec.AddInclude(sync => sync.UserJob.RequestFile);

            var sync = await unitOfWork.Repository<Sync>().GetEntityWithSpec(spec);

            if (sync == null)
            {
                return Result<SyncJobDto>.Failure("NOT_FOUND", "Sync job not found.");
            }

            return Result.Success(new SyncJobDto
            {
                Id = sync.Id,
                JobId = sync.JobId,
                UserId = sync.UserJob?.UserId ?? 0,
                UserEmail = sync.UserJob?.User?.Email,
                Status = sync.Status,
                LocalFilePath = sync.LocalFilePath,
                S3KeyPrefix = sync.S3KeyPrefix,
                TotalBytes = sync.TotalBytes,
                BytesSynced = sync.BytesSynced,
                FilesTotal = sync.FilesTotal,
                FilesSynced = sync.FilesSynced,
                ErrorMessage = sync.ErrorMessage,
                StartedAt = sync.StartedAt,
                CompletedAt = sync.CompletedAt,
                RetryCount = sync.RetryCount,
                NextRetryAt = sync.NextRetryAt,
                LastHeartbeat = sync.LastHeartbeat,
                CreatedAt = sync.CreatedAt,
                UpdatedAt = sync.UpdatedAt,
                RequestFileName = sync.UserJob?.RequestFile?.FileName,
                RequestFileId = sync.UserJob?.RequestFileId,
                StorageProfileName = sync.UserJob?.StorageProfile?.ProfileName,
                StorageProfileId = sync.UserJob?.StorageProfileId
            });
        }

        public async Task<Result<SyncJobStatisticsDto>> GetSyncJobStatisticsAsync()
        {
            var allSyncJobsSpec = new BaseSpecification<Sync>();
            var allSyncJobs = await unitOfWork.Repository<Sync>().ListAsync(allSyncJobsSpec);

            var statistics = new SyncJobStatisticsDto
            {
                TotalSyncJobs = allSyncJobs.Count,
                ActiveSyncJobs = allSyncJobs.Count(s => 
                    s.Status == SyncStatus.SYNCING || 
                    s.Status == SyncStatus.SYNC_RETRY),
                CompletedSyncJobs = allSyncJobs.Count(s => s.Status == SyncStatus.COMPLETED),
                FailedSyncJobs = allSyncJobs.Count(s => s.Status == SyncStatus.FAILED),
                SyncingJobs = allSyncJobs.Count(s => s.Status == SyncStatus.SYNCING),
                NotStartedJobs = allSyncJobs.Count(s => s.Status == SyncStatus.NotStarted),
                RetryingJobs = allSyncJobs.Count(s => s.Status == SyncStatus.SYNC_RETRY)
            };

            return Result.Success(statistics);
        }
    }
}

