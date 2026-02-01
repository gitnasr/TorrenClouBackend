using TorreClou.Core.DTOs.Storage.GoogleDrive;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IGoogleDriveService
    {
        Task<string> GetAuthorizationUrlAsync(int userId, string? profileName = null);
        Task<Result<string>> ConfigureAndGetAuthUrlAsync(int userId, ConfigureGoogleDriveRequestDto request);
        Task<string> GetGoogleCallback(string code, string state);
    }
}
