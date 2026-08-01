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

Wireless debugging is simpler than USB — WSL's NAT handles outbound connections fine, so
`usbipd-win` is not needed. Enable *Developer options → Wireless debugging*:

```bash
just mobile::pair 192.168.1.50:37000   # port and code shown on the phone
just mobile::connect 192.168.1.50
just mobile::install
```

Point a real phone at your **deployed HTTPS server**. `10.0.2.2` is meaningless off the
emulator, and the cleartext exception does not cover your LAN.
