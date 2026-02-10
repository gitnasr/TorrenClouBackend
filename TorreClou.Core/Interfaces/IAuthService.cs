using TorreClou.Core.DTOs.Auth;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IAuthService
    {
        Task<Result<AuthResponseDto>> LoginAsync(string email, string password);
    }
}
