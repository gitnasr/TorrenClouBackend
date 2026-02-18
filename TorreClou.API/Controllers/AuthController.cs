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
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequestDto request)
    {
        var response = await authService.LoginAsync(request.Email, request.Password);
        return Ok(response);
    }
}
