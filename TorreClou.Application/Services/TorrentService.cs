using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;

public class TorrentService(ITorrentParser parser) : ITorrentService
{


    public Result<TorrentAnalysisDto> AnalyzeFile(Stream fileStream)
    {
        var result = parser.ParseTorrentFile(fileStream);
        if (!result.IsSuccess) return Result<TorrentAnalysisDto>.Failure(result.Error);

        return Result.Success(MapToAnalysisDto(result.Value));
    }

    private static TorrentAnalysisDto MapToAnalysisDto(TorrentInfoDto info)
    {
        return new TorrentAnalysisDto
        {
            InfoHash = info.InfoHash,
            Name = info.Name,
            Files = info.Files 
        };
    }
}