using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Exceptions;
using TorreClou.Core.Interfaces;

namespace TorreClou.Application.Services.Torrent
{
    public class TorrentAnalysisService(
        ITorrentService torrentService) : ITorrentAnalysisService
    {
        public async Task<TorrentAnalysisResponseDto> AnalyzeTorrentAsync(AnalyzeTorrentRequestDto request, int userId, Stream torrentFile)
        {
            // 1. Validate file
            if (torrentFile == null || torrentFile.Length == 0)
                throw new ValidationException("Invalid", "No torrent file provided.");

            var fileExtension = Path.GetExtension(request.TorrentFile.FileName).ToLowerInvariant();
            if (fileExtension != ".torrent")
                throw new ValidationException("InvalidTorrent", "Invalid file format. Only .torrent files are accepted.");

            // 2. Parse & enrich torrent info (throws ValidationException on bad torrent)
            var torrentInfo = await torrentService.GetTorrentInfoFromTorrentFileAsync(torrentFile);

            if (string.IsNullOrEmpty(torrentInfo.InfoHash))
                throw new ValidationException("InvalidTorrent", "Failed to parse torrent info.");

            // 3. Store torrent file
            torrentFile.Position = 0;
            var torrentStored = await torrentService.FindOrCreateTorrentFile(torrentInfo, userId, torrentFile);

            // 4. Return analysis response
            return new TorrentAnalysisResponseDto
            {
                TorrentFileId = torrentStored.Id,
                FileName = torrentStored.FileName,
                InfoHash = torrentStored.InfoHash,
                TotalSizeInBytes = torrentInfo.TotalSize,
                Files = torrentInfo.Files,
                TorrentHealth = torrentInfo.Health,
            };
        }
    }
}
