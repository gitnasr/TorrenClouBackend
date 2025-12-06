using System.Buffers.Binary;
using System.Net.Sockets;
using System.Net;
using TorreClou.Core.Interfaces;

namespace TorreClou.Application.Services
{
    public class UdpTrackerScraper : ITrackerScraper
    {
        // الثابت السحري لبروتوكول التورنت (Protocol ID)
        private const long PROTOCOL_ID = 0x41727101980;
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(1.5); // التراكرز بطيئة، لو مجاش رد في ثانية ونص اقلب

        public async Task<int> GetSeedersCountAsync(string infoHash, IEnumerable<string> trackersUrl)
        {
            // هنحول الهاش من String Hex لـ Byte Array
            var hashBytes = StringToByteArray(infoHash);

            // عشان نسرع الدنيا، هنسأل أول 3-5 تراكرز شغالين بالتوازي (Parallel)
            // وناخد أكبر رقم يرجع لنا (Optimistic approach)
            var validTrackers = trackersUrl
                .Where(t => t.StartsWith("udp://"))
                .Take(5); // كفاية 5 عشان منعملش Traffic عالي

            var tasks = validTrackers.Select(t => ScrapeSingleTrackerAsync(t, hashBytes));

            var results = await Task.WhenAll(tasks);

            // لو كله فشل رجع 0، غير كده رجع أكبر رقم
            return results.Max();
        }

        private async Task<int> ScrapeSingleTrackerAsync(string trackerUrl, byte[] infoHash)
        {
            using var udpClient = new UdpClient();

            try
            {
                // 1. Parse URL (udp://tracker.opentrackr.org:1337)
                var uri = new Uri(trackerUrl);
                var ipAddresses = await Dns.GetHostAddressesAsync(uri.Host);
                var ip = ipAddresses.FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork); // IPv4 prefered

                if (ip == null) return 0;

                udpClient.Connect(ip, uri.Port);
                udpClient.Client.ReceiveTimeout = (int)_timeout.TotalMilliseconds;
                udpClient.Client.SendTimeout = (int)_timeout.TotalMilliseconds;

                // --- STEP 1: CONNECT REQUEST ---
                var transactionId = Random.Shared.Next();
                var connectReq = new byte[16];

                // كتابة البيانات بنظام Big Endian (Network Order)
                BinaryPrimitives.WriteInt64BigEndian(connectReq.AsSpan(0), PROTOCOL_ID);
                BinaryPrimitives.WriteInt32BigEndian(connectReq.AsSpan(8), 0); // Action = 0 (Connect)
                BinaryPrimitives.WriteInt32BigEndian(connectReq.AsSpan(12), transactionId);

                await udpClient.SendAsync(connectReq, connectReq.Length);

                // استلام الرد
                var connectResResult = await ReceiveWithTimeout(udpClient);
                var connectRes = connectResResult.Buffer;

                // التحقق من صحة الرد (Action لازم يكون 0 والـ TransactionId مطابق)
                if (connectRes.Length < 16) return 0;
                var action = BinaryPrimitives.ReadInt32BigEndian(connectRes.AsSpan(0));
                var resTransId = BinaryPrimitives.ReadInt32BigEndian(connectRes.AsSpan(4));

                if (action != 0 || resTransId != transactionId) return 0;

                var connectionId = BinaryPrimitives.ReadInt64BigEndian(connectRes.AsSpan(8));

                // --- STEP 2: SCRAPE REQUEST ---
                var scrapeReq = new byte[36]; // 8 + 4 + 4 + 20 (InfoHash)
                BinaryPrimitives.WriteInt64BigEndian(scrapeReq.AsSpan(0), connectionId);
                BinaryPrimitives.WriteInt32BigEndian(scrapeReq.AsSpan(8), 2); // Action = 2 (Scrape)
                BinaryPrimitives.WriteInt32BigEndian(scrapeReq.AsSpan(12), transactionId);
                Buffer.BlockCopy(infoHash, 0, scrapeReq, 16, 20); // نسخ الهاش

                await udpClient.SendAsync(scrapeReq, scrapeReq.Length);

                var scrapeResResult = await ReceiveWithTimeout(udpClient);
                var scrapeRes = scrapeResResult.Buffer;

                if (scrapeRes.Length < 8) return 0;

                // الرد بيكون: Action(4) + TransId(4) + Seeders(4) + Completed(4) + Leechers(4)
                // إحنا يهمنا الـ Seeders اللي بيبدأ من الـ Offset 8

                var seeders = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(8));
                // var completed = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(12));
                // var leechers = BinaryPrimitives.ReadInt32BigEndian(scrapeRes.AsSpan(16));

                return seeders; // هو ده الرقم اللي هيحدد السعر! 💰
            }
            catch
            {
                return 0; // التراكر واقع أو تايم أوت
            }
        }

        // Helper عشان التايم أوت مع UDP في C# رخم شوية
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
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }
    }
}