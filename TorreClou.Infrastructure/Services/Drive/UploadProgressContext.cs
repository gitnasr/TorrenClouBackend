using Microsoft.Extensions.Logging;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Services.Redis; // Ensure namespace matches your RedisCacheService

namespace TorreClou.Infrastructure.Services.Drive
{
  

    public class UploadProgressContext(IRedisCacheService redisCache) : IUploadProgressContext
    {
        // Throttling constants
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(30);
        private const double DbUpdateThresholdPercent = 5.0;
        private static readonly TimeSpan ResumeUriTtl = TimeSpan.FromDays(7);
        private static readonly TimeSpan CompletedFileTtl = TimeSpan.FromDays(30);
        private static readonly TimeSpan RootFolderTtl = TimeSpan.FromDays(30);

        // State
        private double _lastDbPercent;
        private DateTime _lastLogTime = DateTime.MinValue;
        private long _completedBytes;

        // Config
        private int _jobId;
        private long _totalBytes;
        private ILogger? _logger;
        private Func<string, double, Task>? _onDbUpdate;
        private bool _isConfigured;

        public bool IsConfigured => _isConfigured;

        public void Configure(int jobId, long totalBytes, ILogger logger, Func<string, double, Task> onDbUpdate)
        {
            _jobId = jobId;
            _totalBytes = totalBytes;
            _logger = logger;
            _onDbUpdate = onDbUpdate;
            _isConfigured = true;

            // Reset state
            _lastDbPercent = 0;
            _lastLogTime = DateTime.MinValue;
            _completedBytes = 0;

            _logger.LogInformation("[UPLOAD_CTX] Configured | JobId: {JobId} | Total: {TotalMB:F2} MB",
                jobId, totalBytes / 1048576.0);
        }

        public async Task ReportProgressAsync(string fileName, long bytesUploaded, long fileSize)
        {
            if (!_isConfigured) return;

            var currentTotal = _completedBytes + bytesUploaded;
            await CheckAndReportAsync(currentTotal, fileName, isFileComplete: false);
        }

        // FIXED: Changed to Async to support DB updates
        public async Task MarkFileCompletedAsync(string fileName, long fileSize)
        {
            if (!_isConfigured) return;

            _completedBytes += fileSize;

            // Force check progress to update UI after file finish
            await CheckAndReportAsync(_completedBytes, fileName, isFileComplete: true);
        }

        // FIXED: Renamed to Async
        public async Task MarkBytesCompletedAsync(string fileName, long bytesUploaded)
        {
            if (!_isConfigured) return;

            _completedBytes += bytesUploaded;
            await CheckAndReportAsync(_completedBytes, fileName, isFileComplete: false);
        }

        private async Task CheckAndReportAsync(long currentBytes, string currentFile, bool isFileComplete)
        {
            var now = DateTime.UtcNow;
            var overallPercent = _totalBytes > 0 ? (currentBytes * 100.0) / _totalBytes : 0;

            // 1. DB Update Logic (Throttled or Forced on Completion)
            // If the jump is big enough OR if we just finished a file (good visual checkpoint), update DB
            if (overallPercent - _lastDbPercent >= DbUpdateThresholdPercent || (isFileComplete && overallPercent > _lastDbPercent))
            {
                var stateMessage = $"Uploading: {overallPercent:F1}%";
                if (_onDbUpdate != null)
                {
                    await _onDbUpdate(stateMessage, overallPercent);
                }
                _lastDbPercent = overallPercent;
            }

            // 2. Logging Logic (Throttled)
            if ((now - _lastLogTime) >= LogInterval || isFileComplete)
            {
                var fileTag = isFileComplete ? "[FILE_DONE]" : "[UPLOADING]";
                _logger?.LogInformation(
                    "{Tag} JobId: {JobId} | {Percent:F1}% | File: {File} | {Uploaded}/{Total} MB",
                    fileTag, _jobId, overallPercent, currentFile,
                    currentBytes >> 20, _totalBytes >> 20); // Bitshift >> 20 is fast / 1024*1024

                _lastLogTime = now;
            }
        }


        public async Task<string?> GetResumeUriAsync(string relativePath)
        {
            if (!_isConfigured) return null;
            return await redisCache.GetAsync(GetResumeKey(relativePath));
        }

        public async Task SetResumeUriAsync(string relativePath, string resumeUri)
        {
            if (!_isConfigured) return;
            await redisCache.SetAsync(GetResumeKey(relativePath), resumeUri, ResumeUriTtl);
        }

        public async Task ClearResumeUriAsync(string relativePath)
        {
            if (!_isConfigured) return;
            await redisCache.DeleteAsync(GetResumeKey(relativePath));
        }

        public async Task SetCompletedFileAsync(string relativePath, string driveFileId)
        {
            if (!_isConfigured) return;
            await redisCache.SetAsync(GetCompletedFileKey(relativePath), driveFileId, CompletedFileTtl);
        }

        public async Task<string?> GetCompletedFileAsync(string relativePath)
        {
            if (!_isConfigured) return null;
            return await redisCache.GetAsync(GetCompletedFileKey(relativePath));
        }

        public async Task SetRootFolderIdAsync(int jobId, string folderId)
        {
            await redisCache.SetAsync(GetRootFolderKey(jobId), folderId, RootFolderTtl);
        }

        public async Task<string?> GetRootFolderIdAsync(int jobId)
        {
            return await redisCache.GetAsync(GetRootFolderKey(jobId));
        }

        public async Task ClearJobStateAsync(int jobId)
        {
            await redisCache.DeleteAsync(GetRootFolderKey(jobId));
        }

        // --- Helpers ---

        private string GetResumeKey(string path) => $"gdrive:resume:{_jobId}:{SanitizeKey(path)}";
        private string GetCompletedFileKey(string path) => $"gdrive:completed:{_jobId}:{SanitizeKey(path)}";
        private static string GetRootFolderKey(int jobId) => $"gdrive:rootfolder:{jobId}";

        private static string SanitizeKey(string path)
        {
            var clean = path.Replace('\\', '/');
            if (clean.Length <= 100) return clean;

            // Fast hash for long paths
            var hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(clean));
            return Convert.ToBase64String(hashBytes)[..20]; // Truncate hash for readability
        }
    }
}