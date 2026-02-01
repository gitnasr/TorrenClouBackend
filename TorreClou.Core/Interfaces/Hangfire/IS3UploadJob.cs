using Hangfire;

namespace TorreClou.Core.Interfaces.Hangfire
{
    public interface IS3UploadJob
    {
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
        [Queue("s3")]
        Task ExecuteAsync(int jobId, CancellationToken cancellationToken = default);
    }
}
