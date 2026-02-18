using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace TorreClou.API.Controllers;

[ApiController]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// Gets the authenticated user's ID from claims.
    /// </summary>
    protected int UserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException("User ID not found in claims"));

    protected int GetCurrentUserId() => UserId;
}
