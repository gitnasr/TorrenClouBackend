using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Specifications;
using Microsoft.Extensions.Logging;
using Hangfire;
using TorreClou.Infrastructure.Workers;
using System.Text.Json;

namespace TorreClou.GoogleDrive.Worker.Services
{
    /// <summary>
    /// Hangfire job that handles uploading downloaded torrent files to Google Drive.
    /// </summary>
    public class GoogleDriveUploadJob(
        IUnitOfWork unitOfWork,
        ILogger<GoogleDriveUploadJob> logger,
        IGoogleDriveService googleDriveService) : BaseJob<GoogleDriveUploadJob>(unitOfWork, logger)
    {
        protected override string LogPrefix => "[GOOGLE_DRIVE:UPLOAD]";

        protected override void ConfigureSpecification(BaseSpecification<UserJob> spec)
        {
            spec.AddInclude(j => j.StorageProfile);
            spec.AddInclude(j => j.User);
        }

        [DisableConcurrentExecution(timeoutInSeconds: 3600)] // 1 hour max for large uploads
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
        [Queue("googledrive")]
        public new async Task ExecuteAsync(int jobId, CancellationToken cancellationToken = default)
        {
            await base.ExecuteAsync(jobId, cancellationToken);
        }

        protected override async Task ExecuteCoreAsync(UserJob job, CancellationToken cancellationToken)
        {
            // 1. Validate job state - must be in UPLOADING status
            if (job.Status != JobStatus.UPLOADING)
            {
                Logger.LogWarning("{LogPrefix} Unexpected job status | JobId: {JobId} | Status: {Status}", 
                    LogPrefix, job.Id, job.Status);
            }

            // 2. Validate download path exists
            if (string.IsNullOrEmpty(job.DownloadPath) || !Directory.Exists(job.DownloadPath))
            {
                await MarkJobFailedAsync(job, "Download path not found. Files may have been deleted.");
                return;
            }

            // 3. Validate storage profile
            if (job.StorageProfile == null)
            {
                await MarkJobFailedAsync(job, "Storage profile not found.");
                return;
            }

            if (job.StorageProfile.ProviderType != StorageProviderType.GoogleDrive)
            {
                await MarkJobFailedAsync(job, $"Invalid storage provider. Expected GoogleDrive, got {job.StorageProfile.ProviderType}");
                return;
            }

            // 4. Get access token
            await UpdateHeartbeatAsync(job, "Getting access token...");
            var tokenResult = await googleDriveService.GetAccessTokenAsync(job.StorageProfile.CredentialsJson, cancellationToken);
            if (tokenResult.IsFailure)
            {
                await MarkJobFailedAsync(job, $"Failed to get access token: {tokenResult.Error.Message}");
                return;
            }

            var accessToken = tokenResult.Value;

            // 5. Create folder in Google Drive
            await UpdateHeartbeatAsync(job, "Creating folder in Google Drive...");
            var folderName = $"Torrent_{job.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            var folderResult = await googleDriveService.CreateFolderAsync(folderName, null, accessToken, cancellationToken);
            if (folderResult.IsFailure)
            {
                await MarkJobFailedAsync(job, $"Failed to create folder: {folderResult.Error.Message}");
                return;
            }

            var folderId = folderResult.Value;
            Logger.LogInformation("{LogPrefix} Created folder | JobId: {JobId} | FolderId: {FolderId}", 
                LogPrefix, job.Id, folderId);

            // 6. Get files to upload
            var filesToUpload = GetFilesToUpload(job.DownloadPath);
            if (filesToUpload.Length == 0)
            {
                await MarkJobFailedAsync(job, "No files found in download path.");
                return;
            }

            Logger.LogInformation("{LogPrefix} Found {FileCount} files to upload | JobId: {JobId} | TotalSize: {SizeMB:F2} MB",
                LogPrefix, filesToUpload.Length, job.Id, filesToUpload.Sum(f => f.Length) / (1024.0 * 1024.0));

            // 7. Upload files
            await UploadFilesAsync(job, filesToUpload, folderId, accessToken, cancellationToken);

            // 8. Mark as completed
            job.Status = JobStatus.COMPLETED;
            job.CompletedAt = DateTime.UtcNow;
            job.CurrentState = "Upload completed successfully";
            await UnitOfWork.Complete();

            Logger.LogInformation("{LogPrefix} Job completed successfully | JobId: {JobId}", LogPrefix, job.Id);
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

        private async Task UploadFilesAsync(
            UserJob job, 
            FileInfo[] files, 
            string folderId, 
            string accessToken, 
            CancellationToken cancellationToken)
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
                await UnitOfWork.Complete();

                Logger.LogInformation("{LogPrefix} Uploading file | JobId: {JobId} | File: {File} | Progress: {Progress:F1}%",
                    LogPrefix, job.Id, file.Name, progress);
                var progressHandler = new Progress<double>(percent =>
                {
                    // Only log progress updates - do NOT update database from background thread
                    // Database updates should only happen on the main thread to avoid DbContext concurrency issues
                    Logger.LogInformation("{LogPrefix} Upload progress | JobId: {JobId} | File: {File} | Percent: {Percent:F1}%",
                        LogPrefix, job.Id, file.Name, percent);
                });
                // Upload file to Google Drive
                var uploadResult = await googleDriveService.UploadFileAsync(
                    file.FullName,
                    file.Name,
                    folderId,
                    accessToken,
                    progressHandler,
                    cancellationToken);

                if (uploadResult.IsFailure)
                {
                    Logger.LogError("{LogPrefix} Failed to upload file | JobId: {JobId} | File: {File} | Error: {Error}",
                        LogPrefix, job.Id, file.Name, uploadResult.Error.Message);
                    
                    // Continue with other files, but log the error
                    // Optionally, we could mark the job as failed here if critical
                }
                else
                {
                    Logger.LogInformation("{LogPrefix} File uploaded successfully | JobId: {JobId} | File: {File} | FileId: {FileId}",
                        LogPrefix, job.Id, file.Name, uploadResult.Value);
                }
            }
        }
    }
}

