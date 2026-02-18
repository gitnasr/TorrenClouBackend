using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers.Torrents
{
    [Authorize]
    [ApiController]
    [Route("api/torrents")]
    public class TorrentFileController(
        IJobService jobService,
        ILogger<TorrentFileController> logger) : BaseApiController
    {
      
        [HttpPost("create-job")]
 
        public async Task<IActionResult> CreateJobAsync([FromBody] CreateJobRequestDto request)
        {
            var userId = GetCurrentUserId();

            logger.LogInformation("Create job requested | TorrentFileId: {TorrentFileId} | UserId: {UserId}", request.TorrentFileId, userId);

            var result = await jobService.CreateAndDispatchJobAsync(
                request.TorrentFileId,
                userId,
                request.SelectedFilePaths,
                request.StorageProfileId);

            logger.LogInformation("Job created successfully | JobId: {JobId} | TorrentFileId: {TorrentFileId} | UserId: {UserId}",
                result.JobId, request.TorrentFileId, userId);

            return Ok(result);
        }
    }
}
