#!/usr/bin/env bash
# Getting an Android device in front of adb, from WSL.
#
#   ./scripts/device.sh ensure             make sure exactly one device is usable; print its serial
#   ./scripts/device.sh list               everything adb can see
#   ./scripts/device.sh serial             the one connected phone's serial (not the emulator)
#   ./scripts/device.sh pair IP:PORT       wireless, step 1 - from the pairing-code dialog
#   ./scripts/device.sh connect IP:PORT    wireless, step 2 - from "IP address & Port"
#   ./scripts/device.sh usb [BUSID]        forward a USB-tethered phone into WSL
#   ./scripts/device.sh usb-detach         hand it back to Windows
#   ./scripts/device.sh reverse [PORT]     tunnel the dev stack to the device (default 8000)
#   ./scripts/device.sh doctor             check the toolchain before blaming the code
#
# `ensure` prints the serial on stdout and everything else on stderr, so it composes:
#
#     ANDROID_SERIAL=$(./scripts/device.sh ensure)
#
# Two separate gaps sit between WSL and a phone, and they are worth keeping apart because
# different commands close them:
#
#   1. WSL cannot see the phone.   USB is not passed through to the VM at all, and the phone's
#      adb port is not routable from here either. Closed by `usb`, or `pair` + `connect`.
#
#   2. The phone cannot see the dev stack.   Its ports are published on the Windows loopback,
#      not on the LAN, so no address typed on the phone reaches them. Closed by `reverse`,
#      which tunnels port 8000 down the adb connection - making the server address on the
#      phone http://localhost:8000, which the Debug cleartext exception already covers. No LAN
#      address, no TLS, and nothing to undo before a release build.
set -euo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

USBIPD="/mnt/c/Program Files/usbipd-win/usbipd.exe"

# The header comment above IS the usage message - printed by stripping the leading '#' from
# every line of it. Reading the file rather than repeating it keeps the two from drifting.
usage() {
    awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "${BASH_SOURCE[0]}"
}

# --- wireless -------------------------------------------------------------------------------
# Wireless debugging shows TWO ports and mixing them up is the usual failure - pairing
# succeeds and the phone still never appears in `adb devices`.
#
#   pair     takes the temporary port from the "Pair device with pairing code" dialog. It
#            exists only while that dialog is open, and only exchanges keys.
#   connect  takes the persistent port under "IP address & Port" on the main Wireless
#            debugging screen. This is what actually attaches the device.
#
# The pairing survives reboots; the connection and its port do not. So `pair` is the one-off
# and `connect` is what gets re-run, with a new port each time. There is no discovering it
# from here - `adb mdns services` finds nothing across the WSL NAT, because multicast does not
# cross it - so the port has to be read off the phone.

cmd_pair() {
    local address="${1:-}"
    [ -n "$address" ] || die "usage: device.sh pair <ip>:<pairing-port>" \
        "       Both come from the phone's 'Pair device with pairing code' dialog," \
        "       and both change every time it is reopened."

    adb_server
    "$ADB" pair "$address"

    echo ""
    echo "Paired - but not yet connected. Take the port from Wireless debugging ->"
    echo "IP address & Port (NOT the pairing port above) and run:"
    echo "  just mobile::connect ${address%%:*}:<port>"
}

cmd_connect() {
    local address="${1:-}"
    [ -n "$address" ] || die "usage: device.sh connect <ip>:<port>"

    case "$address" in
        *:*) ;;
        *) die "error: no port given. It is on the phone under Wireless debugging ->" \
               "       IP address & Port, it changes on every reboot, and it is NOT the" \
               "       pairing port." \
               "         just mobile::connect $address:<port>" ;;
    esac

    adb_server
    # `adb connect` exits 0 even when it prints "failed to connect to ...", so the device list
    # is the only trustworthy answer.
    "$ADB" connect "$address" || true

    if ! adb_devices | grep -qv '^emulator-'; then
        return 1
    fi

    mkdir -p "$STATE_DIR"
    printf '%s\n' "$address" > "$STATE_DIR/last-device"
    "$ADB" devices -l
}

# --- USB ------------------------------------------------------------------------------------
# usbipd-win forwards the device into WSL over USB/IP; it ships its own Linux client, so there
# is nothing to install in the distribution. Binding a device is a one-off needing an ADMIN
# PowerShell on Windows - this prints the exact line when it is needed. Add --auto-attach to
# the attach call to survive unplugging, at the cost of the command no longer returning.
#
# Detection reads `usbipd state`, not `usbipd list`. The list table truncates its DEVICE column
# to fit the terminal, and a phone in USB-tethering mode is described as "Remote NDIS based
# Internet Sharing Device, SAMSUNG Android ADB Interface" - long enough that the ADB part is
# exactly what gets cut off. `state` emits untruncated JSON.

