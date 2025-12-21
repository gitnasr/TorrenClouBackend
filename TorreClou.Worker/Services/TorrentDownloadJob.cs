using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;
using System.Diagnostics;
using System.Threading.RateLimiting;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.Core.Specifications;
using TorreClou.Infrastructure.Services;
using TorreClou.Infrastructure.Settings;
using TorreClou.Infrastructure.Workers;

namespace TorreClou.Worker.Services
{
    /// <summary>
    /// Hangfire job that handles torrent downloading with crash recovery support.
    /// Uses MonoTorrent's FastResume to continue downloads after crashes.
    /// </summary>
    public class TorrentDownloadJob(
        IUnitOfWork unitOfWork,
        IHttpClientFactory httpClientFactory,
        ILogger<TorrentDownloadJob> logger,
        IRedisStreamService redisStreamService,
        ITransferSpeedMetrics speedMetrics,
        IOptions<BackblazeSettings> backblazeSettings) : BaseJob<TorrentDownloadJob>(unitOfWork, logger), ITorrentDownloadJob
    {
        private readonly BackblazeSettings _backblazeSettings = backblazeSettings.Value;

        // Save FastResume state every 30 seconds
        private static readonly TimeSpan FastResumeSaveInterval = TimeSpan.FromSeconds(30);

        // Update database progress every 5 seconds
        private static readonly TimeSpan DbUpdateInterval = TimeSpan.FromSeconds(60);

        // Engine reference for cleanup in error/cancellation handlers
        private ClientEngine? _engine;

        // Semaphore to limit concurrent downloads to 4

        protected override string LogPrefix => "[TORRENT:DOWNLOAD]";

        protected override void ConfigureSpecification(BaseSpecification<UserJob> spec)
        {
            spec.AddInclude(j => j.RequestFile);
            spec.AddInclude(j => j.StorageProfile);
        }

        // Removed DisableConcurrentExecution to allow concurrent downloads (limited by semaphore)
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 }), ]
        [Queue("torrents")]
        public new async Task ExecuteAsync(int jobId, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("{LogPrefix} Starting download | JobId: {JobId} ",
                  LogPrefix, jobId);

            await base.ExecuteAsync(jobId, cancellationToken);
        }

        protected override async Task ExecuteCoreAsync(UserJob job, CancellationToken cancellationToken)
        {
            TorrentManager? manager = null;

            try
            {
                // 1. Initialize download path (use existing or create new)
                var downloadPath = InitializeDownloadPath(job);
                // 3. Download torrent file and load it
                var torrent = await DownloadTorrentFileAsync(job, cancellationToken);
                if (torrent == null)
                {
                    await MarkJobFailedAsync(job, "Failed to download or parse torrent file");
                    return;
                }
                var downloadableSize = torrent.Files
                    .Select((file, index) => new { file.Length, index })
                    .Where(x => job.SelectedFileIndices.Contains(x.index))
                    .Sum(x => x.Length);
                job.Status = JobStatus.DOWNLOADING;
                job.StartedAt ??= DateTime.UtcNow;
                job.LastHeartbeat = DateTime.UtcNow;
                job.DownloadPath = downloadPath;
                job.CurrentState = "Initializing torrent download...";
                job.TotalBytes = downloadableSize;
                await UnitOfWork.Complete();

                // 5. Create and configure MonoTorrent engine with FastResume
                _engine = CreateEngine(downloadPath);
                manager = await _engine.AddAsync(torrent, downloadPath);
                var progress = manager.Progress;
                var selectedSet = new HashSet<int>(job.SelectedFileIndices);
                for (int i = 0; i < manager.Files.Count; i++)
                {
                    var file = manager.Files[i];
                    if (selectedSet.Contains(i))
                    {
                        await manager.SetFilePriorityAsync(file, Priority.Normal);

                    }
                    else
                    {
                        await manager.SetFilePriorityAsync(file, Priority.DoNotDownload);
                    }
                }

                Logger.LogInformation(
                    "{LogPrefix} Torrent loaded | JobId: {JobId} | Name: {Name} | Size: {SizeMB:F2} MB | Path: {Path} | MaxConnections: {MaxConn}",
                    LogPrefix, job.Id, torrent.Name, downloadableSize / (1024.0 * 1024.0), downloadPath, manager.Settings.MaximumConnections);

                // Start manager to load FastResume state
                 await manager.StartAsync();
                    
                    
                    // Check if torrent is already complete
                    if (manager.Progress >= 100.0 && manager.State == TorrentState.Seeding)
                    {
                        Logger.LogInformation("{LogPrefix} Torrent already complete, dispatch to upload worker | JobId: {JobId}", LogPrefix, job.Id);
                        await OnDownloadCompleteAsync(job, _engine);
                        return;
                    }
                    else
                    {
                        // Download not complete, reset to downloading and continue with normal flow
                        Logger.LogWarning("{LogPrefix} Torrent not complete, resuming to DOWNLOADING | JobId: {JobId} | Progress: {Progress}%", 
                            LogPrefix, job.Id
                            , manager.Progress);
                        job.Status = JobStatus.DOWNLOADING;
                        await UnitOfWork.Complete();
                    }



                var downloadedBytesApprox = (long)Math.Round(job.TotalBytes * (progress / 100.0));
                var remainingBytesApprox = Math.Max(0, job.TotalBytes - downloadedBytesApprox);

               

                Logger.LogInformation("{LogPrefix} Download started | JobId: {JobId} | Initial State: {State} | ResumedBytes: {ResumedBytes} | Progress: {Progress}%", 
                    LogPrefix, job.Id, manager.State, downloadedBytesApprox, manager.Progress);

                // 7. Monitor download progress
                var success = await MonitorDownloadAsync(job, _engine, manager, cancellationToken);

                if (success)
                {
                    // 8. Download complete - chain to upload job
                    await OnDownloadCompleteAsync(job, _engine);
                }
            }
            finally
            {
                // Cleanup
                if (manager != null)
                {
                    try { await manager.StopAsync(); } catch { /* ignore */ }
                }
                _engine?.Dispose();
                _engine = null;
            }
        }

        protected override async Task OnJobCancelledAsync(UserJob job)
        {
            if (_engine != null)
            {
                await SaveEngineStateAsync(_engine, "cancellation");
            }
        }

        protected override async Task OnJobErrorAsync(UserJob job, Exception exception)
        {
            if (_engine != null)
            {
                await SaveEngineStateAsync(_engine, "error");
            }
        }

        private string InitializeDownloadPath(UserJob job)
        {
            // Use existing path if resuming, otherwise create new
            if (!string.IsNullOrEmpty(job.DownloadPath) && Directory.Exists(job.DownloadPath))
            {
                Logger.LogInformation("{LogPrefix} Resuming with existing path | JobId: {JobId} | Path: {Path}",
                    LogPrefix, job.Id, job.DownloadPath);
                return job.DownloadPath;
            }

            // Use block storage for downloads
            var blockStoragePath = _backblazeSettings.BlockStoragePath;
            if (string.IsNullOrEmpty(blockStoragePath))
            {
                blockStoragePath = "/mnt/torrents"; // Default fallback
            }

            // Verify block storage path exists
            if (!Directory.Exists(blockStoragePath))
            {
                Logger.LogCritical("{LogPrefix} Block storage path does not exist | JobId: {JobId} | Path: {Path}",
                    LogPrefix, job.Id, blockStoragePath);
                throw new DirectoryNotFoundException($"Block storage path does not exist: {blockStoragePath}");
            }

            // Create job-specific directory on block storage
            var downloadPath = Path.Combine(blockStoragePath, job.Id.ToString());
            Directory.CreateDirectory(downloadPath);

            Logger.LogInformation("{LogPrefix} Using block storage for download | JobId: {JobId} | Path: {Path}",
                LogPrefix, job.Id, downloadPath);

            return downloadPath;
        }

        private async Task<Torrent?> DownloadTorrentFileAsync(UserJob job, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(job.RequestFile?.DirectUrl))
            {
                Logger.LogError("{LogPrefix} No torrent URL | JobId: {JobId}", LogPrefix, job.Id);
                return null;
            }

            try
            {
                Logger.LogInformation("{LogPrefix} Downloading torrent file | JobId: {JobId} | Url: {Url}",
                    LogPrefix, job.Id, job.RequestFile.DirectUrl);

                var httpClient = httpClientFactory.CreateClient();
                var torrentBytes = await httpClient.GetByteArrayAsync(job.RequestFile.DirectUrl, cancellationToken);

                using var stream = new MemoryStream(torrentBytes);
                return await Torrent.LoadAsync(stream);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{LogPrefix} Failed to download torrent file | JobId: {JobId}", LogPrefix, job.Id);
                return null;
            }
        }

        private ClientEngine CreateEngine(string downloadPath)
        {
            var settings = new EngineSettingsBuilder
            {
                AutoSaveLoadDhtCache = true,
                AutoSaveLoadFastResume = true,  // Enable FastResume for crash recovery
                CacheDirectory = downloadPath,   // Store .fresume files alongside downloads
                
                 MaximumConnections = 200,        // Global max connections (default is often 50-100)
                
        
                AllowPortForwarding = true
            }.ToSettings();

            return new ClientEngine(settings);
        }

        private async Task<bool> MonitorDownloadAsync(
            UserJob job,
            ClientEngine engine,
            TorrentManager manager,
            CancellationToken cancellationToken)
        {
            var downloadStartTime = DateTime.UtcNow;
            var lastSaveTime = DateTime.UtcNow;
            var lastDbUpdate = DateTime.MinValue;
            var lastLoggedBytes = 0L;
            var lastLogTime = DateTime.UtcNow;
            const long LogThresholdBytes = 1024 * 1024 *2; // Log every 2 MB

            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                // Update progress metrics
                var actualDownloaded = manager.Monitor.DataBytesReceived;

                // Check for completion
                if (manager.Progress >= 100.0 || manager.State == TorrentState.Seeding)
                {
                    Logger.LogInformation("{LogPrefix} Download complete | JobId: {JobId}", LogPrefix, job.Id);
                    
                    // Record final download metrics
                    var duration = (DateTime.UtcNow - downloadStartTime).TotalSeconds;
                    speedMetrics.RecordDownloadComplete(job.Id, job.UserId, "torrent_download", actualDownloaded, duration);
                    
                    await SaveEngineStateAsync(engine, "completion");
                    return true;
                }

                // Check for error
                if (manager.State == TorrentState.Error)
                {
                    var errorReason = manager.Error?.Reason.ToString() ?? "Unknown error";
                    Logger.LogError("{LogPrefix} Torrent error | JobId: {JobId} | Error: {Error}", 
                        LogPrefix, job.Id, errorReason);
                    // Mark as TORRENT_FAILED - BaseJob will handle retry logic and set TORRENT_DOWNLOAD_RETRY if retries available
                    job.Status = JobStatus.TORRENT_FAILED;
                    await MarkJobFailedAsync(job, $"Torrent error: {errorReason}");
                    return false;
                }

                // Log progress every 1 MB
                if (actualDownloaded - lastLoggedBytes >= LogThresholdBytes)
                {
                    var speed = (actualDownloaded - lastLoggedBytes) / (now - lastLogTime).TotalSeconds;
                    Logger.LogInformation(
                        "{LogPrefix} Progress | JobId: {JobId} | {State} | {Progress:F2}% | {DownloadedMB:F2}/{TotalMB:F2} MB | Speed: {SpeedMBps:F2} MB/s",
                        LogPrefix,
                        job.Id,
                        manager.State,
                        manager.Progress,
                        actualDownloaded / (1024.0 * 1024.0),
                        job.TotalBytes / (1024.0 * 1024.0),
                        speed / (1024.0 * 1024.0));

                    // Record speed metrics
                    speedMetrics.RecordDownloadSpeed(job.Id, job.UserId, "torrent_download", speed);

                    lastLoggedBytes = actualDownloaded;
                    lastLogTime = now;
                }

                // Update database every 5 seconds
                if ((now - lastDbUpdate) >= DbUpdateInterval)
                {
                    await UpdateJobProgressAsync(job, manager, actualDownloaded);
                    lastDbUpdate = now;
                }

                // Save FastResume state every 30 seconds
                if ((now - lastSaveTime) >= FastResumeSaveInterval)
                {
                    await SaveEngineStateAsync(engine, "periodic");
                    lastSaveTime = now;
                }

                await Task.Delay(2000, cancellationToken);
            }

            // Graceful shutdown - save state
            await SaveEngineStateAsync(engine, "shutdown");
            return false;
        }

        private async Task UpdateJobProgressAsync(UserJob job, TorrentManager manager, long bytesDownloaded)
        {
            try
            {
                var stateDescription = manager.State switch
                {
                    TorrentState.Hashing => $"Validating files: {manager.Progress:F1}%",
                    TorrentState.Downloading => $"Downloading: {manager.Progress:F2}%",
                    TorrentState.Seeding => "Download complete",
                    _ => manager.State.ToString()
                };

                job.BytesDownloaded = bytesDownloaded;
                job.LastHeartbeat = DateTime.UtcNow;
                job.CurrentState = stateDescription;

                await UnitOfWork.Complete();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "{LogPrefix} Failed to update progress | JobId: {JobId}", LogPrefix, job.Id);
            }
        }

        private async Task SaveEngineStateAsync(ClientEngine engine, string reason)
        {
            try
            {
                await engine.SaveStateAsync();
                Logger.LogDebug("{LogPrefix} FastResume saved | Reason: {Reason}", LogPrefix, reason);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "{LogPrefix} Failed to save FastResume | Reason: {Reason}", LogPrefix, reason);
            }
        }

        private async Task OnDownloadCompleteAsync(UserJob job, ClientEngine engine)
        {
            // Save final state
            await SaveEngineStateAsync(engine, "download-complete");

            // Store original download path (block storage path) for cleanup
            var originalDownloadPath = job.DownloadPath;

            try
            {
                // Step 1: Update job status to PENDING_UPLOAD
                job.Status = JobStatus.PENDING_UPLOAD;
                job.CurrentState = "Download complete. Starting  upload...";
                job.BytesDownloaded = job.TotalBytes;
                await UnitOfWork.Complete();

                Logger.LogInformation("{LogPrefix} Publishing to upload stream | JobId: {JobId} | Provider: {Provider}", 
                    LogPrefix, job.Id, job.StorageProfile?.ProviderType);

                // Publish to upload stream for Google Drive worker (sync will be triggered after upload completes)
                var uploadStreamKey = GetUploadStreamKey(job.StorageProfile?.ProviderType ?? StorageProviderType.GoogleDrive);
                await redisStreamService.PublishAsync(uploadStreamKey, new Dictionary<string, string>
                {
                    { "jobId", job.Id.ToString() },
                    { "downloadPath", job.DownloadPath ?? string.Empty },
                    { "storageProfileId", job.StorageProfileId.ToString() },
                    { "userId", job.UserId.ToString() },
                    { "createdAt", DateTime.UtcNow.ToString("O") }
                });

                Logger.LogInformation("{LogPrefix} Published to upload stream | JobId: {JobId} | Stream: {Stream}", 
                    LogPrefix, job.Id, uploadStreamKey);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{LogPrefix} Failed during download completion | JobId: {JobId}", 
                    LogPrefix, job.Id);
                
                // Mark job as failed - sync or other step failed
                // Do NOT cleanup block storage on failure (files may be needed for retry)
                job.Status = JobStatus.FAILED;
                job.ErrorMessage = $"Failed during download completion: {ex.Message}";
                await UnitOfWork.Complete();
                throw; // Re-throw to ensure job is marked as failed
            }
        }

        private static string GetUploadStreamKey(StorageProviderType providerType)
        {
            var providerName = providerType.ToString().ToLowerInvariant();
            return $"uploads:{providerName}:stream";
        }
    }
}
