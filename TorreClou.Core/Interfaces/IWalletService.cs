using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IWalletService
    {
        // إضافة رصيد (موجب)
        Task<Result> AddDepositAsync(int userId, decimal amount, string? referenceId = null, string description = "Deposit");

        // معرفة الرصيد الحالي
        Task<Result<decimal>> GetUserBalanceAsync(int userId);
    }
}