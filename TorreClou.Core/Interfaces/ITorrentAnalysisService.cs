using TorreClou.Core.DTOs.Torrents;

namespace TorreClou.Core.Interfaces
{
    public interface ITorrentAnalysisService
    {
        Task<TorrentAnalysisResponseDto> AnalyzeTorrentAsync(
            AnalyzeTorrentRequestDto request,
            int userId,
            Stream torrentFile);
    }
}
