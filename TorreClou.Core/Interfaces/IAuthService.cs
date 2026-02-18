using TorreClou.Core.DTOs.Auth;

namespace TorreClou.Core.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponseDto> LoginAsync(string email, string password);
    }
}
