using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.Enums;
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
        var statusCode = error.Code switch
        {
            // 404 Not Found
            ErrorCode.NotFound or ErrorCode.ProfileNotFound or ErrorCode.TorrentNotFound
            or ErrorCode.JobNotFound or ErrorCode.UserNotFound or ErrorCode.FileNotFound
            or ErrorCode.BucketNotFound
                => StatusCodes.Status404NotFound,

            // 401 Unauthorized
            ErrorCode.Unauthorized or ErrorCode.InvalidCredentials
                => StatusCodes.Status401Unauthorized,

            // 403 Forbidden
            ErrorCode.AccessDenied
                => StatusCodes.Status403Forbidden,

            // 409 Conflict
            ErrorCode.DuplicateEmail or ErrorCode.AlreadyDisconnected
            or ErrorCode.JobAlreadyExists or ErrorCode.JobAlreadyCancelled
                => StatusCodes.Status409Conflict,

            // 422 Unprocessable Entity (Validation)
            ErrorCode.Invalid or ErrorCode.InvalidTorrent or ErrorCode.InvalidInfoHash
            or ErrorCode.InvalidFileName or ErrorCode.InvalidFileSize
            or ErrorCode.InvalidProfileName or ErrorCode.InvalidS3Config
            or ErrorCode.InvalidClientId or ErrorCode.InvalidClientSecret
            or ErrorCode.InvalidRedirectUri or ErrorCode.InvalidState
            or ErrorCode.InvalidResponse or ErrorCode.InvalidProfile
            or ErrorCode.InvalidCredentialsJson or ErrorCode.InvalidStorageProfile
            or ErrorCode.V2OnlyNotSupported or ErrorCode.ProfileNameTooShort
            or ErrorCode.ProfileNameTooLong
                => StatusCodes.Status422UnprocessableEntity,

            // 400 Bad Request (default)
            _ => StatusCodes.Status400BadRequest
        };

        return StatusCode(statusCode, new
        {
            code = error.Code.ToString(),
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
}
