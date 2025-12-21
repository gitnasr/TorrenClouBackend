using TorreClou.Core.Entities;

namespace TorreClou.Core.Interfaces
{
    public interface IUserService
    {
        Task<User?> GetActiveUserById(int userId);
    }
}