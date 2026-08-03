#!/usr/bin/env bash
# The dev stack and the database behind it.
#
#   ./scripts/server.sh up [--build]        start db + backend + frontend on :8000
#   ./scripts/server.sh down                stop it, keeping the database
#   ./scripts/server.sh reset               stop it AND delete the database volume
#   ./scripts/server.sh rebuild SERVICE     rebuild and restart one service
#   ./scripts/server.sh ps
#   ./scripts/server.sh logs [SERVICE]
#   ./scripts/server.sh health              is it actually alive
#   ./scripts/server.sh reset-link          the password-reset link the log provider wrote
#   ./scripts/server.sh db                  only Postgres, for the `just server::watch` loop
#   ./scripts/server.sh images              remove the images this project built
#   ./scripts/server.sh check-prod          does the deployment compose file parse
#   ./scripts/server.sh migration add NAME | undo | list
#
# The dev stack's credentials are throwaway and its data is disposable; nothing here should
# ever be pointed at a real deployment. That is what `check-prod` is for - it parses the
# production compose file without running it.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

DEV="docker/docker-compose.dev.yml"
PROD="docker/docker-compose.yml"

usage() {
    awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "${BASH_SOURCE[0]}"
}

compose() {
    docker compose -f "$DEV" "$@"
}

cmd_up() {
    # --build only when asked: rebuilding images is most of the wait, and only matters when
    # code changed rather than data.
    if [ "${1:-}" = "--build" ]; then
        compose up -d --build
    else
        compose up -d
    fi
}

# The next `up` starts from an empty database, so registration reopens and the first account
# claims the server again.
cmd_reset() {
    compose down -v
}

cmd_health() {
    curl -fsS http://localhost:8000/api/health | python3 -m json.tool
}

cmd_reset_link() {
    compose logs backend | grep -A3 "was not sent" | tail -20
}

# Deliberately not postgres or the dotnet base images: re-pulling those is a slow download for
# no benefit.
cmd_images() {
    docker image rm trackr-backend:dev trackr-frontend:dev || true
    docker image rm trackr-backend:latest trackr-frontend:latest || true
}

cmd_check_prod() {
    POSTGRES_PASSWORD=dummy docker compose -f "$PROD" config >/dev/null
    echo "$PROD is valid"
}

# The ASPNETCORE_ENVIRONMENT prefix is load-bearing: `dotnet ef` ignores launchSettings.json,
# so without it the connection string is missing and startup throws.
cmd_migration() {
    export ASPNETCORE_ENVIRONMENT=Development

    case "${1:-}" in
        add)
            [ -n "${2:-}" ] || die_usage "usage: server.sh migration add <Name>"
            dotnet dotnet-ef migrations add "$2" --project src/Trackr.Api --output-dir Migrations ;;
        undo)
            dotnet dotnet-ef migrations remove --project src/Trackr.Api ;;
        list|"")
            dotnet dotnet-ef migrations list --project src/Trackr.Api ;;
        *)
            die_usage "usage: server.sh migration add <Name> | undo | list" ;;
    esac
}

die_usage() {
    printf '%s\n' "$@" >&2
    exit 1
}

case "${1:-}" in
    up)         shift; cmd_up "$@" ;;
    down)       compose down ;;
    reset)      cmd_reset ;;
    rebuild)    shift; [ -n "${1:-}" ] || die_usage "usage: server.sh rebuild <service>"
                compose up -d --build "$1" ;;
    ps)         compose ps ;;
    logs)       shift; compose logs -f "$@" ;;
    health)     cmd_health ;;
    reset-link) cmd_reset_link ;;
    db)         compose up -d db ;;
    images)     cmd_images ;;
    check-prod) cmd_check_prod ;;
    migration)  shift; cmd_migration "$@" ;;
    *)          usage; exit 1 ;;
esac
