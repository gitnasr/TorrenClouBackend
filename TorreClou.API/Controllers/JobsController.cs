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
            var result = await jobService.GetUserJobsAsync(UserId, pageNumber, pageSize, status);
            return HandleResult(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetJob(int id)
        {
            var result = await jobService.GetJobByIdAsync(UserId, id);
            return HandleResult(result);
        }

        /// <summary>
        /// Get the full status timeline for a specific job.
        /// </summary>
        [HttpGet("{id}/timeline")]
        public async Task<IActionResult> GetJobTimeline(int id, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            // First verify the user has access to this job
            var jobResult = await jobService.GetJobByIdAsync(UserId, id);
            if (!jobResult.IsSuccess)
            {
                return HandleResult(jobResult);
            }

            var timeline = await jobStatusService.GetJobTimelinePaginatedAsync(id, pageNumber, pageSize);
            return Ok(timeline);
        }

        [HttpGet("statistics")]
        public async Task<IActionResult> GetJobStatistics()
        {
            var result = await jobService.GetUserJobStatisticsAsync(UserId);
            return HandleResult(result);
        }

        [HttpPost("{id}/retry")]
        public async Task<IActionResult> RetryJob(int id)
        {
            var result = await jobService.RetryJobAsync(id, UserId);
            return HandleResult(result);
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelJob(int id)
        {
            var result = await jobService.CancelJobAsync(id, UserId);
            return HandleResult(result);
        }

      
    }
}
