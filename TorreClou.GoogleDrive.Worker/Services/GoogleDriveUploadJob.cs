using Microsoft.Extensions.Logging;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.Core.Specifications;
using TorreClou.Infrastructure.Workers;


namespace TorreClou.GoogleDrive.Worker.Services
{

    public partial class GoogleDriveUploadJob(
        IUnitOfWork unitOfWork,
        ILogger<GoogleDriveUploadJob> logger,
        IGoogleDriveJobService googleDriveService,
        IUploadProgressContext progressContext,
        ITransferSpeedMetrics speedMetrics,
        IRedisLockService redisLockService,
        IJobStatusService jobStatusService) : UserJobBase<GoogleDriveUploadJob>(unitOfWork, logger, jobStatusService), IGoogleDriveUploadJob
    {

        protected override string LogPrefix => "[GOOGLE_DRIVE:UPLOAD]";

        protected override void ConfigureSpecification(BaseSpecification<UserJob> spec)
        {
            spec.AddInclude(j => j.StorageProfile);
            spec.AddInclude(j => j.User);
        }


        public new async Task ExecuteAsync(int jobId, CancellationToken cancellationToken = default)
        {
            await base.ExecuteAsync(jobId, cancellationToken);
        }

        protected override async Task ExecuteCoreAsync(UserJob job, CancellationToken cancellationToken)
        {
            using var distributedLock = await AcquireJobLockAsync(job, cancellationToken);
            if (distributedLock == null) return;

            await HandleStatusTransitionAsync(job);

            if (!await ValidateEnvironmentAsync(job)) return;

            var accessToken = await AuthenticateAsync(job, cancellationToken);
            if (accessToken == null) return;

            var allFiles = GetFilesToUpload(job.DownloadPath!);
            if (allFiles.Length == 0)
            {
                await MarkJobFailedAsync(job, "No valid files found in download path.");
                return;
            }

            var totalBytes = allFiles.Sum(f => f.Length);
            var uploadStartTime = DateTime.UtcNow;

            ConfigureProgressContext(job, totalBytes);

            var rootFolderId = await EnsureRootFolderExistsAsync(job, accessToken, cancellationToken);
            if (rootFolderId == null) return;

            var filesToProcess = await FilterAlreadyUploadedFilesAsync(allFiles, job.DownloadPath!);
            var folderIdMap = await CreateFolderHierarchyAsync([.. filesToProcess], job.DownloadPath!, rootFolderId, accessToken, cancellationToken);
            var result = await UploadFilesAsync(job, [.. filesToProcess], folderIdMap, accessToken, cancellationToken);

            await FinalizeJobAsync(job, result, totalBytes, allFiles.Length, uploadStartTime);
        }

        // --- Step Handlers ---

        private async Task<IRedisLock?> AcquireJobLockAsync(UserJob job, CancellationToken token)
        {
            var lockKey = $"gdrive:lock:{job.Id}";
            // FIX: Extended lock time to 2 hours to cover large file uploads
            var lockExpiry = TimeSpan.FromHours(2);

            var distributedLock = await redisLockService.AcquireLockAsync(lockKey, lockExpiry, token);

            if (distributedLock == null)
            {
                Logger.LogWarning("{LogPrefix} Job is already being processed by another instance | JobId: {JobId}",
                    LogPrefix, job.Id);
                return null;
            }

            Logger.LogInformation("{LogPrefix} Acquired Redis lock | JobId: {JobId} | Expiry: {Expiry}",
                LogPrefix, job.Id, lockExpiry);

            return distributedLock;
        }

        private async Task HandleStatusTransitionAsync(UserJob job)
        {
            if (job.Status == JobStatus.PENDING_UPLOAD)
            {
                Logger.LogInformation("{LogPrefix} Job ready for upload, transitioning to UPLOADING | JobId: {JobId}", LogPrefix, job.Id);
                job.CurrentState = "Starting upload...";
                if (job.StartedAt == null) job.StartedAt = DateTime.UtcNow;
                job.LastHeartbeat = DateTime.UtcNow;

                await JobStatusService.TransitionJobStatusAsync(
                    job,
                    JobStatus.UPLOADING,
                    StatusChangeSource.Worker,
                    metadata: new { provider = "GoogleDrive", startedAt = job.StartedAt });
            }
            else if (job.Status == JobStatus.UPLOAD_RETRY)
            {
                Logger.LogInformation("{LogPrefix} Retrying job | JobId: {JobId} | Retry: {NextRetry}", LogPrefix, job.Id, job.NextRetryAt);
                job.CurrentState = "Retrying upload...";
                job.LastHeartbeat = DateTime.UtcNow;

                await JobStatusService.TransitionJobStatusAsync(
                    job,
                    JobStatus.UPLOADING,
                    StatusChangeSource.Worker,
                    metadata: new { provider = "GoogleDrive", retrying = true, previousNextRetry = job.NextRetryAt });
            }
            else if (job.Status == JobStatus.UPLOADING)
            {
                if (job.StartedAt == null)
                {
                    job.StartedAt = DateTime.UtcNow;
                    await UnitOfWork.Complete();
                }
                Logger.LogInformation("{LogPrefix} Resuming job from recovery | JobId: {JobId}", LogPrefix, job.Id);
            }
            else
            {
                // Warn but allow execution (could be manual trigger)
                Logger.LogWarning("{LogPrefix} Unexpected status: {Status} | JobId: {JobId}", LogPrefix, job.Status, job.Id);
            }
        }

        private async Task<bool> ValidateEnvironmentAsync(UserJob job)
        {
            if (string.IsNullOrEmpty(job.DownloadPath) || !Directory.Exists(job.DownloadPath))
            {
                await MarkJobFailedAsync(job, $"Download path not found: {job.DownloadPath}");
                return false;
            }

            if (job.StorageProfile == null || job.StorageProfile.ProviderType != StorageProviderType.GoogleDrive)
            {
                await MarkJobFailedAsync(job, "Invalid storage profile.");
                return false;
            }

            return true;
        }

        private async Task<string?> AuthenticateAsync(UserJob job, CancellationToken token)
        {
            await UpdateHeartbeatAsync(job, "Authenticating...");

            var tokenResult = await googleDriveService.GetAccessTokenAsync(job.StorageProfile, token);
            if (tokenResult.IsFailure)
            {
                await MarkJobFailedAsync(job, $"Auth failed: {tokenResult.Error.Message}");
                return null;
            }

            // Token refresh has already persisted changes, but ensure we save any other job updates
            await UnitOfWork.Complete();
            return tokenResult.Value;
        }

        private void ConfigureProgressContext(UserJob job, long totalBytes)
        {
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
        }

        private async Task<List<FileInfo>> FilterAlreadyUploadedFilesAsync(FileInfo[] files, string downloadPath)
        {
            var filesToProcess = new List<FileInfo>();

            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(downloadPath, file.FullName);
                var completedId = await progressContext.GetCompletedFileAsync(relativePath);

                if (!string.IsNullOrEmpty(completedId))
                {
                    Logger.LogDebug("{LogPrefix} Skipping {File} (Already in Redis)", LogPrefix, file.Name);
                    await progressContext.MarkFileCompletedAsync(file.Name, file.Length);
                }
                else
                {
                    filesToProcess.Add(file);
                }
            }

            return filesToProcess;
        }

