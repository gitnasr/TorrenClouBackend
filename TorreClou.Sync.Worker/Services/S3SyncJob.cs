using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers; 
using System.Text.Json;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;
using TorreClou.Infrastructure.Settings;
using TorreClou.Infrastructure.Workers;
using SyncEntity = TorreClou.Core.Entities.Jobs.Sync;

namespace TorreClou.Sync.Worker.Services
{
    public class S3SyncJob(
        IUnitOfWork unitOfWork,
        ILogger<S3SyncJob> logger,
        IOptions<BackblazeSettings> backblazeSettings,
        IS3ResumableUploadService s3UploadService) : BaseJob<S3SyncJob>(unitOfWork, logger), IS3SyncJob
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

 
        public new async Task ExecuteAsync(int syncId, CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.LogInformation("{LogPrefix} Starting S3 sync job | SyncId: {SyncId}", LogPrefix, syncId);

                var syncRepository = UnitOfWork.Repository<SyncEntity>();
                var sync = await syncRepository.GetByIdAsync(syncId);
                if (sync == null)
                {
                    Logger.LogError("{LogPrefix} Sync entity not found | SyncId: {SyncId}", LogPrefix, syncId);
                    return;
                }

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

        protected override async Task ExecuteCoreAsync(UserJob job, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("Use ExecuteAsync(int syncId) instead");
        }

        private async Task ExecuteCoreAsync(SyncEntity sync, UserJob job, CancellationToken cancellationToken)
        {
            var originalDownloadPath = sync.LocalFilePath ?? job.DownloadPath;

            try
            {
                if (sync.Status != SyncStatus.Pending && sync.Status != SyncStatus.Retrying)
                {
                    Logger.LogWarning("{LogPrefix} Sync invalid state | SyncId: {SyncId} | Status: {Status}",
                        LogPrefix, sync.Id, sync.Status);
                    return;
                }

                if (string.IsNullOrEmpty(originalDownloadPath) || !Directory.Exists(originalDownloadPath))
                {
                    await MarkSyncFailedAsync(sync, $"Download directory missing: {originalDownloadPath}", hasRetries: false);
                    return;
                }

                Logger.LogInformation("{LogPrefix} Starting | SyncId: {SyncId} | Bucket: {Bucket}",
                    LogPrefix, sync.Id, _backblazeSettings.BucketName);

                sync.Status = SyncStatus.InProgress;
                sync.StartedAt = DateTime.UtcNow;
                await UnitOfWork.Complete();

                var filesToUpload = GetFilesToUpload(originalDownloadPath);
                if (filesToUpload.Length == 0)
                {
                    await MarkSyncFailedAsync(sync, "No files found to upload", hasRetries: false);
                    return;
                }

                var totalBytes = filesToUpload.Sum(f => f.Length);
                if (sync.FilesTotal == 0 || sync.TotalBytes == 0)
                {
                    sync.FilesTotal = filesToUpload.Length;
                    sync.TotalBytes = totalBytes;
                }

                var overallBytesUploaded = sync.BytesSynced;
                var filesSynced = sync.FilesSynced;
                var startTime = sync.StartedAt ?? DateTime.UtcNow;
                var lastProgressUpdate = DateTime.UtcNow;

                // Loop through files, skipping already synced ones
                for (int i = filesSynced; i < filesToUpload.Length; i++)
                {
                    var file = filesToUpload[i];
                    var relativePath = Path.GetRelativePath(originalDownloadPath, file.FullName);
                    var s3Key = sync.S3KeyPrefix != null
                        ? $"{sync.S3KeyPrefix}/{relativePath.Replace('\\', '/')}"
                        : $"torrents/{job.Id}/{relativePath.Replace('\\', '/')}";

                    Logger.LogInformation("{LogPrefix} File {Index}/{Total} | {FileName} | {SizeMB:F2} MB",
                        LogPrefix, i + 1, filesToUpload.Length, file.Name, file.Length / (1024.0 * 1024.0));

                    var uploadResult = await UploadFileWithResumeAsync(sync, job, file, s3Key, cancellationToken);
                    if (uploadResult.IsFailure)
                    {
                        await MarkSyncFailedAsync(sync, $"Upload failed for {file.Name}: {uploadResult.Error.Message}", hasRetries: true);
                        return;
                    }

                    overallBytesUploaded += file.Length;
                    filesSynced++;

                    // Throttle DB updates
                    var now = DateTime.UtcNow;
                    if ((now - lastProgressUpdate).TotalSeconds >= ProgressUpdateIntervalSeconds)
                    {
                        sync.BytesSynced = overallBytesUploaded;
                        sync.FilesSynced = filesSynced;
                        await UnitOfWork.Complete();

                        var percent = (overallBytesUploaded / (double)totalBytes) * 100;
                        Logger.LogInformation("{LogPrefix} Progress: {Percent:F1}% | {Uploaded}/{Total} MB",
                            LogPrefix, percent, overallBytesUploaded >> 20, totalBytes >> 20);

                        lastProgressUpdate = now;
                    }
                }

                sync.Status = SyncStatus.Completed;
                sync.CompletedAt = DateTime.UtcNow;
                sync.BytesSynced = overallBytesUploaded;
                sync.FilesSynced = filesSynced;
                await UnitOfWork.Complete();

                Logger.LogInformation("{LogPrefix} Sync Complete | SyncId: {SyncId}", LogPrefix, sync.Id);

                // Safe to cleanup now as this is the final step in pipeline
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                await CleanupBlockStorageAsync(originalDownloadPath);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{LogPrefix} Fatal error during sync", LogPrefix);
                sync.Status = SyncStatus.Failed;
                sync.ErrorMessage = ex.Message;
                await UnitOfWork.Complete();
                throw;
            }
        }

        private async Task<Result> UploadFileWithResumeAsync(SyncEntity sync, UserJob job, FileInfo file, string s3Key, CancellationToken cancellationToken)
        {
            try
            {
                // 1. Check Existence
                var existsResult = await s3UploadService.CheckObjectExistsAsync(_backblazeSettings.BucketName, s3Key, cancellationToken);
                if (existsResult.IsSuccess && existsResult.Value) return Result.Success();

                // 2. Load/Create Progress Record
                var existingProgress = await UnitOfWork.Repository<S3SyncProgress>()
                    .GetEntityWithSpec(new BaseSpecification<S3SyncProgress>(p => p.SyncId == sync.Id && p.S3Key == s3Key));

                S3SyncProgress? progress = existingProgress;
                string? uploadId = null;
                List<PartETag>? existingParts = null;

                // 3. Resume Logic
                if (progress != null && progress.Status == SyncProgressStatus.InProgress && !string.IsNullOrEmpty(progress.UploadId))
                {
                    uploadId = progress.UploadId;
                    existingParts = ParsePartETags(progress.PartETags);

                    // Verify with S3
                    var listPartsResult = await s3UploadService.ListPartsAsync(_backblazeSettings.BucketName, s3Key, uploadId, cancellationToken);
                    if (listPartsResult.IsFailure)
                    {
                        Logger.LogWarning("{LogPrefix} Remote upload session not found, restarting | Key: {Key}", LogPrefix, s3Key);
                        uploadId = null; // Forces restart
                    }
                    else
                    {
                        // ROBUST MERGE: S3 is the source of truth
                        existingParts = MergePartETags(existingParts, listPartsResult.Value);
                    }
                }

                // 4. Initialize if needed
                if (string.IsNullOrEmpty(uploadId))
                {
                    var init = await s3UploadService.InitiateUploadAsync(_backblazeSettings.BucketName, s3Key, file.Length, cancellationToken: cancellationToken);
                    if (init.IsFailure) return Result.Failure(init.Error.Code, init.Error.Message);

                    uploadId = init.Value;
                    var totalParts = (int)Math.Ceiling((double)file.Length / PartSize);

                    // Create new progress entry
                    progress = new S3SyncProgress
                    {
                        JobId = job.Id,
                        SyncId = sync.Id,
                        LocalFilePath = file.FullName,
                        S3Key = s3Key,
                        UploadId = uploadId,
                        PartSize = PartSize,
                        TotalParts = totalParts,
                        Status = SyncProgressStatus.InProgress,
                        StartedAt = DateTime.UtcNow,
                        TotalBytes = file.Length
                    };
                    UnitOfWork.Repository<S3SyncProgress>().Add(progress);
                    await UnitOfWork.Complete();
                    existingParts = [];
                }
                else
                {
                    progress!.UpdatedAt = DateTime.UtcNow;
                    await UnitOfWork.Complete();
                }

                // 5. Upload Loop (Memory Optimized)
                await using var fileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                var startPartNumber = existingParts!.Count > 0 ? existingParts.Max(p => p.PartNumber) + 1 : 1;

                // RENT buffer from pool to avoid GC pressure
                var buffer = ArrayPool<byte>.Shared.Rent((int)PartSize);

                try
                {
                    for (int partNumber = startPartNumber; partNumber <= progress.TotalParts; partNumber++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var partStart = (partNumber - 1) * PartSize;
                        var currentPartSize = (int)Math.Min(PartSize, file.Length - partStart);

                        fileStream.Seek(partStart, SeekOrigin.Begin);
                        var bytesRead = await fileStream.ReadAsync(buffer, 0, currentPartSize, cancellationToken);

                        if (bytesRead != currentPartSize)
                            return Result.Failure("READ_ERROR", $"Read mismatch part {partNumber}");

                        // Create wrapper stream around rented buffer (no copy)
                        using var partStream = new MemoryStream(buffer, 0, bytesRead);

                        var uploadPart = await s3UploadService.UploadPartAsync(
                            _backblazeSettings.BucketName, s3Key, uploadId, partNumber, partStream, cancellationToken);

                        if (uploadPart.IsFailure) return Result.Failure(uploadPart.Error.Code, uploadPart.Error.Message);

                        existingParts.Add(uploadPart.Value);

                        // Update DB
                        progress.PartsCompleted = existingParts.Count;
                        progress.BytesUploaded = Math.Min(progress.PartsCompleted * PartSize, file.Length);
                        progress.PartETags = SerializePartETags(existingParts);
                        progress.UpdatedAt = DateTime.UtcNow;
                        await UnitOfWork.Complete();
                    }
                }
                finally
                {
                    // RETURN buffer to pool
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                // 6. Complete
                var comp = await s3UploadService.CompleteUploadAsync(_backblazeSettings.BucketName, s3Key, uploadId, existingParts, cancellationToken);
                if (comp.IsFailure) return Result.Failure(comp.Error.Code, comp.Error.Message);

                progress.Status = SyncProgressStatus.Completed;
                progress.CompletedAt = DateTime.UtcNow;
                UnitOfWork.Repository<S3SyncProgress>().Delete(progress); // Clean up tracking row
                await UnitOfWork.Complete();

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure("UPLOAD_ERROR", ex.Message);
            }
        }

        // --- Optimized Helpers ---

        private List<PartETag> MergePartETags(List<PartETag>? stored, List<PartETag> s3Parts)
        {
            // S3 is authoritative source of truth
            var merged = new Dictionary<int, PartETag>();
            foreach (var p in s3Parts) merged[p.PartNumber] = p;

            // Fill gaps from DB if necessary (rare)
            if (stored != null)
            {
                foreach (var p in stored)
                {
                    if (!merged.ContainsKey(p.PartNumber)) merged[p.PartNumber] = p;
                }
            }
            return merged.Values.OrderBy(p => p.PartNumber).ToList();
        }

        private FileInfo[] GetFilesToUpload(string downloadPath)
        {
            try
            {
                var dir = new DirectoryInfo(downloadPath);
                if (!dir.Exists) return [];
                return dir.GetFiles("*", SearchOption.AllDirectories)
                    .Where(f => !f.Name.EndsWith(".torrent") && !f.Name.EndsWith(".dht") && !f.Name.EndsWith(".fresume"))
                    .ToArray();
            }
            catch { return []; }
        }

        private async Task CleanupBlockStorageAsync(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                Logger.LogInformation("{LogPrefix} Deleted local files | Path: {Path}", LogPrefix, path);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "{LogPrefix} Cleanup failed (non-critical)", LogPrefix);
            }
        }

        // --- Serialization Helpers (Keep as is) ---
        private List<PartETag> ParsePartETags(string json)
        {
            try { return string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<PartETag>>(json) ?? []; }
            catch { return []; }
        }
        private string SerializePartETags(List<PartETag> parts) => JsonSerializer.Serialize(parts);

        private async Task MarkSyncFailedAsync(SyncEntity sync, string msg, bool hasRetries)
        {
            sync.Status = hasRetries ? SyncStatus.Retrying : SyncStatus.Failed;
            sync.ErrorMessage = msg;
            if (hasRetries)
            {
                sync.RetryCount++;
                sync.NextRetryAt = DateTime.UtcNow.AddMinutes(5 * sync.RetryCount);
            }
            await UnitOfWork.Complete();
            Logger.LogError("{LogPrefix} Failed: {Error}", LogPrefix, msg);
        }
    }
}