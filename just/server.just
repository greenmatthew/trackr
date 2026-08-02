# Backend, web app and the Docker stacks.
#
# Note on comments: `just` shows the LAST comment line above a recipe as its `--list`
# description, and ECHOES any comment left inside a recipe body when it runs. So longer
# explanations sit above the recipe, separated from the one-line description by a blank line.

# Recipes in a module run from the module FILE's directory (just/), not the repository
# root - so every relative path below would be wrong without this.
set working-directory := '..'

DEV := "docker/docker-compose.dev.yml"
PROD := "docker/docker-compose.yml"

_default:
    @just --list server

# --- the dev stack ------------------------------------------------------------------------

# Build images and start db + backend + frontend, on http://localhost:8000.
up:
    docker compose -f {{ DEV }} up -d --build

# Start without rebuilding - much faster when only data changed.
start:
    docker compose -f {{ DEV }} up -d

# Stop the stack, keeping the database.
down:
    docker compose -f {{ DEV }} down

# The next `up` starts from an empty database, so registration reopens and the first
# account claims the server again.

# Stop the stack AND delete its database volume.
reset:
    docker compose -f {{ DEV }} down -v

# Rebuild and restart one service, e.g. `just server::rebuild backend`.
rebuild service:
    docker compose -f {{ DEV }} up -d --build {{ service }}

# Show container status.
ps:
    docker compose -f {{ DEV }} ps

# Follow logs, optionally for one service: `just server::logs backend`.
logs service="":
    docker compose -f {{ DEV }} logs -f {{ service }}

# Print the password-reset link the log-based email provider wrote.
reset-link:
    docker compose -f {{ DEV }} logs backend | grep -A3 "was not sent" | tail -20

# Is it actually alive?
health:
    @curl -fsS http://localhost:8000/api/health | python3 -m json.tool

# --- images -------------------------------------------------------------------------------

# Deliberately not postgres or the dotnet base images: re-pulling those is a slow download
# for no benefit.

# Remove the images this project built, leaving base images alone.
clean-images:
    -docker image rm trackr-backend:dev trackr-frontend:dev
    -docker image rm trackr-backend:latest trackr-frontend:latest

# Stack, database and this project's images. The nuclear option.
nuke: reset clean-images
    @echo "Dev stack, its database and this project's images are gone."

# Check the deployment compose file parses, without deploying it.
check-prod:
    @POSTGRES_PASSWORD=dummy docker compose -f {{ PROD }} config >/dev/null && echo "docker/docker-compose.yml is valid"

# --- .NET ---------------------------------------------------------------------------------

# Named projects rather than the solution: `dotnet build Trackr.slnx` would also try the
# MAUI app and fail on any machine without the maui-android workload.
#
# One invocation each, because the .NET 10 SDK rejects several projects in one `dotnet build`
# ("MSB1008: Only one project can be specified"). Trackr.Shared builds as a dependency of both,
# so it needs no line of its own.

# Build the backend and web app.
build:
    dotnet build src/Trackr.Api
    dotnet build src/Trackr.Web

# Build the backend and web app in Release.
build-release:
    dotnet build src/Trackr.Api -c Release
    dotnet build src/Trackr.Web -c Release

# In Debug the API also serves the Blazor app, so this is the whole thing on one origin.

# Hot-reload loop on http://localhost:5277, with Postgres in Docker.
watch: db
    dotnet watch --project src/Trackr.Api

# Start only Postgres, for the `watch` loop.
db:
    docker compose -f {{ DEV }} up -d db

# These start their own throwaway Postgres via Testcontainers, so they never touch the dev
# stack's database.

# Run the API integration tests. Needs Docker.
test:
    dotnet test tests/Trackr.Api.Tests

# --- migrations ---------------------------------------------------------------------------

# The ASPNETCORE_ENVIRONMENT prefix is load-bearing: `dotnet ef` ignores
# launchSettings.json, so without it the connection string is missing and startup throws.

# Add an EF migration: `just server::migration AddFoodItems`.
migration name:
    ASPNETCORE_ENVIRONMENT=Development \
      dotnet dotnet-ef migrations add {{ name }} --project src/Trackr.Api --output-dir Migrations

# Undo the most recent migration, provided it has not been applied.
migration-undo:
    ASPNETCORE_ENVIRONMENT=Development \
      dotnet dotnet-ef migrations remove --project src/Trackr.Api

# List migrations and whether they have been applied.
migrations-list:
    ASPNETCORE_ENVIRONMENT=Development \
      dotnet dotnet-ef migrations list --project src/Trackr.Api
