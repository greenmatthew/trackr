# Milestone 1 — Scaffold

Solution with a Blazor WASM frontend, an ASP.NET Core Web API backend, EF Core + Postgres,
and Docker Compose bringing up db + backend + frontend behind a health check.

- **nginx also reverse-proxies `/api/` to the backend.** §3 requires the WASM app and the
  API on the same origin so the session can be an HttpOnly cookie. The `frontend` container
  therefore does two jobs: serve the static assets and forward `/api/` to `backend`. Only
  `frontend` joins the external reverse-proxy network; `backend` and `db` stay internal.
  Config lives in `src/Trackr.Web/nginx.conf`.
- **No HTTPS inside the containers.** TLS terminates at the user's reverse proxy, so the
  backend speaks plain HTTP on 8080 and reads `X-Forwarded-Proto` (via
  `UseForwardedHeaders`) rather than redirecting. Milestone 2 needs this to mark the
  Identity cookie `Secure`.
- **Two compose files.** `docker/docker-compose.yml` is the Portainer deployment stack
  (external proxy network, no published ports, build-from-repo since nothing is pushed to a
  registry). `docker/docker-compose.dev.yml` is a standalone local stack with published
  ports and throwaway credentials — standalone rather than an override because Compose
  merges networks additively and an override cannot drop the external network.
- **Dev inner loop.** In `Debug` only, `Trackr.Api` also serves the Blazor app
  (`dotnet watch`, single origin, hot reload). The project reference and the matching
  `#if DEBUG` block are excluded from `Release`, so the shipped backend image does not
  contain the web app.

## Since superseded

The project named `Trackr.Client` here was renamed `Trackr.Web`, and the compose files moved
from the repository root into `docker/`, during the Android-first pivot — see
[03-android-pivot.md](03-android-pivot.md). Paths above are written as they are today.
