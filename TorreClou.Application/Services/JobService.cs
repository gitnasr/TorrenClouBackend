using System.Text.Json;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TorreClou.Core.DTOs.Common;
using TorreClou.Core.DTOs.Jobs;
using TorreClou.Core.Entities.Financals;
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
        IRedisStreamService redisStreamService,
        IJobStatusService jobStatusService,
        IServiceScopeFactory serviceScopeFactory,
        IWalletService walletService,
        IJobHandlerFactory jobHandlerFactory,
        ILogger<JobService> logger) : IJobService
    {
        private const string JobStreamKey = "jobs:stream";

        public async Task<Result<JobCreationResult>> CreateAndDispatchJobAsync(int invoiceId, int userId)
        {
            logger.LogInformation("Create and dispatch job requested | InvoiceId: {InvoiceId} | UserId: {UserId}", invoiceId, userId);

            // 1. Load Invoice: Move it to invoice service and inject it 
            var invoiceSpec = new BaseSpecification<Invoice>(i => i.Id == invoiceId && i.UserId == userId);
            invoiceSpec.AddInclude(i => i.TorrentFile);
            var invoice = await unitOfWork.Repository<Invoice>().GetEntityWithSpec(invoiceSpec);

            // 2. Validate invoice for job creation
            var invoiceValidation = ValidateInvoiceForJobCreation(invoice, userId);
            if (invoiceValidation.IsFailure)
                return Result<JobCreationResult>.Failure(invoiceValidation.Error.Code, invoiceValidation.Error.Message);

            invoice = invoiceValidation.Value;

            // 3. Check for existing active jobs
            var existingJobCheck = await CheckExistingActiveJobAsync(userId, invoice.TorrentFileId);
            if (existingJobCheck.IsFailure)
                return Result<JobCreationResult>.Failure(existingJobCheck.Error.Code, existingJobCheck.Error.Message);

            // 4. Validate storage profile
            var defaultStorageProfileSpec = new BaseSpecification<UserStorageProfile>(sp => sp.UserId == userId && sp.IsDefault && sp.IsActive);
            var defaultStorageProfile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(defaultStorageProfileSpec);
            
            var profileValidation = ValidateStorageProfileForJob(defaultStorageProfile, userId);
            if (profileValidation.IsFailure)
                return Result<JobCreationResult>.Failure(profileValidation.Error.Code, profileValidation.Error.Message);

            defaultStorageProfile = profileValidation.Value;


            // 5. Create UserJob (default job type from first handler, can be extended for multiple job types)
            var defaultJobType = jobHandlerFactory.GetAllJobTypeHandlers().FirstOrDefault()?.JobType ?? JobType.Torrent;
            var job = new UserJob
            {
                UserId = userId,
                StorageProfileId = defaultStorageProfile.Id,
                Status = JobStatus.QUEUED,
                Type = defaultJobType,
                RequestFileId = invoice.TorrentFileId,
                SelectedFilePaths = ExtractSelectedFilesFromSnapshot(invoice.PricingSnapshotJson)
            };

            unitOfWork.Repository<UserJob>().Add(job);
            await unitOfWork.Complete();

            logger.LogInformation("Job created | JobId: {JobId} | InvoiceId: {InvoiceId} | StorageProfileId: {StorageProfileId} | RequestFileId: {RequestFileId}", 
                job.Id, invoiceId, defaultStorageProfile.Id, invoice.TorrentFileId);

            // 6. Record initial status in timeline
            await jobStatusService.RecordInitialJobStatusAsync(job, new
            {
                invoiceId,
                storageProfileId = defaultStorageProfile.Id,
                requestFileId = invoice.TorrentFileId,
                selectedFilesCount = job.SelectedFilePaths.Length
            });

            // 7. Link Invoice
            invoice.JobId = job.Id;
            await unitOfWork.Complete();

            // 8. Publish to Stream
            await redisStreamService.PublishAsync(JobStreamKey, new Dictionary<string, string>
            {
                { "jobId", job.Id.ToString() },
                { "userId", userId.ToString() },
                { "jobType", job.Type.ToString() },
                { "createdAt", DateTime.UtcNow.ToString("O") }
            });

            logger.LogInformation("Job creation and dispatch completed successfully | JobId: {JobId} | InvoiceId: {InvoiceId} | UserId: {UserId}", 
                job.Id, invoiceId, userId);

            return Result.Success(new JobCreationResult
            {
                JobId = job.Id,
                InvoiceId = invoiceId,
                StorageProfileId = defaultStorageProfile.Id,
            });
        }
        public async Task<Result<PaginatedResult<JobDto>>> GetUserJobsAsync(int userId, int pageNumber, int pageSize, JobStatus? status = null)
        {
            logger.LogDebug("Get user jobs requested | UserId: {UserId} | Page: {PageNumber} | PageSize: {PageSize} | Status: {Status}", userId, pageNumber, pageSize, status);

            var spec = new UserJobsSpecification(userId, pageNumber, pageSize, status);
            var countSpec = new BaseSpecification<UserJob>(job => 
                job.UserId == userId && (status == null || job.Status == status)
               ); 

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
                SelectedFilePaths = job.SelectedFilePaths,
                CreatedAt = job.CreatedAt,
                UpdatedAt = job.UpdatedAt,
                IsRefunded = job.IsRefunded
            }).ToList();

            return Result.Success(new PaginatedResult<JobDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        public async Task<Result<JobDto>> GetJobByIdAsync(int userId, int jobId, UserRole? userRole = null)
        {
            logger.LogDebug("Get job by ID requested | JobId: {JobId} | UserId: {UserId} | Role: {Role}", jobId, userId, userRole);

            var spec = new BaseSpecification<UserJob>(job => 
                job.Id == jobId && 
                job.UserId == userId ); 
            spec.AddInclude(job => job.StorageProfile);
            spec.AddInclude(job => job.RequestFile);

            var job = await unitOfWork.Repository<UserJob>().GetEntityWithSpec(spec);

            if (job == null)
            {
                logger.LogWarning("Job not found | JobId: {JobId} | UserId: {UserId}", jobId, userId);
                return Result<JobDto>.Failure("NOT_FOUND", "Job not found.");
            }

            // Fetch timeline
            var timeline = await jobStatusService.GetJobTimelineAsync(jobId);

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
                SelectedFilePaths = job.SelectedFilePaths,
                CreatedAt = job.CreatedAt,
                UpdatedAt = job.UpdatedAt,
                IsRefunded = job.IsRefunded,
                Timeline = timeline.ToList()
            });
        }

        public async Task<Result<JobStatisticsDto>> GetUserJobStatisticsAsync(int userId)
        {
            logger.LogDebug("Get user job statistics requested | UserId: {UserId}", userId);

            var allJobsSpec = new BaseSpecification<UserJob>(job => job.UserId == userId);
            var allJobs = await unitOfWork.Repository<UserJob>().ListAsync(allJobsSpec);

            var statistics = new JobStatisticsDto
            {
                TotalJobs = allJobs.Count,
                ActiveJobs = allJobs.Count(j => j.Status.IsActive()),
                CompletedJobs = allJobs.Count(j => j.Status.IsCompleted()),
                FailedJobs = allJobs.Count(j => j.Status.IsFailed()),

                // Granular counts
                QueuedJobs = allJobs.Count(j => j.Status == JobStatus.QUEUED),
                DownloadingJobs = allJobs.Count(j => j.Status == JobStatus.DOWNLOADING),
                PendingUploadJobs = allJobs.Count(j => j.Status == JobStatus.PENDING_UPLOAD),
                UploadingJobs = allJobs.Count(j => j.Status == JobStatus.UPLOADING),
                RetryingJobs = allJobs.Count(j => j.Status.IsRetrying()),
                CancelledJobs = allJobs.Count(j => j.Status.IsCancelled())
            };

            // Build user-based available filters: only statuses with at least one job
            statistics.StatusFilters = allJobs
                .GroupBy(j => j.Status)
                .Select(g => new JobStatusFilterDto
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .Where(f => f.Count > 0)
                .OrderByDescending(f => f.Count)
                .ToList();

            return Result.Success(statistics);
        }

        private static string[] ExtractSelectedFilesFromSnapshot(string pricingSnapshotJson)
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

        public async Task<Result<IReadOnlyList<UserJob>>> GetActiveJobsByStorageProfileIdAsync(int storageProfileId)
        {
            logger.LogDebug("Get active jobs by storage profile requested | StorageProfileId: {StorageProfileId}", storageProfileId);

            var spec = new ActiveJobsByStorageProfileSpecification(storageProfileId);
            var activeJobs = await unitOfWork.Repository<UserJob>().ListAsync(spec);
            
            logger.LogDebug("Found {Count} active jobs for storage profile | StorageProfileId: {StorageProfileId}", activeJobs.Count, storageProfileId);
            return Result.Success(activeJobs);
        }

        public async Task<Result<bool>> RetryJobAsync(int jobId, int userId, UserRole? userRole = null)
        {
            logger.LogInformation("Retry job requested | JobId: {JobId} | UserId: {UserId} | Role: {Role}", jobId, userId, userRole);

            // 1. Load job with all necessary includes
            var spec = new BaseSpecification<UserJob>(j => j.Id == jobId);
            spec.AddInclude(j => j.StorageProfile);
            spec.AddInclude(j => j.RequestFile);
            spec.AddInclude(j => j.User);
            spec.AddInclude(j => j.Invoice);

            var job = await unitOfWork.Repository<UserJob>().GetEntityWithSpec(spec);

            // 2. Validate job exists and user is authorized
            var jobValidation = ValidateJobExistsAndAuthorized(job, userId, userRole, "retry");
            if (jobValidation.IsFailure)
                return Result<bool>.Failure(jobValidation.Error.Code, jobValidation.Error.Message);

            job = jobValidation.Value;

            // 3. Validate job can be retried
            var retryValidation = ValidateJobForRetry(job, userId, userRole);
            if (retryValidation.IsFailure)
                return retryValidation;

            // 4. Cancel existing Hangfire jobs if they exist
            using var scope = serviceScopeFactory.CreateScope();
            var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
            var monitoringApi = JobStorage.Current?.GetMonitoringApi();

            await CancelExistingHangfireJobsAsync(job, backgroundJobClient, monitoringApi);

            // 5. Determine retry strategy based on current state
            var previousStatus = job.Status;
            string? newHangfireJobId = null;
            JobStatus targetStatus;
            StatusChangeSource source = userRole == UserRole.Admin
                ? StatusChangeSource.System
                : StatusChangeSource.User;

            if (IsUploadPhase(job.Status))
            {
                // Retry from upload phase
                targetStatus = JobStatus.PENDING_UPLOAD;
                newHangfireJobId = await RetryUploadPhaseAsync(job, backgroundJobClient);
                logger.LogInformation("Retrying job from upload phase | JobId: {JobId} | PreviousStatus: {PreviousStatus} | HangfireJobId: {HangfireJobId}", job.Id, previousStatus, newHangfireJobId);
            }
            else
            {
                // Retry from download phase (or start from beginning)
                targetStatus = JobStatus.QUEUED;
                newHangfireJobId = await RetryDownloadPhaseAsync(job, backgroundJobClient);
                logger.LogInformation("Retrying job from download phase | JobId: {JobId} | PreviousStatus: {PreviousStatus} | HangfireJobId: {HangfireJobId}", job.Id, previousStatus, newHangfireJobId);
            }

            // 6. Update job state atomically
            job.HangfireJobId = newHangfireJobId;
            job.HangfireUploadJobId = null; // Clear upload job ID if retrying from download
            job.ErrorMessage = null; // Clear previous error
            job.NextRetryAt = null; // Clear retry schedule
            job.LastHeartbeat = DateTime.UtcNow;
            job.CurrentState = $"Manually retried by {(userRole == UserRole.Admin ? "admin" : "user")}";

            // 7. Record status transition with full audit trail
            await jobStatusService.TransitionJobStatusAsync(
                job,
                targetStatus,
                source,
                metadata: new
                {
                    retriedFrom = previousStatus.ToString(),
                    retriedBy = userId,
                    retriedByRole = userRole?.ToString(),
                    retriedAt = DateTime.UtcNow,
                    hangfireJobId = newHangfireJobId
                });

            logger.LogInformation("Job retry completed successfully | JobId: {JobId} | TargetStatus: {TargetStatus} | UserId: {UserId}", job.Id, targetStatus, userId);
            return Result.Success(true);
        }

        public async Task<Result<bool>> CancelJobAsync(int jobId, int userId, UserRole? userRole = null)
        {
            logger.LogInformation("Cancel job requested | JobId: {JobId} | UserId: {UserId} | Role: {Role}", jobId, userId, userRole);

            // 1. Load job with all necessary includes
            var spec = new BaseSpecification<UserJob>(j => j.Id == jobId);
            spec.AddInclude(j => j.StorageProfile);
            spec.AddInclude(j => j.Invoice);

            var job = await unitOfWork.Repository<UserJob>().GetEntityWithSpec(spec);

            // 2. Validate job exists and user is authorized
            var jobValidation = ValidateJobExistsAndAuthorized(job, userId, userRole, "cancel");
            if (jobValidation.IsFailure)
                return Result<bool>.Failure(jobValidation.Error.Code, jobValidation.Error.Message);

            job = jobValidation.Value;

            // 3. Validate job can be cancelled
            var cancelValidation = ValidateJobForCancel(job, userId, userRole);
            if (cancelValidation.IsFailure)
                return cancelValidation;

            // 4. Cancel Hangfire jobs
            using var scope = serviceScopeFactory.CreateScope();
            var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
            var monitoringApi = JobStorage.Current?.GetMonitoringApi();

            await CancelExistingHangfireJobsAsync(job, backgroundJobClient, monitoringApi);

            // 5. Execute job-type-specific cancellation (cleanup, stop manager, etc.)
            var cancellationHandler = jobHandlerFactory.GetCancellationHandler(job.Type);
            if (cancellationHandler != null)
            {
                await cancellationHandler.CancelJobAsync(job);
                logger.LogDebug("Executed cancellation handler for job type {JobType} | JobId: {JobId}", job.Type, job.Id);
            }

            // 6. Delete storage provider locks if exists
            if (job.StorageProfile != null)
            {
                var storageHandler = jobHandlerFactory.GetStorageProviderHandler(job.StorageProfile.ProviderType);
                if (storageHandler != null)
                {
                    await storageHandler.DeleteUploadLockAsync(job.Id);
                    logger.LogDebug("Deleted upload lock for provider {Provider} | JobId: {JobId}", job.StorageProfile.ProviderType, job.Id);
                }
            }

            // 8. Update job state
            job.CompletedAt = DateTime.UtcNow;
            job.HangfireJobId = null;
            job.HangfireUploadJobId = null;
            job.CurrentState = $"Cancelled by {(userRole == UserRole.Admin ? "admin" : "user")}";

            StatusChangeSource source = userRole == UserRole.Admin
                ? StatusChangeSource.System
                : StatusChangeSource.User;

            // 9. Transition status (this will set job.Status and save all changes)
            await jobStatusService.TransitionJobStatusAsync(
                job,
                JobStatus.CANCELLED,
                source,
                metadata: new
                {
                    cancelledBy = userId,
                    cancelledByRole = userRole?.ToString(),
                    cancelledAt = DateTime.UtcNow
                });

            // 10. Automatic Refund
            if (job.Invoice != null && job.Invoice.PaidAt != null && job.Invoice.RefundedAt == null)
            {
                logger.LogInformation("Processing automatic refund for cancelled job | JobId: {JobId} | InvoiceId: {InvoiceId} | Amount: {Amount}", job.Id, job.Invoice.Id, job.Invoice.FinalAmountInNCurrency);
                var refundResult = await walletService.ProcessRefundAsync(
                    job.UserId,
                    job.Invoice.FinalAmountInNCurrency,
                    job.Invoice.Id,
                    $"Refund for cancelled job #{job.Id}");

                if (refundResult.IsSuccess)
                {
                    job.Invoice.RefundedAt = DateTime.UtcNow;
                    job.IsRefunded = true;
                    await unitOfWork.Complete();
                    logger.LogInformation("Automatic refund processed successfully | JobId: {JobId} | TransactionId: {TransactionId}", job.Id, refundResult.Value);
                }
                else
                {
                    logger.LogWarning("Automatic refund failed for cancelled job | JobId: {JobId} | Error: {Error}", job.Id, refundResult.Error.Message);
                    // Log error but don't fail the cancellation
                    // The job is already cancelled, refund can be processed manually
                }
            }

            logger.LogInformation("Job cancellation completed successfully | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
            return Result.Success(true);
        }

        public async Task<Result<bool>> RefundJobAsync(int jobId, int userId)
        {
            logger.LogInformation("Refund job requested | JobId: {JobId} | UserId: {UserId}", jobId, userId);

            // 1. Load job with all necessary includes
            var spec = new BaseSpecification<UserJob>(j => j.Id == jobId);
            spec.AddInclude(j => j.Invoice);

            var job = await unitOfWork.Repository<UserJob>().GetEntityWithSpec(spec);

            // 2. Validate job exists and user is authorized
            var jobValidation = ValidateJobExistsAndAuthorized(job, userId, null, "refund");
            if (jobValidation.IsFailure)
                return Result<bool>.Failure(jobValidation.Error.Code, jobValidation.Error.Message);

            job = jobValidation.Value;

            // 3. Validate job can be refunded
            var refundValidation = ValidateJobForRefund(job, userId);
            if (refundValidation.IsFailure)
                return refundValidation;

            // 4. Process refund
            logger.LogInformation("Processing refund | JobId: {JobId} | InvoiceId: {InvoiceId} | Amount: {Amount}", job.Id, job.Invoice!.Id, job.Invoice.FinalAmountInNCurrency);
            var refundResult = await walletService.ProcessRefundAsync(
                userId,
                job.Invoice.FinalAmountInNCurrency,
                job.Invoice.Id,
                $"Refund for failed job #{job.Id}");

            if (refundResult.IsFailure)
            {
                logger.LogError("Refund processing failed | JobId: {JobId} | Error: {Error}", job.Id, refundResult.Error.Message);
                return Result<bool>.Failure(refundResult.Error.Code, refundResult.Error.Message);
            }

            // 5. Update invoice and job
            job.Invoice.RefundedAt = DateTime.UtcNow;
            job.IsRefunded = true;
            await unitOfWork.Complete();

            // 6. Record refund in status history
            await jobStatusService.TransitionJobStatusAsync(
                job,
                job.Status, // Keep current failed status
                StatusChangeSource.User,
                metadata: new
                {
                    refunded = true,
                    refundedAt = DateTime.UtcNow,
                    refundTransactionId = refundResult.Value,
                    refundAmount = job.Invoice.FinalAmountInNCurrency
                });

            logger.LogInformation("Job refund completed successfully | JobId: {JobId} | TransactionId: {TransactionId} | UserId: {UserId}", job.Id, refundResult.Value, userId);
            return Result.Success(true);
        }

        // --- Guard Check Helper Methods ---

        private Result<UserJob> ValidateJobExistsAndAuthorized(UserJob? job, int userId, UserRole? userRole, string operation)
        {
            if (job == null)
            {
                logger.LogWarning("Job not found for {Operation} | UserId: {UserId}", operation, userId);
                return Result<UserJob>.Failure("JOB_NOT_FOUND", "Job not found.");
            }

            if (userRole != UserRole.Admin && job.UserId != userId)
            {
                logger.LogWarning("Unauthorized {Operation} attempt | JobId: {JobId} | UserId: {UserId}", operation, job.Id, userId);
                return Result<UserJob>.Failure("UNAUTHORIZED", $"You don't have permission to {operation.ToLower()} this job.");
            }

            return Result.Success(job);
        }

        private Result<bool> ValidateJobForRetry(UserJob job, int userId, UserRole? userRole)
        {
            if (job.Status == JobStatus.COMPLETED)
            {
                logger.LogWarning("Attempt to retry completed job | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                return Result<bool>.Failure("JOB_COMPLETED", "Cannot retry a completed job.");
            }

            if (job.Status == JobStatus.CANCELLED)
            {
                logger.LogWarning("Attempt to retry cancelled job | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                return Result<bool>.Failure("JOB_CANCELLED", "Cannot retry a cancelled job.");
            }

            if (job.IsRefunded)
            {
                logger.LogWarning("Attempt to retry refunded job | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                return Result<bool>.Failure("JOB_REFUNDED", "Cannot retry a refunded job. Please create a new job.");
            }

            if (job.Status.IsActive())
            {
                logger.LogWarning("Attempt to retry active job | JobId: {JobId} | Status: {Status} | UserId: {UserId}", job.Id, job.Status, userId);
                return Result<bool>.Failure("JOB_ACTIVE",
                    $"Job is currently {job.Status}. Wait for it to complete or fail before retrying.");
            }

            if (job.StorageProfile == null || !job.StorageProfile.IsActive)
            {
                logger.LogWarning("Attempt to retry job with inactive storage profile | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                return Result<bool>.Failure("STORAGE_INACTIVE",
                    "The storage profile for this job is no longer active.");
            }

            return Result.Success(true);
        }

        private Result<bool> ValidateJobForCancel(UserJob job, int userId, UserRole? userRole)
        {
            if (job.Status == JobStatus.COMPLETED)
            {
                logger.LogWarning("Attempt to cancel completed job | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                return Result<bool>.Failure("JOB_COMPLETED", "Cannot cancel a completed job.");
            }

            if (job.Status == JobStatus.CANCELLED)
            {
                logger.LogWarning("Attempt to cancel already cancelled job | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                return Result<bool>.Failure("JOB_ALREADY_CANCELLED", "Job is already cancelled.");
            }

            // Prevent cancellation during upload phase
            if (IsUploadPhase(job.Status))
            {
                logger.LogWarning("Attempt to cancel job during upload phase | JobId: {JobId} | Status: {Status} | UserId: {UserId}", job.Id, job.Status, userId);
                return Result<bool>.Failure("JOB_IN_UPLOAD_PHASE",
                    "Cannot cancel a job during upload phase. Please wait for the upload to complete.");
            }

            if (!job.Status.IsActive() && !job.Status.IsFailed())
            {
                logger.LogWarning("Attempt to cancel non-cancellable job | JobId: {JobId} | Status: {Status} | UserId: {UserId}", job.Id, job.Status, userId);
                return Result<bool>.Failure("JOB_NOT_CANCELLABLE",
                    $"Job status {job.Status} cannot be cancelled.");
            }

            return Result.Success(true);
        }

        private Result<bool> ValidateJobForRefund(UserJob job, int userId)
        {
            if (!job.Status.IsFailed())
            {
                logger.LogWarning("Attempt to refund non-failed job | JobId: {JobId} | Status: {Status} | UserId: {UserId}", job.Id, job.Status, userId);
                return Result<bool>.Failure("JOB_NOT_FAILED",
                    "Only failed jobs can be refunded.");
            }

            if (job.IsRefunded)
            {
                logger.LogWarning("Attempt to refund already refunded job | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                return Result<bool>.Failure("JOB_ALREADY_REFUNDED", "This job has already been refunded.");
            }

            if (job.Invoice == null)
            {
                logger.LogWarning("Attempt to refund job without invoice | JobId: {JobId} | UserId: {UserId}", job.Id, userId);
                return Result<bool>.Failure("INVOICE_NOT_FOUND", "Invoice not found for this job.");
            }

            if (job.Invoice.PaidAt == null)
            {
                logger.LogWarning("Attempt to refund job with unpaid invoice | JobId: {JobId} | InvoiceId: {InvoiceId} | UserId: {UserId}", job.Id, job.Invoice.Id, userId);
                return Result<bool>.Failure("INVOICE_NOT_PAID", "Invoice has not been paid.");
            }

            if (job.Invoice.RefundedAt != null)
            {
                logger.LogWarning("Attempt to refund job with already refunded invoice | JobId: {JobId} | InvoiceId: {InvoiceId} | UserId: {UserId}", job.Id, job.Invoice.Id, userId);
                return Result<bool>.Failure("INVOICE_ALREADY_REFUNDED", "Invoice has already been refunded.");
            }

            return Result.Success(true);
        }

        private Result<Invoice> ValidateInvoiceForJobCreation(Invoice? invoice, int userId)
        {
            if (invoice == null)
            {
                logger.LogWarning("Invoice not found for job creation | InvoiceId: {InvoiceId} | UserId: {UserId}", userId, userId);
                return Result<Invoice>.Failure("INVOICE_NOT_FOUND", "Invoice not found.");
            }

            if (invoice.PaidAt == null)
            {
                logger.LogWarning("Attempt to create job from unpaid invoice | InvoiceId: {InvoiceId} | UserId: {UserId}", invoice.Id, userId);
                return Result<Invoice>.Failure("INVOICE_NOT_PAID", "Invoice has not been paid.");
            }

            if (invoice.JobId != null)
            {
                logger.LogWarning("Attempt to create job from invoice that already has a job | InvoiceId: {InvoiceId} | JobId: {JobId} | UserId: {UserId}", invoice.Id, invoice.JobId, userId);
                return Result<Invoice>.Failure("JOB_ALREADY_EXISTS", "A job has already been created for this invoice.");
            }

            return Result.Success(invoice);
        }

        private Result<UserStorageProfile> ValidateStorageProfileForJob(UserStorageProfile? profile, int userId)
        {
            if (profile == null)
            {
                logger.LogWarning("No active storage profile found for job creation | UserId: {UserId}", userId);
                return Result<UserStorageProfile>.Failure("NO_STORAGE", "You don't have any stored or active Storage Destination");
            }

            return Result.Success(profile);
        }

        private async Task<Result<UserJob?>> CheckExistingActiveJobAsync(int userId, int requestFileId)
        {
            // Get all failed statuses from all registered job type handlers
            var failedStatuses = new HashSet<JobStatus>
            {
                JobStatus.COMPLETED,
                JobStatus.FAILED,
                JobStatus.CANCELLED
            };

            foreach (var handler in jobHandlerFactory.GetAllJobTypeHandlers())
            {
                foreach (var status in handler.GetFailedStatuses())
                {
                    failedStatuses.Add(status);
                }
            }

            var existingJobSpec = new BaseSpecification<UserJob>(j =>
                j.UserId == userId &&
                j.RequestFileId == requestFileId &&
                !failedStatuses.Contains(j.Status)
            );

            var existingJob = await unitOfWork.Repository<UserJob>().GetEntityWithSpec(existingJobSpec);

            if (existingJob != null)
            {
                if (existingJob.Status.IsRetrying())
                {
                    var nextRetry = existingJob.NextRetryAt.HasValue
                        ? $" Next retry: {existingJob.NextRetryAt.Value:u}" : "";
                    logger.LogWarning("Attempt to create job while existing job is retrying | ExistingJobId: {ExistingJobId} | RequestFileId: {RequestFileId} | UserId: {UserId}", existingJob.Id, requestFileId, userId);
                    return Result<UserJob?>.Failure("JOB_RETRYING",
                        $"Job {existingJob.Id} is currently retrying.{nextRetry}");
                }

                logger.LogWarning("Attempt to create job while active job exists | ExistingJobId: {ExistingJobId} | Status: {Status} | RequestFileId: {RequestFileId} | UserId: {UserId}", existingJob.Id, existingJob.Status, requestFileId, userId);
                return Result<UserJob?>.Failure("JOB_ALREADY_EXISTS",
                    $"Active job exists. ID: {existingJob.Id}, Status: {existingJob.Status}");
            }

            return Result.Success<UserJob?>(null);
        }

        private Task CancelExistingHangfireJobsAsync(UserJob job, Hangfire.IBackgroundJobClient backgroundJobClient, Hangfire.Storage.IMonitoringApi? monitoringApi)
        {
            try
            {
                // Cancel download job if exists and is still active
                if (!string.IsNullOrEmpty(job.HangfireJobId))
                {
                    if (monitoringApi != null)
                    {
                        var downloadJob = monitoringApi.JobDetails(job.HangfireJobId);
                        if (downloadJob != null && downloadJob.History?.FirstOrDefault()?.StateName != "Succeeded")
                        {
                            BackgroundJob.Delete(job.HangfireJobId);
                            logger.LogDebug("Cancelled Hangfire download job | JobId: {JobId} | HangfireJobId: {HangfireJobId}", job.Id, job.HangfireJobId);
                        }
                    }
                    else
                    {
                        // If monitoring API is not available, try to delete anyway
                        BackgroundJob.Delete(job.HangfireJobId);
                        logger.LogDebug("Cancelled Hangfire download job (no monitoring API) | JobId: {JobId} | HangfireJobId: {HangfireJobId}", job.Id, job.HangfireJobId);
                    }
                }

                // Cancel upload job if exists and is still active
                if (!string.IsNullOrEmpty(job.HangfireUploadJobId))
                {
                    if (monitoringApi != null)
                    {
                        var uploadJob = monitoringApi.JobDetails(job.HangfireUploadJobId);
                        if (uploadJob != null && uploadJob.History?.FirstOrDefault()?.StateName != "Succeeded")
                        {
                            BackgroundJob.Delete(job.HangfireUploadJobId);
                            logger.LogDebug("Cancelled Hangfire upload job | JobId: {JobId} | HangfireUploadJobId: {HangfireUploadJobId}", job.Id, job.HangfireUploadJobId);
                        }
                    }
                    else
                    {
                        // If monitoring API is not available, try to delete anyway
                        BackgroundJob.Delete(job.HangfireUploadJobId);
                        logger.LogDebug("Cancelled Hangfire upload job (no monitoring API) | JobId: {JobId} | HangfireUploadJobId: {HangfireUploadJobId}", job.Id, job.HangfireUploadJobId);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail - Hangfire might have already cleaned up
                // This is a best-effort cleanup
                logger.LogWarning(ex, "Error cancelling Hangfire jobs | JobId: {JobId}", job.Id);
            }

            return Task.CompletedTask;
        }

        private Task<string> RetryUploadPhaseAsync(UserJob job, Hangfire.IBackgroundJobClient client)
        {
            if (job.StorageProfile == null)
            {
                throw new InvalidOperationException($"Job {job.Id} has no storage profile assigned");
            }

            var storageHandler = jobHandlerFactory.GetStorageProviderHandler(job.StorageProfile.ProviderType);
            if (storageHandler == null)
            {
                throw new NotSupportedException($"No upload handler for provider: {job.StorageProfile.ProviderType}");
            }

            return Task.FromResult(storageHandler.EnqueueUploadJob(job.Id, client));
        }

        private Task<string> RetryDownloadPhaseAsync(UserJob job, Hangfire.IBackgroundJobClient client)
        {
            // Reset download progress if retrying from beginning
            if (job.Status.IsFailed())
            {
                job.BytesDownloaded = 0;
                job.DownloadPath = null;
            }

            var jobTypeHandler = jobHandlerFactory.GetJobTypeHandler(job.Type);
            if (jobTypeHandler == null)
            {
                throw new NotSupportedException($"No download handler for job type: {job.Type}");
            }

            return Task.FromResult(jobTypeHandler.EnqueueDownloadJob(job.Id, client));
        }

        private bool IsUploadPhase(JobStatus status)
        {
            // Use the first available job type handler to determine if status is in upload phase
            // This is a reasonable default since upload phase statuses are typically shared across job types
            var jobTypeHandlers = jobHandlerFactory.GetAllJobTypeHandlers();
            foreach (var handler in jobTypeHandlers)
            {
                if (handler.IsUploadPhaseStatus(status))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

