using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace TorreClou.Infrastructure.Workers
{
    public abstract class BaseStreamWorker : BackgroundService
    {
        protected readonly ILogger Logger;
        protected readonly IConnectionMultiplexer Redis;
        protected readonly IServiceScopeFactory ScopeFactory; // Moved to Base

        protected abstract string StreamKey { get; }
        protected abstract string ConsumerGroupName { get; }
        protected readonly string ConsumerName = $"worker-{Environment.MachineName}-{Guid.NewGuid():N}";

        // Constructor injection for ScopeFactory
        protected BaseStreamWorker(
            ILogger logger,
            IConnectionMultiplexer redis,
            IServiceScopeFactory scopeFactory)
        {
            Logger = logger;
            Redis = redis;
            ScopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.LogInformation("[WORKER_STARTUP] Starting | Consumer: {C} | Stream: {S}", ConsumerName, StreamKey);
            var db = Redis.GetDatabase();

            await EnsureConsumerGroupExistsAsync(db);
            await ClaimPendingMessagesAsync(db, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var entries = await db.StreamReadGroupAsync(
                        StreamKey, ConsumerGroupName, ConsumerName, ">", count: 10, noAck: false
                    );

                    if (entries.Length == 0)
                    {
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }

                    foreach (var entry in entries)
                    {
                        // Pass 'db' to allow ACK within the processing flow
                        await ProcessMessageWrapperAsync(db, entry, stoppingToken);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (RedisException ex)
                {
                    Logger.LogError(ex, "[REDIS_ERROR] Retrying in 5s...");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }

        private async Task ProcessMessageWrapperAsync(IDatabase db, StreamEntry entry, CancellationToken token)
        {
            var messageId = entry.Id;
            bool success = false;

            // 1. Create Scope automatically for the derived class
            using (var scope = ScopeFactory.CreateScope())
            
                try
                {
                    // 2. Execute Derived Logic
                    // We pass the ServiceProvider so the derived class can resolve what it needs
                    success = await ProcessJobAsync(entry, scope.ServiceProvider, token);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[JOB_ERROR] MsgId: {MsgId}", messageId);
                    success = false;
                }

            // 3. RELIABILITY FIX: Ack ONLY if success
            if (success)
            {
                await db.StreamAcknowledgeAsync(StreamKey, ConsumerGroupName, messageId);
                Logger.LogDebug("[ACK] MsgId: {MsgId}", messageId);
            }
            else
            {
                // Logic: Leave in PEL (Pending Entries List). 
                // It will be picked up by 'ClaimPendingMessagesAsync' on next restart/recovery loop.
                Logger.LogWarning("[NO_ACK] MsgId: {MsgId} - Left pending for retry", messageId);
            }
        }

        // --- Helper Methods for Derived Classes ---

        /// <summary>
        /// safely extracts JobId from the stream entry. Returns null if missing/invalid.
        /// </summary>
        protected int? ParseJobId(StreamEntry entry)
        {
            var dict = entry.Values.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());
            if (dict.TryGetValue("jobId", out var val) && int.TryParse(val, out int id))
                return id;
            return null;
        }

        protected string? GetStreamValue(StreamEntry entry, string key)
        {
            return entry.Values.FirstOrDefault(x => x.Name == key).Value;
        }

        // --- Abstract Method ---

        /// <summary>
        /// Implement business logic here. 
        /// Return TRUE to Ack the message, FALSE to retry later.
        /// ServiceProvider is scoped and ready to use.
        /// </summary>
        protected abstract Task<bool> ProcessJobAsync(
            StreamEntry entry,
            IServiceProvider services,
            CancellationToken token);

        // ... (Keep EnsureConsumerGroupExistsAsync and ClaimPendingMessagesAsync as they were) ...

        private async Task EnsureConsumerGroupExistsAsync(IDatabase db)
        {
            try { await db.StreamCreateConsumerGroupAsync(StreamKey, ConsumerGroupName, "0", true); }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP")) { }
        }

        private async Task ClaimPendingMessagesAsync(IDatabase db, CancellationToken token)
        {
            // (Keep your existing logic here, it was correct)
            // Just ensure it calls ProcessMessageWrapperAsync internally
            // ...
            var pending = await db.StreamPendingAsync(StreamKey, ConsumerGroupName, CommandFlags.None);
            if (pending.PendingMessageCount > 0)
            {
                var claimed = await db.StreamAutoClaimAsync(StreamKey, ConsumerGroupName, ConsumerName, 30000, "0-0", 100);
                foreach (var entry in claimed.ClaimedEntries)
                {
                    await ProcessMessageWrapperAsync(db, entry, token);
                }
            }
        }
    }
}