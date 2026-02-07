using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.DTOs.Storage;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers.Storage
{
    [Authorize]
    [Route("api/storage")]
    public class StorageProviderController : BaseApiController
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
            _logger.LogInformation("Configure S3 storage requested | UserId: {UserId}", UserId);

            var result = await _storageProfilesService.ConfigureS3StorageAsync(
                UserId,
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
                    UserId, result.Error.Message);
                return BadRequest(new { error = result.Error.Message });
            }

            _logger.LogInformation("S3 storage configured successfully | ProfileId: {ProfileId} | UserId: {UserId}",
                result.Value.StorageProfileId, UserId);

            return Ok(result.Value);
        }

        /// <summary>
        /// Get all configured storage providers for the authenticated user
        /// </summary>
        [HttpGet("providers")]
        [ProducesResponseType(typeof(StorageProvidersListDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProvidersAsync()
        {
            _logger.LogDebug("Get storage providers requested | UserId: {UserId}", UserId);

            var result = await _storageProfilesService.GetUserStorageProvidersAsync(UserId);

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
            _logger.LogInformation("Delete storage provider requested | ProfileId: {ProfileId} | UserId: {UserId}",
                profileId, UserId);

            var result = await _storageProfilesService.DeleteStorageProfileAsync(UserId, profileId);

            if (result.IsFailure)
            {
                _logger.LogWarning("Storage provider deletion failed | ProfileId: {ProfileId} | UserId: {UserId} | Error: {Error}",
                    profileId, UserId, result.Error.Message);
                return NotFound(new { error = result.Error.Message });
            }

            _logger.LogInformation("Storage provider deleted successfully | ProfileId: {ProfileId} | UserId: {UserId}",
                profileId, UserId);

            return Ok(new { success = true, message = "Storage provider removed" });
        }
    }
}
