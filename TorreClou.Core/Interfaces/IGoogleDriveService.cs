using TorreClou.Core.DTOs.Storage.GoogleDrive;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    /// <summary>
    /// Google Drive service for OAuth configuration and callback handling.
    /// Credentials are configured per-user via API (not environment variables).
    /// </summary>
    public interface IGoogleDriveService
    {
        /// <summary>
        /// Configure Google Drive with user-provided OAuth credentials and get authorization URL.
        /// </summary>
        Task<Result<string>> ConfigureAndGetAuthUrlAsync(int userId, ConfigureGoogleDriveRequestDto request);

        /// <summary>
        /// Handle OAuth callback from Google and redirect to frontend.
        /// </summary>
        Task<string> GetGoogleCallback(string code, string state);
    }
}
