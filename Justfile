# Trackr — task runner.
#
#   just              list everything
#   just server::up   run a recipe from the server module
#   just mobile::run  ... or the mobile one
#
# Split into modules because the halves have genuinely different toolchains and inner loops:
# the server is Docker and `dotnet watch`, the app is the Android SDK, and the emulator is a
# machine to run the app on rather than part of it. Recipes that span everything stay here.
#
# Module files live in `just/`. They are not in src/Trackr.Api or src/Trackr.Mobile because
# neither module maps to a single project - `server` covers the API, the web app and the
# compose stacks, and `mobile` covers the MAUI app plus its Core library and tests.
#
# Anything with control flow in it lives in scripts/, not in a recipe. `just` is a place to
# name commands, not a language to write them in, and a shebang recipe is written to a
# temporary file before it runs - so `$1` inside one is not what the caller passed to `just`.
#
# Two things to know before editing these files:
#   - `just` shows the LAST comment line above a recipe as its `--list` description, and
#     echoes any comment left INSIDE a recipe body at run time. So explanations go above the
#     recipe, separated from the one-line description by a blank line.
#   - Recipe lines go to the shell. Never put backticks in an echo: they are command
#     substitution, and an innocent-looking message will run the command it mentions.

mod server 'just/server.just'
mod mobile 'just/mobile.just'
mod emulator 'just/emulator.just'
mod docs 'just/docs.just'

# The full listing is short enough to be useful because the modules hold verbs only. The
# individual steps behind them are subcommands of the scripts named below, each of which
# prints its own help when run with no arguments.

_default: help

# Every recipe, in every module.
help:
    @just --list --list-submodules
    @echo ""
    @echo "Lower-level steps live in scripts/, which the recipes above wrap."
    @echo "Run one with no arguments for its own help:"
    @echo ""
    @echo "    scripts/app.sh        build the app; install, launch, log, screenshot, dump its UI"
    @echo "    scripts/device.sh     find a device; pair, connect, USB-forward, tunnel a port"
    @echo "    scripts/emulator.sh   start, stop, wipe, create or delete the emulator"
    @echo "    scripts/server.sh     the dev stack, its logs and database, and EF migrations"

# --- everyday workflow ----------------------------------------------------------------------
# The three recipes below are all most sessions need. Everything else is in the modules.

# Every step is safe to re-run: `docker compose up -d` is a no-op when the stack is already
# healthy, and the emulator script exits early if one is already running.

# Start a working session: dev stack, emulator with a window, app built and launched.
dev:
    just server::up
    just emulator::show
    just mobile::run
    @echo ""
    @echo "  Web      http://localhost:8000"
    @echo "  API      http://localhost:8080"

# Stop the dev stack and the emulator, keeping the database, images and build output.
stop:
    -just server::down
    -just emulator::down
    @echo "Stopped. Data, images and build output are untouched - 'just clean-all' removes those."

# --- whole solution -------------------------------------------------------------------------

# For backend and web only, which needs no Android tooling, use `just server::build`.

# Build every project. Needs the maui-android workload.
build:
    dotnet build Trackr.slnx

# Docs first: it is the fastest suite and needs nothing, so a stale wiki page is reported in
# seconds rather than after Docker has pulled and started Postgres.

# Every test suite. The API tests need Docker; the docs and mobile tests need nothing.
test:
    just docs::test
    just mobile::test
    just server::test

# Two tiers: `clean` for a stale build, `clean-all` to put the machine back the way it was
# found. The modules have the same pair scoped to themselves - `just server::clean-all` leaves
# the app's build output alone.

# Remove the last build's output. Keeps obj/, so the next build is still incremental.
clean:
    dotnet clean Trackr.slnx

# `dotnet clean` only removes what the last build produced and leaves most of obj/ behind -
# the difference between reclaiming a few hundred MB and reclaiming all 1.4GB. The MAUI
# project alone accounts for about 865MB of it, which is why this is `rm -rf`.
#
# Order matters: the emulator first so nothing holds a port, then Docker, then the host-side
# build output - which is the big one, and is NOT inside any image.
#
# If VS Code is open on the project, empty obj/ directories reappear within seconds: the C#
# Dev Kit watches the workspace and re-runs a design-time restore. That is a few MB of
# project.assets.json, not build output, and nothing has gone wrong. Close the editor first
# if you want the directories to stay gone.

# Remove everything this project put on the machine: containers, volumes, images, build output.
[confirm("This deletes the dev database, this project's Docker images and ~1.4GB of build output. Continue? (y/N)")]
clean-all:
    -just emulator::down
    -just server::reset
    -./scripts/server.sh images
    @find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true
    @echo ""
    @echo "Gone: containers, the dev database, trackr images, and all bin/obj."
    @echo "Kept: the postgres base image (slow to re-pull), the Android SDK, and the"
    @echo "      trackr-test AVD (~2GB). Remove the AVD with: just emulator::clean-all"

restore:
    dotnet restore Trackr.slnx

# The AVD line is prefixed with '-' because the Android SDK is optional: someone working on
# the backend alone should not have `setup` fail at them over an emulator they will not run.

# Everything a fresh checkout needs before anything else will work.
setup:
    dotnet tool restore
    dotnet restore Trackr.slnx
    -./scripts/emulator.sh create
    @echo ""
    @echo "For the Android app you also need the maui-android workload, the Android SDK and"
    @echo "JDK 17. See the README. Check what is present with: just doctor"

# What is installed, what is running, and what is plugged in.
doctor:
    @./scripts/device.sh doctor
    @echo ""
    -@./scripts/server.sh ps
