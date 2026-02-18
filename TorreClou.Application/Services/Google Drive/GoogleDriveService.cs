using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TorreClou.Core.DTOs.OAuth;
using TorreClou.Core.DTOs.Storage.GoogleDrive;
using TorreClou.Core.Exceptions;
using TorreClou.Core.Interfaces;

namespace TorreClou.Application.Services.Google_Drive
{
    /// <summary>
    /// Google Drive service implementation — manages OAuth credentials and profile connections.
    /// </summary>
    public class GoogleDriveService(
        IGoogleDriveAuthService googleDriveAuthService,
        IConfiguration configuration,
        ILogger<GoogleDriveService> logger) : IGoogleDriveService
    {
        public Task<SavedCredentialsDto> SaveCredentialsAsync(int userId, SaveGoogleDriveCredentialsRequestDto request)
            => googleDriveAuthService.SaveCredentialsAsync(userId, request);

        public Task<List<OAuthCredentialDto>> GetCredentialsAsync(int userId)
            => googleDriveAuthService.GetCredentialsAsync(userId);

        public Task<string> ConnectAsync(int userId, ConnectGoogleDriveRequestDto request)
            => googleDriveAuthService.ConnectAsync(userId, request);

        public Task<string> ReauthenticateAsync(int userId, int profileId)
            => googleDriveAuthService.ReauthenticateAsync(userId, profileId);

        public async Task<string> GetGoogleCallback(string code, string state)
        {
            var frontendUrl = (configuration["FRONTEND_URL"] ?? "http://localhost:3000").TrimEnd('/');
            var redirectBase = $"{frontendUrl}/storage";

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return $"{redirectBase}?error=INVALID_REQUEST&message={HttpUtility.UrlEncode("Missing code or state parameter")}";

            try
            {
                var profileId = await googleDriveAuthService.HandleOAuthCallbackAsync(code, state);
                return $"{redirectBase}?success=true&profileId={profileId}";
            }
            catch (DomainException ex)
            {
                return $"{redirectBase}?error={HttpUtility.UrlEncode(ex.Code)}&message={HttpUtility.UrlEncode(ex.Message)}";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception in GetGoogleCallback");
                return $"{redirectBase}?error=InternalError&message={HttpUtility.UrlEncode("An unexpected error occurred")}";
            }
        }
    }
}
