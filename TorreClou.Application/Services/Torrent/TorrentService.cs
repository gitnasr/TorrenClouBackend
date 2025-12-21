using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Entities;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;
using RequestedFile = TorreClou.Core.Entities.Torrents.RequestedFile;

namespace TorreClou.Application.Services.Torrent
{
    public class TorrentService(
        IUnitOfWork unitOfWork,
        ITrackerScraper trackerScraper,
        IBlobStorageService blobStorageService, ITorrentHealthService torrentHealthService) : ITorrentService
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
                var health = torrentHealthService.Compute(scrape);
                var healthMultiplier = 1 + (1 - health.HealthScore);
                // 5. Build DTO
                var dto = new TorrentInfoDto
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
                    })],
                    HealthScore = health.HealthScore,
                    HealthMultiplier = healthMultiplier,
                    Health = health,
                    ScrapeResult = scrape
                };

                return Result.Success(dto);
            }
            catch (Exception ex)
            {
                return Result<TorrentInfoDto>.Failure("INVALID_TORRENT", $"Failed to parse torrent file: {ex.Message}");
            }
        }

        public async Task<Result<RequestedFile>> FindOrCreateTorrentFile(TorrentInfoDto torrent, int userId, Stream? fileStream = null)
        {
            // Validate that the user exists before proceeding
            var user = await unitOfWork.Repository<User>().GetByIdAsync(userId);
            if (user == null)
            {
                return Result<RequestedFile>.Failure("USER_NOT_FOUND", 
                    $"User with ID {userId} does not exist. Cannot create RequestedFile.");
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(torrent.InfoHash))
            {
                return Result<RequestedFile>.Failure("INVALID_INFOHASH", 
                    "InfoHash is required and cannot be empty. Cannot create or find RequestedFile.");
            }

            if (string.IsNullOrWhiteSpace(torrent.Name))
            {
                return Result<RequestedFile>.Failure("INVALID_FILENAME", 
                    "FileName is required and cannot be empty. Cannot create RequestedFile.");
            }

            if (torrent.TotalSize <= 0)
            {
                return Result<RequestedFile>.Failure("INVALID_FILESIZE", 
                    "FileSize must be greater than zero. Cannot create RequestedFile.");
            }

            // Check if this specific user has already uploaded this torrent
            var searchCriteria = new BaseSpecification<RequestedFile>(t =>
                t.InfoHash == torrent.InfoHash &&
                t.UploadedByUserId == userId);

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
            // Note: We only set UploadedByUserId (foreign key), not the navigation property
            // EF Core will use the foreign key value to establish the relationship

            // Upload to blob storage if stream provided
            var newTorrentFile = new RequestedFile
            {
                InfoHash = torrent.InfoHash,
                FileName = torrent.Name,
                FileSize = torrent.TotalSize,
                Files = torrent.Files.Select(f => f.Path).ToArray(),
                UploadedByUserId = userId,
                FileType = "Torrent",
                
            };
            if (fileStream != null)
            {
                var uploadResult = await UploadTorrentBlobAsync(fileStream, torrent.InfoHash);
                if (!uploadResult.IsSuccess)
                {
                    return Result<RequestedFile>.Failure("UPLOAD_FAILED", 
                        $"Failed to upload torrent file to blob storage: {uploadResult.Error?.Message ?? "Unknown error"}. Cannot create RequestedFile.");
                }

                newTorrentFile.DirectUrl = uploadResult.Value;
            }

            unitOfWork.Repository<RequestedFile>().Add(newTorrentFile);
            await unitOfWork.Complete();

            return Result.Success(newTorrentFile);
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