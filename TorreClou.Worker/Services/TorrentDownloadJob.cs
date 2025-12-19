using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Specifications;
using MonoTorrent;
using MonoTorrent.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Hangfire;
using TorreClou.Infrastructure.Workers;
using TorreClou.Infrastructure.Services;
using TorreClou.Infrastructure.Settings;
using StackExchange.Redis;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

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
        IConnectionMultiplexer redis,
        ITransferSpeedMetrics speedMetrics,
        IOptions<BackblazeSettings> backblazeSettings) : BaseJob<TorrentDownloadJob>(unitOfWork, logger)
    {
        private readonly BackblazeSettings _backblazeSettings = backblazeSettings.Value;

        // Save FastResume state every 30 seconds
        private static readonly TimeSpan FastResumeSaveInterval = TimeSpan.FromSeconds(30);

        // Update database progress every 5 seconds
        private static readonly TimeSpan DbUpdateInterval = TimeSpan.FromSeconds(60);

        // Engine reference for cleanup in error/cancellation handlers
        private ClientEngine? _engine;

        // Semaphore to limit concurrent downloads to 4
        private static readonly SemaphoreSlim _concurrencyLimiter = new SemaphoreSlim(4, 4);

        protected override string LogPrefix => "[TORRENT:DOWNLOAD]";

        protected override void ConfigureSpecification(BaseSpecification<UserJob> spec)
        {
            spec.AddInclude(j => j.RequestFile);
            spec.AddInclude(j => j.StorageProfile);
        }

        // Removed DisableConcurrentExecution to allow concurrent downloads (limited by semaphore)
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
        [Queue("torrents")]
        public new async Task ExecuteAsync(int jobId, CancellationToken cancellationToken = default)
        {
            // Wait for semaphore slot (max 4 concurrent downloads)
            await _concurrencyLimiter.WaitAsync(cancellationToken);
            try
            {
                Logger.LogInformation("{LogPrefix} Starting download | JobId: {JobId} | ActiveDownloads: {Active}",
                    LogPrefix, jobId, 4 - _concurrencyLimiter.CurrentCount);
                
                await base.ExecuteAsync(jobId, cancellationToken);
            }
            finally
            {
                _concurrencyLimiter.Release();
                Logger.LogInformation("{LogPrefix} Download completed | JobId: {JobId} | ActiveDownloads: {Active}",
                    LogPrefix, jobId, 4 - _concurrencyLimiter.CurrentCount);
            }
        }

        protected override async Task ExecuteCoreAsync(UserJob job, CancellationToken cancellationToken)
        {
            TorrentManager? manager = null;

            try
            {
                // 1. Initialize download path (use existing or create new)
                var downloadPath = InitializeDownloadPath(job);

                // Check if job is in SYNCING state - handle separately before updating status
                var wasSyncing = job.Status == JobStatus.SYNCING || job.Status == JobStatus.SYNC_RETRY;

                // 2. Update job status to PROCESSING (unless we're resuming from SYNCING)
                if (!wasSyncing)
                {
                    job.Status = JobStatus.DOWNLOADING;
                }
                job.StartedAt ??= DateTime.UtcNow;
                job.LastHeartbeat = DateTime.UtcNow;
                job.DownloadPath = downloadPath;
                job.CurrentState = "Initializing torrent download...";
                await UnitOfWork.Complete();

                // 3. Download torrent file and load it
                var torrent = await DownloadTorrentFileAsync(job, cancellationToken);
                if (torrent == null)
                {
                    await MarkJobFailedAsync(job, "Failed to download or parse torrent file");
                    return;
                }

                // 4. Store total bytes
                job.TotalBytes = torrent.Size;
                await UnitOfWork.Complete();

                // 5. Create and configure MonoTorrent engine with FastResume
                _engine = CreateEngine(downloadPath);
                manager = await _engine.AddAsync(torrent, downloadPath);
                
                Logger.LogInformation(
                    "{LogPrefix} Torrent loaded | JobId: {JobId} | Name: {Name} | Size: {SizeMB:F2} MB | Path: {Path} | MaxConnections: {MaxConn}",
                    LogPrefix, job.Id, torrent.Name, torrent.Size / (1024.0 * 1024.0), downloadPath, manager.Settings.MaximumConnections);

                // Check if job was in SYNCING state - handle separately (before starting)
                if (wasSyncing)
                {
                    // Job was interrupted during sync phase, but torrent is loaded
                    Logger.LogInformation("{LogPrefix} Resuming from SYNCING state | JobId: {JobId}", LogPrefix, job.Id);
                    
                    // Start manager to load FastResume state
                    await manager.StartAsync();
                    
                    // Wait for manager to fully initialize from FastResume
                    await Task.Delay(1000, cancellationToken);
                    
                    // Check if torrent is already complete
                    if (manager.Progress >= 100.0 || manager.State == TorrentState.Seeding)
                    {
                        // Download complete, resume sync
                        Logger.LogInformation("{LogPrefix} Torrent already complete, resuming sync | JobId: {JobId}", LogPrefix, job.Id);
                        await OnDownloadCompleteAsync(job, _engine);
                        return;
                    }
                    else
                    {
                        // Download not complete, reset to downloading and continue with normal flow
                        Logger.LogWarning("{LogPrefix} Torrent not complete in SYNCING state, resetting to DOWNLOADING | JobId: {JobId} | Progress: {Progress}%", 
                            LogPrefix, job.Id, manager.Progress);
                        job.Status = JobStatus.DOWNLOADING;
                        await UnitOfWork.Complete();
                        // Continue with normal download flow below
                    }
                }
                else
                {
                    // 6. Start downloading (for normal flow)
                    await manager.StartAsync();
                }
                
                // Wait for manager to fully initialize from FastResume after starting
                await Task.Delay(1000, cancellationToken);
                
                // Read actual downloaded bytes from manager (from FastResume) AFTER starting
                // StartAsync() loads FastResume, so we need to check after it completes
                var actualDownloaded = manager.Monitor.DataBytesReceived;
                if (actualDownloaded > 0 && actualDownloaded != job.BytesDownloaded)
                {
                    Logger.LogInformation("{LogPrefix} Resuming with actual progress | JobId: {JobId} | DB: {DbBytes} | Actual: {ActualBytes} | Progress: {Progress}%", 
                        LogPrefix, job.Id, job.BytesDownloaded, actualDownloaded, manager.Progress);
                    job.BytesDownloaded = actualDownloaded;
                    await UnitOfWork.Complete();
                }
                else if (actualDownloaded == 0 && job.BytesDownloaded > 0)
                {
                    // Manager shows 0 but DB has progress - this shouldn't happen, but log it
                    Logger.LogWarning("{LogPrefix} Manager shows 0 bytes but DB has {DbBytes} bytes | JobId: {JobId} | ManagerState: {State} | ManagerProgress: {Progress}%", 
                        LogPrefix, job.BytesDownloaded, job.Id, manager.State, manager.Progress);
                }

                Logger.LogInformation("{LogPrefix} Download started | JobId: {JobId} | Initial State: {State} | ResumedBytes: {ResumedBytes} | Progress: {Progress}%", 
                    LogPrefix, job.Id, manager.State, actualDownloaded, manager.Progress);

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
                Logger.LogError("{LogPrefix} Block storage path does not exist | JobId: {JobId} | Path: {Path}",
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
                // Step 1: Sync files from block storage to Backblaze B2
                await SyncToBackblazeAsync(job);

                // Step 2: Update job download path to B2 relative path
                // Google Drive worker will read from /mnt/backblaze/torrents/{jobId}
                job.DownloadPath = $"torrents/{job.Id}";
                Logger.LogInformation("{LogPrefix} Updated download path to B2 location | JobId: {JobId} | Path: {Path}", 
                    LogPrefix, job.Id, job.DownloadPath);

                // Step 3: Clean up block storage (delete original download directory)
                await CleanupBlockStorageAsync(new UserJob { Id = job.Id, DownloadPath = originalDownloadPath });

                // Step 4: Update job status to PENDING_UPLOAD - waiting for upload worker to pick it up
                job.Status = JobStatus.PENDING_UPLOAD;
                job.CurrentState = "Download complete. Files synced to B2. Waiting for upload...";
                job.BytesDownloaded = job.TotalBytes;
                await UnitOfWork.Complete();

                Logger.LogInformation("{LogPrefix} Publishing to upload stream | JobId: {JobId} | Provider: {Provider}", 
                    LogPrefix, job.Id, job.StorageProfile?.ProviderType);

                // Step 5: Publish to provider-specific Redis stream
                var streamKey = GetUploadStreamKey(job.StorageProfile?.ProviderType ?? StorageProviderType.GoogleDrive);
                var db = redis.GetDatabase();

                await db.StreamAddAsync(streamKey, [
                    new NameValueEntry("jobId", job.Id.ToString()),
                    new NameValueEntry("downloadPath", job.DownloadPath ?? string.Empty),
                    new NameValueEntry("storageProfileId", job.StorageProfileId.ToString()),
                    new NameValueEntry("userId", job.UserId.ToString()),
                    new NameValueEntry("createdAt", DateTime.UtcNow.ToString("O"))
                ]);

                Logger.LogInformation("{LogPrefix} Published to upload stream | JobId: {JobId} | Stream: {Stream}", 
                    LogPrefix, job.Id, streamKey);
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

        /// <summary>
        /// Syncs downloaded files from block storage to Backblaze B2.
        /// Files are uploaded to B2 bucket at torrents/{jobId} path.
        /// </summary>
        private async Task SyncToBackblazeAsync(UserJob job)
        {
            if (string.IsNullOrEmpty(job.DownloadPath))
            {
                Logger.LogError("{LogPrefix} Cannot sync - download path is empty | JobId: {JobId}", 
                    LogPrefix, job.Id);
                throw new InvalidOperationException("Download path is empty");
            }

            if (string.IsNullOrEmpty(_backblazeSettings.BucketName))
            {
                Logger.LogError("{LogPrefix} Cannot sync - bucket name is not configured | JobId: {JobId}", 
                    LogPrefix, job.Id);
                throw new InvalidOperationException("Bucket name is not configured");
            }

            try
            {
                // Update job status to SYNCING
                job.Status = JobStatus.SYNCING;
                job.CurrentState = "Syncing files to Backblaze B2...";
                await UnitOfWork.Complete();

                Logger.LogInformation("{LogPrefix} Starting Backblaze B2 sync | JobId: {JobId} | Source: {Source} | Bucket: {Bucket}", 
                    LogPrefix, job.Id, job.DownloadPath, _backblazeSettings.BucketName);

                // Build destination path in B2 bucket
                var bucketPath = $"backblaze:{_backblazeSettings.BucketName}/torrents/{job.Id}";

                // Execute rclone copy to upload files from block storage to B2
                // Using "copy" instead of "sync" to avoid deleting files that may exist in B2
                var processInfo = new ProcessStartInfo
                {
                    FileName = "rclone",
                    Arguments = $"copy \"{job.DownloadPath}\" \"{bucketPath}\" --progress --transfers 4 --checkers 8 --no-check-dest --ignore-existing",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = processInfo };
                var outputBuilder = new System.Text.StringBuilder();
                var errorBuilder = new System.Text.StringBuilder();
                
                // Progress tracking - queue updates from background thread, process on main thread
                var progressQueue = new ConcurrentQueue<RcloneProgressInfo>();
                var lastProgressUpdate = DateTime.MinValue;
                var lastProgressPercent = -1.0;
                var progressUpdateInterval = TimeSpan.FromSeconds(10); // Update DB every 10 seconds if progress changed
                
                process.OutputDataReceived += (_, e) => 
                { 
                    if (e.Data != null) 
                    {
                        outputBuilder.AppendLine(e.Data);
                        
                        // Parse rclone progress output
                        // Format: "Transferred:   1.234 GiB / 5.678 GiB, 22%, 12.34 MiB/s, ETA 0s"
                        var progressInfo = ParseRcloneProgress(e.Data);
                        
                        if (progressInfo != null)
                        {
                            // Queue progress info for processing on main thread (thread-safe)
                            progressQueue.Enqueue(progressInfo);
                            
                            // Log debug for all progress updates
                            Logger.LogDebug("{LogPrefix} Sync progress | JobId: {JobId} | {Percent:F1}% | {TransferredMB:F1}/{TotalMB:F1} MB", 
                                LogPrefix, job.Id, progressInfo.Percent, progressInfo.TransferredMB, progressInfo.TotalMB);
                        }
                    }
                };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait for sync to complete (with timeout) and update heartbeat periodically
                var timeout = TimeSpan.FromMinutes(60); // Increased timeout for large files
                var heartbeatInterval = TimeSpan.FromSeconds(30); // Update heartbeat every 30 seconds
                var startTime = DateTime.UtcNow;
                var lastHeartbeat = DateTime.UtcNow;

                while (!process.HasExited)
                {
                    var elapsed = DateTime.UtcNow - startTime;
                    if (elapsed >= timeout)
                    {
                        Logger.LogError("{LogPrefix} Backblaze sync timed out after {Timeout} minutes | JobId: {JobId}", 
                            LogPrefix, timeout.TotalMinutes, job.Id);
                        try { process.Kill(); } catch { /* ignore */ }
                        throw new TimeoutException($"Backblaze sync timed out after {timeout.TotalMinutes} minutes");
                    }

                    // Process queued progress updates (thread-safe, runs on main thread)
                    while (progressQueue.TryDequeue(out var progressInfo))
                    {
                        var now = DateTime.UtcNow;
                        bool shouldUpdate = false;
                        
                        // Update if percentage changed significantly (1% or more) or enough time passed
                        if (Math.Abs(progressInfo.Percent - lastProgressPercent) >= 1.0 || 
                            (now - lastProgressUpdate) >= progressUpdateInterval)
                        {
                            shouldUpdate = true;
                            lastProgressPercent = progressInfo.Percent;
                            lastProgressUpdate = now;
                        }
                        
                        if (shouldUpdate)
                        {
                            try
                            {
                                job.CurrentState = $"Syncing to B2: {progressInfo.Percent:F1}% ({progressInfo.TransferredMB:F1}/{progressInfo.TotalMB:F1} MB) @ {progressInfo.SpeedMBps:F2} MB/s";
                                job.LastHeartbeat = DateTime.UtcNow;
                                await UnitOfWork.Complete();
                                
                                Logger.LogInformation(
                                    "{LogPrefix} Sync progress | JobId: {JobId} | {Percent:F1}% | {TransferredMB:F1}/{TotalMB:F1} MB | Speed: {SpeedMBps:F2} MB/s | ETA: {ETA}",
                                    LogPrefix, job.Id, progressInfo.Percent, progressInfo.TransferredMB, 
                                    progressInfo.TotalMB, progressInfo.SpeedMBps, progressInfo.ETA);
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning(ex, "{LogPrefix} Failed to update sync progress | JobId: {JobId}", 
                                    LogPrefix, job.Id);
                            }
                        }
                    }

                    // Update heartbeat periodically during sync (fallback if no progress updates)
                    if (DateTime.UtcNow - lastHeartbeat >= heartbeatInterval)
                    {
                        try
                        {
                            // Only update if we haven't gotten progress updates recently
                            if (job.LastHeartbeat == null || (DateTime.UtcNow - job.LastHeartbeat.Value).TotalSeconds >= 30)
                            {
                                job.LastHeartbeat = DateTime.UtcNow;
                                // Only update state if we haven't gotten progress updates (no % in state)
                                if (string.IsNullOrEmpty(job.CurrentState) || !job.CurrentState.Contains("%"))
                                {
                                    job.CurrentState = $"Syncing files to Backblaze B2... ({elapsed.TotalMinutes:F1} min elapsed)";
                                }
                                await UnitOfWork.Complete();
                                lastHeartbeat = DateTime.UtcNow;
                                Logger.LogDebug("{LogPrefix} Heartbeat updated during sync | JobId: {JobId} | Elapsed: {Elapsed:F1} min", 
                                    LogPrefix, job.Id, elapsed.TotalMinutes);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "{LogPrefix} Failed to update heartbeat during sync | JobId: {JobId}", 
                                LogPrefix, job.Id);
                            // Continue sync even if heartbeat update fails
                        }
                    }

                    // Wait a bit before checking again
                    await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
                }

                if (process.ExitCode == 0)
                {
                    Logger.LogInformation("{LogPrefix} Backblaze B2 sync completed successfully | JobId: {JobId}", 
                        LogPrefix, job.Id);
                }
                else
                {
                    var errorOutput = errorBuilder.ToString();
                    Logger.LogError("{LogPrefix} Backblaze sync failed with exit code {ExitCode} | JobId: {JobId} | Error: {Error}", 
                        LogPrefix, process.ExitCode, job.Id, errorOutput);
                    throw new Exception($"Rclone sync failed with exit code {process.ExitCode}: {errorOutput}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{LogPrefix} Failed to sync to Backblaze B2 | JobId: {JobId}", 
                    LogPrefix, job.Id);
                throw; // Re-throw to fail the job
            }
        }

        /// <summary>
        /// Cleans up block storage directory after successful sync to B2.
        /// Deletes the entire job directory to free up space.
        /// </summary>
        private async Task CleanupBlockStorageAsync(UserJob job)
        {
            if (string.IsNullOrEmpty(job.DownloadPath))
            {
                Logger.LogWarning("{LogPrefix} Cannot cleanup - download path is empty | JobId: {JobId}", 
                    LogPrefix, job.Id);
                return;
            }

            try
            {
                Logger.LogInformation("{LogPrefix} Cleaning up block storage | JobId: {JobId} | Path: {Path}", 
                    LogPrefix, job.Id, job.DownloadPath);

                if (Directory.Exists(job.DownloadPath))
                {
                    Directory.Delete(job.DownloadPath, recursive: true);
                    Logger.LogInformation("{LogPrefix} Block storage cleaned up successfully | JobId: {JobId} | Path: {Path}", 
                        LogPrefix, job.Id, job.DownloadPath);
                }
                else
                {
                    Logger.LogWarning("{LogPrefix} Block storage path does not exist (may have been cleaned already) | JobId: {JobId} | Path: {Path}", 
                        LogPrefix, job.Id, job.DownloadPath);
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail - cleanup is best effort
                // Job can still proceed even if cleanup fails
                Logger.LogWarning(ex, "{LogPrefix} Failed to cleanup block storage | JobId: {JobId} | Path: {Path}", 
                    LogPrefix, job.Id, job.DownloadPath);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Parses rclone progress output line.
        /// Format: "Transferred:   1.234 GiB / 5.678 GiB, 22%, 12.34 MiB/s, ETA 0s"
        /// </summary>
        private RcloneProgressInfo? ParseRcloneProgress(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.Contains("Transferred:"))
                return null;

            try
            {
                // Pattern: "Transferred:   X.XXX GiB / Y.YYY GiB, Z%, speed, ETA"
                // Extract percentage
                var percentMatch = Regex.Match(line, @"(\d+(?:\.\d+)?)%");
                if (!percentMatch.Success)
                    return null;

                var percent = double.Parse(percentMatch.Groups[1].Value);

                // Extract transferred and total (could be in GiB, MiB, KiB, or bytes)
                var transferredMatch = Regex.Match(line, @"Transferred:\s+([\d.]+)\s+(GiB|MiB|KiB|Bytes)", RegexOptions.IgnoreCase);
                var totalMatch = Regex.Match(line, @"/\s+([\d.]+)\s+(GiB|MiB|KiB|Bytes)", RegexOptions.IgnoreCase);
                
                double transferredMB = 0;
                double totalMB = 0;

                if (transferredMatch.Success)
                {
                    var value = double.Parse(transferredMatch.Groups[1].Value);
                    var unit = transferredMatch.Groups[2].Value;
                    transferredMB = ConvertToMB(value, unit);
                }

                if (totalMatch.Success)
                {
                    var value = double.Parse(totalMatch.Groups[1].Value);
                    var unit = totalMatch.Groups[2].Value;
                    totalMB = ConvertToMB(value, unit);
                }

                // Extract speed
                var speedMatch = Regex.Match(line, @"([\d.]+)\s+(GiB|MiB|KiB)/s", RegexOptions.IgnoreCase);
                double speedMBps = 0;
                if (speedMatch.Success)
                {
                    var value = double.Parse(speedMatch.Groups[1].Value);
                    var unit = speedMatch.Groups[2].Value;
                    speedMBps = ConvertToMB(value, unit);
                }

                // Extract ETA
                var etaMatch = Regex.Match(line, @"ETA\s+([\dhm]+)", RegexOptions.IgnoreCase);
                var eta = etaMatch.Success ? etaMatch.Groups[1].Value : "unknown";

                return new RcloneProgressInfo
                {
                    Percent = percent,
                    TransferredMB = transferredMB,
                    TotalMB = totalMB,
                    SpeedMBps = speedMBps,
                    ETA = eta
                };
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "{LogPrefix} Failed to parse rclone progress line: {Line}", LogPrefix, line);
                return null;
            }
        }

        private double ConvertToMB(double value, string unit)
        {
            return unit.ToUpperInvariant() switch
            {
                "GIB" => value * 1024,
                "MIB" => value,
                "KIB" => value / 1024,
                "BYTES" => value / (1024 * 1024),
                _ => value
            };
        }

        private class RcloneProgressInfo
        {
            public double Percent { get; set; }
            public double TransferredMB { get; set; }
            public double TotalMB { get; set; }
            public double SpeedMBps { get; set; }
            public string ETA { get; set; } = string.Empty;
        }
    }
}
