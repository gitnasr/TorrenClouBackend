using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Entities.Torrents;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface ITorrentService
    {
        Task<Result<TorrentInfoDto>> GetTorrentInfoFromTorrentFileAsync(Stream fileStream);
        Result<TorrentInfoDto> ParseTorrentFile(Stream fileStream);
        Task<Result<TorrentInfoDto>> EnrichWithHealthAsync(TorrentInfoDto torrentInfo);
        Task<Result<RequestedFile>> FindOrCreateTorrentFile(TorrentInfoDto torrent, int userId, Stream? fileStream = null);
    }
}
