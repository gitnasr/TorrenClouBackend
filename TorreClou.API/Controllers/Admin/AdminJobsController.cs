using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers.Admin;

[Route("api/admin/jobs")]
[Authorize(Roles = "Admin")]
public class AdminJobsController(IJobService jobService) : BaseApiController
{
    [HttpPost("{id}/retry")]
    public async Task<IActionResult> RetryJob(int id)
    {
        var result = await jobService.RetryJobAsync(id, UserId, UserRole.Admin);
        return HandleResult(result);
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> CancelJob(int id)
    {
        var result = await jobService.CancelJobAsync(id, UserId, UserRole.Admin);
        return HandleResult(result);
    }
}








