using Hangfire;

namespace TorreClou.Core.Interfaces.Hangfire
{
    public interface IGoogleDriveUploadJob
    {
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
        [Queue("googledrive")]
        Task ExecuteAsync(int jobId, CancellationToken cancellationToken = default);
    }
}
