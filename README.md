# Trackr

A self-hosted personal nutrition tracker with a chat-first interface. See
[CLAUDE.md](CLAUDE.md) for the full project brief.

**Status: milestone 2 (auth) complete.** The stack runs end to end and everything is
behind a login: ASP.NET Core Identity with invite-only registration, authenticator-app
2FA, account lockout and rate limiting. There is no food data model and no chat UI yet.

## Layout

```
src/Trackr.Api/          ASP.NET Core Web API      + Dockerfile
src/Trackr.Client/       Blazor WebAssembly PWA    + Dockerfile + nginx.conf
src/Trackr.Shared/       DTOs used by both
tests/Trackr.Api.Tests/  Integration tests against a throwaway Postgres
```

Requires the .NET 10 SDK and Docker.

## Running it

### Full stack in Docker

```bash
docker compose -f docker-compose.dev.yml up -d --build
```

| URL | What |
|---|---|
| <http://localhost:8000> | the app (nginx: static files + `/api/` proxy) |
| <http://localhost:8080> | the API directly, bypassing nginx |
| `localhost:5433` | Postgres (user/password/db: `trackr` / `trackr_dev` / `trackr`) |

```bash
docker compose -f docker-compose.dev.yml down -v   # -v also drops the database
```

### Fast loop with hot reload

For UI work, run only Postgres in Docker and the app from the SDK. In `Debug` the
API also serves the Blazor client, so you get the same single origin as production
on one port:

```bash
docker compose -f docker-compose.dev.yml up -d db
dotnet watch --project src/Trackr.Api
```

Everything is then on <http://localhost:5277>, with `/openapi/v1.json` available
(Development only).

### Tests

```bash
dotnet test
```

The tests start their own throwaway Postgres container (Testcontainers) and drive the
real application, migrations included, so Docker must be running. They do not touch the
dev stack's database.

## Accounts

**The first account claims the server.** On a fresh database `/register` is open; create
your account and registration closes permanently. After that, new accounts need a
single-use invite, which any signed-in user can mint under **Settings → Invites**. There
is no public sign-up at any point (CLAUDE.md §8.4).

**Two-factor authentication** is opt-in per account, under **Settings**. Scan the QR code
with an authenticator app, type one code to prove it works, then save the ten recovery
codes — they are shown once and only their hashes are stored.

**Lockout and rate limiting** are on by default: five failed attempts locks an account for
15 minutes, and the auth endpoints are rate limited per client address. Both are
configurable through the environment variables in `.env.example`.

### If you forget your password

By default Trackr has no email server, so a reset link is written to the backend log
instead of being sent:

```bash
docker compose logs backend | grep -A3 "was not sent"
```

Open that link to set a new password. The trade-off is explicit: **anyone who can read the
backend logs can take over an account.** On a private server whose logs only you can read,
that is the same trust boundary as the database itself. To send real email instead, set
`TRACKR_EMAIL_PROVIDER=Smtp` and the `TRACKR_SMTP_*` variables — see `.env.example`.

## Health endpoints

| Endpoint | Purpose |
|---|---|
| `GET /api/health` | Full report incl. database state. What the app's status page calls. 503 when unhealthy. |
| `GET /api/health/live` | Liveness. 200 whenever the process is up, **even if Postgres is down** — this is the container healthcheck, and a database blip must not trigger a restart loop. |
| `GET /api/health/ready` | Readiness. 200 only when Postgres is reachable too. |

## Deploying

`docker-compose.yml` is the deployment stack, meant to be added to Portainer as a
**Git repository stack** so Portainer builds the images itself — there is no
registry in the loop.

1. Copy `.env.example` to `.env` and set `POSTGRES_PASSWORD` (or set it as a stack
   environment variable in Portainer). The stack refuses to start without it.
2. Make sure the external network named in `PROXY_NETWORK` (default `proxy`)
   exists and your reverse proxy is on it.
3. Point the reverse proxy at the `frontend` service on port 80. It terminates TLS;
   nothing inside the stack does.

Only `frontend` joins the proxy network — `backend` and `db` stay on the internal
network and are unreachable from outside it.

> **Reach the deployed app over HTTPS.** Outside Development the session cookie is marked
> `Secure`, so a browser will accept it and then silently refuse to send it back over
> plain HTTP: logging in appears to work and then does not stick. Terminate TLS at your
> reverse proxy, and add HSTS and an http→https redirect **there** — deliberately not in
> these containers, which would break `http://localhost:8000` in development.

Strongest protection, per CLAUDE.md §8.5: do not expose the login to the public internet
at all. Reaching it over VPN or LAN only makes brute-force concerns nearly moot.

## Notes on the setup

- **One origin.** nginx serves the WASM app and reverse-proxies `/api/` to the
  backend. That is what allows a plain HttpOnly session cookie instead of a token
  (CLAUDE.md §3), and it means the client never needs CORS.
- **No HTTPS inside the containers.** TLS terminates at the reverse proxy; the app
  reads `X-Forwarded-Proto` instead.
- **Postgres 18** keeps its data in `/var/lib/postgresql/<major>/docker`, so the
  volume mounts `/var/lib/postgresql` — not the `/var/lib/postgresql/data` path used
  by Postgres 17 and earlier.
- **Migrations apply themselves at startup.** There is one replica, so there is no
  migration race, and a Portainer redeploy needs no extra step. To add one:

  ```bash
  dotnet tool restore                          # dotnet-ef, pinned in dotnet-tools.json
  ASPNETCORE_ENVIRONMENT=Development \
    dotnet dotnet-ef migrations add SomeName --project src/Trackr.Api --output-dir Migrations
  ```

  The environment prefix matters: `dotnet ef` ignores `launchSettings.json`, so without it
  the connection string is missing and startup throws.
- **Data-protection keys live in Postgres**, not in the container filesystem. They encrypt
  the session cookie, and the container has no volume for them — on disk they would be
  regenerated on every restart, silently signing everyone out on each redeploy.
- **The session cookie is `SameSite=Strict` on a single origin, with JSON-only request
  bodies.** That combination is the CSRF defence; there are deliberately no antiforgery
  tokens.
- **Data-protection keys are stored unencrypted** in the same database as the password
  hashes, on the internal-only network. Encrypting them would need a certificate and real
  key-management; on a private single-user server the database is already the trust
  boundary.
