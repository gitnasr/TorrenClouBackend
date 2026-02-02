using TorreClou.Core.DTOs.Storage.GoogleDrive;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    /// <summary>
    /// Google Drive authentication service for OAuth flow.
    /// Credentials are configured per-user via API (not environment variables).
    /// </summary>
    public interface IGoogleDriveAuthService
    {
        /// <summary>
        /// Configure Google Drive with user-provided OAuth credentials and get authorization URL.
        /// </summary>
        Task<Result<string>> ConfigureAndGetAuthUrlAsync(int userId, ConfigureGoogleDriveRequestDto request);

        /// <summary>
        /// Handle OAuth callback, exchange code for tokens, and create/update storage profile.
        /// </summary>
        Task<Result<int>> HandleOAuthCallbackAsync(string code, string state);
    }
}

