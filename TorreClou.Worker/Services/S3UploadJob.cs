using Microsoft.Extensions.Logging;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.Core.Specifications;

namespace TorreClou.Worker.Services
{
    /// <summary>
    /// Hangfire job for uploading files to S3-compatible storage
    /// </summary>
    public class S3UploadJob : IS3UploadJob
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IS3ResumableUploadService _s3UploadService;
        private readonly IJobStatusService _jobStatusService;
        private readonly ILogger<S3UploadJob> _logger;

        public S3UploadJob(
            IUnitOfWork unitOfWork,
            IS3ResumableUploadService s3UploadService,
            IJobStatusService jobStatusService,
            ILogger<S3UploadJob> logger)
        {
            _unitOfWork = unitOfWork;
            _s3UploadService = s3UploadService;
            _jobStatusService = jobStatusService;
            _logger = logger;
        }

        public async Task ExecuteAsync(int jobId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("S3 upload job started | JobId: {JobId}", jobId);

            try
            {
                // Load job with storage profile
                var jobSpec = new BaseSpecification<UserJob>(j => j.Id == jobId);
                jobSpec.AddInclude(j => j.StorageProfile);
                var job = await _unitOfWork.Repository<UserJob>().GetEntityWithSpec(jobSpec);

                if (job == null)
                {
                    _logger.LogError("Job not found | JobId: {JobId}", jobId);
                    return;
                }

                if (job.StorageProfile == null || !job.StorageProfile.IsActive)
                {
                    _logger.LogError("Storage profile not found or inactive | JobId: {JobId}", jobId);
                    await _jobStatusService.TransitionJobStatusAsync(
                        job,
                        JobStatus.UPLOAD_FAILED,
                        StatusChangeSource.System,
                        metadata: new { error = "Storage profile not available" });
                    return;
                }

                // Validate downloaded file exists
                if (string.IsNullOrEmpty(job.DownloadPath) || !File.Exists(job.DownloadPath))
                {
                    _logger.LogError("Downloaded file not found | JobId: {JobId} | Path: {Path}", jobId, job.DownloadPath);
                    await _jobStatusService.TransitionJobStatusAsync(
                        job,
                        JobStatus.UPLOAD_FAILED,
                        StatusChangeSource.System,
                        metadata: new { error = "Downloaded file not found" });
                    return;
                }

                // Update status to uploading
                await _jobStatusService.TransitionJobStatusAsync(
                    job,
                    JobStatus.UPLOADING,
                    StatusChangeSource.System,
                    metadata: new { uploadStartedAt = DateTime.UtcNow });

                // Perform S3 upload
                _logger.LogInformation("Starting S3 upload | JobId: {JobId} | File: {Path}", jobId, job.DownloadPath);
                
                var uploadResult = await _s3UploadService.UploadFileAsync(
                    job.DownloadPath,
                    job.StorageProfile.CredentialsJson,
                    cancellationToken);

                if (uploadResult.IsFailure)
                {
                    _logger.LogError("S3 upload failed | JobId: {JobId} | Error: {Error}", jobId, uploadResult.Error.Message);
                    await _jobStatusService.TransitionJobStatusAsync(
                        job,
                        JobStatus.UPLOAD_FAILED,
                        StatusChangeSource.System,
                        metadata: new { error = uploadResult.Error.Message });
                    return;
                }

                // Mark job as completed
                job.CompletedAt = DateTime.UtcNow;
                await _jobStatusService.TransitionJobStatusAsync(
                    job,
                    JobStatus.COMPLETED,
                    StatusChangeSource.System,
                    metadata: new
                    {
                        uploadedAt = DateTime.UtcNow,
                        s3Key = uploadResult.Value
                    });

                _logger.LogInformation("S3 upload completed successfully | JobId: {JobId} | S3Key: {S3Key}", 
                    jobId, uploadResult.Value);

                // Clean up local file
                try
                {
                    if (File.Exists(job.DownloadPath))
                    {
                        File.Delete(job.DownloadPath);
                        _logger.LogDebug("Deleted local file | JobId: {JobId} | Path: {Path}", jobId, job.DownloadPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete local file | JobId: {JobId} | Path: {Path}", 
                        jobId, job.DownloadPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "S3 upload job failed with exception | JobId: {JobId}", jobId);
                
                try
                {
                    var jobSpec = new BaseSpecification<UserJob>(j => j.Id == jobId);
                    var job = await _unitOfWork.Repository<UserJob>().GetEntityWithSpec(jobSpec);
                    
                    if (job != null)
                    {
                        await _jobStatusService.TransitionJobStatusAsync(
                            job,
                            JobStatus.UPLOAD_FAILED,
                            StatusChangeSource.System,
                            metadata: new { error = ex.Message, stackTrace = ex.StackTrace });
                    }
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, "Failed to update job status after exception | JobId: {JobId}", jobId);
                }

                throw;
            }
        }
    }
}
