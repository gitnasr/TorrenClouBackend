using TorreClou.Core.DTOs.Admin;
using TorreClou.Core.DTOs.Common;
using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Financals;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services
{
    public class WalletService(
        IUnitOfWork unitOfWork,
        IRedisLockService redisLockService) : IWalletService 
    {
        public async Task<Result<int>> AddDepositAsync(int userId, decimal amount, string? referenceId = null, string description = "Deposit")
        {
            if (amount <= 0)
                return Result<int>.Failure("DEPOSIT_ERROR", "Deposit amount must be greater than zero.");

            var user = await unitOfWork.Repository<User>().GetByIdAsync(userId);
            if (user == null)
                return Result<int>.Failure("USER_ERROR", "User not found.");

            var transaction = new WalletTransaction
            {
                UserId = userId,
                Amount = amount,
                Type = TransactionType.DEPOSIT,
                ReferenceId = referenceId,
                Description = description,
                CreatedAt = DateTime.UtcNow
            };

            unitOfWork.Repository<WalletTransaction>().Add(transaction);


            var result = await unitOfWork.Complete();

            if (result <= 0)
                return Result<int>.Failure("DATABASE_ERROR", "Failed to save transaction.");

            return Result<int>.Success(transaction.Id);
        }

        public async Task<Result<decimal>> GetUserBalanceAsync(int userId)
        {
            // Performance Note: 
            // Loading all transactions into memory is bad for scale. 
            // Ideally, add a SumAsync method to your GenericRepository or use a 'CurrentBalance' column on the User table.

            var spec = new BaseSpecification<WalletTransaction>(x => x.UserId == userId);
            var transactions = await unitOfWork.Repository<WalletTransaction>().ListAsync(spec);

            return Result.Success(transactions.Sum(x => x.Amount));
        }

        public async Task<Result<int>> DeductBalanceAsync(int userId, decimal amount, string description)
        {
            if (amount <= 0)
                return Result<int>.Failure("DEDUCTION_ERROR", "Deduction amount must be greater than zero.");

            // CRITICAL: Acquire Lock to prevent Double Spend Race Condition
            // Lock key is specific to the user's wallet
            var lockKey = $"wallet:lock:{userId}";
            var lockExpiry = TimeSpan.FromSeconds(10); // Short lock is usually enough for DB op

            using var distributedLock = await redisLockService.AcquireLockAsync(lockKey, lockExpiry);

            if (distributedLock == null)
            {
                // Could not acquire lock (another transaction in progress)
                return Result<int>.Failure("WALLET_BUSY", "Wallet is currently processing another transaction. Please try again.");
            }

            // 1. Read Balance (Inside Lock)
            var balanceResult = await GetUserBalanceAsync(userId);
            if (balanceResult.IsFailure) return Result<int>.Failure(balanceResult.Error);

            var currentBalance = balanceResult.Value;

            // 2. Check Sufficiency
            if (currentBalance < amount)
            {
                return Result<int>.Failure("INSUFFICIENT_FUNDS", $"Insufficient funds. Balance: {currentBalance}, Required: {amount}");
            }

            // 3. Write Transaction
            var transaction = new WalletTransaction
            {
                UserId = userId,
                Amount = -amount, // Store as negative
                Type = TransactionType.DEDUCTION,
                Description = description,
                CreatedAt = DateTime.UtcNow
            };

            unitOfWork.Repository<WalletTransaction>().Add(transaction);


            var result = await unitOfWork.Complete();

            if (result <= 0)
                return Result<int>.Failure("DATABASE_ERROR", "Failed to save deduction.");

            return Result.Success(transaction.Id);
        }


        public async Task<Result<PaginatedResult<WalletTransactionDto>>> GetUserTransactionsAsync(int userId, int pageNumber, int pageSize, TransactionType? transactionType = null)
        {
            var spec = new UserTransactionsSpecification(userId, pageNumber, pageSize, transactionType);
            var countSpec = new BaseSpecification<WalletTransaction>(x => 
                x.UserId == userId && (transactionType == null || x.Type == transactionType));

            var transactions = await unitOfWork.Repository<WalletTransaction>().ListAsync(spec);
            var totalCount = await unitOfWork.Repository<WalletTransaction>().CountAsync(countSpec);

            var items = transactions.Select(t => new WalletTransactionDto
            {
                Id = t.Id,
                Amount = t.Amount,
                Type = t.Type.ToString(),
                ReferenceId = t.ReferenceId,
                Description = t.Description,
                CreatedAt = t.CreatedAt
            }).ToList();

            return Result.Success(new PaginatedResult<WalletTransactionDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        public async Task<Result<List<TransactionTypeFilterDto>>> GetUserTransactionFiltersAsync(int userId)
        {
            var spec = new BaseSpecification<WalletTransaction>(x => x.UserId == userId);
            var transactions = await unitOfWork.Repository<WalletTransaction>().ListAsync(spec);

            var filters = transactions
                .GroupBy(t => t.Type)
                .Select(g => new TransactionTypeFilterDto
                {
                    Type = g.Key,
                    Count = g.Count()
                })
                .Where(f => f.Count > 0)
                .OrderByDescending(f => f.Count)
                .ToList();

            return Result.Success(filters);
        }

        public async Task<Result<int>> ProcessRefundAsync(int userId, decimal amount, int invoiceId, string description)
        {
            if (amount <= 0)
                return Result<int>.Failure("REFUND_ERROR", "Refund amount must be greater than zero.");

            var user = await unitOfWork.Repository<User>().GetByIdAsync(userId);
            if (user == null)
                return Result<int>.Failure("USER_ERROR", "User not found.");

            var transaction = new WalletTransaction
            {
                UserId = userId,
                Amount = amount,
                Type = TransactionType.REFUND,
                ReferenceId = invoiceId.ToString(),
                Description = description,
                CreatedAt = DateTime.UtcNow
            };

            unitOfWork.Repository<WalletTransaction>().Add(transaction);

            var result = await unitOfWork.Complete();

            if (result <= 0)
                return Result<int>.Failure("DATABASE_ERROR", "Failed to save refund transaction.");

            return Result<int>.Success(transaction.Id);
        }

        public async Task<Result<WalletTransactionDto>> GetTransactionByIdAsync(int userId, int transactionId)
        {
            var spec = new BaseSpecification<WalletTransaction>(x => x.Id == transactionId && x.UserId == userId);
            var transaction = await unitOfWork.Repository<WalletTransaction>().GetEntityWithSpec(spec);

            if (transaction == null) return Result<WalletTransactionDto>.Failure("NOT_FOUND", "Transaction not found.");

            return Result.Success(new WalletTransactionDto
            {
                Id = transaction.Id,
                Amount = transaction.Amount,
                Type = transaction.Type.ToString(),
                ReferenceId = transaction.ReferenceId,
                Description = transaction.Description,
                CreatedAt = transaction.CreatedAt
            });
        }

        public async Task<Result<PaginatedResult<WalletTransactionDto>>> AdminGetAllTransactionsAsync(int pageNumber, int pageSize)
        {
            var spec = new AllTransactionsSpecification(pageNumber, pageSize);
            var totalCount = await unitOfWork.Repository<WalletTransaction>().CountAsync(x => true);
            var transactions = await unitOfWork.Repository<WalletTransaction>().ListAsync(spec);

            var items = transactions.Select(t => new WalletTransactionDto
            {
                Id = t.Id,
                Amount = t.Amount,
                Type = t.Type.ToString(),
                ReferenceId = t.ReferenceId,
                Description = t.Description,
                CreatedAt = t.CreatedAt
            }).ToList();

            return Result.Success(new PaginatedResult<WalletTransactionDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        public async Task<Result<WalletTransactionDto>> AdminAdjustBalanceAsync(int adminId, int userId, decimal amount, string description)
        {
            if (amount == 0) return Result<WalletTransactionDto>.Failure("INVALID_AMOUNT", "Amount cannot be zero.");

            var user = await unitOfWork.Repository<User>().GetByIdAsync(userId);
            if (user == null) return Result<WalletTransactionDto>.Failure("USER_ERROR", "User not found.");

            // Use Lock here as well to be safe
            var lockKey = $"wallet:lock:{userId}";
            using (await redisLockService.AcquireLockAsync(lockKey, TimeSpan.FromSeconds(5)))
            {
                var transaction = new WalletTransaction
                {
                    UserId = userId,
                    Amount = amount,
                    Type = TransactionType.ADMIN_ADJUSTMENT,
                    ReferenceId = $"ADMIN-{adminId}",
                    Description = description,
                    CreatedAt = DateTime.UtcNow
                };

                unitOfWork.Repository<WalletTransaction>().Add(transaction);
                await unitOfWork.Complete();

                return Result.Success(new WalletTransactionDto
                {
                    Id = transaction.Id,
                    Amount = transaction.Amount,
                    Type = transaction.Type.ToString(),
                    ReferenceId = transaction.ReferenceId,
                    Description = transaction.Description,
                    CreatedAt = transaction.CreatedAt
                });
            }
        }

        public async Task<Result<PaginatedResult<AdminWalletDto>>> AdminGetAllWalletsAsync(int pageNumber, int pageSize)
        {
            var spec = new AllUsersWithTransactionsSpecification(pageNumber, pageSize);
            var users = await unitOfWork.Repository<User>().ListAsync(spec);
            var totalCount = await unitOfWork.Repository<User>().CountAsync(x => true);

            var items = users.Select(u => new AdminWalletDto
            {
                UserId = u.Id,
                UserEmail = u.Email,
                UserFullName = u.FullName,
                Balance = u.WalletTransactions?.Sum(t => t.Amount) ?? 0,
                TransactionCount = u.WalletTransactions?.Count ?? 0,
                LastTransactionDate = u.WalletTransactions?.OrderByDescending(t => t.CreatedAt).FirstOrDefault()?.CreatedAt
            }).ToList();

            return Result.Success(new PaginatedResult<AdminWalletDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }
    }
}