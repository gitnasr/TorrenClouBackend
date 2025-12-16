using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IGoogleDriveAuthService
    {
        Task<Result<string>> GetAuthorizationUrlAsync(int userId, string? profileName = null);
        Task<Result<int>> HandleOAuthCallbackAsync(string code, string state);
    }
}

