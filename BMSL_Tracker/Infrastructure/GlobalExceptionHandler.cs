using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BMSL_Tracker.Infrastructure;

/// <summary>
/// Last-resort exception handler:
///  - logs the full exception with request metadata;
///  - for browser (HTML) requests it defers to the configured friendly error page
///    (<c>/Home/Error</c>) by returning <c>false</c>;
///  - for everything else it writes an RFC 7807 problem document with no sensitive detail.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private static readonly System.Text.Json.JsonSerializerOptions ProblemJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(
            exception,
            "Unhandled exception while processing {Method} {Path} (trace {TraceId})",
            httpContext.Request.Method,
            httpContext.Request.Path,
            httpContext.TraceIdentifier);

        var acceptsHtml = httpContext.Request.Headers.Accept
            .ToString()
            .Contains("text/html", StringComparison.OrdinalIgnoreCase);

        if (acceptsHtml)
        {
            // Friendly error page (ExceptionHandlingPath) takes over.
            return false;
        }

        if (httpContext.Response.HasStarted)
        {
            return true;
        }

        httpContext.Response.Clear();
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var problem = new ProblemDetails
        {
            Title = "An unexpected error occurred while processing the request.",
            Status = StatusCodes.Status500InternalServerError,
            Instance = httpContext.Request.Path,
        };

        // Write manually so the RFC 7807 media type survives (WriteAsJsonAsync forces application/json).
        var payload = System.Text.Json.JsonSerializer.Serialize(problem, ProblemJsonOptions);
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsync(payload, cancellationToken);
        return true;
    }
}
