using System.Text.Json;
using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Marketing;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Models.Pricing;
using TorreClou.Core.Shared;
 
namespace TorreClou.Application.Services.Torrent
{

    

    public class TorrentQuoteService(IUnitOfWork unitOfWork, IPricingEngine pricingEngine, 
        ITorrentService torrentService,
           IQuotePricingService quotePricingService, ITorrentHealthService torrentHealthService) : ITorrentQuoteService
    {
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
            long targetSize;

            if (selectedFileIndices != null && selectedFileIndices.Any())
            {
                if (torrentInfo.TotalSize == 0)
                    return Result.Failure<long>("Can't get the total size");

                targetSize = torrentInfo.Files
                    .Where(f => selectedFileIndices.Contains(f.Index))
                    .Sum(f => f.Size);
            }
            else
            {
                targetSize = torrentInfo.TotalSize;
            }

            if (targetSize == 0)
                return Result.Failure<long>("Cannot calculate size.");

            return Result.Success(targetSize);
        }


        public async Task<Result<Invoice>> FindInvoiceByTorrentAndUserId(string infoHash, int userId)
        {
            var spec = new ActiveInvoiceByTorrentAndUserSpec(infoHash, userId);
            var quote = await unitOfWork.Repository<Invoice>().GetEntityWithSpec(spec);
            if (quote == null)
                return Result<Invoice>.Failure("QUOTE_NOT_FOUND", "No quote found for the given torrent and user.");
            if (quote.IsExpired)
                return Result<Invoice>.Failure("QUOTE_EXPIRED", "The quote has expired.");
            return Result.Success(quote);
        }


        public async Task<Result<QuoteResponseDto>> GenerateQuoteAsync(   QuoteRequestDto request,   int userId,    Stream torrentFile)
        {
            // 1) Validate + Parse
            var torrentFileValidated = ValidateTorrentFile(torrentFile, request.TorrentFile.FileName);
            if (!torrentFileValidated.IsSuccess)
                return Result<QuoteResponseDto>.Failure(torrentFileValidated.Error);

            var torrentInfoResult = await torrentService.GetTorrentInfoFromTorrentFileAsync(torrentFileValidated.Value);
            if (!torrentInfoResult.IsSuccess || string.IsNullOrEmpty(torrentInfoResult.Value.InfoHash))
                return Result<QuoteResponseDto>.Failure("Can't get torrent info.");

            var torrentInfo = torrentInfoResult.Value;

            // 2) Calculate size based on selected files (Bytes)
            var totalSizeResult = CalculateStorage(request.SelectedFileIndices, torrentInfo);
            if (!totalSizeResult.IsSuccess)
                return Result<QuoteResponseDto>.Failure(totalSizeResult.Error);

            long totalSizeInBytes = totalSizeResult.Value;

            var user = await unitOfWork.Repository<User>().GetByIdAsync(userId);


            if (user == null)
                return Result<QuoteResponseDto>.Failure("USER_NOT_FOUND", "User not found.");


          
            var health = torrentHealthService.Compute(torrentInfoResult.Value.ScrapeResult);
            var healthMultiplier = 1 + (1 - health.HealthScore);
            // 3) New snapshot for current request
            var newSnapshot = pricingEngine.CalculatePrice(
                totalSizeInBytes,
                user.Region,
               healthMultiplier
            );

            // توحيد الداتا جوه الـ snapshot
            newSnapshot.SelectedFiles = request.SelectedFileIndices?.ToList() ?? [];
            newSnapshot.TotalSizeInBytes = totalSizeInBytes;


            // 4) Try to reuse existing invoice
            var torrentInDbResult = await torrentService.FindOrCreateTorrentFile(new()
            {
                InfoHash = torrentInfo.InfoHash,
                FileName = torrentInfo.Name,
                FileSize = torrentInfo.TotalSize,
                Files = torrentInfo.Files.Select(f => f.Path).ToArray(),
                UploadedByUserId = userId
            });
            if (torrentInDbResult.IsFailure)
                return Result<QuoteResponseDto>.Failure("Failed to save torrent information.");
            var pricingRequest = new QuotePricingRequest
            {
                UserId = userId,
                Region = user.Region,
                SizeInBytes = totalSizeInBytes,
                HealthMultiplier = (double)health.HealthScore, // أو HealthMultiplier لو عندك
                IsCacheHit = false, 
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



            return Result.Success(new QuoteResponseDto
            {
                IsReadyToDownload = true,
                OriginalAmountInUSD = invoice.OriginalAmountInUSD,
                FinalAmountInUSD = invoice.FinalAmountInUSD,
                FinalAmountInNCurrency = invoice.FinalAmountInNCurrency,
                FileName = invoice.TorrentFile.FileName,
                SizeInBytes = newSnapshot.TotalSizeInBytes,
                IsCached = newSnapshot.IsCacheHit,
                InfoHash = invoice.TorrentFile.InfoHash,
                PricingDetails = newSnapshot,
                InvoiceId = invoice.Id,
                TorrentHealth = health
            });
        }


     
    

    }
}