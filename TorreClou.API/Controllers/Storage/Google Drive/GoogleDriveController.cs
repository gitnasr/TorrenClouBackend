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
        [HttpPost("configure")]
        public async Task<IActionResult> ConfigureGoogleDrive([FromBody] ConfigureGoogleDriveRequestDto request)
        {
            var result = await googleDriveService.ConfigureAndGetAuthUrlAsync(UserId, request);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error.Message, code = result.Error.Code });

            return Ok(new GoogleDriveAuthResponse { AuthorizationUrl = result.Value });
        }

        /// <summary>
        /// Legacy endpoint - Connect Google Drive using environment credentials.
        /// Prefer using POST /configure with user-provided credentials.
        /// </summary>
        [HttpGet("connect")]
        public async Task<IActionResult> ConnectGoogleDrive([FromQuery] string? profileName = null)
        {
            var result = await googleDriveService.GetAuthorizationUrlAsync(UserId, profileName);

            return Ok(new GoogleDriveAuthResponse
            {
                AuthorizationUrl = result
            });
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
