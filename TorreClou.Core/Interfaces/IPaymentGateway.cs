using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Financals;

namespace TorreClou.Core.Interfaces
{
    public interface IPaymentGateway
    {
        Task<string> InitiatePaymentAsync(Deposit deposit, User user);
    }
}