# Self-Hosting

Trackr runs as four containers behind your own reverse proxy. Nothing is published to the
public internet by the stack itself — the reverse proxy decides what is reachable.

## What the stack contains

| Service | What it does |
| --- | --- |
| `frontend` | nginx. Serves the web app's static files and reverse-proxies `/api/` to the backend. This is the only service your reverse proxy talks to, and the address the Android app points at. |
| `backend` | The ASP.NET Core API — accounts, and later the food-logging cascade. Plain HTTP on 8080, internal network only. |
| `db` | PostgreSQL 18, with a persistent named volume. |
| `ollama` | The local vision model. *Not present yet — arrives with the AI milestone.* |

`frontend` joins both the internal network and your reverse proxy's external network.
`backend` and `db` stay internal and are unreachable from outside the stack.

## Deploying with Portainer

The stack is meant to be added as a **Git repository stack**, so Portainer builds the images
itself — there is no registry involved.

1. Point Portainer at the repository and set the compose path to `docker/docker-compose.yml`.
2. Set `POSTGRES_PASSWORD` as a stack environment variable, or copy `docker/.env.example` to
   `docker/.env` and fill it in. **The stack refuses to start without it.** Generate one with
   `openssl rand -base64 32`.
3. Make sure the external network named by `PROXY_NETWORK` (default `proxy`) already exists
   and your reverse proxy is attached to it.
4. Point the reverse proxy at the `frontend` service on port 80.

Every other setting is optional — see [Configuration](Configuration).

## Deploying with plain Docker Compose

```bash
cp docker/.env.example docker/.env    # then set POSTGRES_PASSWORD
docker compose -f docker/docker-compose.yml up -d --build
```

## TLS terminates at your reverse proxy

Nothing inside the stack speaks HTTPS. The backend reads `X-Forwarded-Proto` instead of
redirecting, which is what lets the session cookie be marked `Secure` while the container
itself serves plain HTTP.

> **Reach the deployed app over HTTPS, or logins will not stick.** Outside Development the
> session cookie is marked `Secure`. A browser will accept it and then silently refuse to
> send it back over plain HTTP, so signing in appears to work and then does not. Add HSTS
> and an http→https redirect **at your reverse proxy** — deliberately not in these
> containers, which would break the local development stack.

## The strongest protection is not exposing it

The single most effective thing you can do for a personal tool is to keep the login off the
public internet entirely — reach it over VPN or LAN only, or gate it at the reverse proxy.
That makes brute-force concerns nearly moot on its own, on top of the account lockout and
rate limiting described in [Accounts and 2FA](Accounts-and-2FA).

## Health endpoints

| Endpoint | Purpose |
| --- | --- |
| `GET /api/health` | Full report including database state. 503 when unhealthy. |
| `GET /api/health/live` | Liveness. 200 whenever the process is up, **even if Postgres is down**. This is the container healthcheck, and a database blip must not trigger a restart loop. |
| `GET /api/health/ready` | Readiness. 200 only when Postgres is reachable too. |

All three are anonymous — they are the one exception to the rule that every endpoint requires
a signed-in user.

## Upgrading

Migrations apply themselves at startup. There is a single replica, so there is no migration
race and a redeploy needs no extra step: pull, rebuild, restart.

Before upgrading, read [Backup and Restore](Backup-and-Restore) — particularly the part about
the data-protection keys, which live in the database and are easy to lose by accident.
