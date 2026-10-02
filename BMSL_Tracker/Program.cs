using System.Net;
using System.Threading.RateLimiting;
using BMSL_Tracker.Data;
using BMSL_Tracker.Hubs;
using BMSL_Tracker.Services;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "A database connection string is required. Configure ConnectionStrings:DefaultConnection " +
        "(for example with the ConnectionStrings__DefaultConnection environment variable). See README.md.");
}

builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
        sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)));

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();
}

builder.Services
    .AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = true;

        options.Password.RequiredLength = 12;
        options.Password.RequiredUniqueChars = 4;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;

        options.User.RequireUniqueEmail = true;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

var isDevelopment = builder.Environment.IsDevelopment();
var secureCookiePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = isDevelopment ? "BMSL.Auth" : "__Host-BMSL.Auth";
    options.Cookie.Path = "/";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = secureCookiePolicy;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
});

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = isDevelopment ? "BMSL.Antiforgery" : "__Host-BMSL.Antiforgery";
    options.Cookie.Path = "/";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = secureCookiePolicy;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("account", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        }));
});

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
var hasGoogleClientId = !string.IsNullOrWhiteSpace(googleClientId);
var hasGoogleClientSecret = !string.IsNullOrWhiteSpace(googleClientSecret);
if (hasGoogleClientId != hasGoogleClientSecret)
{
    throw new InvalidOperationException(
        "Configure both Authentication:Google:ClientId and Authentication:Google:ClientSecret, or leave both unset to disable Google sign-in.");
}

if (hasGoogleClientId)
{
    builder.Services.AddAuthentication()
        .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
        {
            options.ClientId = googleClientId!;
            options.ClientSecret = googleClientSecret!;
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.SaveTokens = false;
            options.ClaimActions.MapJsonKey("urn:google:email_verified", "verified_email");
            options.ClaimActions.MapJsonKey("urn:google:email_verified_v2", "email_verified");
        });
}

var knownProxyAddresses = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxyAddress in knownProxyAddresses)
    {
        if (IPAddress.TryParse(proxyAddress, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }
});

var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
        .SetApplicationName("BMSL_Tracker");
}

builder.Services.AddSingleton<ILocationService, LocationService>();
builder.Services.AddSingleton<IAccountEmailSender, SmtpAccountEmailSender>();
builder.Services.AddHostedService<LocationRetentionService>();
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddSignalR();

var app = builder.Build();

app.UseForwardedHeaders();

// SignalR WebSockets are not covered by browser CORS checks. Reject cross-site browser
// origins for the cookie-authenticated location hub while allowing non-browser clients
// that do not send an Origin header.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/trackerHub", StringComparison.OrdinalIgnoreCase)
        && context.Request.Headers.TryGetValue("Origin", out var originHeader))
    {
        var originValue = originHeader.ToString();
        var requestPort = context.Request.Host.Port;
        var isAllowedOrigin = Uri.TryCreate(originValue, UriKind.Absolute, out var origin)
            && (origin.Scheme == Uri.UriSchemeHttp || origin.Scheme == Uri.UriSchemeHttps)
            && string.Equals(origin.IdnHost, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && (!requestPort.HasValue || origin.Port == requestPort.Value);

        if (!isAllowedOrigin)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
    }

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }))
    .AllowAnonymous();
app.MapGet("/health/ready", CheckReadinessAsync)
    .AllowAnonymous();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<TrackerHub>("/trackerHub");

app.Run();

static async Task<IResult> CheckReadinessAsync(ApplicationDbContext db, CancellationToken cancellationToken)
{
    try
    {
        if (await db.Database.CanConnectAsync(cancellationToken))
        {
            return Results.Ok(new { status = "ready" });
        }
    }
    catch (Exception) when (!cancellationToken.IsCancellationRequested)
    {
        // Deliberately keep database/provider details out of the public readiness response.
    }

    return Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "The application is not ready.");
}
