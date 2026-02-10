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
        /// Configure Google Drive with user-provided OAuth credentials.
        /// Returns authorization URL for user to complete OAuth flow.
        /// </summary>
        /// <remarks>
        /// Users must provide their own Google Cloud OAuth credentials (ClientId, ClientSecret, RedirectUri).
        /// See docs/GOOGLE_DRIVE_SETUP.md for instructions on creating Google Cloud credentials.
        /// </remarks>
        [HttpPost("configure")]
        public async Task<IActionResult> ConfigureGoogleDrive([FromBody] ConfigureGoogleDriveRequestDto request)
        {
            var result = await googleDriveService.ConfigureAndGetAuthUrlAsync(UserId, request);
            return HandleResult(result, url => new GoogleDriveAuthResponse { AuthorizationUrl = url });
        }

        [HttpGet("callback")]
        [AllowAnonymous]
        public async Task<IActionResult> GoogleDriveCallback([FromQuery] string code, [FromQuery] string state)
        {
            var redirectUrl = await googleDriveService.GetGoogleCallback(code, state);
            return Redirect(redirectUrl);
        }
    }
}
