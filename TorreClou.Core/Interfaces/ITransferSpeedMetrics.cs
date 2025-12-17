namespace TorreClou.Core.Interfaces
{
    public interface ITransferSpeedMetrics
    {
        void RecordDownloadBytes(int jobId, int userId, string jobType, long bytes);
        void RecordDownloadComplete(int jobId, int userId, string jobType, long totalBytes, double durationSeconds);
        void RecordDownloadDuration(int jobId, int userId, string jobType, double durationSeconds);
        void RecordDownloadSpeed(int jobId, int userId, string jobType, double bytesPerSecond);
        void RecordUploadBytes(int jobId, int userId, string jobType, long bytes);
        void RecordUploadComplete(int jobId, int userId, string jobType, long totalBytes, double durationSeconds);
        void RecordUploadDuration(int jobId, int userId, string jobType, double durationSeconds);
        void RecordUploadSpeed(int jobId, int userId, string jobType, double bytesPerSecond);
    }
}