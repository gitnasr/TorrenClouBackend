using System.Text.Json;
using MonoTorrent;
using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Financals;
using TorreClou.Core.Entities.Marketing;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Models.Pricing;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;
using TorreClou.Core.Entities.Torrents;
using TorrentFile = TorreClou.Core.Entities.Torrents.TorrentFile;

namespace TorreClou.Application.Services
{
    public interface IQuoteService
    {
        Task<Result<QuoteResponseDto>> GenerateQuoteAsync(QuoteRequestDto request, int userId, Stream torrentFile); 
    }

    

    public class QuoteService(IUnitOfWork unitOfWork, IPricingEngine pricingEngine, 
        ITorrentService torrentService,
        ITrackerScraper trackerScraper, ITorrentParser torrentParser, IVoucherService voucherService) : IQuoteService
    {
        private  Result<Stream> ValidateTorrentFile(Stream torrentFile, string torrentFileName)
        {
            if (torrentFile == null || torrentFile.Length == 0)
                return Result<Stream>.Failure("No torrent file provided.");
            var fileExtension = System.IO.Path.GetExtension(torrentFileName).ToLower();
            if (fileExtension != ".torrent")
                return Result<Stream>.Failure("Invalid file format. Only .torrent files are accepted.");

            return Result<Stream>.Success(torrentFile);

        }
        public bool AreSnapshotsEquivalent(PricingSnapshot oldSnap, PricingSnapshot newSnap)
        {
            if (oldSnap == null || newSnap == null)
                return false;

            bool sameSelectedFiles =
                (oldSnap.SelectedFiles == null && (newSnap.SelectedFiles == null || !newSnap.SelectedFiles.Any()))
                || (
                    oldSnap.SelectedFiles != null
                    && newSnap.SelectedFiles != null
                    && oldSnap.SelectedFiles.OrderBy(x => x)
                           .SequenceEqual(newSnap.SelectedFiles.OrderBy(x => x))
                );

            bool same =
                oldSnap.TotalSizeInBytes == newSnap.TotalSizeInBytes &&
                sameSelectedFiles &&
                oldSnap.BaseRatePerGb == newSnap.BaseRatePerGb &&
                oldSnap.UserRegion == newSnap.UserRegion &&
                Math.Abs(oldSnap.RegionMultiplier - newSnap.RegionMultiplier) < 0.0001 &&
                Math.Abs(oldSnap.HealthMultiplier - newSnap.HealthMultiplier) < 0.0001 &&
                oldSnap.IsCacheHit == newSnap.IsCacheHit &&
                oldSnap.CacheDiscountAmount == newSnap.CacheDiscountAmount &&
                oldSnap.FinalPrice == newSnap.FinalPrice;

            return same;
        }


        private Result<long> CalculateStorage(List<int>? SelectedFileIndices, TorrentInfoDto torrentInfo)
        {
            long targetSize = 0;


            if (SelectedFileIndices != null && SelectedFileIndices.Any())
            {

                if (torrentInfo.TotalSize == 0)
                {
                    return Result.Failure<long>("Can't get the total size");
                }

                targetSize = torrentInfo.Files
                    .Where(f => SelectedFileIndices.Contains(f.Index))
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


        public async Task<Result<QuoteResponseDto>> GenerateQuoteAsync(
       QuoteRequestDto request,
       int userId,
       Stream torrentFile)
        {
            // 1) Validate + Parse
            var torrentFileValidated = ValidateTorrentFile(torrentFile, request.TorrentFile.FileName);
            if (!torrentFileValidated.IsSuccess)
                return Result<QuoteResponseDto>.Failure(torrentFileValidated.Error);

            var torrentInfoResult = torrentParser.ParseTorrentFile(torrentFileValidated.Value);
            if (!torrentInfoResult.IsSuccess || string.IsNullOrEmpty(torrentInfoResult.Value.InfoHash))
                return Result<QuoteResponseDto>.Failure("Can't get torrent info.");

            var torrentInfo = torrentInfoResult.Value;

            // 2) Calculate size based on selected files (Bytes)
            var totalSizeResult = CalculateStorage(request.SelectedFileIndices, torrentInfo);
            if (!totalSizeResult.IsSuccess)
                return Result<QuoteResponseDto>.Failure(totalSizeResult.Error);

            long totalSizeInBytes = totalSizeResult.Value;

            var user = await unitOfWork.Repository<User>().GetByIdAsync(userId);
            int seeders = await GetRealTimeSeeders(torrentInfo.InfoHash, null);

            // 3) New snapshot for current request
            var newSnapshot = pricingEngine.CalculatePrice(
                totalSizeInBytes,
                user.Region,
                seeders
            );

            // توحيد الداتا جوه الـ snapshot
            newSnapshot.TotalSizeInBytes = totalSizeInBytes;
            newSnapshot.SelectedFiles = request.SelectedFileIndices?.ToList() ?? new List<int>();
            newSnapshot.SeedersCount = seeders;
            newSnapshot.UserRegion = user.Region.ToString(); // لو enum

            // 4) Try to reuse existing invoice
            var existingInvoiceResult = await FindInvoiceByTorrentAndUserId(torrentInfo.InfoHash, userId);

            if (existingInvoiceResult.IsSuccess)
            {
                var existingInvoice = existingInvoiceResult.Value;
                var existingSnapshot =
                    JsonSerializer.Deserialize<PricingSnapshot>(existingInvoice.PricingSnapshotJson)!;

                bool sameQuote = AreSnapshotsEquivalent(existingSnapshot, newSnapshot);

                if (sameQuote)
                {
                    // ✅ رجّع نفس الفاتورة، وكل القيم من الـ Invoice / Snapshot
                    return Result.Success(new QuoteResponseDto
                    {
                        IsReadyToDownload = true,
                        OriginalAmountInUSD = existingInvoice.OriginalAmountInUSD,
                        FinalAmountInUSD = existingInvoice.FinalAmountInUSD,
                        FinalAmountInNCurrency = existingInvoice.FinalAmountInNCurrency,
                        FileName = existingInvoice.TorrentFile.FileName,
                        SizeInBytes = existingSnapshot.TotalSizeInBytes,
                        IsCached = existingSnapshot.IsCacheHit,
                        InfoHash = existingInvoice.TorrentFile.InfoHash,
                        PricingDetails = existingSnapshot,
                        InvoiceId = existingInvoice.Id,
                    });
                }
                else
                {
                    existingInvoice.CancelledAt = DateTime.UtcNow;
                    await unitOfWork.Complete();
                }
            }

            // 6) Save torrent (if needed)
            var torrentInDb = await torrentService.FindOrCreateTorrentFile(new()
            {
                InfoHash = torrentInfo.InfoHash,
                FileName = torrentInfo.Name,
                FileSize = torrentInfo.TotalSize,       // bytes
                Files = torrentInfo.Files.Select(f => f.Path).ToArray(),
                UploadedByUserId = userId
            });

            if (torrentInDb.IsFailure)
                return Result<QuoteResponseDto>.Failure("Failed to save torrent information.");

            // 7) Voucher logic
            Voucher? voucher = null;
            if (!string.IsNullOrEmpty(request.VoucherCode))
            {
                var voucherResult = await voucherService.ValidateVoucherAsync(request.VoucherCode, userId);
                if (voucherResult.IsFailure)
                    return Result<QuoteResponseDto>.Failure(voucherResult.Error);

                voucher = voucherResult.Value;
            }

            // 8) Create new invoice (المبالغ بالمينيمم يونت – سنتس مثلاً)
            var originalPriceUsd = newSnapshot.FinalPrice;

            var invoiceResult = await CreateInvoiceAsync(
                userId,
                originalPriceUsd,
                newSnapshot,
                torrentInDb.Value,
                voucher
            );

            if (invoiceResult.IsFailure)
                return Result<QuoteResponseDto>.Failure("Failed to create invoice.");

            var invoice = invoiceResult.Value;

            // ✅ الريسبونس بناءً على الـ Invoice + Snapshot
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
            });
        }


        public async Task<Result<Invoice>> CreateInvoiceAsync(
         int userId,
         decimal originalAmountInUsd,                
         PricingSnapshot pricingSnapshot,
         TorrentFile torrentFile,
         Voucher? voucher = null)
        {
            if (originalAmountInUsd <= 0)
                return Result<Invoice>.Failure("INVALID_AMOUNT", "The original amount must be greater than zero.");
            // var exchangeRate = await currencyService.GetRateAsync(userCurrency);
            var exchangeRate = 1.0m;

            var invoice = new Invoice
            {
                UserId = userId,
                OriginalAmountInUSD = originalAmountInUsd,
                FinalAmountInUSD = originalAmountInUsd,   // هنعدّلها تحت لو فيه خصم
                ExchangeRate = exchangeRate,
                PricingSnapshotJson = JsonSerializer.Serialize(pricingSnapshot),
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                Voucher = voucher,
                TorrentFile = torrentFile,
            };

            if (voucher != null)
            {
                if (voucher.Type == Core.Enums.DiscountType.Percentage)
                {
                    var discount = invoice.OriginalAmountInUSD * (voucher.Value / 100m);
                    invoice.FinalAmountInUSD = invoice.OriginalAmountInUSD - discount;
                }
                else if (voucher.Type == Core.Enums.DiscountType.FixedAmount)
                {
                    invoice.FinalAmountInUSD = invoice.OriginalAmountInUSD - voucher.Value;
                }

                // ضمان عدم نزول السعر تحت الصفر لو حابب
                if (invoice.FinalAmountInUSD < 0)
                    invoice.FinalAmountInUSD = 0;
            }

            // 3) حساب عملة الموقع بناءً على ExchangeRate المثبّت وقتها
            invoice.FinalAmountInNCurrency = invoice.FinalAmountInUSD * invoice.ExchangeRate;

            unitOfWork.Repository<Invoice>().Add(invoice);
            await unitOfWork.Complete();

            return Result.Success(invoice);
        }

        private async Task<int> GetRealTimeSeeders(string infoHash, List<string>? magnetTrackers)
        {
            List<string> defaultTrackers = [
        "udp://tracker.opentrackr.org:1337/announce",
        "udp://9.rarbg.com:2810/announce",
        "udp://tracker.openbittorrent.com:80/announce",
        "udp://tracker.tiny-vps.com:6969/announce"
    ];

            // 2. Merge with magnet trackers (if any)
            var allTrackers = defaultTrackers.Concat(magnetTrackers ?? []).Distinct();


            return await trackerScraper.GetSeedersCountAsync(infoHash, allTrackers);
        }
    }
}