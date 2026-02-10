using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services.Torrent
{
    public class TorrentAnalysisService(
        ITorrentService torrentService,
        IStorageProfilesService storageProfilesService) : ITorrentAnalysisService
    {
        public async Task<Result<QuoteResponseDto>> AnalyzeTorrentAsync(AnalyzeTorrentRequestDto request, int userId, Stream torrentFile)
        {
            //0. Check if user has active storage profile
            var IsActiveStorageProfile = await storageProfilesService.ValidateActiveStorageProfileByUserId(userId, request.StorageProfileId);

            if (!IsActiveStorageProfile.IsSuccess)
                return Result<QuoteResponseDto>.Failure(IsActiveStorageProfile.Error);

           // 1. Validate & Parse Torrent
            var torrentFileValidated = ValidateTorrentFile(torrentFile, request.TorrentFile.FileName);
            if (!torrentFileValidated.IsSuccess)
                return Result<QuoteResponseDto>.Failure(torrentFileValidated.Error);

            var torrentInfoResult = await torrentService.GetTorrentInfoFromTorrentFileAsync(torrentFileValidated.Value);
            if (!torrentInfoResult.IsSuccess || string.IsNullOrEmpty(torrentInfoResult.Value.InfoHash))
                return Result<QuoteResponseDto>.Failure(ErrorCode.InvalidTorrent, "Failed to parse torrent info.");

            var torrentInfo = torrentInfoResult.Value;

            // 2. Calculate Target Storage Size
            var totalSizeResult = CalculateStorage(request.SelectedFilePaths, torrentInfo);
            if (!totalSizeResult.IsSuccess)
                return Result<QuoteResponseDto>.Failure(totalSizeResult.Error);

            long totalSizeInBytes = totalSizeResult.Value;

            // 3. Store torrent file
            // Reset stream position after it was consumed by GetTorrentInfoFromTorrentFileAsync
            torrentFile.Position = 0;
            var torrentStoredResult = await torrentService.FindOrCreateTorrentFile(torrentInfo, userId, torrentFile);

            if (torrentStoredResult.IsFailure)
                return Result<QuoteResponseDto>.Failure(ErrorCode.InvalidTorrent, "Failed to save torrent information.");

            // 4. Return Response (no pricing, no invoice)
            return Result.Success(new QuoteResponseDto
            {
                FileName = torrentStoredResult.Value.FileName,
                SizeInBytes = totalSizeInBytes,
                InfoHash = torrentStoredResult.Value.InfoHash,
                TorrentHealth = torrentInfo.Health,
                TorrentFileId = torrentStoredResult.Value.Id,
                SelectedFiles = request.SelectedFilePaths?.ToArray() ?? []
            });
        }


        // --- Helpers ---

        private Result<Stream> ValidateTorrentFile(Stream torrentFile, string torrentFileName)
        {
            if (torrentFile == null || torrentFile.Length == 0)
                return Result<Stream>.Failure(ErrorCode.Invalid, "No torrent file provided.");

            var fileExtension = Path.GetExtension(torrentFileName).ToLowerInvariant();
            if (fileExtension != ".torrent")
                return Result<Stream>.Failure(ErrorCode.InvalidTorrent, "Invalid file format. Only .torrent files are accepted.");

            return Result<Stream>.Success(torrentFile);
        }

        private Result<long> CalculateStorage(List<string> selectedFilePaths, TorrentInfoDto torrentInfo)
        {
            if (torrentInfo.TotalSize == 0)
                return Result<long>.Failure(ErrorCode.Invalid, "Torrent total size is zero.");

            if (selectedFilePaths == null )
            {
                return Result.Success(torrentInfo.TotalSize);
            }

            long targetSize = torrentInfo.Files
                .Where(f => IsFileSelected(f.Path, selectedFilePaths))
                .Sum(f => f.Size);

            if (targetSize == 0)
                return Result<long>.Failure(ErrorCode.Invalid, "Selected files resulted in 0 bytes size.");

            return Result.Success(targetSize);
        }

        /// <summary>
        /// Checks if a file should be selected based on the selected paths.
        /// Returns true if the file path exactly matches any selected path,
        /// or if the file is inside a selected folder.
        /// </summary>
        private static bool IsFileSelected(string filePath, List<string> selectedPaths)
        {
            // Normalize path separators for cross-platform compatibility
            var normalizedFile = filePath.Replace('\\', '/');

            foreach (var selectedPath in selectedPaths)
            {
                var normalizedSelected = selectedPath.Replace('\\', '/');

                // Exact match (file directly selected)
                if (string.Equals(normalizedFile, normalizedSelected, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Check if file is inside a selected folder (folder path + separator)
                if (normalizedFile.StartsWith(normalizedSelected + "/", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
