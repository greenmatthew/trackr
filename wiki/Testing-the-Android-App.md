# Testing the Android App

Three tiers, and only the first two can be automated. It is worth being explicit about which
one a claim of "it works" rests on — *the APK builds* and *the app runs* are very different
statements, and the gap between them has already hidden two real bugs.

## 1. View-model tests

```bash
just mobile::test
```

Plain xUnit against `Trackr.Mobile.Core`. No device, no emulator, no Android SDK, no Docker —
they run in about a tenth of a second. Most logic should be covered here.

This is the entire reason `Trackr.Mobile.Core` is a separate project from `Trackr.Mobile`.

## 2. The emulator

Needs the `kvm` group — see [Development Environment](Development-Environment).

```bash
just mobile::up        # headless, for automated checks
just mobile::show      # with a window on the desktop, via WSLg
just mobile::run       # build, install and launch
just mobile::down
```

Running it inside WSL rather than on Windows is deliberate: WSLg puts a real window on the
Windows desktop anyway, so one emulator serves both interactive use and headless automation.
A Windows-hosted emulator binds its adb to Windows' loopback, which WSL cannot reach under
default NAT networking.

### Driving it from the command line

More is possible than it first appears:

```bash
just mobile::shot                  # screenshot to a PNG you can actually look at
just mobile::ui                    # every text on screen, from the view hierarchy
just mobile::logs                  # the app's own ILogger output, plus crashes

adb shell input tap 540 1254
adb shell input text "owner@example.test"
adb shell input keyevent KEYCODE_ESCAPE
```

**Prefer `just mobile::ui` over screenshots for assertions.** It dumps the view hierarchy, so
it can confirm a specific label's *text* and gives real element coordinates instead of
guessing where to tap.

### Reaching the dev stack

Inside the emulator the dev stack is at:

```
http://10.0.2.2:8000
```

`10.0.2.2` is the emulator's fixed alias for the host's loopback; `localhost` there means the
emulator itself. Plain HTTP works only because Debug builds ship a narrow cleartext exception
for exactly that address — see [Building](Building).

### A full manual pass

Worth doing after any auth or navigation change:

1. `just mobile::reset-app` — back to first-run setup.
2. Enter `http://10.0.2.2:8000`, connect.
3. Sign in with an account created in the browser first — **the app has no sign-up screen.**
4. Confirm the home screen shows the email the server returned.
5. `just mobile::stop-app` then `just mobile::launch` — it must land on **home, not login**.
   This is the step that proves the token really persisted in the keystore.
6. Sign out; confirm it returns to login with the server address retained.

Step 5 is the one that matters. It exercises the secure token store, the bearer handler and
the API's token scheme together, and nothing below this tier can.

## 3. A physical phone

Required for camera work, and the only real check of the reverse-proxy path.

Two gaps sit between WSL and a phone. They are fixed separately, and confusing them is the
usual reason this feels harder than it is:

| Gap | Why | Fix |
| --- | --- | --- |
| WSL cannot see the phone | WSL2 passes no USB through to the VM, and the phone's adb port is not routable from here | `just mobile::usb`, or `pair` + `connect` |
| The phone cannot see the dev stack | Its ports are published on the Windows loopback, not on the LAN, so there is no address the phone could type | `just mobile::reverse` |

Once both are closed:

```bash
just mobile::phone      # build, install, tunnel and launch, in one go
```

### Connecting — tethered

`usbipd-win` forwards the device into WSL over USB/IP. It ships its own Linux client, so
there is nothing to install in the distribution:

```bash
just mobile::usb           # finds the ADB interface and attaches it
just mobile::usb-detach    # hand it back to Windows; unplugging does the same
```

Sharing a device is a one-off that needs an **admin PowerShell** on Windows — `just
mobile::usb` prints the exact `usbipd bind` line if it is needed. Install usbipd itself with
`winget install usbipd`.

### Connecting — wireless

No Windows-side tooling at all. Enable *Developer options → Wireless debugging*:

```bash
just mobile::pair 192.168.1.50:37000   # port and code shown on the phone
just mobile::connect 192.168.1.50
```

### Reaching the dev stack

`just mobile::reverse` tunnels port 8000 down the adb connection, so on the phone the dev
stack is at:

```
http://localhost:8000
```

This is why there is no LAN address to work out and no TLS to arrange: `localhost` on the
phone is already covered by the Debug cleartext exception, exactly as `10.0.2.2` is on the
emulator. It works over USB and wireless alike, and needs re-running after a phone reboot or
an adb disconnect.

To check the **reverse-proxy path** instead, skip `reverse` and point the phone at your
deployed HTTPS server. `10.0.2.2` is meaningless off the emulator, and the cleartext
exception does not cover your LAN.

### When both a phone and the emulator are connected

adb needs telling which one. `just mobile::phone` and `reverse` resolve the phone
themselves; for everything else, export the serial once and every recipe follows:

```bash
export ANDROID_SERIAL=$(just mobile::serial)
```
