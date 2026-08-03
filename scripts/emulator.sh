#!/usr/bin/env bash
# Android emulator lifecycle for Trackr development.
#
#   ./scripts/emulator.sh start            headless - for automated checks and CI
#   ./scripts/emulator.sh start --window   visible on the Windows desktop via WSLg
#   ./scripts/emulator.sh stop
#   ./scripts/emulator.sh status
#   ./scripts/emulator.sh wipe             next boot starts from a factory-fresh system image
#   ./scripts/emulator.sh create           create the AVD if it is not there yet
#   ./scripts/emulator.sh delete           remove the AVD itself (~2GB)
#
# Installing, screenshotting and following the log are the same on the emulator as on a phone,
# so they are recipes rather than subcommands here: just mobile::run, mobile::shot, mobile::logs.
#
# Requires membership of the `kvm` group. Without it the emulator falls back to interpreting
# every guest instruction in software and is unusable:
#
#   sudo usermod -aG kvm $USER      # then `wsl --shutdown` from Windows PowerShell
set -euo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
    awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "${BASH_SOURCE[0]}"
}

# The Pixel 6 device profile ships hw.keyboard=no, which silently drops every keystroke from
# the host: typing goes nowhere and only the on-screen keyboard works. That is maddening in an
# app whose first two screens are a server address and an email, and it comes back whenever the
# AVD is recreated - hence a check at every start rather than a one-off edit.
require_keyboard() {
    local ini
    ini="$(sed -n 's/^path=//p' "$HOME/.android/avd/$AVD_NAME.ini" 2>/dev/null)/config.ini"

    [ -f "$ini" ] || return 0
    grep -q '^hw\.keyboard *= *no' "$ini" || return 0

    sed -i 's/^hw\.keyboard *= *no/hw.keyboard = yes/' "$ini"
    echo "Enabled host keyboard passthrough (hw.keyboard) in $AVD_NAME."
}

require_kvm() {
    if [ ! -r /dev/kvm ] || [ ! -w /dev/kvm ]; then
        die "error: /dev/kvm is not accessible by $(whoami)." \
            "       Run: sudo usermod -aG kvm \$USER" \
            "       Then restart WSL from Windows PowerShell: wsl --shutdown"
    fi
}

running() {
    adb_devices | grep -q '^emulator-'
}

cmd_start() {
    require_kvm
    adb_server

    if running; then
        echo "An emulator is already running."
        return 0
    fi

    # Must happen before the emulator reads config.ini, i.e. before it launches.
    require_keyboard

    local args=(-avd "$AVD_NAME" -no-boot-anim -no-snapshot-save)

    if [ "${1:-}" = "--window" ]; then
        # WSLg puts this on the Windows desktop as an ordinary window.
        echo "Starting $AVD_NAME with a window (via WSLg)..."
    else
        # swiftshader_indirect renders in software: no GPU is exposed to WSL, and the
        # default 'auto' would try for hardware GL and fail.
        args+=(-no-window -gpu swiftshader_indirect)
        echo "Starting $AVD_NAME headless..."
    fi

    if [ "${1:-}" = "--wipe" ] || [ "${2:-}" = "--wipe" ]; then
        args+=(-wipe-data)
    fi

    emulator "${args[@]}" >/tmp/trackr-emulator.log 2>&1 &

    echo -n "Waiting for the device"
    "$ADB" wait-for-device
    # wait-for-device only waits for adb to answer; the system is still booting after that,
    # and installing too early fails with a confusing PackageManager error.
    until [ "$("$ADB" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; do
        echo -n "."
        sleep 2
    done

    echo " booted."
    "$ADB" devices
}

cmd_stop() {
    "$ADB" emu kill 2>/dev/null || true
    echo "Stopped."
}

# -wipe-data on the next boot rather than deleting anything now, so this is safe to run while
# the emulator is up: it takes effect when it is next started.
cmd_wipe() {
    if running; then
        cmd_stop
        sleep 2
    fi
    cmd_start --wipe
}

# The image the AVD is built from. google_apis rather than google_apis_playstore: the Play
# Store variant is locked down (no root adb, no writable system) for no benefit here, and
# nothing in Trackr uses Play services.
IMAGE="system-images;android-36;google_apis;x86_64"
DEVICE="pixel_6"

cmd_create() {
    local avdmanager="$ANDROID_HOME/cmdline-tools/latest/bin/avdmanager"
    [ -x "$avdmanager" ] || die "error: $avdmanager is missing." \
        "       Install the SDK command-line tools - see the Development-Environment wiki page."

    if [ -d "$HOME/.android/avd/$AVD_NAME.avd" ]; then
        echo "$AVD_NAME already exists."
        return 0
    fi

    if [ ! -d "$ANDROID_HOME/${IMAGE//;//}" ]; then
        echo "Fetching $IMAGE..."
        "$ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager" "$IMAGE"
    fi

    "$avdmanager" create avd --name "$AVD_NAME" --package "$IMAGE" --device "$DEVICE" --force
    require_keyboard
    echo "Created $AVD_NAME. Start it with: just emulator::up"
}

cmd_delete() {
    if running; then
        cmd_stop
        sleep 2
    fi
    "$ANDROID_HOME/cmdline-tools/latest/bin/avdmanager" delete avd -n "$AVD_NAME"
    echo "Deleted the $AVD_NAME AVD. Recreate it with: just emulator::create"
}

cmd_status() {
    echo "AVD:      $AVD_NAME $([ -d "$HOME/.android/avd/$AVD_NAME.avd" ] || echo 'MISSING - see just setup')"
    echo "kvm:      $([ -w /dev/kvm ] && echo accessible || echo 'NOT accessible - see script header')"
    echo "adb:      $ADB ($("$ADB" version | sed -n 2p))"
    "$ADB" devices
}

case "${1:-}" in
    start)  shift; cmd_start "$@" ;;
    stop)   cmd_stop ;;
    wipe)   cmd_wipe ;;
    create) cmd_create ;;
    delete) cmd_delete ;;
    status) cmd_status ;;
    *)      usage; exit 1 ;;
esac
