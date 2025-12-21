using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.DTOs.Storage;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers.Storage
{
    [Route("api/storage/gdrive")]
    [ApiController]
    [Authorize]
    public class GoogleDriveController(IGoogleDriveService googleDriveService) : BaseApiController
    {
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