cmd_usb() {
    local want="${1:-}"

    [ -x "$USBIPD" ] || die "error: usbipd-win is not installed on Windows." \
        "       In an admin PowerShell: winget install usbipd"

    # Prints "<busid> <state> <description>" for the one matching device, or exits non-zero.
    # A device is shared once it has a PersistedGuid, and attached once a client holds it.
    local found
    found=$("$USBIPD" state | python3 -c '
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
' "$want") || die "error: no single USB device advertising an ADB interface." \
        "       Plug the phone in, unlock it, enable USB debugging and accept the" \
        "       prompt on its screen. Then check what Windows can see:" \
        "         '$USBIPD' state" \
        "       and pass the bus ID directly:  just mobile::usb 7-3"

    local busid state description
    read -r busid state description <<<"$found"
    echo "Found $description on bus $busid"

    if [ "$state" = "unshared" ]; then
        die "error: bus $busid is not shared yet. Once, in an ADMIN PowerShell:" \
            "         usbipd bind --busid $busid" \
            "" \
            "       A bind covers one USB configuration, so changing the phone's USB mode" \
            "       (charging, file transfer, tethering) can present a different device" \
            "       that needs its own bind."
    fi

    if [ "$state" = "attached" ]; then
        echo "Already attached to a client; not re-attaching."
    else
        "$USBIPD" attach --wsl --busid "$busid"
    fi

    adb_server
    echo -n "Waiting for adb"
    for _ in $(seq 30); do
        if adb_devices | grep -qv '^emulator-'; then
            echo " ok."
            "$ADB" devices -l
            return 0
        fi
        echo -n "."
        sleep 1
    done

    echo ""
    die "error: attached, but adb never saw it. Check for an 'Allow USB debugging' prompt" \
        "       on the phone, then run: just mobile::devices"
}

cmd_usb_detach() {
    [ -x "$USBIPD" ] || die "error: usbipd-win is not installed on Windows."
    "$USBIPD" detach --all
}

# --- selection ------------------------------------------------------------------------------

cmd_list() {
    adb_server
    "$ADB" devices -l
}

# The one device that is not an emulator. Exported as ANDROID_SERIAL it makes every adb call
# target the phone instead:  export ANDROID_SERIAL=$(./scripts/device.sh serial)
cmd_serial() {
    adb_server

    local -a found=()
    mapfile -t found < <(adb_devices | grep -v '^emulator-' || true)

    case ${#found[@]} in
        1) printf '%s\n' "${found[0]}" ;;
        0) die "error: no phone connected." \
               "       USB:      just mobile::usb" \
               "       Wireless: just mobile::pair <ip>:<pairing-port>   (once)" \
               "                 just mobile::connect <ip>:<debug-port>  (after every reboot)" \
               "       Or let it walk you through it:  just mobile::run" ;;
        *) die "error: ${#found[@]} phones connected; set ANDROID_SERIAL to one of: ${found[*]}" ;;
    esac
}

cmd_reverse() {
    local port="${1:-8000}"
    local serial
    # Assigned first, exported second. `export x=$(cmd)` reports export's exit status rather
    # than the command's, so a failing lookup would sail past set -e and target the emulator.
    serial=$(cmd_serial)
    export ANDROID_SERIAL="$serial"
    "$ADB" reverse "tcp:$port" "tcp:$port"
    echo "In the app, the server address is now  http://localhost:$port"
}

# --- the wizard -----------------------------------------------------------------------------
# Everything above assumes a device is already there. `ensure` is what makes that true: it is
# the difference between `just mobile::run` failing with four lines of instructions and it
# simply working. Prompts go to stderr so the chosen serial is the only thing on stdout.

no_device_help() {
    echo "error: no device. Start one of these first:" >&2
    echo "         just emulator::up                       the emulator, headless" >&2
    echo "         just emulator::show                     the emulator, with a window" >&2
    echo "         just mobile::usb                        a USB-tethered phone" >&2
    echo "         just mobile::connect <ip>:<port>        a phone over wifi" >&2
}

# Wireless, walked through. `connect` first because pairing usually survives from last time,
# and re-pairing when it was not needed costs the user a trip into the phone's settings.
wizard_wireless() {
    local last="" address=""
    [ -f "$STATE_DIR/last-device" ] && last=$(cat "$STATE_DIR/last-device")

    echo "" >&2
    echo "On the phone: Developer options -> Wireless debugging." >&2
    echo "Read the port under 'IP address & Port' - it changes on every reboot." >&2
    if [ -n "$last" ]; then
        read -r -p "  Address [$last]: " address >&2
        address="${address:-$last}"
    else
        read -r -p "  Address (ip:port): " address >&2
    fi
    [ -n "$address" ] || return 1

    if cmd_connect "$address" >&2; then
        return 0
    fi

    echo "" >&2
    echo "Could not connect. Either the port has changed, or this machine is not paired" >&2
    echo "with the phone (deleting ~/.android/adbkey revokes a pairing, and so does" >&2
    echo "'Revoke USB debugging authorizations')." >&2

    local answer=""
    read -r -p "  Pair now? [Y/n]: " answer >&2
    case "$answer" in [nN]*) return 1 ;; esac

    echo "" >&2
    echo "On the phone: tap 'Pair device with pairing code'. That dialog shows its OWN port," >&2
    echo "different from the one above, and both it and the code die when it closes." >&2
    local pair_address=""
    read -r -p "  Pairing address (ip:port): " pair_address >&2
    [ -n "$pair_address" ] || return 1
    cmd_pair "$pair_address" >&2 || return 1

    echo "" >&2
    echo "Now back on the main Wireless debugging screen:" >&2
    read -r -p "  Address from 'IP address & Port' [$address]: " address >&2
    address="${address:-$last}"
    cmd_connect "$address" >&2
}

