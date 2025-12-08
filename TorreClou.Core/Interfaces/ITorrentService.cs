using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Entities.Torrents;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{

    public interface ITorrentService
    {
        Task<Result<TorrentInfoDto>> GetTorrentInfoFromTorrentFileAsync(Stream fileStream);        Task<Result<TorrentFile>> FindOrCreateTorrentFile(TorrentFile torrent);
    }



}
