using System.IO.Compression;
using System.Net;
using System.Threading.RateLimiting;
using BMSL_Tracker.Data;
using BMSL_Tracker.Hubs;
using BMSL_Tracker.Infrastructure;
using BMSL_Tracker.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var environment = builder.Environment;

// ---------------------------------------------------------------------------
// Logging — structured JSON in production, readable console in development.
// ---------------------------------------------------------------------------
if (!environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole();
}

builder.WebHost.ConfigureKestrel(static options =>
{
    // Do not advertise the server stack.
    options.AddServerHeader = false;
});

// ---------------------------------------------------------------------------
// Database — SQL Server for production, SQLite for local dev/tests.
// ---------------------------------------------------------------------------
var databaseProvider = configuration["Database:Provider"] ?? "SqlServer";
var useSqlite = string.Equals(databaseProvider, "Sqlite", StringComparison.OrdinalIgnoreCase);

var connectionString = configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (useSqlite)
    {
        options.UseSqlite(connectionString);
    }
    else
    {
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
            maxRetryCount: 6,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null));
    }
});

if (environment.IsDevelopment() && !useSqlite)
{
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();
}

// ---------------------------------------------------------------------------
// Identity + authentication
// ---------------------------------------------------------------------------
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;

    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = false;

    options.User.RequireUniqueEmail = true;
    options.User.AllowedUserNameCharacters =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-";

    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    options.Cookie.Name = "BMSL_Tracker.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax; // required for the top-level Google OAuth redirect back
    options.Cookie.SecurePolicy = environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

// Google sign-in / sign-up: enabled iff credentials are configured
// (environment variables Authentication__Google__ClientId / __ClientSecret, user-secrets in dev).
var googleClientId = configuration["Authentication:Google:ClientId"];
var googleClientSecret = configuration["Authentication:Google:ClientSecret"];
var googleEnabled = !string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret);

if (googleEnabled)
{
    builder.Services
        .AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId!;
            options.ClientSecret = googleClientSecret!;
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.Scope.Add("email");
            options.Scope.Add("profile");
            options.ClaimActions.MapJsonKey(
                ExternalUserNames.DisplayNameClaimType, "name");
        });
}

// ---------------------------------------------------------------------------
// Options, domain services, health checks
// ---------------------------------------------------------------------------
builder.Services.AddOptions<TrackerOptions>()
    .Bind(configuration.GetSection(TrackerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<ILocationService, LocationService>();
builder.Services.AddScoped<IExternalUserProvisioner, ExternalUserProvisioner>();
builder.Services.AddHostedService<LocationRetentionService>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("database", tags: new[] { "ready" });

// ---------------------------------------------------------------------------
// MVC + anti-forgery by default on all non-GET endpoints
// ---------------------------------------------------------------------------
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

// ---------------------------------------------------------------------------
// Rate limiting — fixed window per client IP over the authentication endpoints
// (the Account controller opts in via [EnableRateLimiting("auth")]).
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        var response = context.HttpContext.Response;
        if (!response.HasStarted)
        {
            response.StatusCode = StatusCodes.Status429TooManyRequests;
            await response.WriteAsync(
                "Too many requests. Please wait a moment before trying again.", cancellationToken);
        }
    };
});

// ---------------------------------------------------------------------------
// SignalR — real-time tracking channel (+ optional Redis backplane for scale-out)
// ---------------------------------------------------------------------------
var signalR = builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    options.MaximumReceiveMessageSize = 32 * 1024; // GPS samples are tiny; keep the ceiling tight
    options.StreamBufferCapacity = 10;
    options.MaximumParallelInvocationsPerClient = 2;
});

var redisConnectionString = configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    // To namespace channels across shared Redis, append ",instanceName=bmsl-tracker"
    // to the connection string (StackExchange.Redis option).
    signalR.AddStackExchangeRedis(redisConnectionString);
}

// ---------------------------------------------------------------------------
// Data Protection — pin key ring location + application name for multi-instance deployments
// ---------------------------------------------------------------------------
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("BMSL Tracker");
var keyRingPath = configuration["DataProtection:KeyRingPath"];
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    Directory.CreateDirectory(keyRingPath);
    dataProtection = dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
}

// ---------------------------------------------------------------------------
// Reverse-proxy support (X-Forwarded-For / X-Forwarded-Proto) — opt-in per deployment
// ---------------------------------------------------------------------------
var forwardedHeadersEnabled = configuration.GetValue("ForwardedHeaders:Enabled", false);
if (forwardedHeadersEnabled)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        {
            if (IPAddress.TryParse(proxy, out var proxyAddress))
            {
                options.KnownProxies.Add(proxyAddress);
            }
        }

        foreach (var network in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
        {
            var parts = network.Split('/', 2);
            if (parts.Length == 2
                && IPAddress.TryParse(parts[0], out var networkAddress)
                && int.TryParse(parts[1], out var prefixLength))
            {
                // Fully qualified: .NET 9's System.Net.IPNetwork shadows the
                // HttpOverrides type of the same name under these usings.
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(networkAddress, prefixLength));
            }
        }
    });
}

// ---------------------------------------------------------------------------
// Response compression for dynamically served text assets
// ---------------------------------------------------------------------------
builder.Services.Configure<GzipCompressionProviderOptions>(
    options => options.Level = CompressionLevel.Fastest);
builder.Services.Configure<BrotliCompressionProviderOptions>(
    options => options.Level = CompressionLevel.Fastest);
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);

// ---------------------------------------------------------------------------
// Hardening: HSTS + RFC 7807 + friendly error page
// ---------------------------------------------------------------------------
builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(configuration.GetValue("Security:HstsMaxAgeDays", 365));
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var requireHttps = configuration.GetValue("Security:RequireHttps", true);
var contentSecurityPolicy = configuration[SecurityHeadersExtensions.ContentSecurityPolicyConfigKey];

var app = builder.Build();

// ---------------------------------------------------------------------------
// Startup: configuration summary + database initialization
// ---------------------------------------------------------------------------
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BMSL_Tracker.Startup");

if (!googleEnabled)
{
    startupLogger.LogWarning(
        "Google sign-in is DISABLED because Authentication:Google:ClientId/ClientSecret are not set. " +
        "See docs/GOOGLE-AUTH.md for setup.");
}

startupLogger.LogInformation("Using {Provider} database provider.", useSqlite ? "SQLite" : "SQL Server");

try
{
    await DbInitializer.InitializeAsync(app.Services, configuration, app.Lifetime.ApplicationStopping);
}
catch (Exception ex) when (!environment.IsDevelopment())
{
    startupLogger.LogCritical(ex, "Database initialization failed; aborting startup.");
    throw;
}
catch (Exception ex)
{
    startupLogger.LogError(ex, "Database initialization failed (ignored in Development).");
}

// ---------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------
if (forwardedHeadersEnabled)
{
    app.UseForwardedHeaders();
}

if (environment.IsDevelopment())
{
    // The host automatically enables the developer exception page in Development.
    if (!useSqlite)
    {
        app.UseMigrationsEndPoint();
    }
}
else
{
    // GlobalExceptionHandler logs every failure; for HTML requests it defers to the friendly page.
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// 404/400/etc. re-render the friendly error page with the right status code.
app.UseStatusCodePagesWithReExecute("/Home/Error", "?code={0}");

app.UseSecurityHeaders(contentSecurityPolicy);
app.UseResponseCompression();

if (requireHttps)
{
    app.UseHttpsRedirection();
}

app.UseRateLimiter();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<TrackerHub>("/trackerHub");

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.Run();
