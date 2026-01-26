using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.Shared;

namespace TorreClou.API.Controllers;

/// <summary>
/// Base controller providing common functionality for all API controllers.
/// Eliminates repetitive try-catch blocks and Result handling.
/// </summary>
[ApiController]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// Gets the authenticated user's ID from claims.
    /// </summary>
    protected int UserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
        ?? throw new UnauthorizedAccessException("User ID not found in claims"));

    protected int GetCurrentUserId()
    {
        return UserId;
    }

    /// <summary>
    /// Gets the authenticated user's email from claims.
    /// </summary>
    protected string? UserEmail => User.FindFirst(ClaimTypes.Email)?.Value;

    /// <summary>
    /// Handles a Result object and returns the appropriate IActionResult.
    /// </summary>
    protected IActionResult HandleResult<T>(Result<T> result, int successStatusCode = 200)
    {
        if (result.IsSuccess)
        {
            return successStatusCode switch
            {
                201 => Created(string.Empty, result.Value),
                204 => NoContent(),
                _ => Ok(result.Value)
            };
        }

        return MapErrorToResponse(result.Error);
    }

    /// <summary>
    /// Handles a Result object (without value) and returns the appropriate IActionResult.
    /// </summary>
    protected IActionResult HandleResult(Result result, int successStatusCode = 200)
    {
        if (result.IsSuccess)
        {
            return successStatusCode switch
            {
                201 => Created(string.Empty, null),
                204 => NoContent(),
                _ => Ok()
            };
        }

        return MapErrorToResponse(result.Error);
    }

    /// <summary>
    /// Handles a Result and wraps the value in a custom response object.
    /// </summary>
    protected IActionResult HandleResult<T>(Result<T> result, Func<T, object> transformer, int successStatusCode = 200)
    {
        if (result.IsSuccess)
        {
            var transformed = transformer(result.Value);
            return successStatusCode switch
            {
                201 => Created(string.Empty, transformed),
                204 => NoContent(),
                _ => Ok(transformed)
            };
        }

        return MapErrorToResponse(result.Error);
    }

    /// <summary>
    /// Maps an Error to the appropriate HTTP response based on the error code.
    /// </summary>
    private IActionResult MapErrorToResponse(Error error)
    {
        // Map error codes to HTTP status codes
        var statusCode = error.Code.ToUpperInvariant() switch
        {
            var code when code.Contains("NOT_FOUND") => StatusCodes.Status404NotFound,
            var code when code.Contains("UNAUTHORIZED") || code.Contains("AUTH") => StatusCodes.Status401Unauthorized,
            var code when code.Contains("FORBIDDEN") => StatusCodes.Status403Forbidden,
            var code when code.Contains("CONFLICT") || code.Contains("DUPLICATE") => StatusCodes.Status409Conflict,
            var code when code.Contains("VALIDATION") || code.Contains("INVALID") => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest
        };

        return StatusCode(statusCode, new
        {
            code = error.Code,
            message = error.Message
        });
    }

    /// <summary>
    /// Returns a success response with a custom object.
    /// </summary>
    protected IActionResult Success<T>(T data, int statusCode = 200)
    {
        return statusCode switch
        {
            201 => Created(string.Empty, data),
            204 => NoContent(),
            _ => Ok(data)
        };
    }

    /// <summary>
    /// Returns an error response with a custom code and message.
    /// </summary>
    protected IActionResult Error(string code, string message, int statusCode = 400)
    {
        return StatusCode(statusCode, new
        {
            code,
            message
        });
    }
}

