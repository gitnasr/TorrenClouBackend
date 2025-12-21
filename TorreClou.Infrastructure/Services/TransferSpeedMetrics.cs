using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using TorreClou.Core.Interfaces;

namespace TorreClou.Infrastructure.Services
{
    /// <summary>
    /// Service for tracking and recording download/upload speeds using OpenTelemetry metrics.
    /// </summary>
    public class TransferSpeedMetrics : ITransferSpeedMetrics
    {
        private static readonly Meter Meter = new("TorreClou.Transfer", "1.0.0");

        private readonly Histogram<double> _downloadSpeedGauge;
        private readonly Histogram<double> _uploadSpeedGauge;
        private readonly Counter<long> _downloadTotalBytes;
        private readonly Counter<long> _uploadTotalBytes;
        private readonly Histogram<double> _downloadDuration;
        private readonly Histogram<double> _uploadDuration;

        private readonly ILogger<TransferSpeedMetrics>? _logger;

        public TransferSpeedMetrics(ILogger<TransferSpeedMetrics>? logger = null)
        {
            _logger = logger;

            _downloadSpeedGauge = Meter.CreateHistogram<double>(
                "torreclou.download.speed.bytes_per_second",
                "bytes/s",
                "Current/average download speed in bytes per second");

            _uploadSpeedGauge = Meter.CreateHistogram<double>(
                "torreclou.upload.speed.bytes_per_second",
                "bytes/s",
                "Current/average upload speed in bytes per second");

            _downloadTotalBytes = Meter.CreateCounter<long>(
                "torreclou.download.total_bytes",
                "bytes",
                "Total bytes downloaded");

            _uploadTotalBytes = Meter.CreateCounter<long>(
                "torreclou.upload.total_bytes",
                "bytes",
                "Total bytes uploaded");

            _downloadDuration = Meter.CreateHistogram<double>(
                "torreclou.download.duration_seconds",
                "s",
                "Download duration in seconds");

            _uploadDuration = Meter.CreateHistogram<double>(
                "torreclou.upload.duration_seconds",
                "s",
                "Upload duration in seconds");
        }

        /// <summary>
        /// Records download speed for a job.
        /// </summary>
        public void RecordDownloadSpeed(int jobId, int userId, string jobType, double bytesPerSecond)
        {
            var tags = new KeyValuePair<string, object?>[]
            {
                new("job_id", jobId.ToString()),
                new("user_id", userId.ToString()),
                new("job_type", jobType)
            };

            _downloadSpeedGauge.Record(bytesPerSecond, tags);
            _logger?.LogDebug("Recorded download speed: {Speed} bytes/s for job {JobId}", bytesPerSecond, jobId);
        }

        /// <summary>
        /// Records upload speed for a job.
        /// </summary>
        public void RecordUploadSpeed(int jobId, int userId, string jobType, double bytesPerSecond)
        {
            var tags = new KeyValuePair<string, object?>[]
            {
                new("job_id", jobId.ToString()),
                new("user_id", userId.ToString()),
                new("job_type", jobType)
            };

            _uploadSpeedGauge.Record(bytesPerSecond, tags);
            _logger?.LogDebug("Recorded upload speed: {Speed} bytes/s for job {JobId}", bytesPerSecond, jobId);
        }

        /// <summary>
        /// Records total bytes downloaded for a job.
        /// </summary>
        public void RecordDownloadBytes(int jobId, int userId, string jobType, long bytes)
        {
            var tags = new KeyValuePair<string, object?>[]
            {
                new("job_id", jobId.ToString()),
                new("user_id", userId.ToString()),
                new("job_type", jobType)
            };

            _downloadTotalBytes.Add(bytes, tags);
            _logger?.LogDebug("Recorded download bytes: {Bytes} for job {JobId}", bytes, jobId);
        }

        /// <summary>
        /// Records total bytes uploaded for a job.
        /// </summary>
        public void RecordUploadBytes(int jobId, int userId, string jobType, long bytes)
        {
            var tags = new KeyValuePair<string, object?>[]
            {
                new("job_id", jobId.ToString()),
                new("user_id", userId.ToString()),
                new("job_type", jobType)
            };

            _uploadTotalBytes.Add(bytes, tags);
            _logger?.LogDebug("Recorded upload bytes: {Bytes} for job {JobId}", bytes, jobId);
        }

        /// <summary>
        /// Records download duration for a job.
        /// </summary>
        public void RecordDownloadDuration(int jobId, int userId, string jobType, double durationSeconds)
        {
            var tags = new KeyValuePair<string, object?>[]
            {
                new("job_id", jobId.ToString()),
                new("user_id", userId.ToString()),
                new("job_type", jobType)
            };

            _downloadDuration.Record(durationSeconds, tags);
            _logger?.LogDebug("Recorded download duration: {Duration}s for job {JobId}", durationSeconds, jobId);
        }

        /// <summary>
        /// Records upload duration for a job.
        /// </summary>
        public void RecordUploadDuration(int jobId, int userId, string jobType, double durationSeconds)
        {
            var tags = new KeyValuePair<string, object?>[]
            {
                new("job_id", jobId.ToString()),
                new("user_id", userId.ToString()),
                new("job_type", jobType)
            };

            _uploadDuration.Record(durationSeconds, tags);
            _logger?.LogDebug("Recorded upload duration: {Duration}s for job {JobId}", durationSeconds, jobId);
        }

        /// <summary>
        /// Records final download metrics when a job completes.
        /// </summary>
        public void RecordDownloadComplete(int jobId, int userId, string jobType, long totalBytes, double durationSeconds)
        {
            RecordDownloadBytes(jobId, userId, jobType, totalBytes);
            RecordDownloadDuration(jobId, userId, jobType, durationSeconds);

            if (durationSeconds > 0)
            {
                var averageSpeed = totalBytes / durationSeconds;
                RecordDownloadSpeed(jobId, userId, jobType, averageSpeed);
            }
        }

        /// <summary>
        /// Records final upload metrics when a job completes.
        /// </summary>
        public void RecordUploadComplete(int jobId, int userId, string jobType, long totalBytes, double durationSeconds)
        {
            RecordUploadBytes(jobId, userId, jobType, totalBytes);
            RecordUploadDuration(jobId, userId, jobType, durationSeconds);

            if (durationSeconds > 0)
            {
                var averageSpeed = totalBytes / durationSeconds;
                RecordUploadSpeed(jobId, userId, jobType, averageSpeed);
            }
        }
    }
}
