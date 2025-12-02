using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Financals;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services
{

    // لاحظ: استخدمنا IUnitOfWork عشان نضمن الـ Saving
    public class WalletService(IUnitOfWork unitOfWork) : IWalletService
    {
        public async Task<Result> AddDepositAsync(int userId, decimal amount, string? referenceId = null, string description = "Deposit")
        {
            // 1. Validation
            if (amount <= 0)
            {
                // افترضت ان الـ Result عندك فيه Generic Type للداتا الراجعة
                return Result.Failure("DEPOSIT_ERROR", "Deposit amount must be greater than zero.");
            }

            // 2. Check User Existence (Optional but recommended)
            var user = await unitOfWork.Repository<User>().GetByIdAsync(userId);
            if (user == null)
            {
                return Result.Failure("USER_ERROR", "User not found.");
            }

            // 3. Create the Transaction (The Ledger Entry)
            var transaction = new WalletTransaction
            {
                UserId = userId,
                Amount = amount, // Positive Value
                Type = TransactionType.DEPOSIT,
                ReferenceId = referenceId, // ده اللي هيربطنا بجدول الـ Deposits او Stripe
                Description = description,
                CreatedAt = DateTime.UtcNow
            };

            // 4. Add to Repo & Save
            unitOfWork.Repository<WalletTransaction>().Add(transaction);

            var result = await unitOfWork.Complete();

            if (result <= 0)
                return Result.Failure("DATABASE_ERROR", "Failed to save transaction to database.");

            var newBalance = await GetUserBalanceAsync(userId);
            return Result.Success(newBalance);
        }

        public async Task<Result<decimal>> GetUserBalanceAsync(int userId)
        {
            var spec = new BaseSpecification<WalletTransaction>(x => x.UserId == userId);
            var transactions = await unitOfWork.Repository<WalletTransaction>().ListAsync(spec);

            return Result.Success(transactions.Sum(x => x.Amount));
        }
    }
}