namespace BMSL_Tracker.Models;

public class ErrorViewModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    /// <summary>The HTTP status code being displayed (500 by default).</summary>
    public int StatusCode { get; set; } = StatusCodes.Status500InternalServerError;

    public string Title => StatusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad request.",
        StatusCodes.Status401Unauthorized => "You need to sign in for that.",
        StatusCodes.Status403Forbidden => "Access denied.",
        StatusCodes.Status404NotFound => "Page not found.",
        StatusCodes.Status429TooManyRequests => "Too many requests.",
        >= 500 => "An error occurred while processing your request.",
        _ => "Something went wrong.",
    };

    public string HtmlTitle => StatusCode switch
    {
        StatusCodes.Status404NotFound => "Not found",
        >= 500 => "Error",
        _ => "Problem",
    };
}
