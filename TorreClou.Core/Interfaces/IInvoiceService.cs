using TorreClou.Core.DTOs.Common;
using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IInvoiceService
    {
        Task<Result<PaginatedResult<InvoiceDto>>> GetUserInvoicesAsync(
            int userId, 
            int pageNumber, 
            int pageSize, 
            DateTime? dateFrom = null, 
            DateTime? dateTo = null);

        Task<Result<InvoiceDto>> GetInvoiceByIdAsync(int userId, int invoiceId);

        Task<Result<InvoiceStatisticsDto>> GetUserInvoiceStatisticsAsync(int userId);
    }
}

