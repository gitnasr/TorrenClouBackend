using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.DTOs.Auth;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : BaseApiController
{
    /// <summary>
    /// Login with email and password (configured in environment variables)
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequestDto request)
    {
        var result = await authService.LoginAsync(request.Email, request.Password);
        return HandleResult(result);
    }
}
