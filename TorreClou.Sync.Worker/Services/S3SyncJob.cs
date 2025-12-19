using System.Text.Json;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
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

        protected override void ConfigureSpecification(BaseSpecification<UserJob> spec)
        {
            spec.AddInclude(j => j.StorageProfile);
            spec.AddInclude(j => j.User);
        }

        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
        [Queue("sync")]
        public new async Task ExecuteAsync(int jobId, CancellationToken cancellationToken = default)
        {
            await base.ExecuteAsync(jobId, cancellationToken);
        }

        protected override async Task ExecuteCoreAsync(UserJob job, CancellationToken cancellationToken)
        {
            // Store original download path (block storage path) for cleanup
            var originalDownloadPath = job.DownloadPath;

            try
            {
                // Validate job status - accept PENDING_UPLOAD (from TorrentDownloadJob) or SYNCING/SYNC_RETRY (retry)
                if (job.Status != JobStatus.PENDING_UPLOAD && 
                    job.Status != JobStatus.SYNCING && 
                    job.Status != JobStatus.SYNC_RETRY)
                {
                    Logger.LogWarning("{LogPrefix} Job is not in valid state for sync | JobId: {JobId} | Status: {Status}",
                        LogPrefix, job.Id, job.Status);
                    return;
                }

                // Validate download path
                if (string.IsNullOrEmpty(originalDownloadPath))
                {
                    await MarkJobFailedAsync(job, "Download path is empty", hasRetries: false);
                    return;
                }

                // Validate local directory exists
                if (!Directory.Exists(originalDownloadPath))
                {
                    await MarkJobFailedAsync(job, $"Download directory does not exist: {originalDownloadPath}", hasRetries: false);
                    return;
                }

                Logger.LogInformation("{LogPrefix} Starting S3 sync | JobId: {JobId} | Source: {Source} | Bucket: {Bucket}",
                    LogPrefix, job.Id, originalDownloadPath, _backblazeSettings.BucketName);

                // Update job status
                job.Status = JobStatus.SYNCING;
                job.CurrentState = "Scanning files for sync...";
                job.LastHeartbeat = DateTime.UtcNow;
                await UnitOfWork.Complete();

                // Scan files to upload (exclude .torrent, .dht, .fresume files)
                var filesToUpload = GetFilesToUpload(originalDownloadPath);
                if (filesToUpload.Length == 0)
                {
                    await MarkJobFailedAsync(job, "No files found to upload", hasRetries: false);
                    return;
                }

                var totalBytes = filesToUpload.Sum(f => f.Length);
                Logger.LogInformation("{LogPrefix} Found {FileCount} files to upload | JobId: {JobId} | TotalSize: {SizeMB:F2} MB",
                    LogPrefix, filesToUpload.Length, job.Id, totalBytes / (1024.0 * 1024.0));

                // Upload each file
                var overallBytesUploaded = 0L;
                var startTime = DateTime.UtcNow;
                var lastProgressUpdate = DateTime.UtcNow;

                for (int i = 0; i < filesToUpload.Length; i++)
                {
                    var file = filesToUpload[i];
                    var relativePath = Path.GetRelativePath(originalDownloadPath, file.FullName);
                    var s3Key = $"torrents/{job.Id}/{relativePath.Replace('\\', '/')}";

                    Logger.LogInformation("{LogPrefix} Uploading file {Index}/{Total} | JobId: {JobId} | File: {FileName} | Size: {SizeMB:F2} MB",
                        LogPrefix, i + 1, filesToUpload.Length, job.Id, file.Name, file.Length / (1024.0 * 1024.0));

                    // Upload file with resume support
                    var uploadResult = await UploadFileWithResumeAsync(job, file, s3Key, cancellationToken);
                    if (uploadResult.IsFailure)
                    {
                        await MarkJobFailedAsync(job, $"Failed to upload file {file.Name}: {uploadResult.Error.Message}", hasRetries: true);
                        return;
                    }

                    overallBytesUploaded += file.Length;

                    // Update progress periodically
                    var now = DateTime.UtcNow;
                    if ((now - lastProgressUpdate).TotalSeconds >= ProgressUpdateIntervalSeconds)
                    {
                        var percent = (overallBytesUploaded / (double)totalBytes) * 100;
                        var elapsed = (now - startTime).TotalSeconds;
                        var speedMBps = elapsed > 0 ? (overallBytesUploaded / (1024.0 * 1024.0)) / elapsed : 0;

                        job.CurrentState = $"Syncing to S3: {percent:F1}% ({overallBytesUploaded / (1024.0 * 1024.0):F1}/{totalBytes / (1024.0 * 1024.0):F1} MB) @ {speedMBps:F2} MB/s";
                        job.LastHeartbeat = DateTime.UtcNow;
                        await UnitOfWork.Complete();
                        lastProgressUpdate = now;

                        Logger.LogInformation("{LogPrefix} Sync progress | JobId: {JobId} | {Percent:F1}% | {UploadedMB:F1}/{TotalMB:F1} MB | Speed: {SpeedMBps:F2} MB/s",
                            LogPrefix, job.Id, percent, overallBytesUploaded / (1024.0 * 1024.0), totalBytes / (1024.0 * 1024.0), speedMBps);
                    }
                }

                // All files uploaded successfully
                Logger.LogInformation("{LogPrefix} All files synced successfully | JobId: {JobId} | Files: {FileCount} | TotalSize: {SizeMB:F2} MB",
                    LogPrefix, job.Id, filesToUpload.Length, totalBytes / (1024.0 * 1024.0));

                // Update job status - sync completed (upload stream was already published by TorrentDownloadJob)
                // Don't change DownloadPath - keep block storage path for Google Drive Worker
                job.Status = JobStatus.PENDING_UPLOAD;
                job.CurrentState = "Files synced to S3. Upload in progress...";
                job.BytesDownloaded = job.TotalBytes;
                await UnitOfWork.Complete();

                // Clean up block storage after successful sync
                // Note: Google Drive Worker may still be using it, but S3 sync is complete
                // We clean up here to free space - Google Drive should have already read the files
                // If Google Drive is still processing, it should have the files in memory/temp already
                // Add a small delay to give Google Drive time to finish reading if it started late
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                await CleanupBlockStorageAsync(originalDownloadPath);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{LogPrefix} Fatal error during sync | JobId: {JobId}",
                    LogPrefix, job.Id);
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

        private async Task<Result> UploadFileWithResumeAsync(UserJob job, FileInfo file, string s3Key, CancellationToken cancellationToken)
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
                    .GetEntityWithSpec(new BaseSpecification<S3SyncProgress>(p => p.JobId == job.Id && p.S3Key == s3Key));

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
                    var totalParts = (int)Math.Ceiling((double)file.Length / PartSize);
                    progress = new S3SyncProgress
                    {
                        JobId = job.Id,
                        LocalFilePath = file.FullName,
                        S3Key = s3Key,
                        UploadId = uploadId,
                        PartSize = PartSize,
                        TotalParts = totalParts,
                        PartsCompleted = 0,
                        BytesUploaded = 0,
                        TotalBytes = file.Length,
                        Status = SyncProgressStatus.InProgress,
                        StartedAt = DateTime.UtcNow
                    };
                    await UnitOfWork.Repository<S3SyncProgress>().AddAsync(progress);
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
                await UnitOfWork.Repository<S3SyncProgress>().DeleteAsync(progress);
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

