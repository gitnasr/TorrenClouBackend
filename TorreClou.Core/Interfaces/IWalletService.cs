using TorreClou.Core.DTOs.Admin;
using TorreClou.Core.DTOs.Common;
using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IWalletService
    {
        // إضافة رصيد (موجب) - يرجع ID الـ WalletTransaction
        Task<Result<int>> AddDepositAsync(int userId, decimal amount, string? referenceId = null, string description = "Deposit");

        // معرفة الرصيد الحالي
        Task<Result<decimal>> GetUserBalanceAsync(int userId);

        Task<Result<int>> DeductBalanceAsync(int userId, decimal amount, string description);

        // User: Get paginated transactions
        Task<Result<PaginatedResult<WalletTransactionDto>>> GetUserTransactionsAsync(int userId, int pageNumber, int pageSize);

        // User: Get single transaction by ID
        Task<Result<WalletTransactionDto>> GetTransactionByIdAsync(int userId, int transactionId);

        // Admin: Get all transactions (paginated)
        Task<Result<PaginatedResult<WalletTransactionDto>>> AdminGetAllTransactionsAsync(int pageNumber, int pageSize);

        // Admin: Adjust user balance (positive = add, negative = deduct)
        Task<Result<WalletTransactionDto>> AdminAdjustBalanceAsync(int adminId, int userId, decimal amount, string description);

        // Admin: Get all users with their wallet balances
        Task<Result<PaginatedResult<AdminWalletDto>>> AdminGetAllWalletsAsync(int pageNumber, int pageSize);
    }
}