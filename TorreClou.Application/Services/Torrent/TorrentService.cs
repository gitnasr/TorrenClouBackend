using MonoTorrent;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Entities.Torrents;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;
using TorrentFile = TorreClou.Core.Entities.Torrents.TorrentFile;

namespace TorreClou.Application.Services.Torrent
{
    public class TorrentService(IUnitOfWork unitOfWork, ITrackerScraper trackerScraper) : ITorrentService
    {

        public async Task<Result<TorrentInfoDto>> GetTorrentInfoFromTorrentFileAsync(Stream fileStream)
        {
            try
            {
                var torrent = MonoTorrent.Torrent.Load(fileStream);

                // ----- Extract Trackers -----
                var trackers = new List<string>();

                // 2) announce-list (tiers)
                if (torrent.AnnounceUrls != null)
                {
                    var list = torrent.AnnounceUrls
                        .SelectMany(tier => tier)
                        .Where(url => !string.IsNullOrWhiteSpace(url))
                        .Distinct()
                        .ToList();

                    trackers.AddRange(list);
                }

                trackers = trackers
                    .Where(t => t.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
                    .Distinct()
                    .ToList();


                string? hash = torrent.InfoHashes.V1?.ToHex();

                if (string.IsNullOrEmpty(hash))
                {
                    // v2-only torrents cannot be scraped by UDP trackers.
                    return Result<TorrentInfoDto>.Failure(
                        "This torrent has no v1 (SHA1) hash. UDP trackers cannot scrape v2-only torrents."
                    );
                }

                // If no trackers found, fallback to public trackers
                if (trackers.Count == 0)
                {
                    trackers.AddRange(new[]
                    {
                        "udp://tracker.openbittorrent.com:80",
                        "udp://tracker.opentrackr.org:1337/announce",
                        "udp://tracker.coppersurfer.tk:6969/announce",
                        "udp://exodus.desync.com:6969/announce"
                    });
                }
                var scrape = await trackerScraper.GetScrapeResultsAsync(
              hash,
              trackers
          );
                // ----- Build DTO -----
                var dto = new TorrentInfoDto
                {
                    Name = torrent.Name,
                    InfoHash = hash,
                    TotalSize = torrent.Size,
                    Trackers = trackers,
                    Files = torrent.Files.Select((f, index) => new TorrentFileDto
                    {
                        Index = index,
                        Path = f.Path,
                        Size = f.Length
                    }).ToList(),
                    ScrapeResult =scrape
                  
                };

                return Result.Success(dto);
            }
            catch (Exception ex)
            {
                return Result<TorrentInfoDto>.Failure($"Corrupted torrent file: {ex.Message}");
            }
        }


        public async Task<Result<TorrentFile>> FindOrCreateTorrentFile(TorrentFile torrent)
        {

            var searchCriteria = new BaseSpecification<TorrentFile>(t => t.InfoHash == torrent.InfoHash && t.UploadedByUserId == torrent.UploadedByUserId);


            var existingTorrent = await unitOfWork.Repository<TorrentFile>().GetEntityWithSpec(searchCriteria);

            if (existingTorrent != null)
            {
                return Result.Success(existingTorrent);
            }

            unitOfWork.Repository<TorrentFile>().Add(torrent);
            await unitOfWork.Complete();

            return Result.Success(torrent);
        }
    }
}