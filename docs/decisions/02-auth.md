# Milestone 2 — Auth

ASP.NET Core Identity behind an HttpOnly cookie: bootstrap-then-invite-only registration,
login, TOTP 2FA with QR enrolment and recovery codes, account lockout, rate limiting,
password change and log-delivered reset, and a fully authed web app.

- **`AddIdentityCore` + `AddIdentityCookies`, with hand-written endpoints.** Not
  `MapIdentityApi<T>()`, whose `/register` cannot be gated behind the invite rule and whose
  reset endpoints hard-require an email sender. Identity still does all the cryptography —
  hashing, TOTP, lockout counting; only the routing and policy are ours. Endpoints live in
  `Endpoints/{Auth,Account,Invite}Endpoints.cs`, mirroring `HealthEndpoints`.
- **Registration = bootstrap, then invites.** `/api/auth/register` is open only while the
  users table is empty; after that it needs a single-use `Invite` token (SHA-256 hashed at
  rest, 8-char prefix kept in the clear for display). User creation and invite redemption
  share one transaction — which is why `UseNpgsql` must **not** enable a retrying execution
  strategy.
- **Fail-safe authorization.** A fallback policy requires a signed-in user everywhere, and
  `_Imports.razor` applies `[Authorize]` to every page. Later endpoints are therefore
  protected by default. The health routes and the dev `MapFallbackToFile` opt out
  explicitly — losing either breaks the container healthcheck (and so the whole stack) or
  the login page.
- **Cookie hardening.** `HttpOnly`, `SameSite=Strict`, and `SecurePolicy` conditional on
  environment: `Always` in Production, `SameAsRequest` in Development because every dev path
  is plain HTTP and `Always` would make the browser silently drop the cookie.
  `SameSite=Strict` + one origin + JSON-only bodies **is** the CSRF story; no antiforgery
  tokens.
- **Data-protection keys in Postgres** (`PersistKeysToDbContext`). The container has no
  volume for the default file key ring, so it would be regenerated on every restart, signing
  everyone out on each redeploy.
- **User key is `Guid`** (`IdentityUser<Guid>`, v7, assigned in the constructor because
  `IdentityUser<TKey>` does not assign one). Effectively permanent — every later table gets
  an owner FK.
- **Password recovery via `IEmailSender<TUser>`.** Default implementation logs the reset link
  at Warning (`docker compose logs backend`); an SMTP implementation is selected by config.
  Documented trade-off: whoever can read the logs can take over an account.
- **Migrations apply at startup** (`MigrateDatabaseAsync`), single replica, no manual step on
  redeploy. `dotnet-ef` is pinned in `dotnet-tools.json`.
- **Tests exist now** — `tests/Trackr.Api.Tests`, xUnit + `WebApplicationFactory` against a
  Testcontainers Postgres, so the real migrations run. They use the environment name
  `Testing` (not Development, which would trigger the `#if DEBUG` Blazor blocks) and an
  `https://localhost` base address (Secure cookies are not sent over http).

## Since amended

Cookies remain the web app's session, unchanged. The API additionally issues **bearer
tokens** for the Android app, because the cookie-not-JWT reasoning recorded here explicitly
assumed there would never be a native mobile client. See
[03-android-pivot.md](03-android-pivot.md).
