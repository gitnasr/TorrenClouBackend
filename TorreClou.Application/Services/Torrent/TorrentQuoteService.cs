using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Models.Pricing;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services.Torrent
{
    public class TorrentQuoteService(
        IUnitOfWork unitOfWork,
        ITorrentService torrentService,
        IQuotePricingService quotePricingService,
        IStorageProfilesService storageProfilesService,
        IUserService userService
        ) : ITorrentQuoteService 
    {
        public async Task<Result<QuoteResponseDto>> GenerateQuoteAsync(QuoteRequestDto request, int userId, Stream torrentFile)
        {
            //0. Check even if the user has active storage profile
            var IsActiveStorageProfile = await storageProfilesService.ValidateActiveStorageProfileByUserId(userId, request.StorageProfileId);

            if (!IsActiveStorageProfile.IsSuccess)
                return Result<QuoteResponseDto>.Failure(IsActiveStorageProfile.Error);
             
            // 1. Validate & Parse Torrent
            var torrentFileValidated = ValidateTorrentFile(torrentFile, request.TorrentFile.FileName);
            if (!torrentFileValidated.IsSuccess)
                return Result<QuoteResponseDto>.Failure(torrentFileValidated.Error);

            var torrentInfoResult = await torrentService.GetTorrentInfoFromTorrentFileAsync(torrentFileValidated.Value);
            if (!torrentInfoResult.IsSuccess || string.IsNullOrEmpty(torrentInfoResult.Value.InfoHash))
                return Result<QuoteResponseDto>.Failure("Failed to parse torrent info.");

            var torrentInfo = torrentInfoResult.Value;

            // 2. Calculate Target Storage Size
            var totalSizeResult = CalculateStorage(request.SelectedFilePaths, torrentInfo);
            if (!totalSizeResult.IsSuccess)
                return Result<QuoteResponseDto>.Failure(totalSizeResult.Error);

            long totalSizeInBytes = totalSizeResult.Value;

            // 3. Get User for Regional Pricing
            var user = await userService.GetActiveUserById(userId);
            if (user == null)
                return Result<QuoteResponseDto>.Failure("USER_NOT_FOUND", "User not found.");


           

            var torrentStoredResult = await torrentService.FindOrCreateTorrentFile(torrentInfo,userId, torrentFile);

            if (torrentStoredResult.IsFailure)
                return Result<QuoteResponseDto>.Failure("Failed to save torrent information.");

            // 6. Generate or Reuse Invoice (Single Source of Truth for Pricing)
            var pricingRequest = new QuotePricingRequest
            {
                UserId = userId,
                Region = user.Region,
                SizeInBytes = totalSizeInBytes,
                HealthMultiplier = torrentInfo.HealthMultiplier,
                IsCacheHit = false, // You might want to check if this torrent exists in S3 here for true CacheHit logic
                SelectedFilePaths = request.SelectedFilePaths,
                VoucherCode = request.VoucherCode,
                TorrentFile = torrentStoredResult.Value,
                InfoHash = torrentInfo.InfoHash
            };

            var pricingResult = await quotePricingService.GenerateOrReuseInvoiceAsync(pricingRequest);
            if (pricingResult.IsFailure)
                return Result<QuoteResponseDto>.Failure(pricingResult.Error);

            var quotePricing = pricingResult.Value;
            var invoice = quotePricing.Invoice;
            var snapshot = quotePricing.Snapshot;

            // Ensure TorrentFile is loaded
            if (invoice.TorrentFile == null)
            {
                return Result<QuoteResponseDto>.Failure("TORRENT_FILE_NOT_LOADED", "Torrent file information is missing from invoice.");
            }

            // 7. Return Response
            return Result.Success(new QuoteResponseDto
            {
                IsReadyToDownload = true,
                OriginalAmountInUSD = invoice.OriginalAmountInUSD,
                FinalAmountInUSD = invoice.FinalAmountInUSD,
                FinalAmountInNCurrency = invoice.FinalAmountInNCurrency,
                FileName = invoice.TorrentFile.FileName,
                SizeInBytes = snapshot.TotalSizeInBytes,
                IsCached = snapshot.IsCacheHit,
                InfoHash = invoice.TorrentFile.InfoHash,
                PricingDetails = snapshot,
                InvoiceId = invoice.Id,
                TorrentHealth = torrentInfo.Health
            });
        }

        public async Task<Result<Invoice>> FindInvoiceByTorrentAndUserId(string infoHash, int userId)
        {
            var spec = new ActiveInvoiceByTorrentAndUserSpec(infoHash, userId);
            var quote = await unitOfWork.Repository<Invoice>().GetEntityWithSpec(spec);

            if (quote == null)
                return Result<Invoice>.Failure("QUOTE_NOT_FOUND", "No active quote found.");

            if (quote.IsExpired)
                return Result<Invoice>.Failure("QUOTE_EXPIRED", "The quote has expired.");

            return Result.Success(quote);
        }

        // --- Helpers ---

        private Result<Stream> ValidateTorrentFile(Stream torrentFile, string torrentFileName)
        {
            if (torrentFile == null || torrentFile.Length == 0)
                return Result<Stream>.Failure("No torrent file provided.");

            var fileExtension = Path.GetExtension(torrentFileName).ToLowerInvariant();
            if (fileExtension != ".torrent")
                return Result<Stream>.Failure("Invalid file format. Only .torrent files are accepted.");

            return Result<Stream>.Success(torrentFile);
        }

        private Result<long> CalculateStorage(List<string> selectedFIlePathes, TorrentInfoDto torrentInfo)
        {
            if (torrentInfo.TotalSize == 0)
                return Result.Failure<long>("Torrent total size is zero.");

            if (selectedFIlePathes == null )
            {
                return Result.Success(torrentInfo.TotalSize);
            }

            var selectedSet = new HashSet<string>(selectedFIlePathes);
            long targetSize = torrentInfo.Files
                .Where(f => IsFileSelected(f.Path, selectedSet))
                .Sum(f => f.Size);

            if (targetSize == 0)
                return Result.Failure<long>("Selected files resulted in 0 bytes size.");

            return Result.Success(targetSize);
        }

        /// <summary>
        /// Checks if a file path should be included based on selected paths.
        /// Handles both direct file selections (exact match) and folder selections (path starts with folder + "/").
        /// </summary>
        /// <param name="filePath">The file path to check (from MonoTorrent, uses forward slashes)</param>
        /// <param name="selectedPaths">Set of selected file/folder paths</param>
        /// <returns>True if the file should be included, false otherwise</returns>
        private static bool IsFileSelected(string filePath, HashSet<string> selectedPaths)
        {
            if (string.IsNullOrEmpty(filePath) || selectedPaths == null || selectedPaths.Count == 0)
                return false;

            // Normalize file path to use forward slashes (MonoTorrent standard)
            var normalizedFilePath = filePath.Replace('\\', '/');

            // Check for exact match first (direct file selection)
            if (selectedPaths.Contains(normalizedFilePath))
                return true;

            // Check if file is inside any selected folder
            foreach (var selectedPath in selectedPaths)
            {
                if (string.IsNullOrEmpty(selectedPath))
                    continue;

                // Normalize selected path to use forward slashes
                var normalizedSelectedPath = selectedPath.Replace('\\', '/').TrimEnd('/');

                // Skip if selected path is empty after normalization
                if (string.IsNullOrEmpty(normalizedSelectedPath))
                    continue;

                // Check if file path starts with folder path + "/"
                // This handles folder selections like "Screens" matching "Screens/file.jpg"
                if (normalizedFilePath.StartsWith(normalizedSelectedPath + "/", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}