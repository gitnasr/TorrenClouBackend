using TorreClou.Core.DTOs.Common;
using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.Entities.Financals;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services
{
    public class InvoiceService(IUnitOfWork unitOfWork) : IInvoiceService
    {
        public async Task<Result<PaginatedResult<InvoiceDto>>> GetUserInvoicesAsync(
            int userId,
            int pageNumber,
            int pageSize,
            DateTime? dateFrom = null,
            DateTime? dateTo = null)
        {
            var spec = new UserInvoicesSpecification(userId, pageNumber, pageSize, dateFrom, dateTo);

            var countSpec = new BaseSpecification<Invoice>(i =>
                i.UserId == userId &&
                (!dateFrom.HasValue || i.CreatedAt >= dateFrom.Value) &&
                (!dateTo.HasValue || i.CreatedAt <= dateTo.Value)
            );

            var invoices = await unitOfWork.Repository<Invoice>().ListAsync(spec);
            var totalCount = await unitOfWork.Repository<Invoice>().CountAsync(countSpec);

            // 3. Mapping
            var items = invoices.Select(invoice => new InvoiceDto
            {
                Id = invoice.Id,
                UserId = invoice.UserId,
                JobId = invoice.JobId,
                OriginalAmountInUSD = invoice.OriginalAmountInUSD,
                FinalAmountInUSD = invoice.FinalAmountInUSD,
                FinalAmountInNCurrency = invoice.FinalAmountInNCurrency,
                ExchangeRate = invoice.ExchangeRate,
                PaidAt = invoice.PaidAt,
                CancelledAt = invoice.CancelledAt,
                RefundedAt = invoice.RefundedAt,
                TorrentFileId = invoice.TorrentFileId,
                TorrentFileName = invoice.TorrentFile?.FileName,
                ExpiresAt = invoice.ExpiresAt,
                CreatedAt = invoice.CreatedAt,
                UpdatedAt = invoice.UpdatedAt
            }).ToList();

            return Result.Success(new PaginatedResult<InvoiceDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        public async Task<Result<InvoiceDto>> GetInvoiceByIdAsync(int userId, int invoiceId)
        {
            var spec = new BaseSpecification<Invoice>(i => i.Id == invoiceId && i.UserId == userId);
            spec.AddInclude(i => i.TorrentFile);
            spec.AddInclude(i => i.Job);

            var invoice = await unitOfWork.Repository<Invoice>().GetEntityWithSpec(spec);

            if (invoice == null)
                return Result<InvoiceDto>.Failure("NOT_FOUND", "Invoice not found.");

            return Result.Success(new InvoiceDto
            {
                Id = invoice.Id,
                UserId = invoice.UserId,
                JobId = invoice.JobId,
                OriginalAmountInUSD = invoice.OriginalAmountInUSD,
                FinalAmountInUSD = invoice.FinalAmountInUSD,
                FinalAmountInNCurrency = invoice.FinalAmountInNCurrency,
                ExchangeRate = invoice.ExchangeRate,
                PaidAt = invoice.PaidAt,
                CancelledAt = invoice.CancelledAt,
                RefundedAt = invoice.RefundedAt,
                TorrentFileId = invoice.TorrentFileId,
                TorrentFileName = invoice.TorrentFile?.FileName,
                ExpiresAt = invoice.ExpiresAt,
                CreatedAt = invoice.CreatedAt,
                UpdatedAt = invoice.UpdatedAt
            });
        }

        public async Task<Result<InvoiceStatisticsDto>> GetUserInvoiceStatisticsAsync(int userId)
        {

            var totalCount = await unitOfWork.Repository<Invoice>()
                .CountAsync(new BaseSpecification<Invoice>(i => i.UserId == userId));

            var paidCount = await unitOfWork.Repository<Invoice>()
                .CountAsync(new BaseSpecification<Invoice>(i => i.UserId == userId && i.PaidAt != null));

            var unpaidCount = await unitOfWork.Repository<Invoice>()
                .CountAsync(new BaseSpecification<Invoice>(i =>
                    i.UserId == userId && i.PaidAt == null && i.CancelledAt == null && i.ExpiresAt > DateTime.UtcNow));

            return Result.Success(new InvoiceStatisticsDto
            {
                TotalInvoices = totalCount,
                PaidInvoices = paidCount,
                UnpaidInvoices = unpaidCount
            });
        }
    }
}