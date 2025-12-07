using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Entities.Torrents;
using TorreClou.Core.Shared;

public interface ITorrentService
{
    Result<TorrentAnalysisDto> AnalyzeFile(Stream fileStream);
    Task<Result<TorrentFile>> FindOrCreateTorrentFile(TorrentFile torrent);
}
