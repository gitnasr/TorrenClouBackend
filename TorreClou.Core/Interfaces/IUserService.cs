using TorreClou.Core.Entities;

namespace TorreClou.Core.Interfaces
{
    public interface IUserService
    {
        Task<bool> UserExistsAsync(string email);
        Task<User> CreateUser(string email, string name);
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByIdAsync(int userId);
    }
}