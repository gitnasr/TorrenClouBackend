using Amazon.S3;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Text.Json;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;
using TorreClou.Infrastructure.Workers;
using TorreClou.S3.Worker.Interfaces;

namespace TorreClou.S3.Worker.Services
{
    public class S3UploadJob(
        IUnitOfWork unitOfWork,
        ILogger<S3UploadJob> logger,
        IS3JobService s3JobService,
        IS3ResumableUploadServiceFactory s3UploadServiceFactory,
        IJobStatusService jobStatusService,
        IServiceScopeFactory serviceScopeFactory,
        IRedisLockService redisLockService)
        : UserJobBase<S3UploadJob>(unitOfWork, logger, jobStatusService), IS3UploadJob
    {
        private readonly IS3JobService _s3JobService = s3JobService;
        private readonly IS3ResumableUploadServiceFactory _s3UploadServiceFactory = s3UploadServiceFactory;
        private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
        private readonly IRedisLockService _redisLockService = redisLockService;
        private const long PartSize = 10 * 1024 * 1024; // 10MB
        private const int ProgressUpdateIntervalSeconds = 10;

        // Heartbeat independent from progress
        private const int HeartbeatIntervalSeconds = 15;

        protected override string LogPrefix => "[S3:UPLOAD]";

        protected override void ConfigureSpecification(BaseSpecification<UserJob> spec)
        {
            spec.AddInclude(j => j.StorageProfile);
        }

        [Queue("s3")]
        [AutomaticRetry(
            Attempts = 3,
            DelaysInSeconds = new[] { 60, 300, 900 },
            OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public new async Task ExecuteAsync(int jobId, CancellationToken cancellationToken = default)
        {
            await base.ExecuteAsync(jobId, cancellationToken);
        }

        protected override async Task ExecuteCoreAsync(UserJob job, CancellationToken cancellationToken)
        {
            // 1. Acquire distributed lock (prevent duplicate processing)
            var lockKey = $"s3:lock:{job.Id}";
            var lockExpiry = TimeSpan.FromHours(2);

            using var distributedLock = await _redisLockService.AcquireLockAsync(lockKey, lockExpiry, cancellationToken);

            if (distributedLock == null)
            {
                Logger.LogWarning("{LogPrefix} Job already being processed | JobId: {JobId}", LogPrefix, job.Id);
                return;
            }

            Logger.LogInformation("{LogPrefix} Acquired lock | JobId: {JobId} | Expiry: {Expiry}",
                LogPrefix, job.Id, lockExpiry);

            // 2. Validate storage profile
            if (job.StorageProfile == null || job.StorageProfile.ProviderType != StorageProviderType.AwsS3)
            {
                await MarkJobFailedAsync(job, "Invalid storage profile or not S3 provider.");
                return;
            }

            // 3. Verify credentials and get S3 config (NO FALLBACK - must fail if invalid)
            Logger.LogInformation("{LogPrefix} Verifying S3 credentials | JobId: {JobId} | ProfileId: {ProfileId}",
                LogPrefix, job.Id, job.StorageProfile.Id);

            var credResult = await _s3JobService.VerifyAndGetCredentialsAsync(job.StorageProfile, cancellationToken);
            if (credResult.IsFailure)
            {
                await MarkJobFailedAsync(job, $"Invalid S3 credentials: {credResult.Error.Message}");
                return;
            }

            var (accessKey, secretKey, endpoint, bucketName) = credResult.Value;

            Logger.LogInformation("{LogPrefix} Credentials verified | JobId: {JobId} | Bucket: {Bucket} | Endpoint: {Endpoint}",
                LogPrefix, job.Id, bucketName, endpoint);

            // 3.1. Create S3 client with user credentials
            var s3Config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true
            };

            using var s3Client = new AmazonS3Client(accessKey, secretKey, s3Config);

            // 3.2. Create S3ResumableUploadService instance with user's S3 client
            var s3UploadService = _s3UploadServiceFactory.Create(s3Client);

            Logger.LogDebug("{LogPrefix} Created S3 client and upload service | JobId: {JobId}",
                LogPrefix, job.Id);

            // 4. Handle Status Transitions
            if (job.Status == JobStatus.PENDING_UPLOAD)
            {
                Logger.LogInformation("{LogPrefix} Job ready for upload, transitioning to UPLOADING | JobId: {JobId}", LogPrefix, job.Id);
                job.CurrentState = "Starting S3 upload...";
                if (job.StartedAt == null) job.StartedAt = DateTime.UtcNow;
                job.LastHeartbeat = DateTime.UtcNow;

                await JobStatusService.TransitionJobStatusAsync(
                    job,
                    JobStatus.UPLOADING,
                    StatusChangeSource.Worker,
                    metadata: new { provider = "S3", startedAt = job.StartedAt, bucket = bucketName });
            }
            else if (job.Status == JobStatus.UPLOAD_RETRY)
            {
                Logger.LogInformation("{LogPrefix} Retrying job | JobId: {JobId} | Retry: {NextRetry}", LogPrefix, job.Id, job.NextRetryAt);
                job.CurrentState = "Retrying S3 upload...";
                job.LastHeartbeat = DateTime.UtcNow;

                await JobStatusService.TransitionJobStatusAsync(
                    job,
                    JobStatus.UPLOADING,
                    StatusChangeSource.Worker,
                    metadata: new { provider = "S3", retrying = true, previousNextRetry = job.NextRetryAt });
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
                Logger.LogWarning("{LogPrefix} Unexpected status: {Status} | JobId: {JobId}", LogPrefix, job.Status, job.Id);
            }

            // 5. Validate download path
            if (string.IsNullOrEmpty(job.DownloadPath) || !Directory.Exists(job.DownloadPath))
            {
                await MarkJobFailedAsync(job, $"Download directory missing: {job.DownloadPath}");
                return;
            }

            // 6. Start heartbeat loop
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var heartbeatTask = RunHeartbeatLoopAsync(job.Id, heartbeatCts.Token);

            try
            {
                Logger.LogInformation("{LogPrefix} Starting | JobId: {JobId} | Bucket: {Bucket}",
                    LogPrefix, job.Id, bucketName);

                var filesToUpload = GetFilesToUpload(job.DownloadPath);
                if (filesToUpload.Length == 0)
                {
                    await MarkJobFailedAsync(job, "No files found to upload", hasRetries: false);
                    return;
                }

                var totalBytes = filesToUpload.Sum(f => f.Length);
                var uploadStartTime = DateTime.UtcNow;
                var overallBytesUploaded = 0L;
                var filesUploaded = 0;
                var lastProgressUpdate = DateTime.UtcNow;

                for (int i = 0; i < filesToUpload.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var file = filesToUpload[i];
                    var relativePath = Path.GetRelativePath(job.DownloadPath, file.FullName);
                    var s3Key = $"torrents/{job.Id}/{relativePath.Replace('\\', '/')}";

                    Logger.LogInformation("{LogPrefix} File {Index}/{Total} | {FileName} | {SizeMB:F2} MB",
                        LogPrefix, i + 1, filesToUpload.Length, file.Name, file.Length / (1024.0 * 1024.0));

                    var uploadResult = await UploadFileWithResumeAsync(job, file, s3Key, bucketName, s3UploadService, cancellationToken);
                    if (uploadResult.IsFailure)
                    {
                        await MarkJobFailedAsync(job, $"Upload failed for {file.Name}: {uploadResult.Error.Message}", hasRetries: true);
                        return;
                    }

                    overallBytesUploaded += file.Length;
                    filesUploaded++;

                    // Throttle DB updates (progress)
                    var progressNow = DateTime.UtcNow;
                    if ((progressNow - lastProgressUpdate).TotalSeconds >= ProgressUpdateIntervalSeconds)
                    {
                        job.BytesDownloaded = overallBytesUploaded; // Reuse BytesDownloaded for upload progress
                        job.LastHeartbeat = progressNow;
                        await UnitOfWork.Complete();

                        var percent = totalBytes == 0 ? 0 : (overallBytesUploaded / (double)totalBytes) * 100;
                        Logger.LogInformation("{LogPrefix} Progress: {Percent:F1}% | {Uploaded}/{Total} MB",
                            LogPrefix, percent, overallBytesUploaded >> 20, totalBytes >> 20);

                        lastProgressUpdate = progressNow;
                    }
                }

                var duration = (DateTime.UtcNow - uploadStartTime).TotalSeconds;
                job.CompletedAt = DateTime.UtcNow;
                job.CurrentState = "S3 upload completed successfully";
                job.NextRetryAt = null;

                await JobStatusService.TransitionJobStatusAsync(
                    job,
                    JobStatus.COMPLETED,
                    StatusChangeSource.Worker,
                    metadata: new { totalBytes = overallBytesUploaded, filesUploaded, completedAt = job.CompletedAt, durationSeconds = duration });

                Logger.LogInformation("{LogPrefix} Upload Complete | JobId: {JobId}", LogPrefix, job.Id);

                // Stop heartbeat now (completed)
                heartbeatCts.Cancel();
                try { await heartbeatTask; } catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{LogPrefix} Fatal error during upload | JobId: {JobId} | ExceptionType: {ExceptionType}",
                    LogPrefix, job.Id, ex.GetType().Name);

                // stop heartbeat before rethrow
                heartbeatCts.Cancel();
                try { await heartbeatTask; } catch { /* ignore */ }

                throw; // let UserJobBase handle transition + hangfire retry
            }
            finally
            {
                // Ensure heartbeat stops
                heartbeatCts.Cancel();
            }
        }

        /// <summary>
        /// Override to clean up distributed lock on job failure
        /// </summary>
        protected override async Task MarkJobFailedAsync(UserJob job, string errorMessage, bool hasRetries = false)
        {
            try
            {
                await _s3JobService.DeleteUploadLockAsync(job.Id);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "{LogPrefix} Failed to delete lock on failure | JobId: {JobId}",
                    LogPrefix, job.Id);
            }
            finally
            {
                await base.MarkJobFailedAsync(job, errorMessage, hasRetries);
            }
        }

