# The Android app: building it, and running it on the emulator or a real phone.
#
# Every recipe exports the toolchain locations itself, because they are not on the ambient
# PATH and forgetting them produces confusing errors rather than clean ones. In particular
# platform-tools must precede /usr/bin: Debian ships adb 34.x while the SDK has 37.x, and adb
# refuses to work across a version mismatch - the symptom is a device that never appears.
#
# `just` shows the LAST comment line above a recipe as its `--list` description, and echoes
# any comment left inside a recipe body when it runs. So longer explanations sit above the
# recipe, separated from the one-line description by a blank line. (Comments inside a
# shebang recipe such as `launch` are fine - those run as a script, not line by line.)

# Recipes in a module run from the module FILE's directory (just/), not the repository
# root - so every relative path below would be wrong without this.
set working-directory := '..'

export JAVA_HOME := env("JAVA_HOME", home_directory() / ".jdks/microsoft-17")
export ANDROID_HOME := env("ANDROID_HOME", home_directory() / "Android/Sdk")

APK := "src/Trackr.Mobile/bin/Debug/net10.0-android/dev.trackr.app-Signed.apk"
PACKAGE := "dev.trackr.app"
ADB := "$ANDROID_HOME/platform-tools/adb"

_default:
    @just --list mobile

# --- building -----------------------------------------------------------------------------

# Build the Debug APK. Self-contained, so it can just be installed.
build:
    PATH="$ANDROID_HOME/platform-tools:$JAVA_HOME/bin:$PATH" \
      dotnet build src/Trackr.Mobile -c Debug -p:AndroidPackageFormat=apk

# Signed with the debug key unless you supply a real keystore - fine for sideloading onto
# your own phone, not for distribution.

# Build the Release APK.
build-release:
    PATH="$ANDROID_HOME/platform-tools:$JAVA_HOME/bin:$PATH" \
      dotnet build src/Trackr.Mobile -c Release -p:AndroidPackageFormat=apk

# Run the view-model tests. No emulator, no Android SDK, no Docker.
test:
    dotnet test tests/Trackr.Mobile.Tests

# Build only the Core library - quicker than a full APK build.
build-core:
    dotnet build src/Trackr.Mobile.Core

# rm rather than `dotnet clean`: the latter leaves most of obj/ in place, and obj/ is where
# nearly all of the MAUI project's bulk lives.

# Delete the mobile projects' build output - about 865MB.
clean:
    @rm -rf src/Trackr.Mobile/bin src/Trackr.Mobile/obj \
            src/Trackr.Mobile.Core/bin src/Trackr.Mobile.Core/obj \
            tests/Trackr.Mobile.Tests/bin tests/Trackr.Mobile.Tests/obj
    @echo "Removed mobile build output."

# --- emulator -----------------------------------------------------------------------------
# Thin wrappers over scripts/emulator.sh, which holds what is awkward in a recipe: the kvm
# check and the wait-for-boot loop.

# Boot the emulator headless.
up:
    ./scripts/emulator.sh start

# Boot the emulator with a window on the Windows desktop, via WSLg.
show:
    ./scripts/emulator.sh start --window

# Shut the emulator down.
down:
    ./scripts/emulator.sh stop

# Show emulator, adb and kvm status.
status:
    ./scripts/emulator.sh status

# Build, install and launch on whatever device adb is talking to.
run: build install launch

# Install the already-built APK.
install:
    {{ ADB }} install -r {{ APK }}

# Launch the app, resolving its activity name from the package manager.
launch:
    #!/usr/bin/env bash
    set -euo pipefail
    # MAUI derives the activity name from a hash of the namespace, so hardcoding it breaks
    # the moment the namespace changes.
    activity=$({{ ADB }} shell cmd package resolve-activity --brief -c android.intent.category.LAUNCHER {{ PACKAGE }} | tail -1 | tr -d '\r')
    echo "Starting $activity"
    {{ ADB }} shell am start -n "$activity"

# Force-stop the app.
stop-app:
    {{ ADB }} shell am force-stop {{ PACKAGE }}

# Clears the saved server address and the tokens in secure storage.

# Wipe stored data so the next launch starts at first-run setup.
reset-app:
    {{ ADB }} shell pm clear {{ PACKAGE }}

# Uninstall the app.
uninstall:
    -{{ ADB }} uninstall {{ PACKAGE }}

# Follow the app's own ILogger output plus any crash.
logs:
    {{ ADB }} logcat -s DOTNET:V {{ PACKAGE }}:V AndroidRuntime:E

# Capture the screen: `just mobile::shot /tmp/x.png`.
shot file="/tmp/trackr-screen.png":
    @{{ ADB }} exec-out screencap -p > {{ file }}
    @echo "{{ file }}"

# Dump every text on screen - better than a screenshot for checking a label.
ui:
    @{{ ADB }} shell uiautomator dump /sdcard/ui.xml >/dev/null
    @{{ ADB }} shell cat /sdcard/ui.xml | tr '>' '>\n' | grep -oE 'text="[^"]+"' | sort -u

# List attached devices and emulators.
devices:
    {{ ADB }} devices -l

# --- physical phone -----------------------------------------------------------------------

# Port and code come from Developer options -> Wireless debugging -> Pair with code.

# Pair with a phone: `just mobile::pair 192.168.1.50:37000`.
pair address:
    {{ ADB }} pair {{ address }}

# Connect to a paired phone: `just mobile::connect 192.168.1.50`.
connect ip port="5555":
    {{ ADB }} connect {{ ip }}:{{ port }}

# --- diagnostics --------------------------------------------------------------------------

# Check the toolchain before blaming the code.
doctor:
    #!/usr/bin/env bash
    echo "JAVA_HOME     $JAVA_HOME"
    if [ -x "$JAVA_HOME/bin/java" ]; then
        v=$("$JAVA_HOME/bin/java" -version 2>&1 | head -1)
        echo "              $v"
        case "$v" in *'"17'*) ;; *) echo "              WARNING: .NET for Android requires JDK 17." ;; esac
    else
        echo "              MISSING"
    fi
    echo "ANDROID_HOME  $ANDROID_HOME $([ -d "$ANDROID_HOME" ] || echo MISSING)"
    echo "workload      $(dotnet workload list 2>/dev/null | grep -c maui-android) maui-android"
    echo "adb           $({{ ADB }} version 2>/dev/null | head -1)"
    echo "kvm           $([ -w /dev/kvm ] && echo 'ok' || echo 'NOT accessible - sudo usermod -aG kvm $USER, then wsl --shutdown')"
    echo "avd           $(ls -d ~/.android/avd/*.avd 2>/dev/null | xargs -n1 basename 2>/dev/null | tr '\n' ' ')"
    {{ ADB }} devices
