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
        {
            var result = await storageProfilesService.GetStorageProfilesAsync(UserId);
            return HandleResult(result);
        }

        [HttpGet("profiles/{id}")]
        public async Task<IActionResult> GetStorageProfile(int id)
        {
            var result = await storageProfilesService.GetStorageProfileAsync(UserId, id);
            return HandleResult(result);
        }

        [HttpPost("profiles/{id}/set-default")]
        public async Task<IActionResult> SetDefaultProfile(int id)
        {
            var result = await storageProfilesService.SetDefaultProfileAsync(UserId, id);
            return HandleResult(result);
        }

        [HttpPost("profiles/{id}/disconnect")]
        public async Task<IActionResult> DisconnectProfile(int id)
        {
            var result = await storageProfilesService.DisconnectProfileAsync(UserId, id);
            return HandleResult(result);
        }

        [HttpDelete("profiles/{id}")]
        public async Task<IActionResult> DeleteProfile(int id)
        {
            var result = await storageProfilesService.DeleteStorageProfileAsync(UserId, id);
            return HandleResult(result);
        }
    }
}

