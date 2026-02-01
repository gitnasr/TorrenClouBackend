using TorreClou.Core.DTOs.Storage.GoogleDrive;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IGoogleDriveAuthService
    {
        Task<Result<string>> GetAuthorizationUrlAsync(int userId, string? profileName = null);
        Task<Result<string>> ConfigureAndGetAuthUrlAsync(int userId, ConfigureGoogleDriveRequestDto request);
        Task<Result<int>> HandleOAuthCallbackAsync(string code, string state);
    }
}

