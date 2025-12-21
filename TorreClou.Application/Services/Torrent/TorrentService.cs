using MonoTorrent;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;
using RequestedFile = TorreClou.Core.Entities.Torrents.RequestedFile;

namespace TorreClou.Application.Services.Torrent
{
    public class TorrentService(
        IUnitOfWork unitOfWork,
        ITrackerScraper trackerScraper,
        IBlobStorageService blobStorageService) : ITorrentService
    {
        // Ideally move this to configuration
        private static readonly string[] FallbackTrackers =
        [
            "udp://tracker.openbittorrent.com:80",
            "udp://tracker.opentrackr.org:1337/announce",
            "udp://tracker.coppersurfer.tk:6969/announce",
            "udp://exodus.desync.com:6969/announce",
            "udp://open.stealth.si:80/announce",
            "udp://tracker.torrent.eu.org:451/announce"
        ];

        public async Task<Result<TorrentInfoDto>> GetTorrentInfoFromTorrentFileAsync(Stream fileStream)
        {
            try
            {
                // Ensure stream is at start
                if (fileStream.CanSeek && fileStream.Position != 0) fileStream.Position = 0;

                var torrent = MonoTorrent.Torrent.Load(fileStream);

                // 1. Extract Trackers (Clean LINQ)
                var trackers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (torrent.AnnounceUrls != null)
                {
                    foreach (var tier in torrent.AnnounceUrls)
                    {
                        foreach (var url in tier)
                        {
                            if (!string.IsNullOrWhiteSpace(url)) trackers.Add(url);
                        }
                    }
                }

                // Filter for UDP only (for scraper compatibility)
                var udpTrackers = trackers
                    .Where(t => t.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // 2. Validate Hash
                var hash = torrent.InfoHashes.V1?.ToHex();
                if (string.IsNullOrEmpty(hash))
                {
                    return Result<TorrentInfoDto>.Failure("V2_ONLY_NOT_SUPPORTED",
                        "This torrent has no v1 (SHA1) hash. System currently requires v1 support.");
                }

                // 3. Fallback Trackers
                if (udpTrackers.Count == 0)
                {
                    udpTrackers.AddRange(FallbackTrackers);
                }

                // 4. Scrape Health (Ideally with short timeout)
                // We pass the hash and list to the scraper service
                var scrape = await trackerScraper.GetScrapeResultsAsync(hash, udpTrackers);

                // 5. Build DTO
                var dto = new TorrentInfoDto
                {
                    Name = torrent.Name,
                    InfoHash = hash,
                    TotalSize = torrent.Size,
                    Trackers = udpTrackers,
                    Files = torrent.Files.Select((f, index) => new TorrentFileDto
                    {
                        Index = index,
                        Path = f.Path,
                        Size = f.Length
                    }).ToList(),
                    ScrapeResult = scrape
                };

                return Result.Success(dto);
            }
            catch (Exception ex)
            {
                return Result<TorrentInfoDto>.Failure("INVALID_TORRENT", $"Failed to parse torrent file: {ex.Message}");
            }
        }

        public async Task<Result<RequestedFile>> FindOrCreateTorrentFile(RequestedFile torrent, Stream? fileStream = null)
        {
            // Check if this specific user has already uploaded this torrent
            var searchCriteria = new BaseSpecification<RequestedFile>(t =>
                t.InfoHash == torrent.InfoHash &&
                t.UploadedByUserId == torrent.UploadedByUserId);

            var existingTorrent = await unitOfWork.Repository<RequestedFile>().GetEntityWithSpec(searchCriteria);

            // LOGIC BRANCH 1: Entity Exists
            if (existingTorrent != null)
            {
                // If DirectUrl is missing but we have a stream, repair it (Upload)
                if (string.IsNullOrEmpty(existingTorrent.DirectUrl) && fileStream != null)
                {
                    var uploadResult = await UploadTorrentBlobAsync(fileStream, existingTorrent.InfoHash);
                    if (uploadResult.IsSuccess)
                    {
                        existingTorrent.DirectUrl = uploadResult.Value;
                        await unitOfWork.Complete();
                    }
                }

                return Result.Success(existingTorrent);
            }

            // LOGIC BRANCH 2: New Entity
            // Upload to blob storage if stream provided
            if (fileStream != null)
            {
                var uploadResult = await UploadTorrentBlobAsync(fileStream, torrent.InfoHash);
                if (uploadResult.IsSuccess)
                {
                    torrent.DirectUrl = uploadResult.Value;
                }
               
            }

            unitOfWork.Repository<RequestedFile>().Add(torrent);
            await unitOfWork.Complete();

            return Result.Success(torrent);
        }

        private async Task<Result<string>> UploadTorrentBlobAsync(Stream fileStream, string infoHash)
        {
            if (fileStream.CanSeek) fileStream.Position = 0;

            return await blobStorageService.UploadAsync(
                fileStream,
                $"{infoHash}.torrent",
                "application/x-bittorrent"
            );
        }
    }
}