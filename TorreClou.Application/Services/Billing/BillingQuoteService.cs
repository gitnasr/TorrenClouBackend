using System.Text.Json;
using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.Entities.Marketing;
using TorreClou.Core.Entities.Torrents;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Models.Pricing;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services.Billing
{
    public class QuotePricingService(
        IWalletService walletService,
        IUnitOfWork unitOfWork,
        IPricingEngine pricingEngine,
        IVoucherService voucherService,
        IJobService jobService) : IQuotePricingService
    {
        public async Task<Result<QuotePricingResult>> GenerateOrReuseInvoiceAsync(QuotePricingRequest request)
        {
            // 1) Build pricing snapshot from PricingEngine
            var snapshot = pricingEngine.CalculatePrice(
                request.SizeInBytes,
                request.Region,
                request.HealthMultiplier,
                request.IsCacheHit
            );

            snapshot.TotalSizeInBytes = request.SizeInBytes;
            snapshot.SelectedFiles = request.SelectedFilePaths;
            snapshot.UserRegion = request.Region.ToString();

            // 2) Check for PENDING invoice first - prevent duplicates
            var pendingInvoiceResult = await FindPendingInvoiceByTorrentAndUserAsync(
                request.InfoHash,
                request.UserId
            );

            if (pendingInvoiceResult.IsSuccess)
            {
                var pendingInvoice = pendingInvoiceResult.Value;

                // Only reuse if the Voucher Code matches!
                if (IsInvoiceCompatibleWithRequest(pendingInvoice, request, snapshot))
                {
                    var pendingSnapshot = JsonSerializer.Deserialize<PricingSnapshot>(pendingInvoice.PricingSnapshotJson)!;
                    return Result.Success(new QuotePricingResult
                    {
                        Invoice = pendingInvoice,
                        Snapshot = pendingSnapshot,
                        IsReused = true
                    });
                }

                // If incompatible (e.g. user added a coupon), expire the old one
                pendingInvoice.CancelledAt = DateTime.UtcNow;
                await unitOfWork.Complete();
            }

            // 3) Check for ACTIVE invoices
            var existingInvoiceResult = await FindActiveInvoiceByTorrentAndUserAsync(
                request.InfoHash,
                request.UserId
            );

            if (existingInvoiceResult.IsSuccess)
            {
                var existingInvoice = existingInvoiceResult.Value;
                var existingSnapshot = JsonSerializer.Deserialize<PricingSnapshot>(existingInvoice.PricingSnapshotJson)!;

                if (IsInvoiceCompatibleWithRequest(existingInvoice, request, snapshot))
                {
                    return Result.Success(new QuotePricingResult
                    {
                        Invoice = existingInvoice,
                        Snapshot = existingSnapshot,
                        IsReused = true
                    });
                }

                existingInvoice.CancelledAt = DateTime.UtcNow;
                await unitOfWork.Complete();
            }

            // 4) Voucher logic
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

            // 5) Create new invoice
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
            var deductResult = await walletService.DeductBalanceAsync(
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

            // Create and dispatch the job
            var jobResult = await jobService.CreateAndDispatchJobAsync(invoice.Id, invoice.UserId);
            if (jobResult.IsFailure)
                return Result<InvoicePaymentResult>.Failure(jobResult.Error);

            var job = jobResult.Value;

            return Result.Success(new InvoicePaymentResult
            {
                InvoiceId = invoice.Id,
                JobId = job.JobId,
                WalletTransaction = deductResult.Value,
                TotalAmountInNCurruncy = invoice.FinalAmountInNCurrency,
                HasStorageProfileWarning = job.HasStorageProfileWarning,
                StorageProfileWarningMessage = job.StorageProfileWarningMessage
            });
        }

        // ==================== Internal helpers ====================

        private async Task<Result<Invoice>> FindPendingInvoiceByTorrentAndUserAsync(string infoHash, int userId)
        {
            var spec = new PendingInvoiceByTorrentAndUserSpec(infoHash, userId);
            var invoice = await unitOfWork.Repository<Invoice>().GetEntityWithSpec(spec);

            if (invoice == null)
                return Result<Invoice>.Failure("NO_PENDING_INVOICE", "Not found.");

            return Result.Success(invoice);
        }

        private async Task<Result<Invoice>> FindActiveInvoiceByTorrentAndUserAsync(string infoHash, int userId)
        {
            var spec = new ActiveInvoiceByTorrentAndUserSpec(infoHash, userId);
            var invoice = await unitOfWork.Repository<Invoice>().GetEntityWithSpec(spec);

            if (invoice == null)
                return Result<Invoice>.Failure("QUOTE_NOT_FOUND", "Not found.");

            if (invoice.IsExpired)
                return Result<Invoice>.Failure("QUOTE_EXPIRED", "Expired.");

            return Result.Success(invoice);
        }

        private bool IsInvoiceCompatibleWithRequest(Invoice invoice, QuotePricingRequest request, PricingSnapshot newSnapshot)
        {
            // 1. Voucher Check
            if (string.IsNullOrEmpty(request.VoucherCode))
            {
                if (invoice.VoucherId != null) return false;
            }
            else
            {
                if (invoice.Voucher == null || !invoice.Voucher.Code.Equals(request.VoucherCode, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // 2. Snapshot Check
            var oldSnapshot = JsonSerializer.Deserialize<PricingSnapshot>(invoice.PricingSnapshotJson);
            return AreSnapshotsEquivalent(oldSnapshot, newSnapshot);
        }

        private bool AreSnapshotsEquivalent(PricingSnapshot? oldSnap, PricingSnapshot newSnap)
        {
            if (oldSnap == null) return false;

            if (oldSnap.TotalSizeInBytes != newSnap.TotalSizeInBytes) return false;
            if (oldSnap.BaseRatePerGb != newSnap.BaseRatePerGb) return false;
            if (oldSnap.IsCacheHit != newSnap.IsCacheHit) return false;
            if (!string.Equals(oldSnap.UserRegion, newSnap.UserRegion, StringComparison.Ordinal)) return false;

            var oldFiles = oldSnap.SelectedFiles ;
            var newFiles = newSnap.SelectedFiles;
            if (oldFiles.Count != newFiles.Count) return false;

            if (!oldFiles.OrderBy(x => x).SequenceEqual(newFiles.OrderBy(x => x))) return false;

            if (oldSnap.FinalPrice != newSnap.FinalPrice) return false;

            if (Math.Abs(oldSnap.RegionMultiplier - newSnap.RegionMultiplier) > 0.0001) return false;
            if (Math.Abs(oldSnap.HealthMultiplier - newSnap.HealthMultiplier) > 0.0001) return false;

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
                return Result<Invoice>.Failure("INVALID_AMOUNT", "Amount must be positive.");

            var exchangeRate = 1.0m;
            var finalAmountUsd = originalAmountInUsd;

            if (voucher != null)
            {
                if (voucher.Type == DiscountType.Percentage)
                {
                    var discount = originalAmountInUsd * (voucher.Value / 100m);
                    finalAmountUsd -= discount;
                }
                else if (voucher.Type == DiscountType.FixedAmount)
                {
                    finalAmountUsd -= voucher.Value;
                }

                if (finalAmountUsd < 0) finalAmountUsd = 0;
            }

            var invoice = new Invoice
            {
                UserId = userId,
                OriginalAmountInUSD = originalAmountInUsd,
                FinalAmountInUSD = finalAmountUsd,
                ExchangeRate = exchangeRate,
                FinalAmountInNCurrency = finalAmountUsd * exchangeRate,
                PricingSnapshotJson = JsonSerializer.Serialize(pricingSnapshot),
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                VoucherId = voucher?.Id,
                TorrentFileId = torrentFile.Id,
            };

            unitOfWork.Repository<Invoice>().Add(invoice);
            await unitOfWork.Complete();

            // Manually populate navigation properties for the return value
            invoice.TorrentFile = torrentFile;
            if (voucher != null) invoice.Voucher = voucher;

            return Result.Success(invoice);
        }
    }
}