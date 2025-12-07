using System.Threading.Tasks;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Entities.Torrents;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

public class TorrentService(ITorrentParser parser, IUnitOfWork unitOfWork) : ITorrentService
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