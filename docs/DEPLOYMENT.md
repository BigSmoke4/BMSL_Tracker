# Production Deployment

## Topology expectations

BMSL Tracker is a standard ASP.NET Core 9 app behind a TLS-terminating reverse proxy
(nginx, Caddy, IIS ARR, Azure App Service, k8s ingress…). WebSockets must be proxied with
upgrade support for the `/trackerHub` path.

```
users ──HTTPS──► proxy ──HTTP──► Kestrel (app) ──► SQL Server
                                 │  └── /trackerHub (WebSocket)
                                 └── optional Redis backplane for multi-instance fan-out
```

## Required configuration

| Variable | Notes |
|---|---|
| `ConnectionStrings__DefaultConnection` | SQL Server 2019+ / Azure SQL. Use `Encrypt=True;TrustServerCertificate=False` against managed SQL. |
| `Database__Provider` | `SqlServer` (default) or `Sqlite` for a single-node file deployment. |
| `Database__ApplyMigrations` | `true` for containers; alternatively run `dotnet ef database update` in the release step. |
| `Authentication__Google__ClientId` / `__ClientSecret` | see [GOOGLE-AUTH.md](GOOGLE-AUTH.md). |
| `ForwardedHeaders__Enabled` + `ForwardedHeaders__KnownProxies[]` / `__KnownNetworks[]` | mandatory when the proxy sets `X-Forwarded-*`; keep the trust list as narrow as possible. |
| `ASPNETCORE_ENVIRONMENT` | `Production` (JSON logging, HSTS on, secure cookies). |
| `DataProtection__KeyRingPath` | **Required for >1 instance or ephemeral containers** — point at a shared volume (e.g. `/keys`). Without it, auth cookies, antiforgery and OAuth state break across restarts/nodes. |

## Secrets policy

* No secrets in git: `appsettings.json` ships empty credentials; use environment variables,
  Docker secrets, Kubernetes secrets, or Azure App Settings.
* `.env` files are git-ignored — commit only `.env.example`.
* Rotate the Google client secret anytime in Cloud Console; no app redeploy needed beyond a restart.

## Migrations vs EnsureCreated

* SQL Server uses EF Core **migrations** (`Migrations/`). Apply with `Database__ApplyMigrations=true`
  at startup (uses the retry execution strategy, safe alongside `EnableRetryOnFailure`) or out of
  band:
  ```bash
  dotnet tool restore
  dotnet ef database update --project BMSL_Tracker
  ```
* SQLite (dev/demo) uses `EnsureCreated` — schema matches the model at creation time; delete the
  file to upgrade.

## Scaling out

Single instance supports the stated 100+ concurrent trackers comfortably. For more:

1. Add Redis and set `ConnectionStrings__Redis` — SignalR fan-out becomes cross-instance.
2. Shared Data Protection key ring (see table above).
3. Sticky sessions are **not required** (SignalR uses WebSocket; long-polling fallback benefits
   from stickiness — configure it if you must disable WebSockets).
4. Per-user throttling, accuracy filtering and the persistence write-gate already run on the
   server, so client bursts cannot flood the hub: see `Tracker:*` options.

## Observability

* `GET /health` — liveness (never touches the database).
* `GET /health/ready` — readiness, includes the EF Core `database` check.
* Production logging is JSON on stdout (one document per line) — ship it straight to Loki/ELK/
  Application Insights. Categories `Microsoft.*` and `Microsoft.EntityFrameworkCore` are capped
  at `Warning` to keep noise down; raise per environment if debugging.
* Unhandled exceptions: `GlobalExceptionHandler` logs the full exception + request id; clients
  receive the friendly error page (HTML) or an RFC 7807 body (APIs).

## Rate limiting & hardening summary

| Control | Value |
|---|---|
| Auth endpoints (`/Account/*`) | 10 requests/min per client IP, then HTTP 429 with a short message |
| Identity lockout | 5 failures → 15 min |
| HSTS | 365 days, includeSubDomains, preload |
| CSP | `script-src 'self'`; tiles whitelisted in `img-src`; `ws:`/`wss:` for SignalR |
| Cookies | HttpOnly, SameSite=Lax, `Secure` outside Development |
| Kestrel | `Server` header suppressed |
| Antiforgery | global `AutoValidateAntiforgeryToken` filter for all non-GET MVC |

## Container runtime user & file system

The image runs as the built-in non-root `$APP_UID`. Mount anything the app writes (key ring
when using SQLite-file or DataProtection) as volumes with matching ownership:

```bash
docker run -d -p 8080:8080 \
  -v /srv/bmsl/keys:/keys \
  -e DataProtection__KeyRingPath=/keys \
  -e ConnectionStrings__DefaultConnection="..." \
  -e ForwardedHeaders__Enabled=true \
  -e ForwardedHeaders__KnownNetworks__0="172.16.0.0/12" \
  bmsl-tracker
```

## CI pipeline

`.github/workflows/ci.yml`:

1. `dotnet restore` → `dotnet build -warnaserror` → `dotnet test` (xUnit suite in
   `tests/BMSL_Tracker.Tests`) → `dotnet publish`
2. `docker build` smoke of the production image.

Add your registry push/deploy steps after `Publish` (or fork the `docker-image` job) — CI
intentionally contains no deploy credentials.
