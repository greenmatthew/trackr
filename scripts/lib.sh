#!/usr/bin/env bash
# Shared setup for the Trackr scripts. Sourced, never executed:
#
#   source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
#
# Everything here exists because the Android toolchain is not on the ambient PATH in WSL and
# forgetting it produces confusing errors rather than clean ones.

# Sourced files must not set -e for the caller; each script does that for itself.

AVD_NAME="${TRACKR_AVD:-trackr-test}"
export JAVA_HOME="${JAVA_HOME:-$HOME/.jdks/microsoft-17}"
export ANDROID_HOME="${ANDROID_HOME:-$HOME/Android/Sdk}"
export ANDROID_SDK_ROOT="$ANDROID_HOME"

# The SDK's platform-tools MUST come before /usr/bin. Debian ships adb 34.x while the SDK has
# 37.x. They are not interchangeable, and the way it goes wrong is silent: see adb_server below.
export PATH="$ANDROID_HOME/platform-tools:$JAVA_HOME/bin:$ANDROID_HOME/emulator:$PATH"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ADB="$ANDROID_HOME/platform-tools/adb"
APK="${APK:-$REPO_ROOT/src/Trackr.Mobile/bin/Debug/net10.0-android/gg.matthewgreen.trackr-Signed.apk}"
# Overridable so the not-installed paths can be exercised without uninstalling anything.
PACKAGE="${PACKAGE:-gg.matthewgreen.trackr}"

# Where the wizard remembers the last wireless address it used.
STATE_DIR="${XDG_CACHE_HOME:-$HOME/.cache}/trackr"

die() {
    printf '%s\n' "$@" >&2
    exit 1
}

# Whether we may ask the user anything. Every prompt is guarded by this, so a script run from
# CI, a pipe or an agent fails with instructions instead of hanging on a read.
interactive() {
    [ -t 0 ] && [ -t 1 ]
}

# One adb server serves every client on the machine, and it belongs to whichever binary
# started it - not the one being invoked. So a bare `adb start-server` from Debian's
# /usr/bin/adb hands port 5037 to a 34.x server, and every later call from the SDK's adb is
# executed by it. That matters because `pair` and `connect` run server-side and the Debian
# build's mDNS support is not the SDK's: the symptom is pairing that inexplicably stops
# working. adb records the owning binary in ~/.android/adb.5037, which makes it checkable.
adb_server() {
    local owner
    owner=$(cat "$HOME/.android/adb.5037" 2>/dev/null || true)

    if [ -n "$owner" ] && [ "$owner" != "$ADB" ]; then
        echo "adb server belongs to $owner - restarting it from the SDK." >&2
        "$ADB" kill-server
    fi

    "$ADB" start-server >/dev/null 2>&1
}

# Serials of everything adb currently calls "device", one per line. Anything unauthorized or
# offline is deliberately excluded: those cannot be installed to, and reporting them as
# present sends the caller looking in the wrong place.
adb_devices() {
    "$ADB" devices | awk '$2 == "device" { print $1 }'
}

is_emulator() {
    case "$1" in emulator-*) return 0 ;; *) return 1 ;; esac
}
