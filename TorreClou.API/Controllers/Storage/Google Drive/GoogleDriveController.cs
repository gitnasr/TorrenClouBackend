using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.DTOs.Storage;
using TorreClou.Core.DTOs.Storage.GoogleDrive;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers.Storage
{
    [Route("api/storage/gdrive")]
    [ApiController]
    [Authorize]
    public class GoogleDriveController(IGoogleDriveService googleDriveService) : BaseApiController
    {
        /// <summary>
        /// Save reusable Google OAuth app credentials (ClientId, ClientSecret, RedirectUri).
        /// These can be linked to multiple storage profiles when connecting new Drive accounts.
        /// Upserts by ClientId — if you already saved the same ClientId, it updates.
        /// </summary>
        [HttpPost("credentials")]
        public async Task<IActionResult> SaveCredentials([FromBody] SaveGoogleDriveCredentialsRequestDto request)
        {
            var result = await googleDriveService.SaveCredentialsAsync(UserId, request);
            return HandleResult(result, value => new SaveGoogleDriveCredentialsResponseDto
            {
                CredentialId = value.CredentialId,
                Name = value.Name
            });
        }

        /// <summary>
        /// List all saved OAuth app credentials for the current user.
        /// ClientId is masked; ClientSecret is never returned.
        /// </summary>
        [HttpGet("credentials")]
        public async Task<IActionResult> GetCredentials()
        {
            var result = await googleDriveService.GetCredentialsAsync(UserId);
            return HandleResult(result);
        }

        /// <summary>
        /// Connect a new Google Drive account using a saved credential.
        /// Creates a storage profile and returns the Google authorization URL.
        /// </summary>
        [HttpPost("connect")]
        public async Task<IActionResult> Connect([FromBody] ConnectGoogleDriveRequestDto request)
        {
            var result = await googleDriveService.ConnectAsync(UserId, request);
            return HandleResult(result, url => new GoogleDriveAuthResponse { AuthorizationUrl = url });
        }

        /// <summary>
        /// Re-initiate OAuth consent flow for a profile whose refresh token has expired.
        /// Uses the linked OAuthCredential — user does not need to re-enter credentials.
        /// </summary>
        [HttpPost("{profileId:int}/reauthenticate")]
        public async Task<IActionResult> Reauthenticate(int profileId)
        {
            var result = await googleDriveService.ReauthenticateAsync(UserId, profileId);
            return HandleResult(result, url => new GoogleDriveAuthResponse { AuthorizationUrl = url });
        }

        /// <summary>
        /// OAuth callback endpoint — Google redirects here after user grants consent.
        /// </summary>
        [HttpGet("callback")]
        [AllowAnonymous]
        public async Task<IActionResult> GoogleDriveCallback([FromQuery] string code, [FromQuery] string state)
        {
            var redirectUrl = await googleDriveService.GetGoogleCallback(code, state);
            return Redirect(redirectUrl);
        }
    }
}
