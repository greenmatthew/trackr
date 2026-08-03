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
just emulator::up      # headless, for automated checks
just emulator::show    # with a window on the desktop, via WSLg
just mobile::run       # build, install and launch
just emulator::down
```

`just mobile::run` works the same on the emulator and on a phone — it finds whatever is
connected, and asks what to start if nothing is. `just emulator::clean` factory-resets the
device; `just emulator::clean-all` deletes the AVD outright, and `./scripts/emulator.sh create`
brings it back.

Running it inside WSL rather than on Windows is deliberate: WSLg puts a real window on the
Windows desktop anyway, so one emulator serves both interactive use and headless automation.
A Windows-hosted emulator binds its adb to Windows' loopback, which WSL cannot reach under
default NAT networking.

### Driving it from the command line

More is possible than it first appears:

```bash
./scripts/app.sh shot    # screenshot to a PNG you can actually look at
./scripts/app.sh ui      # every text on screen, from the view hierarchy
./scripts/app.sh logs    # the app's own ILogger output, plus crashes

adb shell input tap 540 1254
adb shell input text "owner@example.test"
adb shell input keyevent KEYCODE_ESCAPE
```

**Prefer `./scripts/app.sh ui` over screenshots for assertions.** It dumps the view hierarchy, so
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

1. `./scripts/app.sh reset` — back to first-run setup.
2. Enter `http://10.0.2.2:8000`, connect.
3. Sign in. On an empty database, use *No account yet? Create one* and claim the server from
   the app itself; otherwise sign in with an existing account.
4. Confirm it lands on **Home** with the three tabs along the bottom, and that the launch did
   not flicker through another screen on the way.
5. Tap the avatar at the top right. Profile opens *with a back arrow*, and shows the email the
   server returned. Go back; you should return to the tab you came from.
6. *Change picture*, choose an image. The circle becomes the photo in **both** places — the
   profile and the title bar — without a relaunch. *Remove* puts the initials back, also in
   both.
7. `./scripts/app.sh stop` then `./scripts/app.sh launch` — it must land on **Home, not login**,
   with the picture still there.
8. Sign out; confirm it returns to login with the server address retained.

Steps 7 and 8 are the ones that matter, for different reasons. Step 7 exercises the secure
token store, the bearer handler and the API's token scheme together, and nothing below this
tier can. Step 8 is a privacy check as much as a navigation one: signing out must empty the
local database, not merely stop drawing it.

### Checking the offline path

The app keeps the account and the picture locally so it opens without a network. Worth a look
after anything touching `AuthSession` or the local store:

```bash
docker stop trackr-dev-backend-1
./scripts/app.sh stop && ./scripts/app.sh launch
```

It must open **signed in**, on Home, with the avatar drawn from disk — not on the login
screen. Signing someone out because their phone has no signal is the bug this prevents. Then:

```bash
docker start trackr-dev-backend-1
```

To look at what is actually stored, read the database off the device — this is also the check
that SQLite's native library really loaded, which a successful build does not prove:

```bash
adb shell run-as gg.matthewgreen.trackr cat files/trackr.db > /tmp/trackr.db
sqlite3 /tmp/trackr.db 'PRAGMA user_version; SELECT email FROM account;'
```

A rejected session is different from an unreachable one and must still sign you out. There is
no convenient way to force that from the emulator; changing the account's password on the web
app rolls the security stamp and does it.

## 3. A physical phone

Required for camera work, and the only real check of the reverse-proxy path.

Two gaps sit between WSL and a phone. They are fixed separately, and confusing them is the
usual reason this feels harder than it is:

| Gap | Why | Fix |
| --- | --- | --- |
| WSL cannot see the phone | WSL2 passes no USB through to the VM, and the phone's adb port is not routable from here | `./scripts/device.sh usb`, or `pair` + `connect` |
| The phone cannot see the dev stack | Its ports are published on the Windows loopback, not on the LAN, so there is no address the phone could type | `./scripts/device.sh reverse` |

Both are closed for you:

