# The Android app.
#
# Verbs only. Every individual step - install, launch, logs, screenshots, the UI dump, pairing,
# USB forwarding - is a subcommand of a script in scripts/, because a recipe is a poor place
# for anything that branches or takes input:
#
#   ./scripts/app.sh        building, and driving the installed app
#   ./scripts/device.sh     adb, devices, pairing, the port tunnel
#
# Run either with no arguments for its own help.
#
# `just` shows the LAST comment line above a recipe as its `--list` description, and echoes any
# comment left inside a recipe body when it runs. So longer explanations sit above the recipe,
# separated from the one-line description by a blank line.

# Recipes in a module run from the module FILE's directory (just/), not the repository
# root - so every relative path below would be wrong without this.
set working-directory := '..'

APP := "./scripts/app.sh"
DEVICE := "./scripts/device.sh"

_default:
    @just --list mobile

# Works the same on the emulator and on a phone. If nothing is connected it asks what to use
# and walks through pairing or booting; with several connected it asks which. Set
# ANDROID_SERIAL to skip the question.

# Build, install and launch. Finds or sets up a device first.
run port="8000":
    #!/usr/bin/env bash
    set -euo pipefail
    # Assigned first, exported second. `export x=$(cmd)` reports export's exit status rather
    # than the command's, so a failing lookup would sail past set -e and target the wrong thing.
    serial=$({{ DEVICE }} ensure)
    export ANDROID_SERIAL="$serial"
    echo "Targeting $ANDROID_SERIAL"

    {{ APP }} build
    {{ APP }} install
    # Harmless on the emulator and essential on a phone, whose only route to the dev stack is
    # back down the adb connection.
    {{ DEVICE }} reverse {{ port }} >/dev/null
    {{ APP }} launch

    echo ""
    echo "In the app, the server address is  http://localhost:{{ port }}"
    case "$ANDROID_SERIAL" in
        emulator-*) echo "(http://10.0.2.2:{{ port }} works there too.)" ;;
    esac

# `just mobile::build release` for the Release APK, `build core` for the Core library alone.

# Build the Debug APK. Self-contained, so it can just be installed.
build kind="debug":
    @{{ APP }} build {{ kind }}

# Run the view-model tests. No emulator, no Android SDK, no Docker.
test:
    dotnet test tests/Trackr.Mobile.Tests

# Leaves obj/ alone, which is where nearly all of the MAUI project's bulk lives - see
# `clean-all` for the one that reclaims it.

# Remove the last build's output from the mobile projects.
clean:
    dotnet clean src/Trackr.Mobile
    dotnet clean src/Trackr.Mobile.Core
    dotnet clean tests/Trackr.Mobile.Tests

# Delete the mobile projects' build output entirely - about 865MB.
clean-all:
    @rm -rf src/Trackr.Mobile/bin src/Trackr.Mobile/obj \
            src/Trackr.Mobile.Core/bin src/Trackr.Mobile.Core/obj \
            tests/Trackr.Mobile.Tests/bin tests/Trackr.Mobile.Tests/obj
    @echo "Removed mobile build output."
