#!/usr/bin/env bash
# The Trackr app itself: building it, and driving the copy installed on a device.
#
#   ./scripts/app.sh build [release|core]   Debug APK, Release APK, or just the Core library
#   ./scripts/app.sh install                install the already-built APK
#   ./scripts/app.sh launch                 start it without rebuilding
#   ./scripts/app.sh stop                   force-stop it
#   ./scripts/app.sh reset                  wipe its stored data - next launch is first-run setup
#   ./scripts/app.sh uninstall
#   ./scripts/app.sh logs                   follow its ILogger output plus any crash
#   ./scripts/app.sh shot [FILE]            screenshot to a PNG (default /tmp/trackr-screen.png)
#   ./scripts/app.sh ui                     every text on screen, from the view hierarchy
#
# `just mobile::run` is build + install + launch with a device found first; these are the
# individual steps, for when only one of them is wanted.
#
# Everything after `build` talks to whatever adb is pointed at. With more than one device
# attached, set ANDROID_SERIAL first:
#
#     export ANDROID_SERIAL=$(./scripts/device.sh ensure)
set -euo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
    awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "${BASH_SOURCE[0]}"
}

# -p:AndroidPackageFormat=apk rather than the default .aab: an APK is what `adb install`
# takes, and a bundle would need bundletool in between for no benefit on a sideload.
cmd_build() {
    case "${1:-debug}" in
        debug)   dotnet build "$REPO_ROOT/src/Trackr.Mobile" -c Debug -p:AndroidPackageFormat=apk ;;
        release) dotnet build "$REPO_ROOT/src/Trackr.Mobile" -c Release -p:AndroidPackageFormat=apk ;;
        # Quicker than a full APK build, and enough to typecheck view-model work.
        core)    dotnet build "$REPO_ROOT/src/Trackr.Mobile.Core" ;;
        *)       die "usage: app.sh build [release|core]" ;;
    esac
}

cmd_install() {
    [ -f "$APK" ] || die "error: $APK does not exist. Build it first: just mobile::build"
    "$ADB" install -r "$APK"
}

# MAUI derives the activity name from a hash of the namespace, so hardcoding it breaks the
# moment the namespace changes.
cmd_launch() {
    local activity
    activity=$("$ADB" shell cmd package resolve-activity --brief \
        -c android.intent.category.LAUNCHER "$PACKAGE" | tail -1 | tr -d '\r')

    case "$activity" in
        "$PACKAGE"/*) ;;
        *) die "error: $PACKAGE is not installed on this device." \
               "       just mobile::run builds, installs and launches it." ;;
    esac

    echo "Starting $activity"
    "$ADB" shell am start -n "$activity"
}

cmd_stop() {
    "$ADB" shell am force-stop "$PACKAGE"
}

# Clears the saved server address and the tokens in secure storage along with everything else.
cmd_reset() {
    "$ADB" shell pm clear "$PACKAGE"
}

cmd_uninstall() {
    "$ADB" uninstall "$PACKAGE" || true
}

# DOTNET is where Microsoft.Extensions.Logging.Debug output lands on Android.
cmd_logs() {
    "$ADB" logcat -s DOTNET:V "$PACKAGE":V AndroidRuntime:E
}

cmd_shot() {
    local out="${1:-/tmp/trackr-screen.png}"
    "$ADB" exec-out screencap -p > "$out"
    echo "$out"
}

# Better than a screenshot for asserting a label: this is the text the app actually rendered,
# not pixels that have to be read back.
cmd_ui() {
    "$ADB" shell uiautomator dump /sdcard/ui.xml >/dev/null
    "$ADB" shell cat /sdcard/ui.xml | tr '>' '>\n' | grep -oE 'text="[^"]+"' | sort -u
}

case "${1:-}" in
    build)     shift; cmd_build "$@" ;;
    install)   cmd_install ;;
    launch)    cmd_launch ;;
    stop)      cmd_stop ;;
    reset)     cmd_reset ;;
    uninstall) cmd_uninstall ;;
    logs)      cmd_logs ;;
    shot)      shift; cmd_shot "$@" ;;
    ui)        cmd_ui ;;
    *)         usage; exit 1 ;;
esac
