using TorreClou.Core.DTOs.OAuth;
using TorreClou.Core.DTOs.Storage.GoogleDrive;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    /// <summary>
    /// Google Drive service — manages OAuth credentials and profile connections.
    /// </summary>
    public interface IGoogleDriveService
    {
        /// <summary>
        /// Save reusable OAuth app credentials.
        /// </summary>
        Task<Result<SavedCredentialsDto>> SaveCredentialsAsync(int userId, SaveGoogleDriveCredentialsRequestDto request);

        /// <summary>
        /// List saved OAuth app credentials for a user.
        /// </summary>
        Task<Result<List<OAuthCredentialDto>>> GetCredentialsAsync(int userId);

        /// <summary>
        /// Connect a new Google Drive account using a saved credential.
        /// </summary>
        Task<Result<string>> ConnectAsync(int userId, ConnectGoogleDriveRequestDto request);

        /// <summary>
        /// Re-initiate OAuth for a profile with expired refresh token.
        /// </summary>
        Task<Result<string>> ReauthenticateAsync(int userId, int profileId);

        /// <summary>
        /// Handle OAuth callback from Google and redirect to frontend.
        /// </summary>
        Task<string> GetGoogleCallback(string code, string state);
    }
}
