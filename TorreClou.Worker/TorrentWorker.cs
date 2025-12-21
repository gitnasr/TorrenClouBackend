using Hangfire;
using StackExchange.Redis;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Interfaces.Hangfire;
using TorreClou.Infrastructure.Workers;

namespace TorreClou.Worker
{
    public class TorrentWorker(
        ILogger<TorrentWorker> logger,
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory) : BaseStreamWorker(logger, redis, scopeFactory)
    {
        protected override string StreamKey => "jobs:stream";
        protected override string ConsumerGroupName => "torrent-workers";

        protected override async Task<bool> ProcessJobAsync(StreamEntry entry, IServiceProvider services, CancellationToken token)
        {
            // 1. Use Base Helper for Parsing
            var jobId = ParseJobId(entry);
            if (!jobId.HasValue)
            {
                Logger.LogWarning("Invalid JobId in stream. Acking to remove.");
                return true; // Return true to Ack and remove invalid message
            }

            // 2. Resolve services directly from the provided scoped provider
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var hangfireClient = services.GetRequiredService<IBackgroundJobClient>();

            // 3. Business Logic
            var job = await unitOfWork.Repository<UserJob>().GetByIdAsync(jobId.Value);
            if (job == null) return true; // Job deleted? Ack it.

            if (!string.IsNullOrEmpty(job.HangfireJobId))
            {
                Logger.LogInformation("Job {Id} already enqueued.", jobId);
                return true;
            }

            var hfId = hangfireClient.Enqueue<ITorrentDownloadJob>(x => x.ExecuteAsync(jobId.Value, CancellationToken.None));

            job.HangfireJobId = hfId;
            job.Status = JobStatus.QUEUED;
            await unitOfWork.Complete();

            Logger.LogInformation("Enqueued Job {Id} -> HF {HfId}", jobId, hfId);

            return true; // Successfully processed -> Base class will ACK
        }
    }
}