using System.Web;
using Microsoft.Extensions.Configuration;
using TorreClou.Core.DTOs.Storage.GoogleDrive;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;

namespace TorreClou.Application.Services.Google_Drive
{
    /// <summary>
    /// Google Drive service implementation — manages OAuth credentials and profile connections.
    /// </summary>
    public class GoogleDriveService(
        IGoogleDriveAuthService googleDriveAuthService,
        IConfiguration configuration) : IGoogleDriveService
    {
        public async Task<Result<(int CredentialId, string Name)>> SaveCredentialsAsync(int userId, SaveGoogleDriveCredentialsRequestDto request)
        {
            return await googleDriveAuthService.SaveCredentialsAsync(userId, request);
        }

        public async Task<Result<List<OAuthCredentialDto>>> GetCredentialsAsync(int userId)
        {
            return await googleDriveAuthService.GetCredentialsAsync(userId);
        }

        public async Task<Result<string>> ConnectAsync(int userId, ConnectGoogleDriveRequestDto request)
        {
            return await googleDriveAuthService.ConnectAsync(userId, request);
        }

        public async Task<Result<string>> ReauthenticateAsync(int userId, int profileId)
        {
            return await googleDriveAuthService.ReauthenticateAsync(userId, profileId);
        }

        public async Task<string> GetGoogleCallback(string code, string state)
        {
            var frontendUrl = configuration["FRONTEND_URL"] ?? "http://localhost:3000";

            frontendUrl = frontendUrl.TrimEnd('/');
            var redirectBase = $"{frontendUrl}/storage";

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            {
                var errorRedirect = $"{redirectBase}?error=INVALID_REQUEST&message={HttpUtility.UrlEncode("Missing code or state parameter")}";
                return errorRedirect;
            }

            var result = await googleDriveAuthService.HandleOAuthCallbackAsync(code, state);

            if (result.IsFailure)
            {
                var errorRedirect = $"{redirectBase}?error={HttpUtility.UrlEncode(result.Error.Code.ToString())}&message={HttpUtility.UrlEncode(result.Error.Message)}";
                return errorRedirect;
            }

            // Success - redirect with profile ID
            var successRedirect = $"{redirectBase}?success=true&profileId={result.Value}";
            return successRedirect;
        }
    }
}
