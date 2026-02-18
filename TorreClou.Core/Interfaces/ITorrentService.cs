using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Entities.Torrents;

namespace TorreClou.Core.Interfaces
{
    public interface ITorrentService
    {
        Task<TorrentInfoDto> GetTorrentInfoFromTorrentFileAsync(Stream fileStream);
        TorrentInfoDto ParseTorrentFile(Stream fileStream);
        Task<TorrentInfoDto> EnrichWithHealthAsync(TorrentInfoDto torrentInfo);
        Task<RequestedFile> FindOrCreateTorrentFile(TorrentInfoDto torrent, int userId, Stream? fileStream = null);
    }
}
