using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.DTOs.Storage;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers.Storage.S3
{
    [Route("api/storage/s3")]
    [Authorize]
    public class S3StorageController(IS3StorageService s3StorageService) : BaseApiController
    {
        [HttpPost("configure")]
        public async Task<IActionResult> ConfigureS3([FromBody] ConfigureS3RequestDto request)
        {
            var result = await s3StorageService.ConfigureS3StorageAsync(
                UserId,
                request.ProfileName,
                request.S3Endpoint,
                request.S3AccessKey,
                request.S3SecretKey,
                request.S3BucketName,
                request.S3Region,
                request.SetAsDefault);

            return HandleResult(result);
        }
    }
}
