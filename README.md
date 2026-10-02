# BMSL Tracker

A server-rendered ASP.NET Core MVC app for authenticated, real-time team location sharing. The map uses Leaflet and OpenStreetMap tiles; SignalR delivers updates; ASP.NET Core Identity and SQL Server manage accounts and location history.

> **Location data is sensitive.** Every signed-in tracker user can see the active locations of other signed-in users. Before deployment, configure an organization email-domain allow-list, publish an approved privacy notice, and restrict database and backup access.

## Stack

- .NET 10 (LTS), ASP.NET Core MVC and SignalR
- ASP.NET Core Identity with confirmed-email password sign-up and Google external sign-in
- Entity Framework Core 10 with SQL Server
- Leaflet 1.9.4 and OpenStreetMap

## Local development

Prerequisites: .NET 10 SDK and SQL Server LocalDB (or another SQL Server connection string).

```bash
cd BMSL_Tracker
dotnet restore
dotnet ef database update
dotnet run
```

The Visual Studio launch profiles use `Development` and LocalDB. The development database string is not published with the application. If running without a launch profile, set `ASPNETCORE_ENVIRONMENT=Development` and configure `ConnectionStrings:DefaultConnection` with user-secrets or an environment variable.

To configure Google locally without committing credentials:

```bash
dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_GOOGLE_CLIENT_ID"
dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_GOOGLE_CLIENT_SECRET"
```

In Google Cloud Console, create a **Web application** OAuth client. Add the local origin (for example `https://localhost:7192`) and the authorized redirect URI `https://localhost:7192/signin-google`. The Google button appears when both client values are present. A verified Google email creates an Identity account on first sign-in.

## Configuration for deployment

Supply production values through a secret manager or environment variables; never commit credentials. Nested configuration keys use double underscores in environment variables.

| Setting | Required for | Notes |
| --- | --- | --- |
| `ConnectionStrings__DefaultConnection` | App startup | SQL Server connection string. Use encryption and keep credentials out of source control. |
| `Authentication__Google__ClientId` and `Authentication__Google__ClientSecret` | Google sign-in | Configure both or leave both unset. Register `https://YOUR_HOST/signin-google` as an authorized redirect URI in Google Cloud Console. |
| `Registration__AllowedEmailDomains__0` | New account sign-up | Set one or more approved domains, e.g. `company.example`; additional domains use `__1`, `__2`, etc. Outside Development, sign-up is disabled if the list is empty. Domains are matched exactly. |
| `Email__Smtp__Host` and `Email__Smtp__FromAddress` | Password sign-up and confirmation resend | SMTP must be available to send email-confirmation links. Optional `Email__Smtp__Port` defaults to `587`; set `Email__Smtp__UserName`, `Email__Smtp__Password`, and keep `Email__Smtp__EnableSsl=true` as required by your mail provider. |
| `LocationData__RetentionDays` | Location-history retention | Defaults to 30 days; values are clamped to 1–3,650 days. Cleanup runs at startup and then daily. |
| `AllowedHosts` | Host-header filtering | Replace `*` with the public host name(s). |
| `ForwardedHeaders__KnownProxies__0` | TLS-terminating reverse proxy | Add the trusted proxy IP. Only configured proxies are trusted for forwarded IP/protocol headers. |
| `DataProtection__KeyRingPath` | Multi-instance / container deployments | Use a persistent, access-controlled key store shared by app instances so authentication cookies survive restarts and deployments. |

Password-based registration is blocked until SMTP is configured and requires a verified email. Google sign-up requires a verified Google email and, outside Development, an approved domain. Keep SMTP and Google secrets in your hosting platform's secret store. For local email testing, use `dotnet user-secrets` with keys such as `Email:Smtp:Host` and `Email:Smtp:FromAddress`.

## Database and deployment

Migrations are **not** applied automatically at production startup. Back up the database and apply migrations as a controlled deployment step:

```bash
cd BMSL_Tracker
dotnet tool install --global dotnet-ef --version 10.0.12
dotnet ef database update
```

Set the production connection string in the deployment environment before running the migration command. The current migration adds a composite index used by tracker lookups and retention cleanup.

Build a container from the repository root:

```bash
docker build -t bmsl-tracker:local .
```

Run it with a production SQL Server connection string, OAuth/domain/SMTP settings as needed, and a persistent volume mounted at `/var/data-protection`. The image runs as a non-root user and listens on port `8080`.

- Liveness: `GET /health/live`
- Readiness (includes a database connectivity check): `GET /health/ready` — returns `503` while the database is unavailable.

For HTTPS-terminating proxies, configure the trusted proxy IP before relying on forwarded headers, OAuth callback URLs, or HTTPS redirection. Set `AllowedHosts` to the actual host name. Use a managed SQL Server in production; LocalDB is development-only.

## Privacy and operational notes

- Location sharing starts only after the user explicitly presses **Share my location** and grants browser permission. It can be stopped from the tracker or by leaving the page.
- GPS updates are validated server-side for finite/range-valid coordinates, accuracy and implausible movement; server-side throttling prevents high-frequency updates.
- The map currently broadcasts live positions to all authenticated tracker clients. The configured email-domain restriction controls new sign-ups; add organization-specific authorization/administrative approval before allowing untrusted people to access the app.
- The default retention period is 30 days. Review this setting and your backup retention against company policy and applicable law.
- Replace the in-app starter privacy notice with your organization’s final notice and support contact before production use.
- OpenStreetMap’s public tile service is appropriate for development and modest traffic; review its usage policy and use a suitable tile provider for sustained production traffic.

## CI

GitHub Actions restores and builds a Release configuration on pushes and pull requests. The workflow targets the .NET 10 SDK.
