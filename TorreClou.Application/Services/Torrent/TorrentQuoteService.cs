using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Marketing;
using TorreClou.Core.Entities.Torrents;
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
        ITorrentHealthService torrentHealthService) : ITorrentQuoteService 
    {
        public async Task<Result<QuoteResponseDto>> GenerateQuoteAsync(QuoteRequestDto request, int userId, Stream torrentFile)
        {
            // 1. Validate & Parse Torrent
            var torrentFileValidated = ValidateTorrentFile(torrentFile, request.TorrentFile.FileName);
            if (!torrentFileValidated.IsSuccess)
                return Result<QuoteResponseDto>.Failure(torrentFileValidated.Error);

            var torrentInfoResult = await torrentService.GetTorrentInfoFromTorrentFileAsync(torrentFileValidated.Value);
            if (!torrentInfoResult.IsSuccess || string.IsNullOrEmpty(torrentInfoResult.Value.InfoHash))
                return Result<QuoteResponseDto>.Failure("Failed to parse torrent info.");

            var torrentInfo = torrentInfoResult.Value;

            // 2. Calculate Target Storage Size
            var totalSizeResult = CalculateStorage(request.SelectedFileIndices, torrentInfo);
            if (!totalSizeResult.IsSuccess)
                return Result<QuoteResponseDto>.Failure(totalSizeResult.Error);

            long totalSizeInBytes = totalSizeResult.Value;

            // 3. Get User for Regional Pricing
            var user = await unitOfWork.Repository<User>().GetByIdAsync(userId);
            if (user == null)
                return Result<QuoteResponseDto>.Failure("USER_NOT_FOUND", "User not found.");

            // 4. Calculate Health Metrics
            // Higher health = Lower price multiplier. (Score 1.0 = 1x, Score 0.0 = 2x)
            var health = torrentHealthService.Compute(torrentInfoResult.Value.ScrapeResult);
            var healthMultiplier = 1 + (1 - health.HealthScore);

            // 5. Persist Torrent File (Optimized)
            // Ensure stream is ready for reading
            if (torrentFile.CanSeek) torrentFile.Position = 0;

            var torrentInDbResult = await torrentService.FindOrCreateTorrentFile(new RequestedFile
            {
                InfoHash = torrentInfo.InfoHash,
                FileName = torrentInfo.Name,
                FileSize = torrentInfo.TotalSize,
                Files = torrentInfo.Files.Select(f => f.Path).ToArray(),
                UploadedByUserId = userId,
                FileType = "Torrent"
            }, torrentFile);

            if (torrentInDbResult.IsFailure)
                return Result<QuoteResponseDto>.Failure("Failed to save torrent information.");

            // 6. Generate or Reuse Invoice (Single Source of Truth for Pricing)
            var pricingRequest = new QuotePricingRequest
            {
                UserId = userId,
                Region = user.Region,
                SizeInBytes = totalSizeInBytes,
                HealthMultiplier = healthMultiplier,
                IsCacheHit = false, // You might want to check if this torrent exists in S3 here for true CacheHit logic
                SelectedFiles = request.SelectedFileIndices?.ToList() ?? new List<int>(),
                VoucherCode = request.VoucherCode,
                TorrentFile = torrentInDbResult.Value,
                InfoHash = torrentInfo.InfoHash
            };

            var pricingResult = await quotePricingService.GenerateOrReuseInvoiceAsync(pricingRequest);
            if (pricingResult.IsFailure)
                return Result<QuoteResponseDto>.Failure(pricingResult.Error);

            var quotePricing = pricingResult.Value;
            var invoice = quotePricing.Invoice;
            var snapshot = quotePricing.Snapshot;

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
                TorrentHealth = health
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

        private Result<long> CalculateStorage(List<int>? selectedFileIndices, TorrentInfoDto torrentInfo)
        {
            if (torrentInfo.TotalSize == 0)
                return Result.Failure<long>("Torrent total size is zero.");

            if (selectedFileIndices == null || !selectedFileIndices.Any())
            {
                return Result.Success(torrentInfo.TotalSize);
            }

            long targetSize = torrentInfo.Files
                .Where(f => selectedFileIndices.Contains(f.Index))
                .Sum(f => f.Size);

            if (targetSize == 0)
                return Result.Failure<long>("Selected files resulted in 0 bytes size.");

            return Result.Success(targetSize);
        }
    }
}