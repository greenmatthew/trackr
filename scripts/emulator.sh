#!/usr/bin/env bash
# Android emulator lifecycle for Trackr development.
#
#   ./scripts/emulator.sh start            headless - for automated checks and CI
#   ./scripts/emulator.sh start --window   visible on the Windows desktop via WSLg
#   ./scripts/emulator.sh install          build the Debug APK and install it
#   ./scripts/emulator.sh screenshot [f]   capture the screen to a PNG
#   ./scripts/emulator.sh logcat           follow the app's own log output
#   ./scripts/emulator.sh stop
#   ./scripts/emulator.sh status
#
# Requires membership of the `kvm` group. Without it the emulator falls back to interpreting
# every guest instruction in software and is unusable:
#
#   sudo usermod -aG kvm $USER      # then `wsl --shutdown` from Windows PowerShell
set -euo pipefail

AVD_NAME="${TRACKR_AVD:-trackr-test}"
export JAVA_HOME="${JAVA_HOME:-$HOME/.jdks/microsoft-17}"
export ANDROID_HOME="${ANDROID_HOME:-$HOME/Android/Sdk}"
export ANDROID_SDK_ROOT="$ANDROID_HOME"

# The SDK's platform-tools MUST come before /usr/bin. Debian ships its own adb (34.x) while
# the SDK has 37.x, and adb refuses to talk across a version mismatch - whichever starts the
# server first wins and kills the other, which surfaces as a device that will not appear.
export PATH="$ANDROID_HOME/platform-tools:$JAVA_HOME/bin:$ANDROID_HOME/emulator:$PATH"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APK="$REPO_ROOT/src/Trackr.Mobile/bin/Debug/net10.0-android/dev.trackr.app-Signed.apk"
PACKAGE="dev.trackr.app"

require_kvm() {
    if [ ! -r /dev/kvm ] || [ ! -w /dev/kvm ]; then
        echo "error: /dev/kvm is not accessible by $(whoami)." >&2
        echo "       Run: sudo usermod -aG kvm \$USER" >&2
        echo "       Then restart WSL from Windows PowerShell: wsl --shutdown" >&2
        exit 1
    fi
}

cmd_start() {
    require_kvm

    if adb devices | grep -q emulator; then
        echo "An emulator is already running."
        return 0
    fi

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

    emulator "${args[@]}" >/tmp/trackr-emulator.log 2>&1 &

    echo -n "Waiting for the device"
    adb wait-for-device
    # wait-for-device only waits for adb to answer; the system is still booting after that,
    # and installing too early fails with a confusing PackageManager error.
    until [ "$(adb shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; do
        echo -n "."
        sleep 2
    done

    echo " booted."
    adb devices
}

cmd_install() {
    (cd "$REPO_ROOT" && dotnet build src/Trackr.Mobile -c Debug -p:AndroidPackageFormat=apk)
    adb install -r "$APK"
    echo "Installed. Launch with: adb shell monkey -p $PACKAGE -c android.intent.category.LAUNCHER 1"
}

cmd_screenshot() {
    local out="${1:-/tmp/trackr-screen.png}"
    adb exec-out screencap -p > "$out"
    echo "$out"
}

cmd_logcat() {
    # DOTNET is where Microsoft.Extensions.Logging.Debug output lands on Android.
    adb logcat -s DOTNET:V "$PACKAGE":V AndroidRuntime:E
}

cmd_stop() {
    adb emu kill 2>/dev/null || true
    echo "Stopped."
}

cmd_status() {
    echo "AVD:      $AVD_NAME"
    echo "kvm:      $([ -w /dev/kvm ] && echo accessible || echo 'NOT accessible - see script header')"
    echo "adb:      $(command -v adb) ($(adb version | head -1))"
    adb devices
}

case "${1:-}" in
    start)      shift; cmd_start "$@" ;;
    install)    cmd_install ;;
    screenshot) shift; cmd_screenshot "$@" ;;
    logcat)     cmd_logcat ;;
    stop)       cmd_stop ;;
    status)     cmd_status ;;
    *)          sed -n '2,14p' "${BASH_SOURCE[0]}"; exit 1 ;;
esac
