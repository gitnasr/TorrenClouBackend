using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers.Admin
{
    [Route("api/admin/jobs/sync")]
    [ApiController]
    [Authorize]
    public class SyncJobsController(ISyncJobsService syncJobsService) : BaseApiController
    {
        [HttpGet]
        public async Task<IActionResult> GetAllSyncJobs(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] SyncStatus? status = null)
        {
            var result = await syncJobsService.GetAllSyncJobsAsync(pageNumber, pageSize, status);
            return HandleResult(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetSyncJob(int id)
        {
            var result = await syncJobsService.GetSyncJobByIdAsync(id);
            return HandleResult(result);
        }

        [HttpGet("statistics")]
        public async Task<IActionResult> GetSyncJobStatistics()
        {
            var result = await syncJobsService.GetSyncJobStatisticsAsync();
            return HandleResult(result);
        }
    }
}
