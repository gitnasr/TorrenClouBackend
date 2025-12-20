using System.Text.Json;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;
using TorreClou.Infrastructure.Workers;
using TorreClou.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Hangfire;

namespace TorreClou.Sync.Worker.Services
{
    public class S3SyncJob(
        IUnitOfWork unitOfWork,
        ILogger<S3SyncJob> logger,
        IOptions<BackblazeSettings> backblazeSettings,
        IS3ResumableUploadService s3UploadService) : BaseJob<S3SyncJob>(unitOfWork, logger)
    {
        private readonly BackblazeSettings _backblazeSettings = backblazeSettings.Value;
        private const long PartSize = 10 * 1024 * 1024; // 10MB per part
        private const int ProgressUpdateIntervalSeconds = 10;

        protected override string LogPrefix => "[S3:SYNC]";

        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
        [Queue("sync")]
        public async Task ExecuteAsync(int syncId, CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.LogInformation("{LogPrefix} Starting S3 sync job | SyncId: {SyncId}", LogPrefix, syncId);

                // Load Sync entity
                var syncRepository = UnitOfWork.Repository<Sync>();
                var sync = await syncRepository.GetByIdAsync(syncId);
                if (sync == null)
                {
                    Logger.LogError("{LogPrefix} Sync entity not found | SyncId: {SyncId}", LogPrefix, syncId);
                    return;
                }

                // Load UserJob for file access
                var jobRepository = UnitOfWork.Repository<UserJob>();
                var job = await jobRepository.GetByIdAsync(sync.JobId);
                if (job == null)
                {
                    Logger.LogError("{LogPrefix} UserJob not found | SyncId: {SyncId} | JobId: {JobId}", 
                        LogPrefix, syncId, sync.JobId);
                    sync.Status = SyncStatus.Failed;
                    sync.ErrorMessage = "UserJob not found";
                    await UnitOfWork.Complete();
                    return;
                }

                await ExecuteCoreAsync(sync, job, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{LogPrefix} Fatal error in S3 sync job | SyncId: {SyncId}", LogPrefix, syncId);
                throw;
            }
        }

        private async Task ExecuteCoreAsync(Sync sync, UserJob job, CancellationToken cancellationToken)
        {
            // Store original download path (block storage path) for cleanup
            var originalDownloadPath = sync.LocalFilePath ?? job.DownloadPath;

            try
            {
                // Validate sync status - accept Pending or Retrying
                if (sync.Status != SyncStatus.Pending && sync.Status != SyncStatus.Retrying)
                {
                    Logger.LogWarning("{LogPrefix} Sync is not in valid state | SyncId: {SyncId} | JobId: {JobId} | Status: {Status}",
                        LogPrefix, sync.Id, job.Id, sync.Status);
                    return;
                }

                // Validate download path
                if (string.IsNullOrEmpty(originalDownloadPath))
                {
                    await MarkSyncFailedAsync(sync, "Download path is empty", hasRetries: false);
                    return;
                }

                // Validate local directory exists
                if (!Directory.Exists(originalDownloadPath))
                {
                    await MarkSyncFailedAsync(sync, $"Download directory does not exist: {originalDownloadPath}", hasRetries: false);
                    return;
                }

                Logger.LogInformation("{LogPrefix} Starting S3 sync | SyncId: {SyncId} | JobId: {JobId} | Source: {Source} | Bucket: {Bucket}",
                    LogPrefix, sync.Id, job.Id, originalDownloadPath, _backblazeSettings.BucketName);

                // Update sync status
                sync.Status = SyncStatus.InProgress;
                sync.StartedAt = DateTime.UtcNow;
                await UnitOfWork.Complete();

                // Scan files to upload (exclude .torrent, .dht, .fresume files)
                var filesToUpload = GetFilesToUpload(originalDownloadPath);
                if (filesToUpload.Length == 0)
                {
                    await MarkSyncFailedAsync(sync, "No files found to upload", hasRetries: false);
                    return;
                }

                var totalBytes = filesToUpload.Sum(f => f.Length);
                // Update sync with file count and total bytes if not set
                if (sync.FilesTotal == 0 || sync.TotalBytes == 0)
                {
                    sync.FilesTotal = filesToUpload.Length;
                    sync.TotalBytes = totalBytes;
                }

                Logger.LogInformation("{LogPrefix} Found {FileCount} files to upload | SyncId: {SyncId} | JobId: {JobId} | TotalSize: {SizeMB:F2} MB",
                    LogPrefix, filesToUpload.Length, sync.Id, job.Id, totalBytes / (1024.0 * 1024.0));

                // Upload each file
                var overallBytesUploaded = sync.BytesSynced;
                var filesSynced = sync.FilesSynced;
                var startTime = sync.StartedAt ?? DateTime.UtcNow;
                var lastProgressUpdate = DateTime.UtcNow;

                for (int i = filesSynced; i < filesToUpload.Length; i++)
                {
                    var file = filesToUpload[i];
                    var relativePath = Path.GetRelativePath(originalDownloadPath, file.FullName);
                    var s3Key = sync.S3KeyPrefix != null 
                        ? $"{sync.S3KeyPrefix}/{relativePath.Replace('\\', '/')}"
                        : $"torrents/{job.Id}/{relativePath.Replace('\\', '/')}";

                    Logger.LogInformation("{LogPrefix} Uploading file {Index}/{Total} | SyncId: {SyncId} | JobId: {JobId} | File: {FileName} | Size: {SizeMB:F2} MB",
                        LogPrefix, i + 1, filesToUpload.Length, sync.Id, job.Id, file.Name, file.Length / (1024.0 * 1024.0));

                    // Upload file with resume support
                    var uploadResult = await UploadFileWithResumeAsync(sync, job, file, s3Key, cancellationToken);
                    if (uploadResult.IsFailure)
                    {
                        await MarkSyncFailedAsync(sync, $"Failed to upload file {file.Name}: {uploadResult.Error.Message}", hasRetries: true);
                        return;
                    }

                    overallBytesUploaded += file.Length;
                    filesSynced++;

                    // Update sync progress
                    sync.BytesSynced = overallBytesUploaded;
                    sync.FilesSynced = filesSynced;

                    // Update progress periodically
                    var now = DateTime.UtcNow;
                    if ((now - lastProgressUpdate).TotalSeconds >= ProgressUpdateIntervalSeconds)
                    {
                        var percent = (overallBytesUploaded / (double)totalBytes) * 100;
                        var elapsed = (now - startTime).TotalSeconds;
                        var speedMBps = elapsed > 0 ? (overallBytesUploaded / (1024.0 * 1024.0)) / elapsed : 0;

                        await UnitOfWork.Complete();

                        Logger.LogInformation("{LogPrefix} Sync progress | SyncId: {SyncId} | JobId: {JobId} | {Percent:F1}% | {UploadedMB:F1}/{TotalMB:F1} MB | Speed: {SpeedMBps:F2} MB/s",
                            LogPrefix, sync.Id, job.Id, percent, overallBytesUploaded / (1024.0 * 1024.0), totalBytes / (1024.0 * 1024.0), speedMBps);

                        lastProgressUpdate = now;
                    }
                }

                // All files uploaded successfully
                Logger.LogInformation("{LogPrefix} All files synced successfully | SyncId: {SyncId} | JobId: {JobId} | Files: {FileCount} | TotalSize: {SizeMB:F2} MB",
                    LogPrefix, sync.Id, job.Id, filesToUpload.Length, totalBytes / (1024.0 * 1024.0));

                // Update sync status - sync completed
                sync.Status = SyncStatus.Completed;
                sync.CompletedAt = DateTime.UtcNow;
                sync.BytesSynced = overallBytesUploaded;
                sync.FilesSynced = filesSynced;
                await UnitOfWork.Complete();

                // Clean up block storage after successful sync
                // Add a small delay to ensure Google Drive has finished if it was still processing
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                await CleanupBlockStorageAsync(originalDownloadPath);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{LogPrefix} Fatal error during sync | SyncId: {SyncId} | JobId: {JobId}",
                    LogPrefix, sync.Id, job.Id);
                
                // Mark sync as failed
                sync.Status = SyncStatus.Failed;
                sync.ErrorMessage = ex.Message;
                await UnitOfWork.Complete();
                
                throw;
            }
        }

