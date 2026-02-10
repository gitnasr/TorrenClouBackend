using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.DTOs.Jobs;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;

namespace TorreClou.API.Controllers.Torrents
{
    [Authorize]
    [ApiController]
    [Route("api/torrents")]
    public class TorrentFileController(
        IJobService jobService,
        ILogger<TorrentFileController> logger) : BaseApiController
    {

        /// <summary>
        /// Create a job directly without payment.
        /// </summary>
        [HttpPost("create-job")]
        [ProducesResponseType(typeof(JobCreationResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CreateJobAsync([FromBody] CreateJobRequestDto request)
        {
            var userId = GetCurrentUserId();
            
            logger.LogInformation("Create job requested | TorrentFileId: {TorrentFileId} | UserId: {UserId}", request.TorrentFileId, userId);

            var result = await jobService.CreateAndDispatchJobAsync(
                request.TorrentFileId,
                userId,
                request.SelectedFilePaths,
                request.StorageProfileId);

            if (result.IsFailure)
            {
                logger.LogWarning("Job creation failed | TorrentFileId: {TorrentFileId} | UserId: {UserId} | Error: {Error}",
                    request.TorrentFileId, userId, result.Error.Message);
                return HandleResult(result);
            }

            logger.LogInformation("Job created successfully | JobId: {JobId} | TorrentFileId: {TorrentFileId} | UserId: {UserId}",
                result.Value.JobId, request.TorrentFileId, userId);

            return Ok(result.Value);
        }
    }
}
