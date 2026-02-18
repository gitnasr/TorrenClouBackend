using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.Exceptions;

namespace TorreClou.API.Middleware;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, code, message) = exception switch
        {
            NotFoundException ex        => (StatusCodes.Status404NotFound, ex.Code, ex.Message),
            ValidationException ex      => (StatusCodes.Status422UnprocessableEntity, ex.Code, ex.Message),
            ConflictException ex        => (StatusCodes.Status409Conflict, ex.Code, ex.Message),
            UnauthorizedException ex    => (StatusCodes.Status401Unauthorized, ex.Code, ex.Message),
            ForbiddenException ex       => (StatusCodes.Status403Forbidden, ex.Code, ex.Message),
            BusinessRuleException ex    => (StatusCodes.Status400BadRequest, ex.Code, ex.Message),
            ExternalServiceException ex => (StatusCodes.Status500InternalServerError, ex.Code, ex.Message),

            // Legacy / framework exceptions
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized", exception.Message),
            ArgumentException           => (StatusCodes.Status400BadRequest, "InvalidArgument", exception.Message),
            KeyNotFoundException        => (StatusCodes.Status404NotFound, "NotFound", exception.Message),

            _ => (StatusCodes.Status500InternalServerError, "InternalError", "An unexpected error occurred")
        };

        if (statusCode >= 500)
            logger.LogError(exception, "Server error: {Code} - {Message}", code, message);
        else
            logger.LogWarning(exception, "Client error: {Code} - {Message}", code, message);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = code,
            Detail = message,
            Extensions =
            {
                ["code"] = code
            }
        };

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
