using System.Web;
using TorreClou.Core.DTOs.Storage.GoogleDrive;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;

namespace TorreClou.Application.Services.Google_Drive
{
    /// <summary>
    /// Google Drive service implementation.
    /// Credentials are configured per-user via API (not environment variables).
    /// </summary>
    public class GoogleDriveService(IGoogleDriveAuthService googleDriveAuthService) : IGoogleDriveService
    {
        public async Task<Result<string>> ConfigureAndGetAuthUrlAsync(int userId, ConfigureGoogleDriveRequestDto request)
        {
            return await googleDriveAuthService.ConfigureAndGetAuthUrlAsync(userId, request);
        }

        public async Task<string> GetGoogleCallback(string code, string state)
        {
            var frontendUrl = "http://localhost:3000";

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
