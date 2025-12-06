using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Shared; // Result Pattern

namespace TorreClou.Core.Interfaces
{
    public interface ITorrentParser
    {
        Result<TorrentInfoDto> ParseTorrentFile(Stream fileStream);

    }
}