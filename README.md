[![CI](https://github.com/BigSmoke4/BMSL_Tracker/actions/workflows/ci.yml/badge.svg)](https://github.com/BigSmoke4/BMSL_Tracker/actions/workflows/ci.yml)

# BMSL Tracker

Real-time employee geolocation tracking for Billing Master Software Ltd. Field staff open the
map once and their position streams to every connected teammate's dashboard over SignalR — with
GPS sanity filtering, anti-spoofing, smoothing, throttling, and durable history in the database.

## Highlights

| Area | What you get |
|---|---|
| Framework | ASP.NET Core 9 MVC, C#, Razor views, Bootstrap 5.3 |
| Real-time | SignalR hub (`/trackerHub`) — broadcast + replay of recent positions, optional Redis backplane for scale-out |
| Auth | ASP.NET Core Identity with **user name or e-mail sign-in**, **Google sign-in / sign-up**, lockout on repeated failures, per-IP rate limiting on auth endpoints |
| Data | EF Core 9 — **SQL Server** for production (migrations), **SQLite** for local dev/tests; composite index for hot queries; retention pruning worker |
| Security | HSTS, CSP (incl. `geolocation` permission policy), `X-Frame-Options`/`nosniff`, HttpOnly + SameSite=Lax auth cookie, secure cookies in prod, antiforgery on all non-GET, no server header |
| Operations | JSON console logging in prod, `/health` + `/health/ready`, opt-in forwarded headers, Data Protection key-ring sharing, multi-stage non-root Dockerfile, docker-compose stack, GitHub Actions CI (build + tests + image) |
| Frontend | Leaflet + SignalR client **vendored** under `wwwroot/lib` (no CDN runtime dependency, strict CSP-compatible), all inline JS removed |

## Project layout

```
BMSL_Tracker/
├── Controllers/         # Account (login, register, Google flow), Home
├── Data/                # ApplicationDbContext, DbInitializer (startup migrations/EnsureCreated)
├── Hubs/                # TrackerHub — the real-time engine
├── Infrastructure/      # TrackerOptions, GeoMath, security headers, global exception handler
├── Migrations/          # EF Core migrations (SQL Server)
├── Models/              # UserLocation, view models
├── Services/            # LocationService (validate/smooth/throttle/persist policy),
│                        # external user provisioning, retention worker
├── Views/               # Razor views (map, login/register with Google button, layout)
└── wwwroot/             # Vendored libs (bootstrap, jquery, leaflet, signalr) + app css/js
tests/BMSL_Tracker.Tests/ # xUnit tests: location pipeline, username provisioning, validation
docs/                     # Google OAuth setup, production deployment guide
```

## Quick start (local development)

Prerequisites: [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
dotnet restore BMSL_Tracker.sln
dotnet run --project BMSL_Tracker
# → http://localhost:5104 (launch profile: https://localhost:7192)
```

Development uses **SQLite** (`app.db`, auto-created with `EnsureCreated`) — no SQL Server
required. Register an account on `/Account/Register` and open the map.

Optional local secrets (never committed):

```bash
cd BMSL_Tracker
dotnet user-secrets set "Authentication:Google:ClientId" "xxxx.apps.googleusercontent.com"
dotnet user-secrets set "Authentication:Google:ClientSecret" "GOCSPX-xxxx"
```

## Sign in / sign up

* **Local accounts** — `/Account/Register` with user name, optional e-mail, password
  (8+ chars, upper+lower+digit). Login accepts user name *or* e-mail. 5 failed attempts lock the
  account for 15 minutes; auth endpoints are rate-limited (10 req/min/IP, HTTP 429).
* **Google** — the “Continue with Google” button appears once credentials are configured.
  First-time Google users are **auto-provisioned**: a local account is created from the verified
  Google e-mail with a generated unique user name, the external login is linked, and the user is
  signed in immediately. Returning Google users sign in via the same button. Passwordless
  external accounts can attach a local password later by logging in and using Identity's
  password APIs (roadmap: self-service manage page).
* Signed-in local users can link Google from the Login page (the flow detects the session and
  attaches the external key instead of creating a new account).

See **[docs/GOOGLE-AUTH.md](docs/GOOGLE-AUTH.md)** for the OAuth client setup (redirect URIs
`/signin-google` and your public origin).

## Configuration reference

All settings live in `appsettings.json` and can be overridden per environment with
environment variables (`Section__Key`) — see **[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)**.

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | LocalDB / `Data Source=app.db` (dev) | Database connection (`Redis` optional: SignalR backplane) |
| `Database:Provider` | `SqlServer` | `SqlServer` or `Sqlite` |
| `Database:ApplyMigrations` | `false` (prod template: `true`) | Auto-migrate on startup; SQLite uses `EnsureCreated` |
| `Authentication:Google:ClientId` / `:ClientSecret` | empty | Enables Google sign-in when set |
| `Tracker:*` | see file | Throttle, smoothing window, accuracy/jump guards, persistence policy, retention days |
| `ForwardedHeaders:Enabled`, `KnownProxies`, `KnownNetworks` | `false` | Trust X-Forwarded-* behind a reverse proxy |
| `DataProtection:KeyRingPath` | empty | Shared key ring directory for multi-instance |
| `Security:RequireHttps` | `true` | HTTPS redirect |
| `Security:HstsMaxAgeDays` | `365` | HSTS |
| `Security:ContentSecurityPolicy` | strict-ish (see file) | CSP header |

## How the tracking pipeline works

```
Browser (watchPosition, ≥5 s)
   │ SendLocation(lat, lng, accuracy)          WebSocket / SignalR
   ▼
TrackerHub ──────────────────────────────────────────► Clients.All "ReceiveLocation"
   │ 1. GeoMath.IsValidSample (bounds, finite)
   │ 2. LocationService.TryValidateAndSmooth
   │      • per-user throttle (min interval)
   │      • accuracy filter (>2000 m rejected)
   │      • impossible-jump rejection (>300 m/s)
   │      • moving-average smoothing (window 5)
   │ 3. ShouldPersist? (moved >10 m or ≥5 min) → INSERT + MarkPersisted
   ▼
UserLocations table (indexed on UserId+Timestamp) ◄── retention worker prunes >90 days
```

New connections replay the latest position of every active user (last hour) so a fresh
dashboard populates instantly.

## Docker

```bash
cp .env.example .env       # set MSSQL_SA_PASSWORD
docker compose up --build  # app on :8080, SQL Server inside the network, migrations applied
```

Single container against an existing database:

```bash
docker build -t bmsl-tracker .
docker run -d -p 8080:8080 \
  -e ConnectionStrings__DefaultConnection="..." \
  -e Database__ApplyMigrations=true \
  bmsl-tracker
```

## Tests & CI

```bash
dotnet test   # xUnit: validation/smoothing pipeline, Google username provisioning, view-model rules
```

GitHub Actions (`.github/workflows/ci.yml`) runs restore, `build -warnaserror`, tests and a
Docker image build on every push/PR touching `main`.

## Health endpoints

| Route | Checks | Use |
|---|---|---|
| `GET /health` | process alive | liveness probes |
| `GET /health/ready` | + database connectivity | readiness gates |

## Security notes

* The CSP allows `'self'` scripts only — keep new client code in `wwwroot/js`; map tiles are
  whitelisted under `img-src`.
* Location data is only served to authenticated users over the SignalR hub (`[Authorize]`).
* Never commit `appsettings.*.local.json`, key rings or certificates — they are gitignored and
  excluded from Docker builds.

## Tech stack

ASP.NET Core 9 · Entity Framework Core 9 · ASP.NET Core Identity · Google OAuth 2.0/OIDC ·
SignalR · Leaflet 1.9 · OpenStreetMap tiles · Bootstrap 5.3 · xUnit
