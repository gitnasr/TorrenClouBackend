using System.Buffers.Binary;
using System.Net.Sockets;
using System.Net;
using TorreClou.Core.Interfaces;
using TorreClou.Core.DTOs.Torrents;

namespace TorreClou.Application.Services.Torrent
{

    public class UdpTrackerScraper : ITrackerScraper
    {
        private const long PROTOCOL_ID = 0x41727101980;
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

        public async Task<ScrapeAggregationResult> GetScrapeResultsAsync(string infoHash, IEnumerable<string> trackers)
        {
            var hashBytes = StringToByteArray(infoHash);

            var trackerList = trackers
                .Where(t => t.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            var tasks = trackerList.Select(t => ScrapeSingleTrackerAsync(t, hashBytes));

            var results = await Task.WhenAll(tasks);

            int totalSeeders = 0;
            int totalLeechers = 0;
            int totalCompleted = 0;

            int successCount = results.Count(r => r.Sucess);
            int trackerCount = results.Length;

            totalSeeders = results.Max(r => r.Seeders);
            totalLeechers = results.Max(r => r.Leechers);
            totalCompleted = results.Max(r => r.Completed);

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
                var ipAddresses = await Dns.GetHostAddressesAsync(uri.Host);
                var ip = ipAddresses.FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork);

                if (ip == null)
                    return new ScrapeResult(0, 0, 0, false);

                udpClient.Connect(ip, uri.Port);

                var transactionId = Random.Shared.Next();

                // ---- CONNECT ----
                var connectReq = new byte[16];
                BinaryPrimitives.WriteInt64BigEndian(connectReq, PROTOCOL_ID);
                BinaryPrimitives.WriteInt32BigEndian(connectReq.AsSpan(8), 0);
                BinaryPrimitives.WriteInt32BigEndian(connectReq.AsSpan(12), transactionId);

                await udpClient.SendAsync(connectReq);
                var connectRes = (await ReceiveWithTimeout(udpClient)).Buffer;

                if (connectRes.Length < 16)
                    return new ScrapeResult(0, 0, 0, false);

                var action = BinaryPrimitives.ReadInt32BigEndian(connectRes.AsSpan(0));
                var transIdRes = BinaryPrimitives.ReadInt32BigEndian(connectRes.AsSpan(4));
                if (action != 0 || transIdRes != transactionId)
                    return new ScrapeResult(0, 0, 0, false);

                var connectionId = BinaryPrimitives.ReadInt64BigEndian(connectRes.AsSpan(8));

                // ---- SCRAPE ----
                var scrapeReq = new byte[36];
                BinaryPrimitives.WriteInt64BigEndian(scrapeReq, connectionId);
                BinaryPrimitives.WriteInt32BigEndian(scrapeReq.AsSpan(8), 2);
                BinaryPrimitives.WriteInt32BigEndian(scrapeReq.AsSpan(12), transactionId);
                Buffer.BlockCopy(infoHash, 0, scrapeReq, 16, 20);

                await udpClient.SendAsync(scrapeReq);

                var scrapeRes = (await ReceiveWithTimeout(udpClient)).Buffer;

                if (scrapeRes.Length < 20)
                    return new ScrapeResult(0, 0, 0, false);

                var action2 = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(0));
                var transId2 = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(4));

                if (action2 != 2 || transId2 != transactionId)
                    return new ScrapeResult(0, 0, 0, false);

                var seeders = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(8));
                var completed = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(12));
                var leechers = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(16));

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

        private static byte[] StringToByteArray(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return Array.Empty<byte>();

            hex = hex.Trim();

            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }
    }
}
