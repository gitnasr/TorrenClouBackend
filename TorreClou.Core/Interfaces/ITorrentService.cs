using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Shared;

public interface ITorrentService
{
    Result<TorrentAnalysisDto> AnalyzeFile(Stream fileStream);
}
