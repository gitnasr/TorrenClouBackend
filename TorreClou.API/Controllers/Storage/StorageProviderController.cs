using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.DTOs.Storage;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;

namespace TorreClou.API.Controllers.Storage
{
    [Authorize]
    [ApiController]
    [Route("api/storage")]
    public class StorageProviderController : ControllerBase
    {
        private readonly IStorageProfilesService _storageProfilesService;
        private readonly ILogger<StorageProviderController> _logger;

        public StorageProviderController(
            IStorageProfilesService storageProfilesService,
            ILogger<StorageProviderController> logger)
        {
            _storageProfilesService = storageProfilesService;
            _logger = logger;
        }

        /// <summary>
        /// Configure S3 storage provider for the authenticated user
        /// </summary>
        [HttpPost("configure-s3")]
        [ProducesResponseType(typeof(StorageProfileResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ConfigureS3Async([FromBody] ConfigureS3RequestDto request)
        {
            var userId = int.Parse(User.FindFirst("sub")?.Value ?? User.FindFirst("userId")?.Value);
            
            _logger.LogInformation("Configure S3 storage requested | UserId: {UserId}", userId);

            var result = await _storageProfilesService.ConfigureS3StorageAsync(
                userId,
                request.ProfileName,
                request.S3Endpoint,
                request.S3AccessKey,
                request.S3SecretKey,
                request.S3BucketName,
                request.S3Region,
                request.SetAsDefault);

            if (result.IsFailure)
            {
                _logger.LogWarning("S3 configuration failed | UserId: {UserId} | Error: {Error}",
                    userId, result.Error.Message);
                return BadRequest(new { error = result.Error.Message });
            }

            _logger.LogInformation("S3 storage configured successfully | ProfileId: {ProfileId} | UserId: {UserId}",
                result.Value.StorageProfileId, userId);

            return Ok(result.Value);
        }

        /// <summary>
        /// Get all configured storage providers for the authenticated user
        /// </summary>
        [HttpGet("providers")]
        [ProducesResponseType(typeof(StorageProvidersListDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProvidersAsync()
        {
            var userId = int.Parse(User.FindFirst("sub")?.Value ?? User.FindFirst("userId")?.Value ?? "0");
            
            _logger.LogDebug("Get storage providers requested | UserId: {UserId}", userId);

            var result = await _storageProfilesService.GetUserStorageProvidersAsync(userId);

            if (result.IsFailure)
            {
                return BadRequest(new { error = result.Error.Message });
            }

            return Ok(result.Value);
        }

        /// <summary>
        /// Delete a storage provider
        /// </summary>
        [HttpDelete("{profileId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteProviderAsync(int profileId)
        {
            var userId = int.Parse(User.FindFirst("sub")?.Value ?? User.FindFirst("userId")?.Value ?? "0");
            
            _logger.LogInformation("Delete storage provider requested | ProfileId: {ProfileId} | UserId: {UserId}",
                profileId, userId);

            var result = await _storageProfilesService.DeleteStorageProfileAsync(userId, profileId);

            if (result.IsFailure)
            {
                _logger.LogWarning("Storage provider deletion failed | ProfileId: {ProfileId} | UserId: {UserId} | Error: {Error}",
                    profileId, userId, result.Error.Message);
                return NotFound(new { error = result.Error.Message });
            }

            _logger.LogInformation("Storage provider deleted successfully | ProfileId: {ProfileId} | UserId: {UserId}",
                profileId, userId);

            return Ok(new { success = true, message = "Storage provider removed" });
        }
    }
}
