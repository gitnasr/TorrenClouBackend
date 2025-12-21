using System.Text.Json;
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
        IVoucherService voucherService,
        IJobService jobService) : IQuotePricingService
    {
        public async Task<Result<QuotePricingResult>> GenerateOrReuseInvoiceAsync(QuotePricingRequest request)
        {
            // 1) Calculate New Pricing Snapshot
            var newSnapshot = pricingEngine.CalculatePrice(
                request.SizeInBytes,
                request.Region,
                request.HealthMultiplier,
                request.IsCacheHit
            );

            newSnapshot.TotalSizeInBytes = request.SizeInBytes;
            newSnapshot.SelectedFiles = request.SelectedFiles ?? new List<int>();
            newSnapshot.UserRegion = request.Region.ToString();

            // 2) Check for PENDING Invoice (Priority Reuse)
            var pendingInvoiceResult = await FindPendingInvoiceByTorrentAndUserAsync(request.InfoHash, request.UserId);

            if (pendingInvoiceResult.IsSuccess)
            {
                var pendingInvoice = pendingInvoiceResult.Value;

                // CRITICAL FIX: Only reuse if the Voucher Code matches!
                if (IsInvoiceCompatibleWithRequest(pendingInvoice, request, newSnapshot))
                {
                    var pendingSnapshot = JsonSerializer.Deserialize<PricingSnapshot>(pendingInvoice.PricingSnapshotJson)!;
                    return Result.Success(new QuotePricingResult
                    {
                        Invoice = pendingInvoice,
                        Snapshot = pendingSnapshot,
                        IsReused = true
                    });
                }

                // If incompatible (e.g. user added a coupon), we effectively "expire" the old pending one
                // by ignoring it and creating a new one. 
                // Optional: Cancel the old pending invoice explicitly here to keep DB clean.
                pendingInvoice.CancelledAt = DateTime.UtcNow;
                await unitOfWork.Complete();
            }

            // 3) Check for ACTIVE/Expired but Reusable Invoice (Secondary Reuse)
            // Note: Usually we only reuse PENDING. Reuse of "Active" implies previously paid? 
            // If the logic is "Active = Waiting for Payment", it's the same as pending.
            // Assuming this logic checks for older valid quotes that weren't paid yet.
            var existingInvoiceResult = await FindActiveInvoiceByTorrentAndUserAsync(request.InfoHash, request.UserId);

            if (existingInvoiceResult.IsSuccess)
            {
                var existingInvoice = existingInvoiceResult.Value;
                var existingSnapshot = JsonSerializer.Deserialize<PricingSnapshot>(existingInvoice.PricingSnapshotJson)!;

                if (IsInvoiceCompatibleWithRequest(existingInvoice, request, newSnapshot))
                {
                    return Result.Success(new QuotePricingResult
                    {
                        Invoice = existingInvoice,
                        Snapshot = existingSnapshot,
                        IsReused = true
                    });
                }

                // If snapshots/vouchers differ, cancel the old one
                existingInvoice.CancelledAt = DateTime.UtcNow;
                await unitOfWork.Complete();
            }

            // 4) Validate Voucher (New Invoice Path)
            Voucher? voucher = null;
            if (!string.IsNullOrEmpty(request.VoucherCode))
            {
                var voucherResult = await voucherService.ValidateVoucherAsync(request.VoucherCode, request.UserId);
                if (voucherResult.IsFailure) return Result<QuotePricingResult>.Failure(voucherResult.Error);
                voucher = voucherResult.Value;
            }

            // 5) Create New Invoice
            var invoiceResult = await CreateInvoiceAsync(
                request.UserId,
                newSnapshot.FinalPrice, // This comes from PricingEngine (Base Price)
                newSnapshot,
                request.TorrentFile,
                voucher
            );

            if (invoiceResult.IsFailure) return Result<QuotePricingResult>.Failure(invoiceResult.Error);

            return Result.Success(new QuotePricingResult
            {
                Invoice = invoiceResult.Value,
                Snapshot = newSnapshot,
                IsReused = false
            });
        }

        public async Task<Result<InvoicePaymentResult>> Pay(int InvoiceId)
        {
            // 1. Load Invoice
            var invoice = await unitOfWork.Repository<Invoice>().GetByIdAsync(InvoiceId);

            if (invoice == null) return Result<InvoicePaymentResult>.Failure("INVOICE_NOT_FOUND", "Invoice not found.");

            // 2. Validate State
            if (invoice.IsExpired) return Result<InvoicePaymentResult>.Failure("INVOICE_EXPIRED", "Invoice has expired.");
            if (invoice.PaidAt != null) return Result<InvoicePaymentResult>.Failure("INVOICE_ALREADY_PAID", "Invoice is already paid.");
            if (invoice.CancelledAt != null) return Result<InvoicePaymentResult>.Failure("INVOICE_CANCELLED", "Invoice has been cancelled.");

            // 3. Check Wallet
            var walletBalanceResult = await walletService.GetUserBalanceAsync(invoice.UserId);
            if (walletBalanceResult.IsFailure) return Result<InvoicePaymentResult>.Failure(walletBalanceResult.Error);

            if (walletBalanceResult.Value < invoice.FinalAmountInNCurrency)
                return Result<InvoicePaymentResult>.Failure("INSUFFICIENT_FUNDS", "Insufficient wallet balance.");

            // 4. Deduct (Atomic Transaction ideally handled inside WalletService)
            var deductResult = await walletService.DeductBalanceAsync(
                invoice.UserId,
                invoice.FinalAmountInNCurrency,
                $"Payment for Invoice #{invoice.Id}"
            );

            if (deductResult.IsFailure) return Result<InvoicePaymentResult>.Failure(deductResult.Error);

            // 5. Update Invoice
            invoice.PaidAt = DateTime.UtcNow;
            invoice.WalletTransactionId = deductResult.Value;
            await unitOfWork.Complete();

            // 6. Trigger Job
            var jobResult = await jobService.CreateAndDispatchJobAsync(invoice.Id, invoice.UserId);
            if (jobResult.IsFailure)
            {
                // CRITICAL: Job creation failed BUT money was deducted.
                // In a real system, you would Auto-Refund here or use a Saga.
                // For now, we return failure but the money is gone. This is a risk point.
                return Result<InvoicePaymentResult>.Failure(jobResult.Error);
            }

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


        private bool IsInvoiceCompatibleWithRequest(Invoice invoice, QuotePricingRequest request, PricingSnapshot newSnapshot)
        {
            // 1. Voucher Check 
            // If request has no voucher, invoice must have no voucher.
            // If request has voucher, invoice must have SAME voucher.
            if (string.IsNullOrEmpty(request.VoucherCode))
            {
                if (invoice.VoucherId != null) return false; // Invoice has voucher, request doesn't
            }
            else
            {
                if (invoice.Voucher == null || !invoice.Voucher.Code.Equals(request.VoucherCode, StringComparison.OrdinalIgnoreCase))
                    return false; // Invoice has different or no voucher
            }

            // 2. Pricing Snapshot Check
            var oldSnapshot = JsonSerializer.Deserialize<PricingSnapshot>(invoice.PricingSnapshotJson);
            return AreSnapshotsEquivalent(oldSnapshot, newSnapshot);
        }

        private async Task<Result<Invoice>> FindPendingInvoiceByTorrentAndUserAsync(string infoHash, int userId)
        {
            var spec = new PendingInvoiceByTorrentAndUserSpec(infoHash, userId);
            var invoice = await unitOfWork.Repository<Invoice>().GetEntityWithSpec(spec);
            return invoice == null
                ? Result<Invoice>.Failure("NOT_FOUND", "Not found")
                : Result.Success(invoice);
        }

        private async Task<Result<Invoice>> FindActiveInvoiceByTorrentAndUserAsync(string infoHash, int userId)
        {
            var spec = new ActiveInvoiceByTorrentAndUserSpec(infoHash, userId);
            var invoice = await unitOfWork.Repository<Invoice>().GetEntityWithSpec(spec);

            if (invoice == null) return Result<Invoice>.Failure("NOT_FOUND", "Not found");
            if (invoice.IsExpired) return Result<Invoice>.Failure("EXPIRED", "Expired");

            return Result.Success(invoice);
        }

        private bool AreSnapshotsEquivalent(PricingSnapshot? oldSnap, PricingSnapshot newSnap)
        {
            if (oldSnap == null) return false;

            // Fast checks first
            if (oldSnap.TotalSizeInBytes != newSnap.TotalSizeInBytes) return false;
            if (oldSnap.BaseRatePerGb != newSnap.BaseRatePerGb) return false;
            if (oldSnap.IsCacheHit != newSnap.IsCacheHit) return false;
            if (!string.Equals(oldSnap.UserRegion, newSnap.UserRegion, StringComparison.Ordinal)) return false;

            // Collection Check
            // Ensure lists are same length before sorting/comparing
            var oldFiles = oldSnap.SelectedFiles ?? [];
            var newFiles = newSnap.SelectedFiles ?? [];
            if (oldFiles.Count != newFiles.Count) return false;

            if (!oldFiles.OrderBy(x => x).SequenceEqual(newFiles.OrderBy(x => x))) return false;

            // Decimal Comparison (Strict)
            // Use decimal for money/multipliers to avoid floating point drift
            if (oldSnap.FinalPrice != newSnap.FinalPrice) return false;

            // For multipliers stored as double, use epsilon
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

            var exchangeRate = 1.0m; // TODO: Inject ICurrencyService
            var finalAmountUsd = originalAmountInUsd;

            // Apply Voucher
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
                VoucherId = voucher?.Id, // Link by ID
                TorrentFileId = torrentFile.Id // Link by ID
            };

            unitOfWork.Repository<Invoice>().Add(invoice);
            await unitOfWork.Complete();

            // Reload to get relationships if needed, or manually attach
            invoice.TorrentFile = torrentFile;
            if (voucher != null) invoice.Voucher = voucher;

            return Result.Success(invoice);
        }
    }
}