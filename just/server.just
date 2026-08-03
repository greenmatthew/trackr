# Backend, web app and the dev stack.
#
# Verbs only, over scripts/server.sh - which also has `start`, `rebuild`, `ps`, `logs`,
# `health`, `reset-link`, `db`, `images`, `check-prod` and `migration`. Run it with no
# arguments for its own help:
#
#   ./scripts/server.sh logs backend
#   ./scripts/server.sh migration add AddFoodItems
#
# `just` shows the LAST comment line above a recipe as its `--list` description, and ECHOES any
# comment left inside a recipe body when it runs. So longer explanations sit above the recipe,
# separated from the one-line description by a blank line.

# Recipes in a module run from the module FILE's directory (just/), not the repository
# root - so every relative path below would be wrong without this.
set working-directory := '..'

SERVER := "./scripts/server.sh"

_default:
    @just --list server

# --- the dev stack --------------------------------------------------------------------------

# Rebuilds the images. `./scripts/server.sh up` on its own skips that, which is much faster
# when only data changed.

# Build images and start db + backend + frontend, on http://localhost:8000.
up:
    {{ SERVER }} up --build

# Stop the stack, keeping the database.
down:
    {{ SERVER }} down

# The next `up` starts from an empty database, so registration reopens and the first
# account claims the server again.

# Stop the stack AND delete its database volume.
reset:
    {{ SERVER }} reset

# In Debug the API also serves the Blazor app, so this is the whole thing on one origin.

# Hot-reload loop on http://localhost:5277, with Postgres in Docker.
watch:
    {{ SERVER }} db
    dotnet watch --project src/Trackr.Api

# --- .NET ---------------------------------------------------------------------------------

# Named projects rather than the solution: `dotnet build Trackr.slnx` would also try the
# MAUI app and fail on any machine without the maui-android workload.
#
# One invocation each, because the .NET 10 SDK rejects several projects in one `dotnet build`
# ("MSB1008: Only one project can be specified"). Trackr.Shared builds as a dependency of both,
# so it needs no line of its own.

# Build the backend and web app. `just server::build Release` for a Release build.
build configuration="Debug":
    dotnet build src/Trackr.Api -c {{ configuration }}
    dotnet build src/Trackr.Web -c {{ configuration }}

# These start their own throwaway Postgres via Testcontainers, so they never touch the dev
# stack's database.

# Run the API integration tests. Needs Docker.
test:
    dotnet test tests/Trackr.Api.Tests

# Remove the last build's output. Keeps obj/ and touches nothing in Docker.
clean:
    dotnet clean src/Trackr.Api
    dotnet clean src/Trackr.Web

# The images are this project's own two, not postgres or the dotnet base images - re-pulling
# those is a slow download for no benefit. The database volume survives too; that is `reset`.

# Delete the backend and web build output, and the images this project built.
clean-all:
    @rm -rf src/Trackr.Api/bin src/Trackr.Api/obj \
            src/Trackr.Web/bin src/Trackr.Web/obj \
            tests/Trackr.Api.Tests/bin tests/Trackr.Api.Tests/obj
    {{ SERVER }} images
