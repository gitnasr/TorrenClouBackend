using System.Text.Json;
using System.Threading.Tasks;
using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Financals;
using TorreClou.Core.Entities.Marketing;
using TorreClou.Core.Entities.Torrents;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Models.Pricing;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services
{
    public class QuotePricingService(
        IWalletService walletService,
        IUnitOfWork unitOfWork,
        IPricingEngine pricingEngine,
        IVoucherService voucherService) : IQuotePricingService
    {
        public async Task<Result<QuotePricingResult>> GenerateOrReuseInvoiceAsync(QuotePricingRequest request)
        {
            // 1) Build pricing snapshot من الـ PricingEngine
            var snapshot = pricingEngine.CalculatePrice(
                request.SizeInBytes,
                request.Region,
                request.HealthMultiplier,
                request.IsCacheHit
            );

            snapshot.TotalSizeInBytes = request.SizeInBytes;
            snapshot.SelectedFiles = request.SelectedFiles ?? new List<int>();
            snapshot.UserRegion = request.Region.ToString();

            // 2) حاول تعيد استخدام Invoice موجودة
            var existingInvoiceResult = await FindActiveInvoiceByTorrentAndUserAsync(
                request.InfoHash,
                request.UserId
            );

            if (existingInvoiceResult.IsSuccess)
            {
                var existingInvoice = existingInvoiceResult.Value;
                var existingSnapshot =
                    JsonSerializer.Deserialize<PricingSnapshot>(existingInvoice.PricingSnapshotJson)!;

                if (AreSnapshotsEquivalent(existingSnapshot, snapshot))
                {
                    // Reuse existing
                    return Result.Success(new QuotePricingResult
                    {
                        Invoice = existingInvoice,
                        Snapshot = existingSnapshot,
                        IsReused = true
                    });
                }

                // Cancel old invoice لو snapshot اتغيرت
                existingInvoice.CancelledAt = DateTime.UtcNow;
                await unitOfWork.Complete();
            }

            // 3) Voucher logic (generic)
            Voucher? voucher = null;
            if (!string.IsNullOrEmpty(request.VoucherCode))
            {
                var voucherResult = await voucherService.ValidateVoucherAsync(
                    request.VoucherCode,
                    request.UserId
                );

                if (voucherResult.IsFailure)
                    return Result<QuotePricingResult>.Failure(voucherResult.Error);

                voucher = voucherResult.Value;
            }

            // 4) Create new invoice
            var originalPriceUsd = snapshot.FinalPrice;

            var invoiceResult = await CreateInvoiceAsync(
                request.UserId,
                originalPriceUsd,
                snapshot,
                request.TorrentFile,
                voucher
            );

            if (invoiceResult.IsFailure)
                return Result<QuotePricingResult>.Failure(invoiceResult.Error);

            var invoice = invoiceResult.Value;

            return Result.Success(new QuotePricingResult
            {
                Invoice = invoice,
                Snapshot = snapshot,
                IsReused = false
            });
        }
        public async Task<Result<InvoicePaymentResult>> Pay(int InvoiceId)
        {
            // Find Invoice By Speces

            var PayInvoiceSepc = new BaseSpecification<Invoice>(i => i.Id == InvoiceId);
            var invoice = await unitOfWork.Repository<Invoice>().GetEntityWithSpec(PayInvoiceSepc);
            if (invoice == null)
                return Result<InvoicePaymentResult>.Failure("INVOICE_NOT_FOUND", "Invoice not found.");
            if (invoice.IsExpired || invoice.PaidAt != null || invoice.RefundedAt != null)
                return Result<InvoicePaymentResult>.Failure("INVOICE_INVALID", "Invoice is not valid for payment.");

            // Check Balance 

            var walletBalanceResult = await walletService.GetUserBalanceAsync(invoice.UserId);
            if (walletBalanceResult.IsFailure)
                return Result<InvoicePaymentResult>.Failure(walletBalanceResult.Error);
            var walletBalance = walletBalanceResult.Value;
            if (walletBalance < invoice.FinalAmountInNCurrency)
                return Result<InvoicePaymentResult>.Failure("INSUFFICIENT_FUNDS", "Insufficient funds in wallet.");
            // Deduct Balance
            var deductResult = await walletService.DetuctBalanceAync(
                invoice.UserId,
                invoice.FinalAmountInNCurrency,
                $"Payment for Invoice #{invoice.Id}"
            );

            if (deductResult.IsFailure)
                return Result<InvoicePaymentResult>.Failure(deductResult.Error);

            // Mark Invoice as Paid
            invoice.PaidAt = DateTime.UtcNow;
            invoice.WalletTransactionId = deductResult.Value;
            await unitOfWork.Complete();
            // FIRE EVENT TO START THE JOB


            return Result.Success(new InvoicePaymentResult
            {
                InvoiceId = invoice.Id,
                WalletTransaction = deductResult.Value,
                TotalAmountInNCurruncy = invoice.FinalAmountInNCurrency
            });

        }
        // ==================== Internal helpers ====================

        private async Task<Result<Invoice>> FindActiveInvoiceByTorrentAndUserAsync(
            string infoHash,
            int userId)
        {
            var spec = new ActiveInvoiceByTorrentAndUserSpec(infoHash, userId);
            var invoice = await unitOfWork.Repository<Invoice>().GetEntityWithSpec(spec);

            if (invoice == null)
                return Result<Invoice>.Failure("QUOTE_NOT_FOUND", "No quote found for the given torrent and user.");

            if (invoice.IsExpired)
                return Result<Invoice>.Failure("QUOTE_EXPIRED", "The quote has expired.");

            return Result.Success(invoice);
        }

        private bool AreSnapshotsEquivalent(PricingSnapshot oldSnap, PricingSnapshot newSnap)
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

            if (!sameSelectedFiles)
                return false;

            if (oldSnap.TotalSizeInBytes != newSnap.TotalSizeInBytes)
                return false;

            if (oldSnap.BaseRatePerGb != newSnap.BaseRatePerGb)
                return false;

            if (!string.Equals(oldSnap.UserRegion, newSnap.UserRegion, StringComparison.Ordinal))
                return false;

            if (Math.Abs(oldSnap.RegionMultiplier - newSnap.RegionMultiplier) > 0.0001)
                return false;

            if (oldSnap.IsCacheHit != newSnap.IsCacheHit)
                return false;

            if (oldSnap.CacheDiscountAmount != newSnap.CacheDiscountAmount)
                return false;

            if (Math.Abs(oldSnap.HealthMultiplier - newSnap.HealthMultiplier) > 0.0001)
                return false;

            if (oldSnap.FinalPrice != newSnap.FinalPrice)
                return false;

            return true;
        }

        private async Task<Result<Invoice>> CreateInvoiceAsync(
            int userId,
            decimal originalAmountInUsd,
            PricingSnapshot pricingSnapshot,
            RequestedFile torrentFile,
            Voucher? voucher = null)
        {
            if (originalAmountInUsd <= 0)
                return Result<Invoice>.Failure("INVALID_AMOUNT", "The original amount must be greater than zero.");

            var exchangeRate = 1.0m; // TODO: currencyService later

            var invoice = new Invoice
            {
                UserId = userId,
                OriginalAmountInUSD = originalAmountInUsd,
                FinalAmountInUSD = originalAmountInUsd,
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
                else if (voucher.Type == DiscountType.FixedAmount)
                {
                    invoice.FinalAmountInUSD = invoice.OriginalAmountInUSD - voucher.Value;
                }

                if (invoice.FinalAmountInUSD < 0)
                    invoice.FinalAmountInUSD = 0;
            }

            invoice.FinalAmountInNCurrency = invoice.FinalAmountInUSD * invoice.ExchangeRate;

            unitOfWork.Repository<Invoice>().Add(invoice);
            await unitOfWork.Complete();

            return Result.Success(invoice);
        }
    }
}
