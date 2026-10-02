using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BMSL_Tracker.Models;

namespace BMSL_Tracker.Controllers;

/// <summary>
/// The tracking dashboard itself requires an authenticated session; <c>/</c> and <c>/Privacy</c>
/// stay open so unauthenticated visitors get the Identity sign-in redirect (or read the policy).
/// </summary>
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    [Authorize]
    public IActionResult Index()
    {
        return View();
    }

    [AllowAnonymous]
    public IActionResult Privacy()
    {
        return View();
    }

    /// <summary>Friendly error page (also targeted by the exception handler and status-code re-execution).</summary>
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(int? code = null)
    {
        var statusCode = code is >= 400 and <= 599 ? code.Value : HttpContext.Response.StatusCode;
        if (statusCode is < 400 or > 599)
        {
            statusCode = StatusCodes.Status500InternalServerError;
        }

        var model = new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            StatusCode = statusCode,
        };

        if (statusCode >= 500)
        {
            _logger.LogWarning("Rendering error page for status code {StatusCode} (request {RequestId}).",
                statusCode, model.RequestId);
        }

        return View(model);
    }
}