        private async Task FinalizeJobAsync(UserJob job, UploadResult result, long totalBytes, int fileCount, DateTime uploadStartTime)
        {
            if (!result.AllFilesUploaded)
            {
                await MarkJobFailedAsync(job, $"Failed to upload {result.FailedFiles} of {result.TotalFiles} files.", hasRetries: true);
                return;
            }

            var duration = (DateTime.UtcNow - uploadStartTime).TotalSeconds;
            speedMetrics.RecordUploadComplete(job.Id, job.UserId, "googledrive_upload", totalBytes, duration);

            job.CompletedAt = DateTime.UtcNow;
            job.CurrentState = "Upload completed successfully";
            job.NextRetryAt = null;

            await JobStatusService.TransitionJobStatusAsync(
                job,
                JobStatus.COMPLETED,
                StatusChangeSource.Worker,
                metadata: new { totalBytes, filesCount = fileCount, durationSeconds = duration, completedAt = job.CompletedAt });

            Logger.LogInformation("{LogPrefix} Completed successfully | JobId: {JobId}", LogPrefix, job.Id);
        }

        // --- Failure Hook ---

        protected override async Task MarkJobFailedAsync(UserJob job, string errorMessage, bool hasRetries = false)
        {
            try
            {
                // Delete Redis lock before marking as failed
                await googleDriveService.DeleteUploadLockAsync(job.Id);
            }
            catch (Exception ex)
            {
                // Log but don't fail - lock might not exist or already expired
                Logger.LogWarning(ex, "{LogPrefix} Failed to delete lock on job failure | JobId: {JobId}", LogPrefix, job.Id);
            }
            finally
            {
                await base.MarkJobFailedAsync(job, errorMessage, hasRetries);
            }
        }

        // --- Helper Methods ---

