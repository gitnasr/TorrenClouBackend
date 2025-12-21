using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers
{
    [Route("api/jobs")]
    [Authorize]
    public class JobsController(IJobService jobService) : BaseApiController
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
            var result = await jobService.GetJobByIdAsync(UserId, id, UserRole);
            return HandleResult(result);
        }

        [HttpGet("statistics")]
        public async Task<IActionResult> GetJobStatistics()
        {
            var result = await jobService.GetUserJobStatisticsAsync(UserId);
            return HandleResult(result);
        }
    }
}
