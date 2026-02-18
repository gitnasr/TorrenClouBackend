using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers.Storage
{
    [Route("api/storage")]
    [Authorize]
    public class StorageProfilesController(
        IStorageProfilesService storageProfilesService
        ) : BaseApiController
    {
        [HttpGet("profiles")]
        public async Task<IActionResult> GetStorageProfiles()
            => Ok(await storageProfilesService.GetStorageProfilesAsync(UserId));

        [HttpGet("profiles/{id}")]
        public async Task<IActionResult> GetStorageProfile(int id)
            => Ok(await storageProfilesService.GetStorageProfileAsync(UserId, id));

        [HttpPost("profiles/{id}/set-default")]
        public async Task<IActionResult> SetDefaultProfile(int id)
        {
            await storageProfilesService.SetDefaultProfileAsync(UserId, id);
            return Ok();
        }

        [HttpPost("profiles/{id}/disconnect")]
        public async Task<IActionResult> DisconnectProfile(int id)
        {
            await storageProfilesService.DisconnectProfileAsync(UserId, id);
            return Ok();
        }

        [HttpDelete("profiles/{id}")]
        public async Task<IActionResult> DeleteProfile(int id)
        {
            await storageProfilesService.DeleteStorageProfileAsync(UserId, id);
            return NoContent();
        }
    }
}
