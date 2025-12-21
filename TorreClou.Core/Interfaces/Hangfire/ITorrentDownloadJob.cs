using Hangfire;

namespace TorreClou.Core.Interfaces.Hangfire
{
    public interface ITorrentDownloadJob
    {
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
        [Queue("torrents")] 
        Task ExecuteAsync(int jobId, CancellationToken cancellationToken = default);
    }
}
