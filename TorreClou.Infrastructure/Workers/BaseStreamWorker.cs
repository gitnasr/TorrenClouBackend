using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace TorreClou.Infrastructure.Workers
{
    public abstract class BaseStreamWorker : BackgroundService
    {
        protected readonly ILogger Logger;
        protected readonly IConnectionMultiplexer Redis;
        protected abstract string StreamKey { get; }
        protected abstract string ConsumerGroupName { get; }
        protected readonly string ConsumerName;

        protected BaseStreamWorker(ILogger logger, IConnectionMultiplexer redis)
        {
            Logger = logger;
            Redis = redis;
            ConsumerName = $"worker-{Environment.MachineName}-{Guid.NewGuid():N}";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var db = Redis.GetDatabase();

            // Ensure consumer group exists
            await EnsureConsumerGroupExistsAsync(db);

            // First, claim any pending messages from previous runs (retry logic)
            await ClaimPendingMessagesAsync(db, stoppingToken);

            Logger.LogInformation(
                "Worker started | Consumer: {Consumer} | Stream: {Stream} | Group: {Group}",
                ConsumerName, StreamKey, ConsumerGroupName
            );

            // Main processing loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Read new messages from stream (blocking for 5 seconds)
                    var entries = await db.StreamReadGroupAsync(
                        StreamKey,
                        ConsumerGroupName,
                        ConsumerName,
                        ">",  // Only new messages
                        count: 10,
                        noAck: false
                    );

                    if (entries.Length == 0)
                    {
                        // No new messages, wait a bit before trying again
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                        continue;
                    }

                    foreach (var entry in entries)
                    {
                        await ProcessMessageAsync(db, entry, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (RedisException ex)
                {
                    Logger.LogError(ex, "Redis error while reading from stream. Retrying in 5 seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            Logger.LogInformation("Worker stopping | Consumer: {Consumer}", ConsumerName);
        }

        private async Task EnsureConsumerGroupExistsAsync(IDatabase db)
        {
            try
            {
                // Try to create the consumer group (MKSTREAM creates the stream if it doesn't exist)
                await db.StreamCreateConsumerGroupAsync(StreamKey, ConsumerGroupName, "0", createStream: true);
                Logger.LogInformation("Created consumer group: {Group} on stream: {Stream}", ConsumerGroupName, StreamKey);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
            {
                // Group already exists, that's fine
                Logger.LogDebug("Consumer group {Group} already exists", ConsumerGroupName);
            }
        }

        private async Task ClaimPendingMessagesAsync(IDatabase db, CancellationToken stoppingToken)
        {
            try
            {
                // Get pending messages that have been idle for more than 30 seconds
                var pendingInfo = await db.StreamPendingAsync(StreamKey, ConsumerGroupName);
                
                if (pendingInfo.PendingMessageCount > 0)
                {
                    Logger.LogInformation(
                        "Found {Count} pending messages in stream. Attempting to claim...",
                        pendingInfo.PendingMessageCount
                    );

                    // Claim messages that have been pending for more than 30 seconds
                    var claimed = await db.StreamAutoClaimAsync(
                        StreamKey,
                        ConsumerGroupName,
                        ConsumerName,
                        30000,  // minIdleTimeInMs: 30 seconds
                        "0-0",  // start position
                        100     // count
                    );

                    foreach (var entry in claimed.ClaimedEntries)
                    {
                        Logger.LogInformation(
                            "[RETRY] Claimed pending message | MessageId: {MessageId}",
                            entry.Id
                        );
                        await ProcessMessageAsync(db, entry, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error claiming pending messages. Will process new messages only.");
            }
        }

        private async Task ProcessMessageAsync(IDatabase db, StreamEntry entry, CancellationToken stoppingToken)
        {
            var messageId = entry.Id;

            try
            {
                Logger.LogInformation(
                    "[RECEIVED] Message received from Redis Stream | MessageId: {MessageId}",
                    messageId
                );

                // Acknowledge immediately (as per plan: ACK before processing)
                await db.StreamAcknowledgeAsync(StreamKey, ConsumerGroupName, messageId);
                Logger.LogInformation(
                    "[ACKNOWLEDGED] Message acknowledged | MessageId: {MessageId}",
                    messageId
                );

                // Process the job (implemented by derived classes)
                var success = await ProcessJobAsync(entry, stoppingToken);

                if (success)
                {
                    Logger.LogInformation(
                        "[COMPLETED] Job processed successfully | MessageId: {MessageId}",
                        messageId
                    );
                }
                else
                {
                    Logger.LogWarning(
                        "[FAILED] Job processing returned false | MessageId: {MessageId}",
                        messageId
                    );
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "[ERROR] Error processing job | MessageId: {MessageId} | Error: {Error}",
                    messageId,
                    ex.Message
                );
                // Job status will be updated to FAILED in database by the worker
            }
        }

        /// <summary>
        /// Process a job from the Redis Stream. Derived classes must implement this method.
        /// </summary>
        /// <param name="entry">The stream entry containing job data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if job was processed successfully, false otherwise</returns>
        protected abstract Task<bool> ProcessJobAsync(StreamEntry entry, CancellationToken cancellationToken);
    }
}

