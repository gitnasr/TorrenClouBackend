using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Specifications;
using Microsoft.Extensions.Logging;
using Hangfire;
using TorreClou.Infrastructure.Workers;
using TorreClou.Infrastructure.Services;
using TorreClou.Infrastructure.Settings;
using Microsoft.Extensions.Options;

namespace TorreClou.GoogleDrive.Worker.Services
{
    /// <summary>
    /// Hangfire job that handles uploading downloaded torrent files to Google Drive.
    /// Supports resumable uploads and preserves folder structure.
    /// </summary>
    public class GoogleDriveUploadJob(
        IUnitOfWork unitOfWork,
        ILogger<GoogleDriveUploadJob> logger,
        IGoogleDriveJob googleDriveService,
        IUploadProgressContext progressContext,
        ITransferSpeedMetrics speedMetrics,
        IOptions<BackblazeSettings> backblazeSettings) : BaseJob<GoogleDriveUploadJob>(unitOfWork, logger)
    {
        private readonly BackblazeSettings _backblazeSettings = backblazeSettings.Value;

        protected override string LogPrefix => "[GOOGLE_DRIVE:UPLOAD]";

        protected override void ConfigureSpecification(BaseSpecification<UserJob> spec)
        {
            spec.AddInclude(j => j.StorageProfile);
            spec.AddInclude(j => j.User);
        }

        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
        [Queue("googledrive")]
        public new async Task ExecuteAsync(int jobId, CancellationToken cancellationToken = default)
        {
            await base.ExecuteAsync(jobId, cancellationToken);
        }

        protected override async Task ExecuteCoreAsync(UserJob job, CancellationToken cancellationToken)
        {
            // 1. Idempotency check - if job is already completed, skip
            if (job.Status == JobStatus.COMPLETED)
            {
                Logger.LogInformation("{LogPrefix} Job already completed, skipping execution | JobId: {JobId}", 
                    LogPrefix, job.Id);
                return;
            }

            // 2. Handle job status transitions
            if (job.Status == JobStatus.PENDING_UPLOAD)
            {
                // Job just completed download - transition to UPLOADING
                Logger.LogInformation("{LogPrefix} Job ready for upload, transitioning to UPLOADING | JobId: {JobId}", 
                    LogPrefix, job.Id);
                job.Status = JobStatus.UPLOADING;
                job.CurrentState = "Starting upload...";
                await UnitOfWork.Complete();
            }
            else if (job.Status == JobStatus.RETRYING)
            {
                // Job is retrying - transition back to UPLOADING for this attempt
                Logger.LogInformation("{LogPrefix} Retrying job | JobId: {JobId} | NextRetryAt: {NextRetry} | Error: {Error}", 
                    LogPrefix, job.Id, job.NextRetryAt, job.ErrorMessage);
                job.Status = JobStatus.UPLOADING;
                job.CurrentState = "Retrying upload...";
                await UnitOfWork.Complete();
            }
            else if (job.Status != JobStatus.UPLOADING)
            {
                Logger.LogWarning("{LogPrefix} Unexpected job status | JobId: {JobId} | Status: {Status}", 
                    LogPrefix, job.Id, job.Status);
                // Don't return - allow execution if in unexpected state (might be recovery scenario)
            }

            // 4. Validate download path exists
            if (string.IsNullOrEmpty(job.DownloadPath))
            {
                await MarkJobFailedAsync(job, "Download path is not set.");
                return;
            }

            // Check if path is under Backblaze mount and validate mount exists
            if (_backblazeSettings.UseFuseMount && 
                !string.IsNullOrEmpty(_backblazeSettings.MountPath) &&
                job.DownloadPath.StartsWith(_backblazeSettings.MountPath, StringComparison.OrdinalIgnoreCase))
            {
                // Validate mount point exists
                if (!Directory.Exists(_backblazeSettings.MountPath))
                {
                    await MarkJobFailedAsync(job, $"Backblaze mount point {_backblazeSettings.MountPath} not found. Ensure rclone mount is running.");
                    return;
                }

                Logger.LogInformation("{LogPrefix} Using Backblaze mount | JobId: {JobId} | MountPath: {MountPath} | DownloadPath: {DownloadPath}",
                    LogPrefix, job.Id, _backblazeSettings.MountPath, job.DownloadPath);
            }

            // Validate download path exists
            if (!Directory.Exists(job.DownloadPath))
            {
                await MarkJobFailedAsync(job, $"Download path {job.DownloadPath} not found. Files may have been deleted or not synced to mount.");
                return;
            }

            // 5. Validate storage profile
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

            // 6. Get access token
            await UpdateHeartbeatAsync(job, "Getting access token...");
            var tokenResult = await googleDriveService.GetAccessTokenAsync(job.StorageProfile.CredentialsJson, cancellationToken);
            if (tokenResult.IsFailure)
            {
                await MarkJobFailedAsync(job, $"Failed to get access token: {tokenResult.Error.Message}");
                return;
            }

            var accessToken = tokenResult.Value;

            // 7. Get files to upload (excluding .dht, .fresume, .torrent files)
            var filesToUpload = GetFilesToUpload(job.DownloadPath);
            if (filesToUpload.Length == 0)
            {
                await MarkJobFailedAsync(job, "No files found in download path.");
                return;
            }

            var totalBytes = filesToUpload.Sum(f => f.Length);
            var uploadStartTime = DateTime.UtcNow;
            Logger.LogInformation("{LogPrefix} Found {FileCount} files to upload | JobId: {JobId} | TotalSize: {SizeMB:F2} MB",
                LogPrefix, filesToUpload.Length, job.Id, totalBytes / (1024.0 * 1024.0));

            // 8. Configure the progress context
            progressContext.Configure(
                job.Id,
                totalBytes,
                Logger,
                async (stateMessage, percent) =>
                {
                    job.CurrentState = stateMessage;
                    job.LastHeartbeat = DateTime.UtcNow;
                    await UnitOfWork.Complete();
                });

            // 9. Create root folder in Google Drive (idempotent - will create new folder each time)
            await UpdateHeartbeatAsync(job, "Creating folder in Google Drive...");
            var parentFolder = $"Torrent_{job.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            var folderResult = await googleDriveService.CreateFolderAsync(parentFolder, null, accessToken, cancellationToken);
            if (folderResult.IsFailure)
            {
                await MarkJobFailedAsync(job, $"Failed to create folder: {folderResult.Error.Message}");
                return;
            }

            var rootFolderId = folderResult.Value;
            Logger.LogInformation("{LogPrefix} Created root folder | JobId: {JobId} | FolderId: {FolderId}", 
                LogPrefix, job.Id, rootFolderId);

            // 8. Create folder hierarchy on Google Drive (preserving local structure)
            var folderIdMap = await CreateFolderHierarchyAsync(
                filesToUpload, job.DownloadPath, rootFolderId, accessToken, cancellationToken);

            // 9. Upload files - this must complete successfully before marking as COMPLETED
            var uploadResult = await UploadFilesAsync(job, filesToUpload, folderIdMap, accessToken, cancellationToken);

            // 10. Only mark as completed if ALL files uploaded successfully
            if (!uploadResult.AllFilesUploaded)
            {
                var errorMessage = $"Upload incomplete: {uploadResult.FailedFiles} of {uploadResult.TotalFiles} files failed to upload.";
                Logger.LogError("{LogPrefix} Upload incomplete | JobId: {JobId} | Failed: {Failed}/{Total}", 
                    LogPrefix, job.Id, uploadResult.FailedFiles, uploadResult.TotalFiles);
                
                // Mark as failed - Hangfire will retry if configured
                await MarkJobFailedAsync(job, errorMessage, hasRetries: true);
                return;
            }

            // 11. Record final upload metrics (only if all files succeeded)
            var uploadDuration = (DateTime.UtcNow - uploadStartTime).TotalSeconds;
            speedMetrics.RecordUploadComplete(job.Id, job.UserId, "googledrive_upload", totalBytes, uploadDuration);

            // 12. Mark as completed - ALL files uploaded successfully
            job.Status = JobStatus.COMPLETED;
            job.CompletedAt = DateTime.UtcNow;
            job.CurrentState = "Upload completed successfully";
            job.NextRetryAt = null; // Clear any retry time
            await UnitOfWork.Complete();

            Logger.LogInformation("{LogPrefix} Job completed successfully | JobId: {JobId} | Files: {Files}", 
                LogPrefix, job.Id, uploadResult.TotalFiles);
        }

        private FileInfo[] GetFilesToUpload(string downloadPath)
        {
            var directory = new DirectoryInfo(downloadPath);

            // Get all files excluding MonoTorrent metadata files and .dht files
            return [.. directory.GetFiles("*", SearchOption.AllDirectories)
                .Where(f => !f.Name.EndsWith(".fresume", StringComparison.OrdinalIgnoreCase) &&
                           !f.Name.EndsWith(".dht", StringComparison.OrdinalIgnoreCase) &&
                           !f.Name.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) &&
                           !f.Name.Equals("dht_nodes.cache", StringComparison.OrdinalIgnoreCase) &&
                           !f.Name.Equals("fastresume", StringComparison.OrdinalIgnoreCase))];
        }