```bash
just mobile::run        # build, install, tunnel and launch, in one go
```

It asks what to connect to if nothing is attached, and which one if several are. The
underlying steps are `./scripts/device.sh` subcommands — run it with no arguments for the
list — and the ones worth reaching for by hand are wrapped as `./scripts/device.sh pair`,
`connect` and `usb`.

**If you last installed a build from before the package was renamed** to
`gg.matthewgreen.trackr`, remove the old one first. Android treats a changed application ID as
a different app, so the two would otherwise sit side by side:

```bash
adb uninstall dev.trackr.app
```

The saved server address and stored token do not survive the change — they live in storage
keyed by package name — so the app starts again at first-run setup. A password manager entry
for the old ID needs repointing as well.

### Connecting — tethered

`usbipd-win` forwards the device into WSL over USB/IP. It ships its own Linux client, so
there is nothing to install in the distribution:

```bash
./scripts/device.sh usb           # finds the ADB interface and attaches it
./scripts/device.sh usb-detach    # hand it back to Windows; unplugging does the same
```

Sharing a device is a one-off that needs an **admin PowerShell** on Windows — the script
prints the exact `usbipd bind` line if it is needed. Install usbipd itself with
`winget install usbipd`.

### Connecting — wireless

No Windows-side tooling at all. Enable *Developer options → Wireless debugging*. `just
mobile::run` walks the whole thing and remembers the address for next time; the two steps it
walks are also available on their own:

```bash
./scripts/device.sh pair 192.168.1.50:41543      # "Pair device with pairing code" — port and code
./scripts/device.sh connect 192.168.1.50:37000   # "IP address & Port" on the main screen
```

**The two ports are different, and using the pairing port for `connect` is the usual failure —
pairing succeeds and the phone still never shows up in `./scripts/device.sh list`.** The pairing
port belongs to the pairing dialog and dies with it; the connect port is the one on the main
Wireless debugging screen. Pairing survives reboots, the connection does not, so `connect` is
what gets re-run — with a **new port each time** the phone reboots or wireless debugging is
toggled. `adb mdns services` cannot discover it from WSL, since multicast does not cross the
NAT; read it off the phone.

Two failures worth telling apart:

| Symptom | Cause |
| --- | --- |
| `failed to connect to <ip>:<port>` | Nothing is listening — stale port, or wireless debugging is off. Confirm with `ping <ip>`: if the phone answers, the network is fine and only the port is wrong. |
| *Pairing unsuccessful: make sure the device is connected to the same network* | Usually not the network. The pairing port and code belong to the open dialog and change every time it is reopened, so a stale pair is the first thing to rule out. |

**Only ever delete `~/.android/adbkey` as a last resort** — the phone trusts that key, so removing
it revokes the pairing and forces a fresh one. The Windows copy under `%USERPROFILE%\.android`
is a different machine's key and is irrelevant here: every `just mobile::*` recipe runs the
Linux adb at `$ANDROID_HOME/platform-tools/adb`.

Which is not necessarily the adb that is *serving* it. One adb server, on port 5037, serves
every client on the machine, and it belongs to whichever binary started it — so a bare
`adb start-server` can hand it to Debian's older `/usr/bin/adb` and leave the recipes talking
to that instead. Pairing runs server-side, so this shows up as pairing that mysteriously stops
working. `just doctor` prints the owning binary; `pair` and `connect` restart the
server from the SDK when it is the wrong one.

### Reaching the dev stack

`./scripts/device.sh reverse` tunnels port 8000 down the adb connection, so on the phone the dev
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

adb needs telling which one. `just mobile::run` asks; for everything else, export the serial
once and every recipe follows:

```bash
export ANDROID_SERIAL=$(./scripts/device.sh serial)     # the phone, not the emulator
export ANDROID_SERIAL=$(./scripts/device.sh ensure)     # whichever, asking if ambiguous
```

With `ANDROID_SERIAL` already set, `just mobile::run` skips the question entirely — which is
also how to drive it from a script.
