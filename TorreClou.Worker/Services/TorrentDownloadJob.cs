using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Specifications;
using MonoTorrent;
using MonoTorrent.Client;
using Microsoft.Extensions.Logging;
using Hangfire;

namespace TorreClou.Worker.Services
{
    /// <summary>
    /// Hangfire job that handles torrent downloading with crash recovery support.
    /// Uses MonoTorrent's FastResume to continue downloads after crashes.
    /// </summary>
    public class TorrentDownloadJob
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TorrentDownloadJob> _logger;
        private readonly IBackgroundJobClient _backgroundJobClient;

        // Save FastResume state every 30 seconds
        private static readonly TimeSpan FastResumeSaveInterval = TimeSpan.FromSeconds(30);
        
        // Update database progress every 5 seconds
        private static readonly TimeSpan DbUpdateInterval = TimeSpan.FromSeconds(5);

        public TorrentDownloadJob(
            IUnitOfWork unitOfWork,
            IHttpClientFactory httpClientFactory,
            ILogger<TorrentDownloadJob> logger,
            IBackgroundJobClient backgroundJobClient)
        {
            _unitOfWork = unitOfWork;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _backgroundJobClient = backgroundJobClient;
        }

        [DisableConcurrentExecution(timeoutInSeconds: 3600)] // 1 hour max for large torrents
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
        [Queue("torrents")]
        public async Task ExecuteAsync(int jobId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("[DOWNLOAD] Starting download job | JobId: {JobId}", jobId);

            UserJob? job = null;
            ClientEngine? engine = null;
            TorrentManager? manager = null;

            try
            {
                // 1. Load job from database
                job = await LoadJobAsync(jobId);
                if (job == null) return;

                // 2. Check if already completed or cancelled
                if (job.Status == JobStatus.COMPLETED || job.Status == JobStatus.CANCELLED)
                {
                    _logger.LogInformation("[DOWNLOAD] Job already finished | JobId: {JobId} | Status: {Status}", jobId, job.Status);
                    return;
                }

                // 3. Initialize download path (use existing or create new)
                var downloadPath = InitializeDownloadPath(job);

                // 4. Update job status to PROCESSING
                job.Status = JobStatus.PROCESSING;
                job.StartedAt ??= DateTime.UtcNow;
                job.LastHeartbeat = DateTime.UtcNow;
                job.DownloadPath = downloadPath;
                job.CurrentState = "Initializing torrent download...";
                await _unitOfWork.Complete();

                // 5. Download torrent file and load it
                var torrent = await DownloadTorrentFileAsync(job, cancellationToken);
                if (torrent == null)
                {
                    await MarkJobFailedAsync(job, "Failed to download or parse torrent file");
                    return;
                }

                // 6. Store total bytes
                job.TotalBytes = torrent.Size;
                await _unitOfWork.Complete();

                // 7. Create and configure MonoTorrent engine with FastResume
                engine = CreateEngine(downloadPath);
                manager = await engine.AddAsync(torrent, downloadPath);

                _logger.LogInformation(
                    "[DOWNLOAD] Torrent loaded | JobId: {JobId} | Name: {Name} | Size: {SizeMB:F2} MB | Path: {Path}",
                    jobId, torrent.Name, torrent.Size / (1024.0 * 1024.0), downloadPath);

                // 8. Start downloading
                await manager.StartAsync();
                _logger.LogInformation("[DOWNLOAD] Download started | JobId: {JobId} | Initial State: {State}", jobId, manager.State);

                // 9. Monitor download progress
                var success = await MonitorDownloadAsync(job, engine, manager, cancellationToken);

                if (success)
                {
                    // 10. Download complete - chain to upload job
                    await OnDownloadCompleteAsync(job, engine);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[DOWNLOAD] Job cancelled | JobId: {JobId}", jobId);
                if (engine != null)
                {
                    await SaveEngineStateAsync(engine, "cancellation");
                }
                throw; // Let Hangfire handle the cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DOWNLOAD] Fatal error | JobId: {JobId}", jobId);
                
                if (engine != null)
                {
                    await SaveEngineStateAsync(engine, "error");
                }

                if (job != null)
                {
                    await MarkJobFailedAsync(job, ex.Message);
                }
                
                throw; // Let Hangfire retry if attempts remain
            }
            finally
            {
                // Cleanup
                if (manager != null)
                {
                    try { await manager.StopAsync(); } catch { /* ignore */ }
                }
                engine?.Dispose();
            }
        }

        private async Task<UserJob?> LoadJobAsync(int jobId)
        {
            var spec = new BaseSpecification<UserJob>(j => j.Id == jobId);
            spec.AddInclude(j => j.RequestFile);
            spec.AddInclude(j => j.StorageProfile);

            var job = await _unitOfWork.Repository<UserJob>().GetEntityWithSpec(spec);
            
            if (job == null)
            {
                _logger.LogError("[DOWNLOAD] Job not found | JobId: {JobId}", jobId);
            }

            return job;
        }

        private string InitializeDownloadPath(UserJob job)
        {
            // Use existing path if resuming, otherwise create new
            if (!string.IsNullOrEmpty(job.DownloadPath) && Directory.Exists(job.DownloadPath))
            {
                _logger.LogInformation("[DOWNLOAD] Resuming with existing path | JobId: {JobId} | Path: {Path}", 
                    job.Id, job.DownloadPath);
                return job.DownloadPath;
            }

            var downloadPath = Path.Combine(AppContext.BaseDirectory, "data", "torrents", job.Id.ToString());
            Directory.CreateDirectory(downloadPath);
            
            _logger.LogInformation("[DOWNLOAD] Created new download path | JobId: {JobId} | Path: {Path}", 
                job.Id, downloadPath);
            
            return downloadPath;
        }

        private async Task<Torrent?> DownloadTorrentFileAsync(UserJob job, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(job.RequestFile?.DirectUrl))
            {
                _logger.LogError("[DOWNLOAD] No torrent URL | JobId: {JobId}", job.Id);
                return null;
            }

            try
            {
                _logger.LogInformation("[DOWNLOAD] Downloading torrent file | JobId: {JobId} | Url: {Url}",
                    job.Id, job.RequestFile.DirectUrl);

                var httpClient = _httpClientFactory.CreateClient();
                var torrentBytes = await httpClient.GetByteArrayAsync(job.RequestFile.DirectUrl, cancellationToken);

                using var stream = new MemoryStream(torrentBytes);
                return await Torrent.LoadAsync(stream);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DOWNLOAD] Failed to download torrent file | JobId: {JobId}", job.Id);
                return null;
            }
        }

        private ClientEngine CreateEngine(string downloadPath)
        {
            var settings = new EngineSettingsBuilder
            {
                AllowPortForwarding = false,
                AutoSaveLoadDhtCache = true,
                AutoSaveLoadFastResume = true,  // Enable FastResume for crash recovery
                CacheDirectory = downloadPath    // Store .fresume files alongside downloads
            }.ToSettings();

            return new ClientEngine(settings);
        }

        private async Task<bool> MonitorDownloadAsync(
            UserJob job, 
            ClientEngine engine, 
            TorrentManager manager, 
            CancellationToken cancellationToken)
        {
            var lastSaveTime = DateTime.UtcNow;
            var lastDbUpdate = DateTime.MinValue;
            var lastLoggedBytes = 0L;
            var lastLogTime = DateTime.UtcNow;
            const long LogThresholdBytes = 1024 * 1024; // Log every 1 MB

            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                // Check for completion
                if (manager.Progress >= 100.0 || manager.State == TorrentState.Seeding)
                {
                    _logger.LogInformation("[DOWNLOAD] Download complete | JobId: {JobId}", job.Id);
                    await SaveEngineStateAsync(engine, "completion");
                    return true;
                }

                // Check for error
                if (manager.State == TorrentState.Error)
                {
                    var errorReason = manager.Error?.Reason.ToString() ?? "Unknown error";
                    _logger.LogError("[DOWNLOAD] Torrent error | JobId: {JobId} | Error: {Error}", job.Id, errorReason);
                    await MarkJobFailedAsync(job, $"Torrent error: {errorReason}");
                    return false;
                }

                // Update progress metrics
                var currentBytes = manager.Monitor.DataBytesReceived + job.BytesDownloaded; // Add previous progress
                var actualDownloaded = manager.Monitor.DataBytesReceived;

                // Log progress every 1 MB
                if (actualDownloaded - lastLoggedBytes >= LogThresholdBytes)
                {
                    var speed = (actualDownloaded - lastLoggedBytes) / (now - lastLogTime).TotalSeconds;
                    _logger.LogInformation(
                        "[DOWNLOAD] Progress | JobId: {JobId} | {State} | {Progress:F2}% | {DownloadedMB:F2}/{TotalMB:F2} MB | Speed: {SpeedMBps:F2} MB/s",
                        job.Id,
                        manager.State,
                        manager.Progress,
                        actualDownloaded / (1024.0 * 1024.0),
                        job.TotalBytes / (1024.0 * 1024.0),
                        speed / (1024.0 * 1024.0));

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
                
                await _unitOfWork.Complete();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DOWNLOAD] Failed to update progress | JobId: {JobId}", job.Id);
            }
        }

        private async Task SaveEngineStateAsync(ClientEngine engine, string reason)
        {
            try
            {
                await engine.SaveStateAsync();
                _logger.LogDebug("[DOWNLOAD] FastResume saved | Reason: {Reason}", reason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DOWNLOAD] Failed to save FastResume | Reason: {Reason}", reason);
            }
        }

        private async Task OnDownloadCompleteAsync(UserJob job, ClientEngine engine)
        {
            // Save final state
            await SaveEngineStateAsync(engine, "download-complete");

            // Update job status
            job.Status = JobStatus.UPLOADING;
            job.CurrentState = "Download complete. Starting upload...";
            job.BytesDownloaded = job.TotalBytes;
            await _unitOfWork.Complete();

            _logger.LogInformation("[DOWNLOAD] Chaining to upload job | JobId: {JobId}", job.Id);

            // Chain to upload job
            var uploadJobId = _backgroundJobClient.Enqueue<TorrentUploadJob>(
                service => service.ExecuteAsync(job.Id, CancellationToken.None));

            _logger.LogInformation("[DOWNLOAD] Upload job enqueued | JobId: {JobId} | HangfireUploadJobId: {UploadJobId}", 
                job.Id, uploadJobId);
        }

        private async Task MarkJobFailedAsync(UserJob job, string errorMessage)
        {
            try
            {
                job.Status = JobStatus.FAILED;
                job.ErrorMessage = errorMessage;
                job.CompletedAt = DateTime.UtcNow;
                await _unitOfWork.Complete();
                
                _logger.LogError("[DOWNLOAD] Job marked as failed | JobId: {JobId} | Error: {Error}", job.Id, errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DOWNLOAD] Failed to mark job as failed | JobId: {JobId}", job.Id);
            }
        }
    }
}

