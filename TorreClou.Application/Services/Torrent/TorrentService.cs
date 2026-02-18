using Microsoft.Extensions.Configuration;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Entities;
using TorreClou.Core.Exceptions;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Specifications;
using RequestedFile = TorreClou.Core.Entities.Torrents.RequestedFile;

namespace TorreClou.Application.Services.Torrent
{
    public class TorrentService(
        IUnitOfWork unitOfWork,
        ITrackerScraper trackerScraper,
        ITorrentHealthService torrentHealthService,
        IConfiguration configuration) : ITorrentService
    {
        private readonly string _downloadPath = configuration["TORRENT_DOWNLOAD_PATH"] ?? "/app/downloads";

        private static readonly string[] FallbackTrackers =
        [
            "udp://tracker.openbittorrent.com:80",
            "udp://tracker.opentrackr.org:1337/announce",
            "udp://tracker.coppersurfer.tk:6969/announce",
            "udp://exodus.desync.com:6969/announce",
            "udp://open.stealth.si:80/announce",
            "udp://tracker.torrent.eu.org:451/announce",
            "udp://open.demonoid.ch:6969/announce",
            "udp://open.demonii.com:1337/announce",
            "udp://explodie.org:6969/announce",
            "udp://tracker.qu.ax:6969/announce",
            "udp://tracker.dler.org:6969/announce",
            "udp://tracker.0x7c0.com:6969/announce",
            "udp://tracker-udp.gbitt.info:80/announce",
            "udp://run.publictracker.xyz:6969/announce",
            "udp://retracker01-msk-virt.corbina.net:80/announce",
            "udp://p4p.arenabg.com:1337/announce",
            "udp://opentracker.io:6969/announce",
            "udp://leet-tracker.moe:1337/announce",
            "udp://bt.bontal.net:6969/announce",
            "udp://bittorrent-tracker.e-n-c-r-y-p-t.net:1337/announce",
            "udp://6ahddutb1ucc3cp.ru:6969/announce",
            "https://tracker.yemekyedim.com:443/announce"
        ];

        public TorrentInfoDto ParseTorrentFile(Stream fileStream)
        {
            if (fileStream.CanSeek && fileStream.Position != 0) fileStream.Position = 0;

            var torrent = MonoTorrent.Torrent.Load(fileStream);

            var trackers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (torrent.AnnounceUrls != null)
            {
                foreach (var tier in torrent.AnnounceUrls)
                    foreach (var url in tier)
                        if (!string.IsNullOrWhiteSpace(url)) trackers.Add(url);
            }

            var udpTrackers = trackers
                .Where(t => t.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var hash = torrent.InfoHashes.V1?.ToHex();
            if (string.IsNullOrEmpty(hash))
                throw new ValidationException("V2OnlyNotSupported", "This torrent has no v1 (SHA1) hash. System currently requires v1 support.");

            if (udpTrackers.Count == 0)
                udpTrackers.AddRange(FallbackTrackers);

            return new TorrentInfoDto
            {
                Name = torrent.Name,
                InfoHash = hash,
                TotalSize = torrent.Size,
                Trackers = udpTrackers,
                Files = [.. torrent.Files.Select((f, index) => new TorrentFileDto
                {
                    Index = index,
                    Path = f.Path,
                    Size = f.Length
                })]
            };
        }

        public async Task<TorrentInfoDto> EnrichWithHealthAsync(TorrentInfoDto torrentInfo)
        {
            var scrape = await trackerScraper.GetScrapeResultsAsync(torrentInfo.InfoHash, torrentInfo.Trackers);
            var health = torrentHealthService.Compute(scrape);
            var healthMultiplier = 1 + (1 - health.HealthScore);

            return torrentInfo with
            {
                HealthScore = health.HealthScore,
                HealthMultiplier = healthMultiplier,
                Health = health,
                ScrapeResult = scrape
            };
        }

        public async Task<TorrentInfoDto> GetTorrentInfoFromTorrentFileAsync(Stream fileStream)
        {
            var torrentInfo = ParseTorrentFile(fileStream);
            return await EnrichWithHealthAsync(torrentInfo);
        }

        public async Task<RequestedFile> FindOrCreateTorrentFile(TorrentInfoDto torrent, int userId, Stream? fileStream = null)
        {
            var user = await unitOfWork.Repository<User>().GetByIdAsync(userId);
            if (user == null)
                throw new NotFoundException("UserNotFound", $"User with ID {userId} does not exist. Cannot create RequestedFile.");

            if (string.IsNullOrWhiteSpace(torrent.InfoHash))
                throw new ValidationException("InvalidInfoHash", "InfoHash is required and cannot be empty. Cannot create or find RequestedFile.");

            if (string.IsNullOrWhiteSpace(torrent.Name))
                throw new ValidationException("InvalidFileName", "FileName is required and cannot be empty. Cannot create RequestedFile.");

            if (torrent.TotalSize <= 0)
                throw new ValidationException("InvalidFileSize", "FileSize must be greater than zero. Cannot create RequestedFile.");

            var searchCriteria = new BaseSpecification<RequestedFile>(t =>
                t.InfoHash == torrent.InfoHash &&
                t.UploadedByUserId == userId);

            var existingTorrent = await unitOfWork.Repository<RequestedFile>().GetEntityWithSpec(searchCriteria);

            if (existingTorrent != null)
                return existingTorrent;

            string? localPath = null;
            if (fileStream != null)
            {
                var torrentsDir = Path.Combine(_downloadPath, "torrents");
                Directory.CreateDirectory(torrentsDir);

                var fileName = $"{torrent.InfoHash}.torrent";
                localPath = Path.Combine(torrentsDir, fileName);

                if (fileStream.CanSeek)
                    fileStream.Position = 0;

                await using var fs = File.Create(localPath);
                await fileStream.CopyToAsync(fs);
            }

            var newTorrentFile = new RequestedFile
            {
                InfoHash = torrent.InfoHash,
                FileName = torrent.Name,
                FileSize = torrent.TotalSize,
                Files = torrent.Files.Select(f => f.Path).ToArray(),
                UploadedByUserId = userId,
                FileType = "Torrent",
                DirectUrl = localPath
            };

            unitOfWork.Repository<RequestedFile>().Add(newTorrentFile);
            await unitOfWork.Complete();

            return newTorrentFile;
        }
    }
}
