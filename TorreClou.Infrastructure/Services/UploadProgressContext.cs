using Microsoft.Extensions.Logging;
using TorreClou.Core.Interfaces;

namespace TorreClou.Infrastructure.Services
{
    /// <summary>
    /// Scoped context for tracking upload progress with throttled DB updates and logging.
    /// Also handles Redis-backed resume URI caching for interrupted uploads.
    /// </summary>
    public class UploadProgressContext : IUploadProgressContext
    {
        private readonly IRedisCacheService _redisCache;

        // Throttling constants
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(30);
        private const double DbUpdateThresholdPercent = 5.0;
        private static readonly TimeSpan ResumeUriTtl = TimeSpan.FromDays(7);
        private static readonly TimeSpan CompletedFileTtl = TimeSpan.FromDays(30);
        private static readonly TimeSpan RootFolderTtl = TimeSpan.FromDays(30);

        // Throttling state
        private double _lastDbPercent = 0;
        private DateTime _lastLogTime = DateTime.MinValue;
        private long _completedBytes = 0;

        // Configuration state
        private int _jobId;
        private long _totalBytes;
        private ILogger? _logger;
        private Func<string, double, Task>? _onDbUpdate;
        private bool _isConfigured = false;

        public bool IsConfigured => _isConfigured;

        public UploadProgressContext(IRedisCacheService redisCache)
        {
            _redisCache = redisCache;
        }

        public void Configure(int jobId, long totalBytes, ILogger logger, Func<string, double, Task> onDbUpdate)
        {
            _jobId = jobId;
            _totalBytes = totalBytes;
            _logger = logger;
            _onDbUpdate = onDbUpdate;
            _isConfigured = true;

            // Reset throttling state for fresh start
            _lastDbPercent = 0;
            _lastLogTime = DateTime.MinValue;
            _completedBytes = 0;

            _logger.LogInformation("[UPLOAD_CONTEXT] Configured | JobId: {JobId} | TotalBytes: {TotalMB:F2} MB",
                jobId, totalBytes / (1024.0 * 1024.0));
        }

        public async Task ReportProgressAsync(string fileName, long bytesUploaded, long fileSize)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("UploadProgressContext must be configured before reporting progress.");
            }

            var now = DateTime.UtcNow;
            var overallBytes = _completedBytes + bytesUploaded;
            var overallPercent = _totalBytes > 0 ? (overallBytes * 100.0) / _totalBytes : 0;
            var filePercent = fileSize > 0 ? (bytesUploaded * 100.0) / fileSize : 0;

            // DB update every 5% overall progress
            if (overallPercent - _lastDbPercent >= DbUpdateThresholdPercent)
            {
                var stateMessage = $"Uploading: {overallPercent:F1}%";
                if (_onDbUpdate != null)
                {
                    await _onDbUpdate(stateMessage, overallPercent);
                }
                _lastDbPercent = overallPercent;
            }

