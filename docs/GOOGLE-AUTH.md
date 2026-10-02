# Google Sign-in / Sign-up Setup

BMSL Tracker implements Google as an ASP.NET Core Identity *external authentication scheme*.
When credentials are present the login and registration pages automatically show the
“Continue with Google” button; when absent, the button is hidden and password login keeps working.

## 1. Create OAuth credentials

1. Open <https://console.cloud.google.com/apis/credentials> in a project you own.
2. **Configure the consent screen** (External is fine). Scopes requested: `openid`, `email`,
   `profile`. Publishing the app to production avoids the "unverified app" warning for your users.
3. **Create Credentials → OAuth client ID**
   * Application type: **Web application**
   * Authorized JavaScript origins: `https://tracker.yourcompany.com` (and
     `http://localhost:5104` for development)
   * Authorized redirect URIs:
     * `https://tracker.yourcompany.com/signin-google`
     * `http://localhost:5104/signin-google` (development; HTTPS also works)
4. Copy the **Client ID** and **Client secret**.

> `/signin-google` is the handler path registered by
> `Microsoft.AspNetCore.Authentication.Google`. The Identity callback (`/Account/ExternalLoginCallback`)
> is a normal MVC route and does **not** need to be whitelisted at Google.

## 2. Give the app the credentials

**Development (user-secrets; already wired to this project):**

```bash
cd BMSL_Tracker
dotnet user-secrets set "Authentication:Google:ClientId" "1234-abc.apps.googleusercontent.com"
dotnet user-secrets set "Authentication:Google:ClientSecret" "GOCSPX-secret"
```

**Production (environment variables or your secret store):**

```bash
export Authentication__Google__ClientId="1234-abc.apps.googleusercontent.com"
export Authentication__Google__ClientSecret="GOCSPX-secret"
```

Docker Compose: put the two variables into `.env` (compose passes them through).

No restart chore beyond the normal process restart — `Program.cs` registers `AddGoogle` only
when both values are non-empty and logs a startup warning otherwise.

## 3. What happens at sign-in

1. Browser posts to `POST /Account/ExternalLogin?provider=Google` (antiforgery-protected,
   rate-limited like all auth endpoints).
2. The app issues a `Challenge` against the Google handler (OIDC-ish OAuth flow with state and
   nonce stored in the short-lived `Identity.External` cookie).
3. Google redirects back to `/signin-google`, the handler validates the response, and Identity
   redirects to `/Account/ExternalLoginCallback`.
4. Callback branches:
   * **Known external login** → sign in (locked-out accounts are rejected).
   * **First-time Google user** → auto sign-up: account is created with the Google e-mail
     (`EmailConfirmed = true`), a unique user name is derived from the e-mail local part
     (plus-tags and non-ASCII characters stripped; numeric suffixes on collisions), the external
     key is linked, and the display name is stored as a cookie claim.
   * **Already signed in** → the Google account is *linked* to the current user instead of
     creating a duplicate.

If provisioning fails, the half-created account is rolled back so no orphaned rows remain.

## 4. Gotchas

* **Behind a reverse proxy**: enable `ForwardedHeaders:Enabled` and configure
  `KnownProxies`/`KnownNetworks`, otherwise the redirect URI is built with the internal
  `http://host:port` and Google returns `redirect_uri_mismatch`.
* **Data Protection**: the external/XSRF cookies are protected with keys from the Data Protection
  stack. With more than one instance (or containers that restart often) set
  `DataProtection:KeyRingPath` to a shared volume — otherwise callbacks randomly fail after scale-out.
* **Session loss during sign-in**: nothing is persisted to the session; only the short-lived
  Identity external cookie, so the Google round-trip should complete within minutes.
