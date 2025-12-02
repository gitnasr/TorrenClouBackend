using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Financals;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;

namespace TorreClou.Application.Services
{
    public class PaymentBusinessService(
        IUnitOfWork unitOfWork,
        IPaymentGateway paymentGateway) : IPaymentBusinessService
    {
        public async Task<Result<string>> InitiateDepositAsync(int userId, decimal amount)
        {
            // 1. Validate User
            var user = await unitOfWork.Repository<User>().GetByIdAsync(userId);
            if (user == null)
            {
                return Result<string>.Failure("User not found.");
            }

            if (amount <= 0)
            {
                return Result<string>.Failure("Amount must be greater than zero.");
            }

            // 2. Create Deposit Record (Pending)
            var deposit = new Deposit
            {
                UserId = userId,
                Amount = amount,
                Currency = "EGP", // أو حسب عملة السيستم
                Status = DepositStatus.Pending,
                PaymentProvider = "Kashier",
                CreatedAt = DateTime.UtcNow
            };

            unitOfWork.Repository<Deposit>().Add(deposit);

            // 3. Save first to generate Deposit ID (عشان نحتاجه في الـ Gateway)
            var rows = await unitOfWork.Complete();
            if (rows <= 0) return Result<string>.Failure("Failed to initialize deposit.");

            try
            {
                var paymentUrl = await paymentGateway.InitiatePaymentAsync(deposit, user);

                deposit.PaymentUrl = paymentUrl;
                unitOfWork.Repository<Deposit>().Update(deposit);
                await unitOfWork.Complete();

                return Result<string>.Success(paymentUrl);
            }
            catch (Exception ex)
            {
                deposit.Status = DepositStatus.Failed;
                await unitOfWork.Complete();

                return Result<string>.Failure($"Payment Gateway Error: {ex.Message}");
            }
        }
    }
}