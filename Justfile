# Trackr — task runner.
#
#   just              list everything
#   just server::up   run a recipe from the server module
#   just mobile::run  ... or the mobile one
#
# Split into two modules because the two halves have genuinely different toolchains and
# different inner loops: the server is Docker and `dotnet watch`, the app is the Android SDK
# and an emulator. Recipes that span both stay here.
#
# Module files live in `just/`. They are not in src/Trackr.Api or src/Trackr.Mobile because
# neither module maps to a single project - `server` covers the API, the web app and the
# compose stacks, and `mobile` covers the MAUI app plus its Core library and tests.
#
# Two things to know before editing these files:
#   - `just` shows the LAST comment line above a recipe as its `--list` description, and
#     echoes any comment left INSIDE a recipe body at run time. So explanations go above the
#     recipe, separated from the one-line description by a blank line.
#   - Recipe lines go to the shell. Never put backticks in an echo: they are command
#     substitution, and an innocent-looking message will run the command it mentions.

mod server 'just/server.just'
mod mobile 'just/mobile.just'

_default:
    @just --list --list-submodules

# --- everyday workflow ----------------------------------------------------------------------
# The three recipes below are all most sessions need. Everything else is in the modules.

# Every step is safe to re-run: `docker compose up -d` is a no-op when the stack is already
# healthy, and the emulator script exits early if one is already running.

# Start a working session: dev stack, emulator with a window, app built and launched.
dev:
    just server::up
    just mobile::show
    just mobile::run
    @echo ""
    @echo "  Web      http://localhost:8000"
    @echo "  API      http://localhost:8080"
    @echo "  In the emulator, the server address is  http://10.0.2.2:8000"

# Stop the dev stack and the emulator, keeping the database, images and build output.
stop:
    -just server::down
    -just mobile::down
    @echo "Stopped. Data, images and build output are untouched - 'just nuke' removes those."

# Order matters: the emulator first so nothing holds a port, then Docker, then the host-side
# build output - which is the big one, and is NOT inside any image.

# Remove everything this project put on the machine: containers, volumes, images, build output.
[confirm("This deletes the dev database, this project's Docker images and ~1.4GB of build output. Continue? (y/N)")]
nuke:
    -just mobile::down
    -just server::reset
    -just server::clean-images
    just clean
    @echo ""
    @echo "Gone: containers, the dev database, trackr images, and all bin/obj."
    @echo "Kept: the postgres base image (slow to re-pull), the Android SDK, and the"
    @echo "      trackr-test AVD (~2GB). Remove the AVD with:"
    @echo "        \$ANDROID_HOME/cmdline-tools/latest/bin/avdmanager delete avd -n trackr-test"

# --- whole solution -------------------------------------------------------------------------

# For backend and web only, which needs no Android tooling, use `just server::build`.

# Build every project. Needs the maui-android workload.
build:
    dotnet build Trackr.slnx

# Every test suite. The API tests need Docker; the mobile tests need nothing.
test:
    just server::test
    just mobile::test

# Not `dotnet clean`, which only removes what the last build produced and leaves most of obj/
# behind - here that is the difference between reclaiming a few hundred MB and reclaiming all
# 1.4GB. The MAUI project alone accounts for about 865MB of it.
#
# If VS Code is open on the project, empty obj/ directories reappear within seconds: the C#
# Dev Kit watches the workspace and re-runs a design-time restore. That is a few MB of
# project.assets.json, not build output, and nothing has gone wrong. Close the editor first
# if you want the directories to stay gone.

# Delete every bin/ and obj/ in the repository.
clean:
    @find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true
    @echo "Removed all bin/ and obj/ directories."

restore:
    dotnet restore Trackr.slnx

# Everything a fresh checkout needs before anything else will work.
setup:
    dotnet tool restore
    dotnet restore Trackr.slnx
    @echo ""
    @echo "For the Android app you also need the maui-android workload, the Android SDK and"
    @echo "JDK 17. See the README. Check what is present with: just mobile::doctor"
