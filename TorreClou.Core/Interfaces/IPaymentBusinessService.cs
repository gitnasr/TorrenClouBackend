using TorreClou.Core.DTOs.Admin;
using TorreClou.Core.DTOs.Common;
using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.Enums;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IPaymentBusinessService
    {
        Task<Result<string>> InitiateDepositAsync(int userId, decimal amount, string currency);
        Task<Result> ProcessCryptoWebhookAsync(string invoiceId, string coin);

        // User: Get paginated deposits
        Task<Result<PaginatedResult<DepositDto>>> GetUserDepositsAsync(int userId, int pageNumber, int pageSize);

        // User: Get single deposit by ID
        Task<Result<DepositDto>> GetDepositByIdAsync(int userId, int depositId);

        // Admin: Get all deposits (paginated, optionally filtered by status)
        Task<Result<PaginatedResult<AdminDepositDto>>> AdminGetAllDepositsAsync(int pageNumber, int pageSize, DepositStatus? status = null);

        // Admin: Get analytics dashboard
        Task<Result<AdminDashboardDto>> GetAnalyticsAsync(DateTime? dateFrom, DateTime? dateTo);
    }
}