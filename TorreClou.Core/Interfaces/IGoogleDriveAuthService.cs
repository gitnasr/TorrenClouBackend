using TorreClou.Core.DTOs.OAuth;
using TorreClou.Core.DTOs.Storage.GoogleDrive;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{

    public interface IGoogleDriveAuthService
    {

        Task<Result<SavedCredentialsDto>> SaveCredentialsAsync(int userId, SaveGoogleDriveCredentialsRequestDto request);
        /// <summary>
        /// List all saved OAuth app credentials for a user.
        /// </summary>
        Task<Result<List<OAuthCredentialDto>>> GetCredentialsAsync(int userId);

        /// <summary>
        /// Connect a new Google Drive account: creates profile + starts OAuth flow in one step.
        /// Returns the Google authorization URL.
        /// </summary>
        Task<Result<string>> ConnectAsync(int userId, ConnectGoogleDriveRequestDto request);

        /// <summary>
        /// Re-initiate OAuth consent flow for a profile whose refresh token has expired.
        /// Uses the linked OAuthCredential — user does not need to re-enter them.
        /// </summary>
        Task<Result<string>> ReauthenticateAsync(int userId, int profileId);

        /// <summary>
        /// Handle OAuth callback, exchange code for tokens, and update the storage profile.
        /// </summary>
        Task<Result<int>> HandleOAuthCallbackAsync(string code, string state);
    }
}

