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

APK := "src/Trackr.Mobile/bin/Debug/net10.0-android/gg.matthewgreen.trackr-Signed.apk"
PACKAGE := "gg.matthewgreen.trackr"
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
# Two separate gaps sit between WSL and a phone, and it is worth keeping them apart because
# they are fixed by different recipes.
#
#   1. WSL cannot see the phone.   USB is not passed through to the VM at all, and the phone's
#      adb port is not routable from here either. Closed by `usb` (tethered) or `pair` +
#      `connect` (wireless).
#
#   2. The phone cannot see the dev stack.   Its ports are published on the Windows loopback,
#      not on the LAN, so no address the phone can type reaches them. Closed by `reverse`,
#      which tunnels port 8000 back down the adb connection - so the server address on the
#      phone is http://localhost:8000, an address the Debug cleartext exception already
#      covers. No LAN address, no TLS, and nothing to undo before a release build.
#
# `phone` does both plus build, install and launch. Everything here needs USB debugging on,
# under Developer options.

USBIPD := "/mnt/c/Program Files/usbipd-win/usbipd.exe"

# The serial of the connected phone, i.e. the one device that is not an emulator. Exported as
# ANDROID_SERIAL it makes every other recipe here target the phone instead:
#
#     export ANDROID_SERIAL=$(just mobile::serial)

# Print the connected phone's adb serial.
serial:
    #!/usr/bin/env bash
    set -euo pipefail
    mapfile -t found < <({{ ADB }} devices | awk '$2 == "device" && $1 !~ /^emulator-/ { print $1 }')
    case ${#found[@]} in
        1) echo "${found[0]}" ;;
        0) echo "error: no phone connected. Run 'just mobile::usb', or pair over wireless." >&2; exit 1 ;;
        *) printf 'error: %d phones connected; set ANDROID_SERIAL to one of: %s\n' "${#found[@]}" "${found[*]}" >&2; exit 1 ;;
    esac

# Tethered USB. usbipd-win forwards the device into WSL over USB/IP; it ships its own Linux
# client, so there is nothing to install in the distribution. Binding a device is a one-off
# that needs an ADMIN PowerShell on Windows - the recipe prints the exact line if it is
# needed. Add --auto-attach to the attach line below to have it survive unplugging the cable,
# at the cost of the command no longer returning.
#
# Detection reads `usbipd state`, not `usbipd list`. The list table truncates its DEVICE
# column to fit the terminal, and a phone in USB-tethering mode is described as "Remote NDIS
# based Internet Sharing Device, SAMSUNG Android ADB Interface" - long enough that the ADB
# part is exactly what gets cut off. `state` emits untruncated JSON.

# Attach a USB-tethered phone into WSL: `just mobile::usb [BUSID]`.
usb busid="":
    #!/usr/bin/env bash
    set -euo pipefail

    if [ ! -x "{{ USBIPD }}" ]; then
        echo "error: usbipd-win is not installed on Windows." >&2
        echo "       In an admin PowerShell: winget install usbipd" >&2
        exit 1
    fi

    # Prints "<busid> <state> <description>" for the one matching device, or exits non-zero.
    # A device is shared once it has a PersistedGuid, and attached once a client holds it.
    found=$("{{ USBIPD }}" state | python3 -c '
    import json, sys
    want = sys.argv[1]
    devices = [d for d in json.load(sys.stdin)["Devices"] if d.get("BusId")]
    hits = [d for d in devices if d["BusId"] == want] if want else \
           [d for d in devices if "ADB" in (d.get("Description") or "")]
    if len(hits) != 1:
        sys.exit(1)
    d = hits[0]
    state = "attached" if d.get("ClientIPAddress") else "shared" if d.get("PersistedGuid") else "unshared"
    print(d["BusId"], state, d.get("Description") or "")
    ' "{{ busid }}") || {
        echo "error: no single USB device advertising an ADB interface." >&2
        echo "       Plug the phone in, unlock it, enable USB debugging and accept the" >&2
        echo "       prompt on its screen. Then check what Windows can see:" >&2
        echo "         '{{ USBIPD }}' state" >&2
        echo "       and pass the bus ID directly:  just mobile::usb 7-3" >&2
        exit 1
    }

    read -r busid state description <<<"$found"
    echo "Found $description on bus $busid"

    if [ "$state" = "unshared" ]; then
        echo "error: bus $busid is not shared yet. Once, in an ADMIN PowerShell:" >&2
        echo "         usbipd bind --busid $busid" >&2
        echo "" >&2
        echo "       A bind covers one USB configuration, so changing the phone's USB mode" >&2
        echo "       (charging, file transfer, tethering) can present a different device" >&2
        echo "       that needs its own bind." >&2
        exit 1
    fi

    if [ "$state" = "attached" ]; then
        echo "Already attached to a client; not re-attaching."
    else
        "{{ USBIPD }}" attach --wsl --busid "$busid"
    fi

    echo -n "Waiting for adb"
    for _ in $(seq 30); do
        if {{ ADB }} devices | awk '$2 == "device" && $1 !~ /^emulator-/' | grep -q .; then
            echo " ok."
            {{ ADB }} devices -l
            exit 0
        fi
        echo -n "."
        sleep 1
    done

    echo ""
    echo "error: attached, but adb never saw it. Check for an 'Allow USB debugging' prompt" >&2
    echo "       on the phone, then run: just mobile::devices" >&2
    exit 1

# Hand the phone back to Windows. Unplugging it does the same thing.
usb-detach:
    "{{ USBIPD }}" detach --all

# Port and code come from Developer options -> Wireless debugging -> Pair with code.

# Pair with a phone over wifi: `just mobile::pair 192.168.1.50:37000`.
pair address:
    {{ ADB }} pair {{ address }}

# Connect to a paired phone: `just mobile::connect 192.168.1.50`.
connect ip port="5555":
    {{ ADB }} connect {{ ip }}:{{ port }}

# Undone by a phone reboot or an adb disconnect, so re-run it after either. Harmless to
# repeat. Works over USB and wireless alike - the tunnel rides the adb connection itself.

# Tunnel the dev stack to the phone, where it becomes http://localhost:8000.
reverse port="8000":
    #!/usr/bin/env bash
    set -euo pipefail
    # Assigned first, exported second. 'export x=$(cmd)' reports export's exit status, not
    # the command's, so a failing lookup would sail past set -e and target the emulator.
    serial=$(just mobile::serial)
    export ANDROID_SERIAL="$serial"
    {{ ADB }} reverse tcp:{{ port }} tcp:{{ port }}
    echo "In the app, the server address is now  http://localhost:{{ port }}"

# The whole loop on a real phone: build, install, tunnel, launch.
phone port="8000":
    #!/usr/bin/env bash
    set -euo pipefail
    # Assigned first, exported second - see the note in `reverse`.
    serial=$(just mobile::serial)
    export ANDROID_SERIAL="$serial"
    echo "Targeting $ANDROID_SERIAL"
    just mobile::build
    {{ ADB }} install -r {{ APK }}
    {{ ADB }} reverse tcp:{{ port }} tcp:{{ port }}
    activity=$({{ ADB }} shell cmd package resolve-activity --brief -c android.intent.category.LAUNCHER {{ PACKAGE }} | tail -1 | tr -d '\r')
    {{ ADB }} shell am start -n "$activity"
    echo ""
    echo "In the app, the server address is  http://localhost:{{ port }}"

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
