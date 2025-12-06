using MonoTorrent;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;

namespace TorreClou.Application.Services
{
    public class TorrentParserService : ITorrentParser
    {
        public Result<TorrentInfoDto> ParseTorrentFile(Stream fileStream)
        {
            try
            {
                var torrent = Torrent.Load(fileStream);

                var dto = new TorrentInfoDto
                {
                    Name = torrent.Name,
                    InfoHash = torrent.InfoHashes.V1OrV2.ToHex(), 
                    TotalSize = torrent.Size,
                    IsMagnet = false,
                    Files = torrent.Files.Select((f, index) => new TorrentFileDto 
                    {
                        Index = index, // ده الرقم اللي اليوزر هيبعتهولنا
                        Path = f.Path,
                        Size = f.Length
                    }).ToList()
                };

                return Result.Success(dto);
            }
            catch (Exception ex)
            {
                return Result<TorrentInfoDto>.Failure($"Corrupted torrent file: {ex.Message}");
            }
        }

    }
}