        private FileInfo[] GetFilesToUpload(string downloadPath)
        {
            try
            {
                var dir = new DirectoryInfo(downloadPath);
                if (!dir.Exists) return [];

                // FIX: Only filter strictly system files, allow .torrent files if they are part of user content
                return dir.GetFiles("*", SearchOption.AllDirectories)
                    .Where(f =>
                        !f.Name.Equals("dht_nodes.cache", StringComparison.OrdinalIgnoreCase) &&
                        !f.Name.Equals("fastresume", StringComparison.OrdinalIgnoreCase) &&
                        !f.Name.EndsWith(".fresume", StringComparison.OrdinalIgnoreCase) &&
                        !f.Name.EndsWith(".dht", StringComparison.OrdinalIgnoreCase)
                    )
                    .ToArray();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{LogPrefix} Error listing files", LogPrefix);
                return [];
            }
        }

        private async Task<string?> EnsureRootFolderExistsAsync(UserJob job, string accessToken, CancellationToken token)
        {
            await UpdateHeartbeatAsync(job, "Checking Google Drive folder...");

            var rootId = await progressContext.GetRootFolderIdAsync(job.Id);
            if (!string.IsNullOrEmpty(rootId)) return rootId;

            var folderName = $"Torrent_{job.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            var result = await googleDriveService.CreateFolderAsync(folderName, null, accessToken, token);

            if (result.IsFailure)
            {
                await MarkJobFailedAsync(job, $"Failed to create root folder: {result.Error.Message}");
                return null;
            }

            await progressContext.SetRootFolderIdAsync(job.Id, result.Value);
            return result.Value;
        }

        private async Task<Dictionary<string, string>> CreateFolderHierarchyAsync(
            FileInfo[] files,
            string rootPath,
            string rootId,
            string accessToken,
            CancellationToken token)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [""] = rootId,
                ["."] = rootId
            };

            var uniqueDirs = files
                .Select(f => Path.GetRelativePath(rootPath, f.DirectoryName!))
                .Where(p => p != "." && !string.IsNullOrEmpty(p))
                .Distinct()
                .OrderBy(p => p.Split(Path.DirectorySeparatorChar).Length); // Create parents first

            foreach (var relPath in uniqueDirs)
            {
                if (token.IsCancellationRequested) break;

                var parts = relPath.Split(Path.DirectorySeparatorChar);
                var parentRel = parts.Length > 1 ? string.Join(Path.DirectorySeparatorChar, parts[..^1]) : ".";
                var name = parts[^1];

                var parentId = map.TryGetValue(parentRel, out var pid) ? pid : rootId;

                // Use FindOrCreateFolderAsync to check for existing folder before creating
                var result = await googleDriveService.FindOrCreateFolderAsync(name, parentId, accessToken, token);

                if (result.IsSuccess)
                    map[relPath] = result.Value;
                else
                    map[relPath] = parentId; // Fallback to parent
            }
            return map;
        }

        private async Task<UploadResult> UploadFilesAsync(
            UserJob job,
            FileInfo[] files,
            Dictionary<string, string> folderMap,
            string accessToken,
            CancellationToken token)
        {
            var result = new UploadResult { TotalFiles = files.Length };

            foreach (var file in files)
            {
                if (token.IsCancellationRequested) break;

                var relDir = Path.GetRelativePath(job.DownloadPath!, file.DirectoryName!);
                var relPath = Path.GetRelativePath(job.DownloadPath!, file.FullName);
                var folderId = folderMap.TryGetValue(relDir, out var fid) ? fid : folderMap["."];

                // Check Drive First (Fallback if Redis was flushed)
                var exists = await googleDriveService.CheckFileExistsAsync(folderId, file.Name, accessToken, token);
                if (exists.IsSuccess && !string.IsNullOrEmpty(exists.Value))
                {
                    await progressContext.SetCompletedFileAsync(relPath, exists.Value);
                    await progressContext.MarkFileCompletedAsync(file.Name, file.Length);
                    continue;
                }

                // Upload
                var upload = await googleDriveService.UploadFileAsync(
                    file.FullName, file.Name, folderId, accessToken, relPath, token);

                if (upload.IsSuccess)
                {
                    await progressContext.SetCompletedFileAsync(relPath, upload.Value);
                    await progressContext.MarkFileCompletedAsync(file.Name, file.Length);
                }
                else
                {
                    result.FailedFiles++;
                    Logger.LogCritical("{LogPrefix} Upload failed for {File}: {Error}", LogPrefix, file.Name, upload.Error.Message);

                    // Recover partial progress
                    var resumeUri = await progressContext.GetResumeUriAsync(relPath);
                    if (!string.IsNullOrEmpty(resumeUri))
                    {
                        var status = await googleDriveService.QueryUploadStatusAsync(resumeUri, file.Length, accessToken, token);
                        if (status.IsSuccess) await progressContext.MarkBytesCompletedAsync(file.Name, status.Value);
                    }
                }
            }
            return result;
        }


    }
}
