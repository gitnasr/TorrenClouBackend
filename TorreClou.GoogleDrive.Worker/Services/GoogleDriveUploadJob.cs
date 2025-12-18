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
            // Handle job status transitions for business logic
            if (job.Status == JobStatus.PENDING_UPLOAD)
            {
                // Job just completed download - transition to UPLOADING
                Logger.LogInformation("{LogPrefix} Job ready for upload, transitioning to UPLOADING | JobId: {JobId}", 
                    LogPrefix, job.Id);
                job.Status = JobStatus.UPLOADING;
                job.CurrentState = "Starting upload...";
                // Set StartedAt if not already set (for recovery tracking)
                if (job.StartedAt == null)
                {
                    job.StartedAt = DateTime.UtcNow;
                }
                job.LastHeartbeat = DateTime.UtcNow;
                await UnitOfWork.Complete();
            }
            else if (job.Status == JobStatus.RETRYING)
            {
                // Job is retrying - transition back to UPLOADING for this attempt
                Logger.LogInformation("{LogPrefix} Retrying job | JobId: {JobId} | NextRetryAt: {NextRetry} | Error: {Error}", 
                    LogPrefix, job.Id, job.NextRetryAt, job.ErrorMessage);
                job.Status = JobStatus.UPLOADING;
                job.CurrentState = "Retrying upload...";
                job.LastHeartbeat = DateTime.UtcNow;
                await UnitOfWork.Complete();
            }
            else if (job.Status == JobStatus.UPLOADING)
            {
                // Job is already UPLOADING (recovery scenario) - ensure StartedAt is set for elapsed time tracking
                if (job.StartedAt == null)
                {
                    Logger.LogInformation("{LogPrefix} Setting StartedAt for recovered job | JobId: {JobId}", 
                        LogPrefix, job.Id);
                    job.StartedAt = DateTime.UtcNow;
                    await UnitOfWork.Complete();
                }
                else
                {
                    // StartedAt already set - this is a recovery, elapsed time will be calculated from original start
                    Logger.LogInformation("{LogPrefix} Resuming job from recovery | JobId: {JobId} | StartedAt: {StartedAt} | Elapsed: {ElapsedMinutes:F1} min", 
                        LogPrefix, job.Id, job.StartedAt, (DateTime.UtcNow - job.StartedAt.Value).TotalMinutes);
                }
            }
            else
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
            // Wait for files to appear (for FUSE mount sync delays)
            var filesToUpload = await WaitForFilesAsync(job, job.DownloadPath, cancellationToken);
            if (filesToUpload.Length == 0)
            {
                await MarkJobFailedAsync(job, $"No files found in download path {job.DownloadPath} after waiting 2 hours. Directory exists: {Directory.Exists(job.DownloadPath)}");
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

        /// <summary>
        /// Waits for files to appear in the download path with extended retry strategy:
        /// - Phase 1: Retry every 20 seconds for 1 hour (180 retries)
        /// - Phase 2: Retry every 10 minutes for 1 hour (6 retries)
        /// - After 2 hours total: Return empty array to trigger failure
        /// This handles FUSE mount sync delays where files might not be immediately visible.
        /// Uses job.StartedAt to track total elapsed time across worker restarts for proper recovery.
        /// </summary>
        private async Task<FileInfo[]> WaitForFilesAsync(UserJob job, string downloadPath, CancellationToken cancellationToken)
        {
            const int phase1IntervalSeconds = 20;  // 20 seconds
            const int phase2IntervalSeconds = 600; // 10 minutes
            const int phase1DurationSeconds = 3600; // 1 hour
            const int totalDurationSeconds = 7200;   // 2 hours total

            // Use StartedAt as baseline for recovery - if job was restarted, this preserves elapsed time
            var baselineTime = job.StartedAt ?? DateTime.UtcNow;
            var attemptCount = 0;
            var lastPhase = 0; // Track phase transitions for logging

            Logger.LogInformation("{LogPrefix} Starting file wait with extended retry | JobId: {JobId} | Path: {Path} | Baseline: {Baseline}", 
                LogPrefix, job.Id, downloadPath, baselineTime);

            // IMMEDIATE CHECK: Don't wait before first check - files might already be there
            var immediateFiles = GetFilesToUpload(downloadPath);
            if (immediateFiles.Length > 0)
            {
                Logger.LogInformation("{LogPrefix} Files found immediately (no wait needed) | JobId: {JobId} | Count: {Count}", 
                    LogPrefix, job.Id, immediateFiles.Length);
                return immediateFiles;
            }

            // If no files found immediately, start retry loop
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.LogWarning("{LogPrefix} File wait cancelled | JobId: {JobId}", LogPrefix, job.Id);
                    break;
                }

                var elapsed = DateTime.UtcNow - baselineTime;
                
                // Check if we've exceeded total duration
                if (elapsed.TotalSeconds >= totalDurationSeconds)
                {
                    Logger.LogError("{LogPrefix} File wait timeout after {ElapsedMinutes:F1} minutes | JobId: {JobId} | Path: {Path}", 
                        LogPrefix, elapsed.TotalMinutes, job.Id, downloadPath);
                    return GetFilesToUpload(downloadPath); // Return empty or whatever is found
                }

                // Check for files
                var files = GetFilesToUpload(downloadPath);
                attemptCount++;

                if (files.Length > 0)
                {
                    if (attemptCount > 1)
                    {
                        Logger.LogInformation("{LogPrefix} Files found after {ElapsedMinutes:F1} minutes ({Attempt} attempts) | JobId: {JobId} | Count: {Count}", 
                            LogPrefix, elapsed.TotalMinutes, attemptCount, job.Id, files.Length);
                    }
                    return files;
                }

                // Determine current phase and wait interval
                int waitIntervalSeconds;
                string phaseDescription;
                string statusMessage;

                if (elapsed.TotalSeconds < phase1DurationSeconds)
                {
                    // Phase 1: Retry every 20 seconds
                    waitIntervalSeconds = phase1IntervalSeconds;
                    phaseDescription = "Phase 1 (20s intervals)";
                    statusMessage = $"Waiting for files to sync (retrying every 20s)... Elapsed: {elapsed.TotalMinutes:F1} min";
                    
                    if (lastPhase != 1)
                    {
                        Logger.LogInformation("{LogPrefix} Entering Phase 1: Retrying every 20 seconds for up to 1 hour | JobId: {JobId}", 
                            LogPrefix, job.Id);
                        lastPhase = 1;
                    }
                }
                else
                {
                    // Phase 2: Retry every 10 minutes
                    waitIntervalSeconds = phase2IntervalSeconds;
                    phaseDescription = "Phase 2 (10min intervals)";
                    statusMessage = $"Waiting for files to sync (retrying every 10min)... Elapsed: {elapsed.TotalMinutes:F1} min";
                    
                    if (lastPhase != 2)
                    {
                        Logger.LogInformation("{LogPrefix} Entering Phase 2: Retrying every 10 minutes for up to 1 hour | JobId: {JobId} | Elapsed: {ElapsedMinutes:F1} min", 
                            LogPrefix, job.Id, elapsed.TotalMinutes);
                        lastPhase = 2;
                    }
                }

                // Update job heartbeat and status
               
                    job.CurrentState = statusMessage;
                    job.LastHeartbeat = DateTime.UtcNow;
                    await UnitOfWork.Complete();
               

                // Log retry attempt
                Logger.LogCritical("{LogPrefix} No files found, waiting {Interval}s before retry | JobId: {JobId} | {Phase} | Attempt: {Attempt} | Elapsed: {ElapsedMinutes:F1} min | Path: {Path}", 
                    LogPrefix, waitIntervalSeconds, job.Id, phaseDescription, attemptCount, elapsed.TotalMinutes, downloadPath);

                // Wait before next retry
                await Task.Delay(TimeSpan.FromSeconds(waitIntervalSeconds), cancellationToken);
            }

            // If we exit the loop (cancellation), return whatever we find
            return GetFilesToUpload(downloadPath);
        }

        private FileInfo[] GetFilesToUpload(string downloadPath)
        {
            try
            {
                var directory = new DirectoryInfo(downloadPath);

                // Force refresh to get latest directory state (important for FUSE mounts)
                directory.Refresh();

                if (!directory.Exists)
                {
                    Logger.LogWarning("{LogPrefix} Download directory does not exist | Path: {Path}", LogPrefix, downloadPath);
                    return [];
                }

                // Get all files excluding MonoTorrent metadata files and .dht files
                // Use try-catch around GetFiles to handle FUSE mount sync issues gracefully
                FileInfo[] allFiles;
                try
                {
                    allFiles = directory.GetFiles("*", SearchOption.AllDirectories);
                }
                catch (UnauthorizedAccessException ex)
                {
                    // Some files/directories might not be accessible yet on FUSE mount during sync
                    Logger.LogWarning(ex, "{LogPrefix} UnauthorizedAccessException while getting files (FUSE mount may be syncing) | Path: {Path}", 
                        LogPrefix, downloadPath);
                    return [];
                }
                catch (DirectoryNotFoundException ex)
                {
                    // Directory might have been removed or not fully synced yet
                    Logger.LogWarning(ex, "{LogPrefix} DirectoryNotFoundException (directory may not be fully synced) | Path: {Path}", 
                        LogPrefix, downloadPath);
                    return [];
                }

                var files = allFiles
                    .Where(f => f.Exists && // Ensure file still exists (FUSE mount might have stale entries)
                               !f.Name.EndsWith(".fresume", StringComparison.OrdinalIgnoreCase) &&
                               !f.Name.EndsWith(".dht", StringComparison.OrdinalIgnoreCase) &&
                               !f.Name.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) &&
                               !f.Name.Equals("dht_nodes.cache", StringComparison.OrdinalIgnoreCase) &&
                               !f.Name.Equals("fastresume", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                Logger.LogDebug("{LogPrefix} Found {Count} files in directory (out of {Total} total files) | Path: {Path}", 
                    LogPrefix, files.Length, allFiles.Length, downloadPath);

                return files;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{LogPrefix} Error getting files from directory | Path: {Path}", LogPrefix, downloadPath);
                return [];
            }
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
