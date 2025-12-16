using System.Text.Json;
using StackExchange.Redis;
using TorreClou.Core.DTOs.Common;
using TorreClou.Core.DTOs.Jobs;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Models.Pricing;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services
{
    public class JobService(
        IUnitOfWork unitOfWork,
        IConnectionMultiplexer redis) : IJobService
    {
        private const string JobStreamKey = "jobs:stream";

        public async Task<Result<JobCreationResult>> CreateAndDispatchJobAsync(int invoiceId, int userId)
        {
            // 1. Load Invoice
            var invoiceSpec = new BaseSpecification<Invoice>(i => i.Id == invoiceId && i.UserId == userId);
            invoiceSpec.AddInclude(i => i.TorrentFile);
            var invoice = await unitOfWork.Repository<Invoice>().GetEntityWithSpec(invoiceSpec);

            if (invoice == null)
                return Result<JobCreationResult>.Failure("INVOICE_NOT_FOUND", "Invoice not found.");

            if (invoice.PaidAt == null)
                return Result<JobCreationResult>.Failure("INVOICE_NOT_PAID", "Invoice has not been paid.");

            if (invoice.JobId != null)
                return Result<JobCreationResult>.Failure("JOB_ALREADY_EXISTS", "A job has already been created for this invoice.");

            // 2. Find user's default StorageProfile
            var storageProfileSpec = new BaseSpecification<UserStorageProfile>(
                sp => sp.UserId == userId && sp.IsDefault && sp.IsActive
            );
            var defaultStorageProfile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(storageProfileSpec);

            bool hasStorageProfileWarning = defaultStorageProfile == null;
            string? warningMessage = hasStorageProfileWarning
                ? "No default storage profile configured. Please set up a storage profile to receive your files."
                : null;

            // 3. Extract SelectedFileIndices from PricingSnapshot
            var selectedFileIndices = ExtractSelectedFilesFromSnapshot(invoice.PricingSnapshotJson);

            // 4. Create UserJob
            var job = new UserJob
            {
                UserId = userId,
                StorageProfileId = defaultStorageProfile?.Id ?? 0,
                Status = JobStatus.QUEUED,
                Type = JobType.Torrent,
                RequestFileId = invoice.TorrentFileId,
                SelectedFileIndices = selectedFileIndices
            };

            unitOfWork.Repository<UserJob>().Add(job);
            await unitOfWork.Complete();

            // 5. Link Invoice to Job
            invoice.JobId = job.Id;
            await unitOfWork.Complete();

            // 6. Publish to Redis Stream (guaranteed delivery)
            var db = redis.GetDatabase();
            await db.StreamAddAsync(JobStreamKey, [
                new NameValueEntry("jobId", job.Id.ToString()),
                new NameValueEntry("userId", userId.ToString()),
                new NameValueEntry("jobType", job.Type.ToString()),
                new NameValueEntry("createdAt", DateTime.UtcNow.ToString("O"))
            ]);

            return Result.Success(new JobCreationResult
            {
                JobId = job.Id,
                InvoiceId = invoiceId,
                StorageProfileId = defaultStorageProfile?.Id,
                HasStorageProfileWarning = hasStorageProfileWarning,
                StorageProfileWarningMessage = warningMessage
            });
        }

        public async Task<Result<PaginatedResult<JobDto>>> GetUserJobsAsync(int userId, int pageNumber, int pageSize, JobStatus? status = null)
        {
            var spec = new UserJobsSpecification(userId, pageNumber, pageSize, status);
            var countSpec = new BaseSpecification<UserJob>(job => job.UserId == userId && (status == null || job.Status == status));

            var jobs = await unitOfWork.Repository<UserJob>().ListAsync(spec);
            var totalCount = await unitOfWork.Repository<UserJob>().CountAsync(countSpec);

            var items = jobs.Select(job => new JobDto
            {
                Id = job.Id,
                StorageProfileId = job.StorageProfileId,
                StorageProfileName = job.StorageProfile?.ProfileName,
                Status = job.Status.ToString(),
                Type = job.Type.ToString(),
                RequestFileId = job.RequestFileId,
                RequestFileName = job.RequestFile?.FileName,
                ErrorMessage = job.ErrorMessage,
                CurrentState = job.CurrentState,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                LastHeartbeat = job.LastHeartbeat,
                BytesDownloaded = job.BytesDownloaded,
                TotalBytes = job.TotalBytes,
                SelectedFileIndices = job.SelectedFileIndices,
                CreatedAt = job.CreatedAt,
                UpdatedAt = job.UpdatedAt
            }).ToList();

            return Result.Success(new PaginatedResult<JobDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        public async Task<Result<JobDto>> GetJobByIdAsync(int userId, int jobId)
        {
            var spec = new BaseSpecification<UserJob>(job => job.Id == jobId && job.UserId == userId);
            spec.AddInclude(job => job.StorageProfile);
            spec.AddInclude(job => job.RequestFile);

            var job = await unitOfWork.Repository<UserJob>().GetEntityWithSpec(spec);

            if (job == null)
            {
                return Result<JobDto>.Failure("NOT_FOUND", "Job not found.");
            }

            return Result.Success(new JobDto
            {
                Id = job.Id,
                StorageProfileId = job.StorageProfileId,
                StorageProfileName = job.StorageProfile?.ProfileName,
                Status = job.Status.ToString(),
                Type = job.Type.ToString(),
                RequestFileId = job.RequestFileId,
                RequestFileName = job.RequestFile?.FileName,
                ErrorMessage = job.ErrorMessage,
                CurrentState = job.CurrentState,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                LastHeartbeat = job.LastHeartbeat,
                BytesDownloaded = job.BytesDownloaded,
                TotalBytes = job.TotalBytes,
                SelectedFileIndices = job.SelectedFileIndices,
                CreatedAt = job.CreatedAt,
                UpdatedAt = job.UpdatedAt
            });
        }

        public async Task<Result<JobStatisticsDto>> GetUserJobStatisticsAsync(int userId)
        {
            var allJobsSpec = new BaseSpecification<UserJob>(job => job.UserId == userId);
            var allJobs = await unitOfWork.Repository<UserJob>().ListAsync(allJobsSpec);

            var statistics = new JobStatisticsDto
            {
                TotalJobs = allJobs.Count,
                ActiveJobs = allJobs.Count(job => 
                    job.Status == JobStatus.QUEUED || 
                    job.Status == JobStatus.PROCESSING || 
                    job.Status == JobStatus.UPLOADING),
                CompletedJobs = allJobs.Count(job => job.Status == JobStatus.COMPLETED),
                FailedJobs = allJobs.Count(job => job.Status == JobStatus.FAILED),
                QueuedJobs = allJobs.Count(job => job.Status == JobStatus.QUEUED),
                ProcessingJobs = allJobs.Count(job => job.Status == JobStatus.PROCESSING),
                UploadingJobs = allJobs.Count(job => job.Status == JobStatus.UPLOADING),
                CancelledJobs = allJobs.Count(job => job.Status == JobStatus.CANCELLED)
            };

            return Result.Success(statistics);
        }

        private static int[] ExtractSelectedFilesFromSnapshot(string pricingSnapshotJson)
        {
            if (string.IsNullOrEmpty(pricingSnapshotJson))
                return [];

            try
            {
                var snapshot = JsonSerializer.Deserialize<PricingSnapshot>(pricingSnapshotJson);
                return snapshot?.SelectedFiles?.ToArray() ?? [];
            }
            catch
            {
                return [];
            }
        }
    }
}