            // Log every 30 seconds
            if ((now - _lastLogTime) >= LogInterval)
            {
                _logger?.LogInformation(
                    "[UPLOAD] JobId: {JobId} | Overall: {OverallPercent:F1}% | File: {FileName} | FileProgress: {FilePercent:F1}% | {UploadedMB:F2}/{TotalMB:F2} MB",
                    _jobId,
                    overallPercent,
                    fileName,
                    filePercent,
                    overallBytes / (1024.0 * 1024.0),
                    _totalBytes / (1024.0 * 1024.0));
                _lastLogTime = now;
            }
        }

        public void MarkFileCompleted(string fileName, long fileSize)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("UploadProgressContext must be configured before marking files complete.");
            }

            _completedBytes += fileSize;

            _logger?.LogInformation(
                "[UPLOAD] File completed | JobId: {JobId} | File: {FileName} | Size: {SizeMB:F2} MB | CompletedTotal: {CompletedMB:F2} MB",
                _jobId,
                fileName,
                fileSize / (1024.0 * 1024.0),
                _completedBytes / (1024.0 * 1024.0));
        }

        public void MarkBytesCompleted(string fileName, long bytesUploaded)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("UploadProgressContext must be configured before marking bytes complete.");
            }

            _completedBytes += bytesUploaded;

            _logger?.LogInformation(
                "[UPLOAD] Bytes completed | JobId: {JobId} | File: {FileName} | Bytes: {BytesMB:F2} MB | CompletedTotal: {CompletedMB:F2} MB",
                _jobId,
                fileName,
                bytesUploaded / (1024.0 * 1024.0),
                _completedBytes / (1024.0 * 1024.0));
        }

        public async Task<string?> GetResumeUriAsync(string relativePath)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("UploadProgressContext must be configured before accessing resume URIs.");
            }

            var key = GetResumeKey(relativePath);
            var value = await _redisCache.GetAsync(key);

            if (value != null)
            {
                _logger?.LogInformation("[UPLOAD_RESUME] Found resume URI | JobId: {JobId} | Path: {Path}",
                    _jobId, relativePath);
            }

            return value;
        }

        public async Task SetResumeUriAsync(string relativePath, string resumeUri)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("UploadProgressContext must be configured before setting resume URIs.");
            }

            var key = GetResumeKey(relativePath);
            await _redisCache.SetAsync(key, resumeUri, ResumeUriTtl);

            _logger?.LogDebug("[UPLOAD_RESUME] Cached resume URI | JobId: {JobId} | Path: {Path} | TTL: {TTL}",
                _jobId, relativePath, ResumeUriTtl);
        }

        public async Task ClearResumeUriAsync(string relativePath)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("UploadProgressContext must be configured before clearing resume URIs.");
            }

            var key = GetResumeKey(relativePath);
            await _redisCache.DeleteAsync(key);

            _logger?.LogDebug("[UPLOAD_RESUME] Cleared resume URI | JobId: {JobId} | Path: {Path}",
                _jobId, relativePath);
        }

        public async Task SetCompletedFileAsync(string relativePath, string driveFileId)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("UploadProgressContext must be configured before setting completed files.");
            }

            var key = GetCompletedFileKey(relativePath);
            await _redisCache.SetAsync(key, driveFileId, CompletedFileTtl);

            _logger?.LogDebug("[UPLOAD_COMPLETED] Cached completed file | JobId: {JobId} | Path: {Path} | FileId: {FileId} | TTL: {TTL}",
                _jobId, relativePath, driveFileId, CompletedFileTtl);
        }

        public async Task<string?> GetCompletedFileAsync(string relativePath)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("UploadProgressContext must be configured before accessing completed files.");
            }

            var key = GetCompletedFileKey(relativePath);
            var value = await _redisCache.GetAsync(key);

            if (value != null)
            {
                _logger?.LogInformation("[UPLOAD_COMPLETED] Found completed file | JobId: {JobId} | Path: {Path} | FileId: {FileId}",
                    _jobId, relativePath, value);
            }

            return value;
        }

        public async Task SetRootFolderIdAsync(int jobId, string folderId)
        {
            var key = GetRootFolderKey(jobId);
            await _redisCache.SetAsync(key, folderId, RootFolderTtl);

            _logger?.LogInformation("[UPLOAD_ROOT_FOLDER] Cached root folder ID | JobId: {JobId} | FolderId: {FolderId} | TTL: {TTL}",
                jobId, folderId, RootFolderTtl);
        }

        public async Task<string?> GetRootFolderIdAsync(int jobId)
        {
            var key = GetRootFolderKey(jobId);
            var value = await _redisCache.GetAsync(key);

            if (value != null)
            {
                _logger?.LogInformation("[UPLOAD_ROOT_FOLDER] Found root folder ID | JobId: {JobId} | FolderId: {FolderId}",
                    jobId, value);
            }

            return value;
        }

        public async Task ClearJobStateAsync(int jobId)
        {
            // Clear root folder
            var rootFolderKey = GetRootFolderKey(jobId);
            await _redisCache.DeleteAsync(rootFolderKey);

            // Note: We don't clear individual completed files or resume URIs here
            // They will expire naturally with their TTL (30 days for completed files, 7 days for resume URIs)
            // This allows for recovery scenarios even after job completion

            _logger?.LogInformation("[UPLOAD_CLEANUP] Cleared root folder ID | JobId: {JobId}", jobId);
        }

        private string GetResumeKey(string relativePath)
        {
            // Sanitize path for Redis key (replace backslashes, limit length)
            var sanitizedPath = relativePath.Replace('\\', '/');
            
            // If path is too long, use a hash
            if (sanitizedPath.Length > 200)
            {
                sanitizedPath = Convert.ToBase64String(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(relativePath)))[..32];
            }

            return $"gdrive:resume:{_jobId}:{sanitizedPath}";
        }

        private string GetCompletedFileKey(string relativePath)
        {
            // Sanitize path for Redis key (replace backslashes, limit length)
            var sanitizedPath = relativePath.Replace('\\', '/');
            
            // If path is too long, use a hash
            if (sanitizedPath.Length > 200)
            {
                sanitizedPath = Convert.ToBase64String(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(relativePath)))[..32];
            }

            return $"gdrive:completed:{_jobId}:{sanitizedPath}";
        }

        private static string GetRootFolderKey(int jobId)
        {
            return $"gdrive:rootfolder:{jobId}";
        }
    }
}
