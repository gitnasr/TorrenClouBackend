using System.Web;
using TorreClou.Core.DTOs.Storage.GoogleDrive;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;

namespace TorreClou.Application.Services.Google_Drive
{
    public class GoogleDriveService(IGoogleDriveAuthService googleDriveAuthService) : IGoogleDriveService
    {
        public async Task<string> GetAuthorizationUrlAsync(int userId, string? profileName = null)
        {
            var result = await googleDriveAuthService.GetAuthorizationUrlAsync(userId, profileName);
            if (result.IsFailure)
            {
                throw new UnauthorizedAccessException($"Failed to get authorization URL: {result.Error}");
            }
            return result.Value;
        }

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
                var errorRedirect = $"{redirectBase}?error={HttpUtility.UrlEncode(result.Error.Code)}&message={HttpUtility.UrlEncode(result.Error.Message)}";
                return errorRedirect;
            }

            // Success - redirect with profile ID
            var successRedirect = $"{redirectBase}?success=true&profileId={result.Value}";
            return successRedirect;
        }
    }
}
