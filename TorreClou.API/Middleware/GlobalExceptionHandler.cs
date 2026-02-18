using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace TorreClou.API.Middleware;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, code) = exception switch
        {
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized", "UNAUTHORIZED"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Bad Request", "INVALID_ARGUMENT"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found", "NOT_FOUND"),
            _ => (StatusCodes.Status500InternalServerError, "Server Error", "INTERNAL_ERROR")
        };

        // Log based on severity
        if (statusCode >= 500)
        {
            logger.LogError(exception, "Server error occurred: {Message}", exception.Message);
        }
        else
        {
            logger.LogWarning(exception, "Client error occurred: {Code} - {Message}", code, exception.Message);
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
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