        private FileInfo[] GetFilesToUpload(string downloadPath)
        {
            try
            {
                var directory = new DirectoryInfo(downloadPath);
                if (!directory.Exists)
                {
                    return [];
                }

                var allFiles = directory.GetFiles("*", SearchOption.AllDirectories);
                
                // Exclude MonoTorrent metadata files
                var filesToUpload = allFiles
                    .Where(f => !f.Name.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) &&
                               !f.Name.EndsWith(".dht", StringComparison.OrdinalIgnoreCase) &&
                               !f.Name.EndsWith(".fresume", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                return filesToUpload;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{LogPrefix} Error scanning files | Path: {Path}",
                    LogPrefix, downloadPath);
                return [];
            }
        }

        private async Task<Result> UploadFileWithResumeAsync(Sync sync, UserJob job, FileInfo file, string s3Key, CancellationToken cancellationToken)
        {
            try
            {
                // Check if file already exists in S3
                var existsResult = await s3UploadService.CheckObjectExistsAsync(_backblazeSettings.BucketName, s3Key, cancellationToken);
                if (existsResult.IsSuccess && existsResult.Value)
                {
                    Logger.LogInformation("{LogPrefix} File already exists in S3, skipping | JobId: {JobId} | Key: {Key}",
                        LogPrefix, job.Id, s3Key);
                    return Result.Success();
                }

                // Check for existing sync progress
                var existingProgress = await UnitOfWork.Repository<S3SyncProgress>()
                    .GetEntityWithSpec(new BaseSpecification<S3SyncProgress>(p => p.SyncId == sync.Id && p.S3Key == s3Key));

                S3SyncProgress? progress = existingProgress;
                string? uploadId = null;
                List<PartETag>? existingParts = null;

                if (progress != null && progress.Status == SyncProgressStatus.InProgress && !string.IsNullOrEmpty(progress.UploadId))
                {
                    // Resume existing upload
                    uploadId = progress.UploadId;
                    existingParts = ParsePartETags(progress.PartETags);
                    
                    Logger.LogInformation("{LogPrefix} Resuming upload | JobId: {JobId} | Key: {Key} | UploadId: {UploadId} | PartsCompleted: {PartsCompleted}/{TotalParts}",
                        LogPrefix, job.Id, s3Key, uploadId, progress.PartsCompleted, progress.TotalParts);

                    // Verify upload still exists in S3
                    var listPartsResult = await s3UploadService.ListPartsAsync(_backblazeSettings.BucketName, s3Key, uploadId, cancellationToken);
                    if (listPartsResult.IsFailure)
                    {
                        // Upload expired or doesn't exist, start fresh
                        Logger.LogWarning("{LogPrefix} Multipart upload expired, starting fresh | JobId: {JobId} | Key: {Key}",
                            LogPrefix, job.Id, s3Key);
                        await s3UploadService.AbortUploadAsync(_backblazeSettings.BucketName, s3Key, uploadId, cancellationToken);
                        uploadId = null;
                        existingParts = null;
                        progress = null;
                    }
                    else
                    {
                        // Merge S3 parts with our stored parts
                        var s3Parts = listPartsResult.Value;
                        existingParts = MergePartETags(existingParts, s3Parts);
                    }
                }

                // Initiate new upload if needed
                if (string.IsNullOrEmpty(uploadId))
                {
                    var initResult = await s3UploadService.InitiateUploadAsync(_backblazeSettings.BucketName, s3Key, file.Length, cancellationToken: cancellationToken);
                    if (initResult.IsFailure)
                    {
                        return Result.Failure(initResult.Error.Code, initResult.Error.Message);
                    }
                    uploadId = initResult.Value;

                    // Create new progress record
                    var calculatedTotalParts = (int)Math.Ceiling((double)file.Length / PartSize);
                    progress = new S3SyncProgress
                    {
                        JobId = job.Id,
                        SyncId = sync.Id,
                        LocalFilePath = file.FullName,
                        S3Key = s3Key,
                        UploadId = uploadId,
                        PartSize = PartSize,
                        TotalParts = calculatedTotalParts,
                        PartsCompleted = 0,
                        BytesUploaded = 0,
                        TotalBytes = file.Length,
                        Status = SyncProgressStatus.InProgress,
                        StartedAt = DateTime.UtcNow
                    };
                    UnitOfWork.Repository<S3SyncProgress>().Add(progress);
                    await UnitOfWork.Complete();
                    existingParts = [];
                }
                else
                {
                    // Update existing progress
                    progress!.Status = SyncProgressStatus.InProgress;
                    progress.UpdatedAt = DateTime.UtcNow;
                    await UnitOfWork.Complete();
                }

                // Upload parts
                await using var fileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                var startPartNumber = existingParts!.Count > 0 ? existingParts.Max(p => p.PartNumber) + 1 : 1;
                var totalParts = progress.TotalParts;

                for (int partNumber = startPartNumber; partNumber <= totalParts; partNumber++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var partStart = (partNumber - 1) * PartSize;
                    var partSize = (int)Math.Min(PartSize, file.Length - partStart);
                    fileStream.Seek(partStart, SeekOrigin.Begin);

                    var partBuffer = new byte[partSize];
                    var bytesRead = await fileStream.ReadAsync(partBuffer, 0, partSize, cancellationToken);
                    if (bytesRead != partSize)
                    {
                        return Result.Failure("READ_ERROR", $"Failed to read part {partNumber}: expected {partSize} bytes, got {bytesRead}");
                    }

                    await using var partStream = new MemoryStream(partBuffer);
                    var uploadPartResult = await s3UploadService.UploadPartAsync(
                        _backblazeSettings.BucketName, s3Key, uploadId, partNumber, partStream, cancellationToken);

                    if (uploadPartResult.IsFailure)
                    {
                        return Result.Failure(uploadPartResult.Error.Code, uploadPartResult.Error.Message);
                    }

                    existingParts.Add(uploadPartResult.Value);
                    progress.PartsCompleted = existingParts.Count;
                    progress.BytesUploaded = Math.Min(progress.PartsCompleted * PartSize, file.Length);
                    progress.LastPartNumber = partNumber;
                    progress.PartETags = SerializePartETags(existingParts);
                    progress.UpdatedAt = DateTime.UtcNow;
                    await UnitOfWork.Complete();

                    Logger.LogDebug("{LogPrefix} Uploaded part {PartNumber}/{TotalParts} | JobId: {JobId} | Key: {Key}",
                        LogPrefix, partNumber, totalParts, job.Id, s3Key);
                }

                // Complete multipart upload
                var completeResult = await s3UploadService.CompleteUploadAsync(
                    _backblazeSettings.BucketName, s3Key, uploadId, existingParts, cancellationToken);

                if (completeResult.IsFailure)
                {
                    return Result.Failure(completeResult.Error.Code, completeResult.Error.Message);
                }

                // Mark progress as completed and delete record
                progress.Status = SyncProgressStatus.Completed;
                progress.CompletedAt = DateTime.UtcNow;
                UnitOfWork.Repository<S3SyncProgress>().Delete(progress);
                await UnitOfWork.Complete();

                Logger.LogInformation("{LogPrefix} File uploaded successfully | JobId: {JobId} | Key: {Key} | Parts: {Parts}",
                    LogPrefix, job.Id, s3Key, totalParts);

                return Result.Success();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{LogPrefix} Error uploading file | JobId: {JobId} | Key: {Key}",
                    LogPrefix, job.Id, s3Key);
                return Result.Failure("UPLOAD_ERROR", ex.Message);
            }
        }

        private List<PartETag> ParsePartETags(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json) || json == "[]")
                {
                    return [];
                }
                return JsonSerializer.Deserialize<List<PartETag>>(json) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private string SerializePartETags(List<PartETag> parts)
        {
            return JsonSerializer.Serialize(parts);
        }

        private List<PartETag> MergePartETags(List<PartETag>? stored, List<PartETag> s3Parts)
        {
            var merged = new List<PartETag>();
            if (stored != null && stored.Count > 0)
            {
                merged.AddRange(stored);
            }
            
            // Add S3 parts that aren't already in stored
            foreach (var s3Part in s3Parts)
            {
                if (!merged.Any(p => p.PartNumber == s3Part.PartNumber))
                {
                    merged.Add(s3Part);
                }
            }

            return merged.OrderBy(p => p.PartNumber).ToList();
        }

        private async Task MarkSyncFailedAsync(Sync sync, string errorMessage, bool hasRetries = true)
        {
            sync.Status = SyncStatus.Failed;
            sync.ErrorMessage = errorMessage;
            
            if (hasRetries)
            {
                sync.RetryCount++;
                sync.Status = SyncStatus.Retrying;
                sync.NextRetryAt = DateTime.UtcNow.AddMinutes(5 * sync.RetryCount); // Exponential backoff
            }
            
            await UnitOfWork.Complete();
            
            Logger.LogError("{LogPrefix} Sync marked as failed | SyncId: {SyncId} | JobId: {JobId} | Error: {Error} | Retries: {Retries}",
                LogPrefix, sync.Id, sync.JobId, errorMessage, sync.RetryCount);
        }

        private async Task CleanupBlockStorageAsync(string originalDownloadPath)
        {
            try
            {
                if (string.IsNullOrEmpty(originalDownloadPath) || !Directory.Exists(originalDownloadPath))
                {
                    return;
                }

                Logger.LogInformation("{LogPrefix} Cleaning up block storage | Path: {Path}",
                    LogPrefix, originalDownloadPath);

                Directory.Delete(originalDownloadPath, recursive: true);

                Logger.LogInformation("{LogPrefix} Block storage cleaned up | Path: {Path}",
                    LogPrefix, originalDownloadPath);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "{LogPrefix} Failed to cleanup block storage | Path: {Path}",
                    LogPrefix, originalDownloadPath);
                // Don't fail the job if cleanup fails
            }
        }

    }
}

