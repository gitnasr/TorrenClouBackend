using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace TorreClou.API.Controllers.Admin
{
    [Route("api/admin/jobs/sync")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class SyncJobsController : ControllerBase
    {
    }
}
