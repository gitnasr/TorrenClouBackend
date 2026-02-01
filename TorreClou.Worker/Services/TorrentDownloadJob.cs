using Hangfire;
using MonoTorrent;
using MonoTorrent.Client;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.Core.Specifications;
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
        IJobStatusService jobStatusService) : UserJobBase<TorrentDownloadJob>(unitOfWork, logger, jobStatusService), ITorrentDownloadJob
    {
        // Default download path - can be made configurable via environment variable if needed
        private const string DefaultDownloadPath = "/mnt/torrents";

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
                
                // If SelectedFilePaths is null, select all files; otherwise use the specified paths
                var selectedSet = job.SelectedFilePaths != null 
                    ? new HashSet<string>(job.SelectedFilePaths)
                    : null;
                    
                var downloadableSize = torrent.Files
                    .Where(file => selectedSet == null || IsFileSelected(file.Path, selectedSet))
                    .Sum(file => file.Length);
                
                job.StartedAt ??= DateTime.UtcNow;
                job.LastHeartbeat = DateTime.UtcNow;
                job.DownloadPath = downloadPath;
                job.CurrentState = "Initializing torrent download...";
                job.TotalBytes = downloadableSize;
                
                await JobStatusService.TransitionJobStatusAsync(
                    job,
                    JobStatus.DOWNLOADING,
                    StatusChangeSource.Worker,
                    metadata: new { downloadPath, totalBytes = downloadableSize, torrentName = torrent.Name });

                // 5. Create and configure MonoTorrent engine with FastResume
                _engine = CreateEngine(downloadPath);
                manager = await _engine.AddAsync(torrent, downloadPath);
                var progress = manager.Progress;
                foreach (var file in manager.Files)
                {
                    // If selectedSet is null, select all files; otherwise check if file matches selected paths
                    if (selectedSet == null || IsFileSelected(file.Path, selectedSet))
                    {
                        await manager.SetFilePriorityAsync(file, Priority.Normal);
                        Logger.LogInformation(
                            "{LogPrefix} Selected file for download | JobId: {JobId} | FilePath: {FilePath} | SizeMB: {SizeMB:F2} MB",
                            LogPrefix, job.Id, file.Path, file.Length / (1024.0 * 1024.0));
                    }
                    else
                    {

                        // Set to DoNotDownload for unselected files

                      await  manager.SetFilePriorityAsync(file, Priority.DoNotDownload);
                        Logger.LogInformation(
                            "{LogPrefix} Skipped file from download as per user request | JobId: {JobId} | FilePath: {FilePath} | SizeMB: {SizeMB:F2} MB",
                            LogPrefix, job.Id, file.Path, file.Length / (1024.0 * 1024.0));

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
                            LogPrefix, job.Id, manager.Progress);
                        
                        await JobStatusService.TransitionJobStatusAsync(
                            job,
                            JobStatus.DOWNLOADING,
                            StatusChangeSource.Worker,
                            metadata: new { resuming = true, currentProgress = manager.Progress });
                    }

                var actualBytesDownloaded = (long)(downloadableSize * (progress / 100.0));
                job.BytesDownloaded = actualBytesDownloaded;
                var remainingBytesApprox = Math.Max(0, job.TotalBytes - actualBytesDownloaded);

               

                Logger.LogInformation("{LogPrefix} Download started | JobId: {JobId} | Initial State: {State} | ResumedBytes: {ResumedBytes} | Progress: {Progress}%", 
                    LogPrefix, job.Id, manager.State, actualBytesDownloaded, manager.Progress);

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

            // Use default download path
            var downloadBasePath = Environment.GetEnvironmentVariable("TORRENT_DOWNLOAD_PATH") ?? DefaultDownloadPath;

            // Verify download path exists
            if (!Directory.Exists(downloadBasePath))
            {
                Logger.LogCritical("{LogPrefix} Download path does not exist | JobId: {JobId} | Path: {Path}",
                    LogPrefix, job.Id, downloadBasePath);
                throw new DirectoryNotFoundException($"Download path does not exist: {downloadBasePath}");
            }

            // Create job-specific directory
            var downloadPath = Path.Combine(downloadBasePath, job.Id.ToString());
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
            const long LogThresholdBytes = 1024 * 1024 * 100; // Log every 100 MB

            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                
                // Update progress metrics
                var actualBytesDownloaded = (long)(job.TotalBytes * (manager.Progress / 100.0));

                // Check for completion
                if (manager.Progress >= 100.0 || manager.State == TorrentState.Seeding)
                {
                    Logger.LogInformation("{LogPrefix} Download complete | JobId: {JobId}", LogPrefix, job.Id);
                    
                    // Record final download metrics
                    var duration = (DateTime.UtcNow - downloadStartTime).TotalSeconds;
                    speedMetrics.RecordDownloadComplete(job.Id, job.UserId, "torrent_download", actualBytesDownloaded, duration);
                    
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
                    await MarkJobFailedAsync(job, $"Torrent error: {errorReason}");
                    return false;
                }

                // Log progress every 100 MB
                if (actualBytesDownloaded - lastLoggedBytes >= LogThresholdBytes)
                {
                    var speed = (actualBytesDownloaded - lastLoggedBytes) / (now - lastLogTime).TotalSeconds;
                   
                    Logger.LogInformation(
                        "{LogPrefix} Progress | JobId: {JobId} | {State} | {Progress:F2}% | {DownloadedMB:F2}/{TotalMB:F2} MB | Speed: {SpeedMBps:F2} MB/s",
                        LogPrefix,
                        job.Id,
                        manager.State,
                        manager.Progress,
                        actualBytesDownloaded / (1024.0 * 1024.0),
                        job.TotalBytes / (1024.0 * 1024.0),
                        speed / (1024.0 * 1024.0));

                    // Record speed metrics
                    speedMetrics.RecordDownloadSpeed(job.Id, job.UserId, "torrent_download", speed);

                    lastLoggedBytes = actualBytesDownloaded;
                    lastLogTime = now;
                }

                // Update database every 5 seconds
                if ((now - lastDbUpdate) >= DbUpdateInterval)
                {
                    await UpdateJobProgressAsync(job, manager, actualBytesDownloaded);
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
                Logger.LogCritical(ex, "{LogPrefix} Failed to save FastResume | Reason: {Reason}", LogPrefix, reason);
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
                job.CurrentState = "Download complete. Starting upload...";
                job.BytesDownloaded = job.TotalBytes;
                
                await JobStatusService.TransitionJobStatusAsync(
                    job,
                    JobStatus.PENDING_UPLOAD,
                    StatusChangeSource.Worker,
                    metadata: new { bytesDownloaded = job.BytesDownloaded, storageProvider = job.StorageProfile?.ProviderType.ToString() });

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
                Logger.LogError(ex, "{LogPrefix} Failed during download completion | JobId: {JobId} | ExceptionType: {ExceptionType}", 
                    LogPrefix, job.Id, ex.GetType().Name);
                
                // Let base class UserJobBase.ExecuteAsync handle the status transition
                // to avoid duplicate timeline entries
                throw;
            }
        }

        private static string GetUploadStreamKey(StorageProviderType providerType)
        {
            var providerName = providerType.ToString().ToLowerInvariant();
            return $"uploads:{providerName}:stream";
        }

        /// <summary>
        /// Checks if a file should be selected for download.
        /// Returns true if the file path exactly matches any selected path,
        /// or if the file is inside a selected folder.
        /// </summary>
        private static bool IsFileSelected(string filePath, HashSet<string> selectedPaths)
        {
            // Normalize path separators for cross-platform compatibility
            var normalizedFile = filePath.Replace('\\', '/');

            foreach (var selectedPath in selectedPaths)
            {
                var normalizedSelected = selectedPath.Replace('\\', '/');

                // Exact match (file directly selected)
                if (string.Equals(normalizedFile, normalizedSelected, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Check if file is inside a selected folder (folder path + separator)
                if (normalizedFile.StartsWith(normalizedSelected + "/", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