        private async Task RunHeartbeatLoopAsync(int jobId, CancellationToken ct)
        {
            // CRITICAL: Use a separate scope to avoid DbContext concurrency issues
            // The main upload thread uses the injected UnitOfWork, this loop uses its own
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(HeartbeatIntervalSeconds), ct);

                    // Create a new scope for each heartbeat to get a fresh DbContext
                    using var scope = _serviceScopeFactory.CreateScope();
                    var scopedUnitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                    var hbSpec = new BaseSpecification<UserJob>(j => j.Id == jobId);
                    var current = await scopedUnitOfWork.Repository<UserJob>().GetEntityWithSpec(hbSpec);

                    if (current == null) return;

                    // Only heartbeat while actively uploading
                    if (current.Status != JobStatus.UPLOADING) return;

                    current.LastHeartbeat = DateTime.UtcNow;
                    await scopedUnitOfWork.Complete();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "{LogPrefix} Heartbeat loop failed | JobId: {JobId}", LogPrefix, jobId);
                    // keep loop running
                }
            }
        }

        private async Task<Result> UploadFileWithResumeAsync(UserJob job, FileInfo file, string s3Key, string bucketName, IS3ResumableUploadService s3UploadService, CancellationToken cancellationToken)
        {
            try
            {
                // 1) Check if file already exists in S3
                var existsResult = await s3UploadService.CheckObjectExistsAsync(bucketName, s3Key, cancellationToken);
                if (existsResult.IsSuccess && existsResult.Value)
                {
                    Logger.LogInformation("{LogPrefix} File already exists in S3, skipping | Key: {Key}", LogPrefix, s3Key);
                    return Result.Success();
                }

                // 2) Load/Create progress record (using S3SyncProgress but with nullable SyncId)
                var existingProgress = await UnitOfWork.Repository<S3SyncProgress>()
                    .GetEntityWithSpec(new BaseSpecification<S3SyncProgress>(p => p.JobId == job.Id && p.S3Key == s3Key));

                S3SyncProgress? progress = existingProgress;
                string? uploadId = null;
                List<PartETag>? existingParts = null;

                // 3) Resume logic
                if (progress != null && progress.Status == S3UploadProgressStatus.InProgress && !string.IsNullOrEmpty(progress.UploadId))
                {
                    uploadId = progress.UploadId;
                    existingParts = ParsePartETags(progress.PartETags);

                    var listPartsResult = await s3UploadService.ListPartsAsync(bucketName, s3Key, uploadId, cancellationToken);
                    if (listPartsResult.IsFailure)
                    {
                        Logger.LogWarning("{LogPrefix} Remote upload session not found, restarting | Key: {Key}", LogPrefix, s3Key);
                        uploadId = null;
                    }
                    else
                    {
                        existingParts = MergePartETags(existingParts, listPartsResult.Value);
                    }
                }

                // 4) Init if needed
                if (string.IsNullOrEmpty(uploadId))
                {
                    var init = await s3UploadService.InitiateUploadAsync(bucketName, s3Key, file.Length, cancellationToken: cancellationToken);
                    if (init.IsFailure) return Result.Failure(init.Error.Code, init.Error.Message);

                    uploadId = init.Value;
                    var totalParts = (int)Math.Ceiling((double)file.Length / PartSize);

                    progress = new S3SyncProgress
                    {
                        JobId = job.Id,
                        LocalFilePath = file.FullName,
                        S3Key = s3Key,
                        UploadId = uploadId,
                        PartSize = PartSize,
                        TotalParts = totalParts,
                        Status = S3UploadProgressStatus.InProgress,
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
                    existingParts ??= [];
                }

                // 5) Upload loop (memory optimized)
                await using var fileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);

                var startPartNumber = existingParts.Count > 0
                    ? existingParts.Max(p => p.PartNumber) + 1
                    : 1;

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

                        using var partStream = new MemoryStream(buffer, 0, bytesRead);

                        var uploadPart = await s3UploadService.UploadPartAsync(
                            bucketName, s3Key, uploadId, partNumber, partStream, cancellationToken);

                        if (uploadPart.IsFailure) return Result.Failure(uploadPart.Error.Code, uploadPart.Error.Message);

                        existingParts.Add(uploadPart.Value);

                        progress.PartsCompleted = existingParts.Count;
                        progress.BytesUploaded = Math.Min(progress.PartsCompleted * PartSize, file.Length);
                        progress.PartETags = SerializePartETags(existingParts);
                        progress.UpdatedAt = DateTime.UtcNow;

                        await UnitOfWork.Complete();
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                // 6) Complete
                var comp = await s3UploadService.CompleteUploadAsync(bucketName, s3Key, uploadId, existingParts, cancellationToken);
                if (comp.IsFailure) return Result.Failure(comp.Error.Code, comp.Error.Message);

                progress.Status = S3UploadProgressStatus.Completed;
                progress.CompletedAt = DateTime.UtcNow;

                UnitOfWork.Repository<S3SyncProgress>().Delete(progress);
                await UnitOfWork.Complete();

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure("UPLOAD_ERROR", ex.Message);
            }
        }

        // Helpers
        private List<PartETag> MergePartETags(List<PartETag>? stored, List<PartETag> s3Parts)
        {
            var merged = new Dictionary<int, PartETag>();
            foreach (var p in s3Parts) merged[p.PartNumber] = p;

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

                // Filter system files, similar to GoogleDriveUploadJob
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

        // Serialization helpers
        private List<PartETag> ParsePartETags(string json)
        {
            try { return string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<PartETag>>(json) ?? []; }
            catch { return []; }
        }

        private string SerializePartETags(List<PartETag> parts) => JsonSerializer.Serialize(parts);
    }
}
