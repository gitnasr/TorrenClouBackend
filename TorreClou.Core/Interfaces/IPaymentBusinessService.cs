using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IPaymentBusinessService
    {
        Task<Result<string>> InitiateDepositAsync(int userId, decimal amount);
    }
}