        /// <summary>
        /// Creates the folder hierarchy on Google Drive matching the local directory structure.
        /// Returns a dictionary mapping relative paths to Google Drive folder IDs.
        /// </summary>
        private async Task<Dictionary<string, string>> CreateFolderHierarchyAsync(
            FileInfo[] files,
            string downloadPath,
            string rootFolderId,
            string accessToken,
            CancellationToken cancellationToken)
        {
            var folderIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [""] = rootFolderId,
                ["."] = rootFolderId
            };

            // Get unique directory paths, sorted by depth (parents first)
            var directories = files
                .Select(f => Path.GetRelativePath(downloadPath, f.DirectoryName!))
                .Where(p => p != "." && !string.IsNullOrEmpty(p))
                .Distinct()
                .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar))
                .ToList();

            if (directories.Count == 0)
            {
                Logger.LogInformation("{LogPrefix} No subdirectories to create", LogPrefix);
                return folderIdMap;
            }

            Logger.LogInformation("{LogPrefix} Creating {Count} subdirectories on Google Drive", LogPrefix, directories.Count);

            foreach (var relPath in directories)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                // Split path into parts
                var parts = relPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                
                // Determine parent path
                var parentPath = parts.Length > 1
                    ? string.Join(Path.DirectorySeparatorChar, parts[..^1])
                    : "";

                var folderName = parts[^1];

                // Get parent folder ID
                if (!folderIdMap.TryGetValue(parentPath, out var parentId))
                {
                    parentId = rootFolderId;
                }

                // Create folder in Google Drive
                var result = await googleDriveService.CreateFolderAsync(folderName, parentId, accessToken, cancellationToken);
                
                if (result.IsSuccess)
                {
                    folderIdMap[relPath] = result.Value;
                    Logger.LogDebug("{LogPrefix} Created subfolder | Path: {Path} | FolderId: {FolderId}", 
                        LogPrefix, relPath, result.Value);
                }
                else
                {
                    Logger.LogWarning("{LogPrefix} Failed to create subfolder | Path: {Path} | Error: {Error}",
                        LogPrefix, relPath, result.Error.Message);
                    // Use root folder as fallback
                    folderIdMap[relPath] = rootFolderId;
                }
            }

            return folderIdMap;
        }

        private class UploadResult
        {
            public int TotalFiles { get; set; }
            public int FailedFiles { get; set; }
            public bool AllFilesUploaded => FailedFiles == 0;
        }

        private async Task<UploadResult> UploadFilesAsync(
            UserJob job,
            FileInfo[] files,
            Dictionary<string, string> folderIdMap,
            string accessToken,
            CancellationToken cancellationToken)
        {
            var result = new UploadResult
            {
                TotalFiles = files.Length
            };
            
            var uploadedFiles = 0;
            var lastUploadTime = DateTime.UtcNow;
            var lastUploadedBytes = 0L;

            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                uploadedFiles++;

                // Get the relative path for this file's directory
                var relativeDir = Path.GetRelativePath(job.DownloadPath!, file.DirectoryName!);
                var relativePath = Path.GetRelativePath(job.DownloadPath!, file.FullName);

                // Get the target folder ID
                if (!folderIdMap.TryGetValue(relativeDir, out var folderId))
                {
                    folderId = folderIdMap[""] ?? folderIdMap.Values.First();
                }

                Logger.LogInformation("{LogPrefix} Uploading file {Current}/{Total} | JobId: {JobId} | File: {File}",
                    LogPrefix, uploadedFiles, result.TotalFiles, job.Id, file.Name);

                // Upload file to Google Drive (progress is reported via context)
                var uploadResult = await googleDriveService.UploadFileAsync(
                    file.FullName,
                    file.Name,
                    folderId,
                    accessToken,
                    relativePath,
                    cancellationToken);

                if (uploadResult.IsFailure)
                {
                    result.FailedFiles++;
                    Logger.LogError("{LogPrefix} Failed to upload file | JobId: {JobId} | File: {File} | Error: {Error}",
                        LogPrefix, job.Id, file.Name, uploadResult.Error.Message);
                    
                    // Query upload status to get actual bytes uploaded before failure
                    // This ensures progress accurately reflects partial uploads, not full file size
                    var resumeUri = await progressContext.GetResumeUriAsync(relativePath);
                    if (!string.IsNullOrEmpty(resumeUri))
                    {
                        var statusResult = await googleDriveService.QueryUploadStatusAsync(resumeUri, file.Length, accessToken, cancellationToken);
                        if (statusResult.IsSuccess && statusResult.Value > 0)
                        {
                            // Mark only the bytes that were actually uploaded
                            progressContext.MarkBytesCompleted(file.Name, statusResult.Value);
                            Logger.LogWarning("{LogPrefix} Marked partial upload | JobId: {JobId} | File: {File} | BytesUploaded: {BytesMB:F2} MB / {TotalMB:F2} MB",
                                LogPrefix, job.Id, file.Name, statusResult.Value / (1024.0 * 1024.0), file.Length / (1024.0 * 1024.0));
                        }
                        else
                        {
                            // No bytes uploaded or couldn't query status - mark as 0 bytes
                            Logger.LogCritical("{LogPrefix} No bytes uploaded before failure | JobId: {JobId} | File: {File}",
                                LogPrefix, job.Id, file.Name);
                        }
                    }
                    else
                    {
                        // No resume URI means upload never started or failed immediately
                        Logger.LogCritical("{LogPrefix} No resume URI found for failed file | JobId: {JobId} | File: {File}",
                            LogPrefix, job.Id, file.Name);
                    }
                }
                else
                {
                    // Mark file as completed in progress context
                    progressContext.MarkFileCompleted(file.Name, file.Length);
                    
                    // Record upload speed metrics
                    var now = DateTime.UtcNow;
                    var timeDelta = (now - lastUploadTime).TotalSeconds;
                    if (timeDelta > 0)
                    {
                        var bytesDelta = file.Length;
                        var speed = bytesDelta / timeDelta;
                        speedMetrics.RecordUploadSpeed(job.Id, job.UserId, "googledrive_upload", speed);
                        lastUploadTime = now;
                        lastUploadedBytes += bytesDelta;
                    }
                    
                    Logger.LogInformation("{LogPrefix} File uploaded successfully | JobId: {JobId} | File: {File} | FileId: {FileId}",
                        LogPrefix, job.Id, file.Name, uploadResult.Value);
                }
            }

            if (result.FailedFiles > 0)
            {
                Logger.LogWarning("{LogPrefix} Upload completed with {FailedCount} failed files | JobId: {JobId}",
                    LogPrefix, result.FailedFiles, job.Id);
            }

            return result;
        }
    }
}
