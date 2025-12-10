using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Specifications;
using Microsoft.Extensions.Logging;
using Hangfire;

namespace TorreClou.Worker.Services
{
    /// <summary>
    /// Hangfire job that handles uploading downloaded torrent files to the user's cloud storage.
    /// This is chained after TorrentDownloadJob completes successfully.
    /// </summary>
    public class TorrentUploadJob
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<TorrentUploadJob> _logger;

        public TorrentUploadJob(
            IUnitOfWork unitOfWork,
            ILogger<TorrentUploadJob> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        [DisableConcurrentExecution(timeoutInSeconds: 3600)] // 1 hour max for large uploads
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
        [Queue("torrents")]
        public async Task ExecuteAsync(int jobId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("[UPLOAD] Starting upload job | JobId: {JobId}", jobId);

            UserJob? job = null;

            try
            {
                // 1. Load job from database
                job = await LoadJobAsync(jobId);
                if (job == null) return;

                // 2. Validate job state
                if (job.Status == JobStatus.COMPLETED)
                {
                    _logger.LogInformation("[UPLOAD] Job already completed | JobId: {JobId}", jobId);
                    return;
                }

                if (job.Status == JobStatus.CANCELLED)
                {
                    _logger.LogInformation("[UPLOAD] Job was cancelled | JobId: {JobId}", jobId);
                    return;
                }

                if (job.Status != JobStatus.UPLOADING)
                {
                    _logger.LogWarning("[UPLOAD] Unexpected job status | JobId: {JobId} | Status: {Status}", jobId, job.Status);
                }

                // 3. Validate download path exists
                if (string.IsNullOrEmpty(job.DownloadPath) || !Directory.Exists(job.DownloadPath))
                {
                    await MarkJobFailedAsync(job, "Download path not found. Files may have been deleted.");
                    return;
                }

                // 4. Update heartbeat
                job.LastHeartbeat = DateTime.UtcNow;
                job.CurrentState = "Preparing upload...";
                await _unitOfWork.Complete();

                // 5. Get files to upload
                var filesToUpload = GetFilesToUpload(job.DownloadPath);
                if (filesToUpload.Length == 0)
                {
                    await MarkJobFailedAsync(job, "No files found in download path.");
                    return;
                }

                _logger.LogInformation("[UPLOAD] Found {FileCount} files to upload | JobId: {JobId} | TotalSize: {SizeMB:F2} MB",
                    filesToUpload.Length, jobId, filesToUpload.Sum(f => f.Length) / (1024.0 * 1024.0));

                // 6. Upload to storage provider
                // TODO: Implement actual upload logic based on job.StorageProfile
                await UploadFilesAsync(job, filesToUpload, cancellationToken);

                // 7. Mark as completed
                job.Status = JobStatus.COMPLETED;
                job.CompletedAt = DateTime.UtcNow;
                job.CurrentState = "Upload completed successfully";
                await _unitOfWork.Complete();

                _logger.LogInformation("[UPLOAD] Job completed successfully | JobId: {JobId}", jobId);

                // 8. Optionally cleanup local files
                // CleanupDownloadedFiles(job);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[UPLOAD] Job cancelled | JobId: {JobId}", jobId);
                throw; // Let Hangfire handle
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UPLOAD] Fatal error | JobId: {JobId}", jobId);
                
                if (job != null)
                {
                    await MarkJobFailedAsync(job, ex.Message);
                }
                
                throw; // Let Hangfire retry
            }
        }

        private async Task<UserJob?> LoadJobAsync(int jobId)
        {
            var spec = new BaseSpecification<UserJob>(j => j.Id == jobId);
            spec.AddInclude(j => j.StorageProfile);
            spec.AddInclude(j => j.User);

            var job = await _unitOfWork.Repository<UserJob>().GetEntityWithSpec(spec);
            
            if (job == null)
            {
                _logger.LogError("[UPLOAD] Job not found | JobId: {JobId}", jobId);
            }

            return job;
        }

        private FileInfo[] GetFilesToUpload(string downloadPath)
        {
            var directory = new DirectoryInfo(downloadPath);
            
            // Get all files excluding MonoTorrent metadata files
            return directory.GetFiles("*", SearchOption.AllDirectories)
                .Where(f => !f.Name.EndsWith(".fresume") && 
                           !f.Name.EndsWith(".dht") && 
                           !f.Name.EndsWith(".torrent"))
                .ToArray();
        }

        private async Task UploadFilesAsync(UserJob job, FileInfo[] files, CancellationToken cancellationToken)
        {
            var totalFiles = files.Length;
            var uploadedFiles = 0;

            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                uploadedFiles++;
                var progress = (uploadedFiles * 100.0) / totalFiles;

                // Update progress
                job.CurrentState = $"Uploading: {progress:F1}% ({uploadedFiles}/{totalFiles} files)";
                job.LastHeartbeat = DateTime.UtcNow;
                await _unitOfWork.Complete();

                _logger.LogInformation("[UPLOAD] Uploading file | JobId: {JobId} | File: {File} | Progress: {Progress:F1}%",
                    job.Id, file.Name, progress);

                // TODO: Implement actual upload based on StorageProfile.Provider
                // Example:
                // switch (job.StorageProfile.Provider)
                // {
                //     case StorageProviderType.GoogleDrive:
                //         await _googleDriveService.UploadAsync(file, job.StorageProfile, cancellationToken);
                //         break;
                //     case StorageProviderType.OneDrive:
                //         await _oneDriveService.UploadAsync(file, job.StorageProfile, cancellationToken);
                //         break;
                //     // etc.
                // }

                // Simulate upload delay for now
                await Task.Delay(100, cancellationToken);
            }
        }

        private void CleanupDownloadedFiles(UserJob job)
        {
            if (string.IsNullOrEmpty(job.DownloadPath))
                return;

            try
            {
                if (Directory.Exists(job.DownloadPath))
                {
                    Directory.Delete(job.DownloadPath, recursive: true);
                    _logger.LogInformation("[UPLOAD] Cleaned up download path | JobId: {JobId} | Path: {Path}", 
                        job.Id, job.DownloadPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[UPLOAD] Failed to cleanup download path | JobId: {JobId}", job.Id);
            }
        }

        private async Task MarkJobFailedAsync(UserJob job, string errorMessage)
        {
            try
            {
                job.Status = JobStatus.FAILED;
                job.ErrorMessage = errorMessage;
                job.CompletedAt = DateTime.UtcNow;
                await _unitOfWork.Complete();
                
                _logger.LogError("[UPLOAD] Job marked as failed | JobId: {JobId} | Error: {Error}", job.Id, errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UPLOAD] Failed to mark job as failed | JobId: {JobId}", job.Id);
            }
        }
    }
}

