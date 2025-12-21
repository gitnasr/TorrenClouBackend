using System.Text.Json;
using TorreClou.Core.DTOs.Common;
using TorreClou.Core.DTOs.Jobs;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Models.Pricing;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;
using TorreClou.Core.Extensions;
namespace TorreClou.Application.Services
{
    public class JobService(
        IUnitOfWork unitOfWork,
        IRedisStreamService redisStreamService) : IJobService
    {
        private const string JobStreamKey = "jobs:stream";

        public async Task<Result<JobCreationResult>> CreateAndDispatchJobAsync(int invoiceId, int userId)
        {
            // 1. Load Invoice
            var invoiceSpec = new BaseSpecification<Invoice>(i => i.Id == invoiceId && i.UserId == userId);
            invoiceSpec.AddInclude(i => i.TorrentFile);
            var invoice = await unitOfWork.Repository<Invoice>().GetEntityWithSpec(invoiceSpec);

            if (invoice == null) return Result<JobCreationResult>.Failure("INVOICE_NOT_FOUND", "Invoice not found.");
            if (invoice.PaidAt == null) return Result<JobCreationResult>.Failure("INVOICE_NOT_PAID", "Invoice has not been paid.");
            if (invoice.JobId != null) return Result<JobCreationResult>.Failure("JOB_ALREADY_EXISTS", "A job has already been created for this invoice.");


            // 1.5. Check for existing active jobs (REFACTORED)

            var existingJobSpec = new BaseSpecification<UserJob>(j =>
                j.UserId == userId &&
                j.RequestFileId == invoice.TorrentFileId &&
                (j.Status != JobStatus.COMPLETED && j.Status != JobStatus.FAILED && j.Status != JobStatus.CANCELLED &&
                 j.Status != JobStatus.TORRENT_FAILED && j.Status != JobStatus.UPLOAD_FAILED && j.Status != JobStatus.GOOGLE_DRIVE_FAILED)
            );

            var existingJob = await unitOfWork.Repository<UserJob>().GetEntityWithSpec(existingJobSpec);

            if (existingJob != null)
            {
                // Use Extensions for readable business logic
                if (existingJob.Status.IsRetrying())
                {
                    var nextRetry = existingJob.NextRetryAt.HasValue
                        ? $" Next retry: {existingJob.NextRetryAt.Value:u}" : "";
                    return Result<JobCreationResult>.Failure("JOB_RETRYING",
                        $"Job {existingJob.Id} is currently retrying.{nextRetry}");
                }

                return Result<JobCreationResult>.Failure("JOB_ALREADY_EXISTS",
                    $"Active job exists. ID: {existingJob.Id}, Status: {existingJob.Status}");
            }
            var defualtStorageProfileSpec = new BaseSpecification<UserStorageProfile>(sp => sp.UserId == userId && sp.IsDefault && sp.IsActive );
            var defaultStorageProfile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(defualtStorageProfileSpec);
            if (defaultStorageProfile == null)
                return Result< JobCreationResult>.Failure("NO_STORAGE", "You don't have any stored or active Storage Destenation");


            // 4. Create UserJob
            var job = new UserJob
            {
                UserId = userId,
                StorageProfileId = defaultStorageProfile?.Id ?? 0,
                Status = JobStatus.QUEUED,
                Type = JobType.Torrent,
                RequestFileId = invoice.TorrentFileId,
                SelectedFileIndices = ExtractSelectedFilesFromSnapshot(invoice.PricingSnapshotJson)
            };

            unitOfWork.Repository<UserJob>().Add(job);
            await unitOfWork.Complete();

            // 5. Link Invoice
            invoice.JobId = job.Id;
            await unitOfWork.Complete();

            // 6. Publish to Stream
            await redisStreamService.PublishAsync(JobStreamKey, new Dictionary<string, string>
            {
                { "jobId", job.Id.ToString() },
                { "userId", userId.ToString() },
                { "jobType", job.Type.ToString() },
                { "createdAt", DateTime.UtcNow.ToString("O") }
            });

            return Result.Success(new JobCreationResult
            {
                JobId = job.Id,
                InvoiceId = invoiceId,
                StorageProfileId = defaultStorageProfile?.Id,
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
                Status = job.Status,
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
                Status = job.Status,
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
                ActiveJobs = allJobs.Count(j => j.Status.IsActive()), // <--- Clean
                CompletedJobs = allJobs.Count(j => j.Status.IsCompleted()),
                FailedJobs = allJobs.Count(j => j.Status.IsFailed()),

                // Granular counts still use direct comparison
                QueuedJobs = allJobs.Count(j => j.Status == JobStatus.QUEUED),
                DownloadingJobs = allJobs.Count(j => j.Status == JobStatus.DOWNLOADING),
                PendingUploadJobs = allJobs.Count(j => j.Status == JobStatus.PENDING_UPLOAD),
                UploadingJobs = allJobs.Count(j => j.Status == JobStatus.UPLOADING),
                RetryingJobs = allJobs.Count(j => j.Status.IsRetrying()), // <--- Clean
                CancelledJobs = allJobs.Count(j => j.Status.IsCancelled())
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