wizard_attach() {
    if ! interactive; then
        no_device_help
        return 1
    fi

    echo "No device is connected. What would you like to use?" >&2
    echo "  1) the emulator" >&2
    echo "  2) a phone over wifi" >&2
    echo "  3) a phone over USB" >&2
    local choice=""
    read -r -p "  [1]: " choice >&2

    case "${choice:-1}" in
        1) "$REPO_ROOT/scripts/emulator.sh" start >&2 ;;
        2) wizard_wireless ;;
        3) cmd_usb >&2 ;;
        *) die "Not one of the options." ;;
    esac
}

# More than one device is an ambiguity adb resolves by refusing, so resolve it here instead.
wizard_choose() {
    local -a found=("$@")

    if ! interactive; then
        echo "error: ${#found[@]} devices connected. Set ANDROID_SERIAL to one of:" >&2
        printf '         %s\n' "${found[@]}" >&2
        return 1
    fi

    echo "Several devices are connected:" >&2
    local i=1
    for d in "${found[@]}"; do
        echo "  $i) $d" >&2
        i=$((i + 1))
    done

    local choice=""
    read -r -p "  [1]: " choice >&2
    choice="${choice:-1}"
    [ "$choice" -ge 1 ] 2>/dev/null && [ "$choice" -le ${#found[@]} ] || die "Not one of the options."
    printf '%s\n' "${found[$((choice - 1))]}"
}

cmd_ensure() {
    adb_server

    local -a found=()
    mapfile -t found < <(adb_devices)

    if [ -n "${ANDROID_SERIAL:-}" ]; then
        for d in ${found[@]+"${found[@]}"}; do
            if [ "$d" = "$ANDROID_SERIAL" ]; then
                printf '%s\n' "$ANDROID_SERIAL"
                return 0
            fi
        done
        die "error: ANDROID_SERIAL=$ANDROID_SERIAL, but adb cannot see it."
    fi

    if [ ${#found[@]} -eq 0 ]; then
        wizard_attach || return 1
        mapfile -t found < <(adb_devices)
        [ ${#found[@]} -gt 0 ] || { no_device_help; return 1; }
    fi

    if [ ${#found[@]} -gt 1 ]; then
        wizard_choose "${found[@]}"
        return
    fi

    printf '%s\n' "${found[0]}"
}

# --- diagnostics ----------------------------------------------------------------------------

cmd_doctor() {
    echo "JAVA_HOME     $JAVA_HOME"
    if [ -x "$JAVA_HOME/bin/java" ]; then
        local v
        v=$("$JAVA_HOME/bin/java" -version 2>&1 | head -1)
        echo "              $v"
        case "$v" in *'"17'*) ;; *) echo "              WARNING: .NET for Android requires JDK 17." ;; esac
    else
        echo "              MISSING"
    fi

    echo "ANDROID_HOME  $ANDROID_HOME $([ -d "$ANDROID_HOME" ] || echo MISSING)"
    echo "workload      $(dotnet workload list 2>/dev/null | grep -c maui-android) maui-android"
    echo "adb           $("$ADB" version 2>/dev/null | sed -n 2p)"
    # Which adb is on PATH and which one owns the server are different questions, and the
    # second is the one that breaks pairing. See adb_server in lib.sh.
    echo "              on PATH: $(command -v adb || echo none)"
    echo "              server:  $(cat "$HOME/.android/adb.5037" 2>/dev/null || echo 'not running')"
    echo "kvm           $([ -w /dev/kvm ] && echo 'ok' || echo 'NOT accessible - sudo usermod -aG kvm $USER, then wsl --shutdown')"
    echo "avd           $(ls -d "$HOME"/.android/avd/*.avd 2>/dev/null | xargs -n1 basename 2>/dev/null | tr '\n' ' ')"
    "$ADB" devices
}

case "${1:-}" in
    ensure)     cmd_ensure ;;
    doctor)     cmd_doctor ;;
    list)       cmd_list ;;
    serial)     cmd_serial ;;
    pair)       shift; cmd_pair "$@" ;;
    connect)    shift; cmd_connect "$@" || die "error: not connected. Check the port is the current one - it changes" \
                    "       whenever wireless debugging is toggled or the phone reboots - and" \
                    "       re-pair if the phone has forgotten this machine." ;;
    usb)        shift; cmd_usb "$@" ;;
    usb-detach) cmd_usb_detach ;;
    reverse)    shift; cmd_reverse "$@" ;;
    *)          usage; exit 1 ;;
esac
