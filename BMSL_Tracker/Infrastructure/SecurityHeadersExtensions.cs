namespace BMSL_Tracker.Infrastructure;

/// <summary>
/// Attaches hardened response headers to every response. The Content-Security-Policy value is
/// configuration-driven (Security:ContentSecurityPolicy) so operators can adjust trusted origins
/// without a code change.
/// </summary>
public static class SecurityHeadersExtensions
{
    public const string ContentSecurityPolicyConfigKey = "Security:ContentSecurityPolicy";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, string? contentSecurityPolicy)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;

            // Only set if not already present (lets specific endpoints opt out by overriding).
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("X-Frame-Options", "DENY");
            headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
            headers.TryAdd("Permissions-Policy",
                "geolocation=(self), camera=(), microphone=(), display-capture=(), usb=(), fullscreen=(self)");
            headers.TryAdd("X-Permitted-Cross-Domain-Policies", "none");

            if (!string.IsNullOrWhiteSpace(contentSecurityPolicy))
            {
                headers.TryAdd("Content-Security-Policy", contentSecurityPolicy);
            }

            await next();
        });
    }
}
