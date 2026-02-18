using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers
{
    [Route("api/jobs")]
    [Authorize]
    public class JobsController(IJobService jobService, IJobStatusService jobStatusService) : BaseApiController
    {
        [HttpGet]
        public async Task<IActionResult> GetJobs(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] JobStatus? status = null)
        {
            return Ok(await jobService.GetUserJobsAsync(UserId, pageNumber, pageSize, status));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetJob(int id)
        {
            return Ok(await jobService.GetJobByIdAsync(UserId, id));
        }

        /// <summary>
        /// Get the full status timeline for a specific job.
        /// </summary>
        [HttpGet("{id}/timeline")]
        public async Task<IActionResult> GetJobTimeline(int id, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            // Verify the user has access to this job (throws NotFoundException if not found)
            await jobService.GetJobByIdAsync(UserId, id);

            var timeline = await jobStatusService.GetJobTimelinePaginatedAsync(id, pageNumber, pageSize);
            return Ok(timeline);
        }

        [HttpGet("statistics")]
        public async Task<IActionResult> GetJobStatistics()
        {
            return Ok(await jobService.GetUserJobStatisticsAsync(UserId));
        }

        [HttpPost("{id}/retry")]
        public async Task<IActionResult> RetryJob(int id)
        {
            await jobService.RetryJobAsync(id, UserId);
            return Ok();
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelJob(int id)
        {
            await jobService.CancelJobAsync(id, UserId);
            return Ok();
        }
    }
}
