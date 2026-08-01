# Trackr

A self-hosted personal nutrition tracker with a chat-first interface. See
[CLAUDE.md](CLAUDE.md) for the full project brief.

**Status: milestone 1 (scaffold) complete.** The stack runs end to end — the Blazor
app reaches the API through nginx, and the API reaches Postgres through EF Core —
but there is no auth, no data model and no chat UI yet.

## Layout

```
src/Trackr.Api/      ASP.NET Core Web API      + Dockerfile
src/Trackr.Client/   Blazor WebAssembly PWA    + Dockerfile + nginx.conf
src/Trackr.Shared/   DTOs used by both
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

## Notes on the setup

- **One origin.** nginx serves the WASM app and reverse-proxies `/api/` to the
  backend. That is what allows a plain HttpOnly session cookie instead of a token
  (CLAUDE.md §3), and it means the client never needs CORS.
- **No HTTPS inside the containers.** TLS terminates at the reverse proxy; the app
  reads `X-Forwarded-Proto` instead.
- **Postgres 18** keeps its data in `/var/lib/postgresql/<major>/docker`, so the
  volume mounts `/var/lib/postgresql` — not the `/var/lib/postgresql/data` path used
  by Postgres 17 and earlier.
- **No EF Core migrations yet.** `TrackrDbContext` has no entities; the schema
  arrives in milestone 3.
