using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

using TorreClou.Core.Interfaces;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Entities.Torrents;

namespace TorreClou.Application.Services.Torrent
{
    public class UdpTrackerScraper : ITrackerScraper
    {
        private const long PROTOCOL_ID = 0x41727101980;

        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

        private const int MaxConcurrentTrackers = 25;

        public async Task<ScrapeAggregationResult> GetScrapeResultsAsync(string infoHash, IEnumerable<string> trackers)
        {
            if (trackers == null) throw new ArgumentNullException(nameof(trackers));

            // Parse info hash (must be 20 bytes)
            var hashBytes = TryParseInfoHash(infoHash);
            if (hashBytes == null)
            {
                // invalid hash => no scrape
                return new ScrapeAggregationResult
                {
                    Seeders = 0,
                    Leechers = 0,
                    Completed = 0,
                    TrackersSuccess = 0,
                    TrackersTotal = 0
                };
            }

            var trackerList = trackers
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Where(t => t.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (trackerList.Count == 0)
            {
                return new ScrapeAggregationResult
                {
                    Seeders = 0,
                    Leechers = 0,
                    Completed = 0,
                    TrackersSuccess = 0,
                    TrackersTotal = 0
                };
            }

            using var throttler = new SemaphoreSlim(MaxConcurrentTrackers);

            var tasks = trackerList.Select(async t =>
            {
                await throttler.WaitAsync();
                try
                {
                    return await ScrapeSingleTrackerAsync(t, hashBytes);
                }
                finally
                {
                    throttler.Release();
                }
            });

            var results = await Task.WhenAll(tasks);

            // NOTE: your ScrapeResult uses .Sucess (typo). If it is .Success, change it here.
            int trackerCount = results.Length;
            int successCount = results.Count(r => r.Sucess); // <-- change to r.Success if needed

            var successful = results.Where(r => r.Sucess).ToArray(); // <-- change to r.Success if needed

            if (successful.Length == 0)
            {
                return new ScrapeAggregationResult
                {
                    Seeders = 0,
                    Leechers = 0,
                    Completed = 0,
                    TrackersSuccess = 0,
                    TrackersTotal = trackerCount
                };
            }

            // Keep your approach: take Max across successful trackers (often closer to "real" swarm)
            int totalSeeders = successful.Max(r => r.Seeders);
            int totalLeechers = successful.Max(r => r.Leechers);
            int totalCompleted = successful.Max(r => r.Completed);

            return new ScrapeAggregationResult
            {
                Seeders = totalSeeders,
                Leechers = totalLeechers,
                Completed = totalCompleted,
                TrackersSuccess = successCount,
                TrackersTotal = trackerCount
            };
        }

        private async Task<ScrapeResult> ScrapeSingleTrackerAsync(string trackerUrl, byte[] infoHash)
        {
            using var udpClient = new UdpClient();

            try
            {
                var uri = new Uri(trackerUrl);

                // Resolve host
                var ipAddresses = await Dns.GetHostAddressesAsync(uri.Host);

                // Prefer IPv4, then IPv6 (but support both)
                var ip =
                    ipAddresses.FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork) ??
                    ipAddresses.FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetworkV6);

                if (ip == null)
                    return new ScrapeResult(0, 0, 0, false);

                udpClient.Connect(ip, uri.Port);

                // --------------------
                // CONNECT
                // --------------------
                int connectTransactionId = Random.Shared.Next();

                var connectReq = new byte[16];
                BinaryPrimitives.WriteInt64BigEndian(connectReq.AsSpan(0, 8), PROTOCOL_ID);
                BinaryPrimitives.WriteInt32BigEndian(connectReq.AsSpan(8, 4), 0); // action = connect
                BinaryPrimitives.WriteInt32BigEndian(connectReq.AsSpan(12, 4), connectTransactionId);

                await udpClient.SendAsync(connectReq);

                var connectRes = (await ReceiveWithTimeout(udpClient)).Buffer;
                if (connectRes.Length < 16)
                    return new ScrapeResult(0, 0, 0, false);

                var action = BinaryPrimitives.ReadInt32BigEndian(connectRes.AsSpan(0, 4));
                var transIdRes = BinaryPrimitives.ReadInt32BigEndian(connectRes.AsSpan(4, 4));

                if (action != 0 || transIdRes != connectTransactionId)
                    return new ScrapeResult(0, 0, 0, false);

                var connectionId = BinaryPrimitives.ReadInt64BigEndian(connectRes.AsSpan(8, 8));

                // --------------------
                // SCRAPE
                // --------------------
                int scrapeTransactionId = Random.Shared.Next();

                var scrapeReq = new byte[36];
                BinaryPrimitives.WriteInt64BigEndian(scrapeReq.AsSpan(0, 8), connectionId);
                BinaryPrimitives.WriteInt32BigEndian(scrapeReq.AsSpan(8, 4), 2); // action = scrape
                BinaryPrimitives.WriteInt32BigEndian(scrapeReq.AsSpan(12, 4), scrapeTransactionId);

                // infohash 20 bytes at offset 16
                Buffer.BlockCopy(infoHash, 0, scrapeReq, 16, 20);

                await udpClient.SendAsync(scrapeReq);

                var scrapeRes = (await ReceiveWithTimeout(udpClient)).Buffer;
                if (scrapeRes.Length < 20)
                    return new ScrapeResult(0, 0, 0, false);

                var action2 = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(0, 4));
                var transId2 = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(4, 4));

                if (action2 != 2 || transId2 != scrapeTransactionId)
                    return new ScrapeResult(0, 0, 0, false);

                var seeders = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(8, 4));
                var completed = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(12, 4));
                var leechers = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(16, 4));

                return new ScrapeResult(
                    Math.Max(0, seeders),
                    Math.Max(0, leechers),
                    Math.Max(0, completed),
                    true
                );
            }
            catch
            {
                return new ScrapeResult(0, 0, 0, false);
            }
        }

        private async Task<(byte[] Buffer, IPEndPoint RemoteEndPoint)> ReceiveWithTimeout(UdpClient client)
        {
            var receiveTask = client.ReceiveAsync();
            var timeoutTask = Task.Delay(_timeout);

            var completedTask = await Task.WhenAny(receiveTask, timeoutTask);

            if (completedTask == timeoutTask)
                throw new TimeoutException();

            var result = await receiveTask;
            return (result.Buffer, result.RemoteEndPoint);
        }

        private static byte[]? TryParseInfoHash(string infoHash)
        {
            if (string.IsNullOrWhiteSpace(infoHash))
                return null;

            infoHash = infoHash.Trim();

            if (infoHash.Length != 40)
                return null;

            try
            {
                var bytes = Convert.FromHexString(infoHash);
                return bytes.Length == 20 ? bytes : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
