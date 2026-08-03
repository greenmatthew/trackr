# Development Environment

## Prerequisites

| For | You need |
| --- | --- |
| Backend and web app | .NET 10 SDK, Docker |
| Android app | ...plus the `maui-android` workload, the Android SDK, and **JDK 17** |
| Running the emulator | ...plus membership of the `kvm` group |

`just doctor` checks all of it and says what is missing.

### JDK 17 specifically

.NET for Android does not accept newer JDKs, and fails with errors that never mention the
version — a machine with only JDK 21 or 25 will produce something baffling. Install 17
alongside whatever else you have; nothing needs to be uninstalled.

### The kvm group

The emulator boots a real x86-64 VM. Without hardware virtualization QEMU interprets every
guest instruction in software and the emulator is unusable, so it refuses instead.

```bash
sudo usermod -aG kvm $USER     # then restart WSL: `wsl --shutdown` from PowerShell
```

Building and signing an APK needs none of this — only *running* one does.

### Environment variables

The Android toolchain is not on the ambient `PATH`:

```bash
export JAVA_HOME=$HOME/.jdks/microsoft-17
export ANDROID_HOME=$HOME/Android/Sdk
export PATH=$ANDROID_HOME/platform-tools:$JAVA_HOME/bin:$PATH
```

**`platform-tools` must come before `/usr/bin`.** Debian ships its own `adb` (34.x) while the
SDK has 37.x, and adb refuses to work across a client/server version mismatch — whichever
starts the server first wins and kills the other. The symptom is a device that never appears,
which looks nothing like a `PATH` problem.

The `just` recipes set this themselves, so this only matters when running tools by hand.

## Task runner

Everything is wrapped in [`just`](https://just.systems). Most sessions need three recipes:

```bash
just dev        # dev stack + emulator with a window + app built, installed, launched
just stop       # stop both, keeping data, images and build output
just clean-all  # also delete the dev database, this project's images and ~1.4GB of bin/obj
```

`just dev` is safe to re-run — `docker compose up -d` is a no-op on a healthy stack and the
emulator script exits early if one is already running.

Everything else lives in modules. Bare `just` lists the top level and names them; `just
mobile` lists that one.

```bash
just server::watch          # hot-reload loop on http://localhost:5277
./scripts/server.sh logs backend
just server::reset          # drop the dev database only
./scripts/server.sh migration add AddFoodItems
just mobile::run            # build, install and launch, finding a device first
just emulator::up
just docs::publish          # push wiki/ to the wiki repository
just test                   # every suite
```

### Where the logic lives

`just` holds verbs — build, run, test, clean, up, down. Every individual step behind them is a
subcommand of a script in `scripts/`:

| Script | Covers |
| --- | --- |
| `scripts/app.sh` | The app — build, install, launch, stop, reset, uninstall, logs, screenshots, the UI dump |
| `scripts/device.sh` | adb and devices — pairing, connecting, USB forwarding, the port tunnel, `doctor`, and `ensure`, which is what makes `just mobile::run` work with nothing plugged in |
| `scripts/emulator.sh` | AVD lifecycle — start, stop, status, wipe, create, delete |
| `scripts/server.sh` | The dev stack — up, down, reset, logs, ps, health, images, and EF migrations |
| `scripts/lib.sh` | Sourced by the others: toolchain paths, and the check that the adb *server* belongs to the SDK's adb rather than Debian's |

Run any of them with no arguments for its own help. `just help` lists every recipe and names
the scripts.

The split is deliberate: `just` is for naming commands, and a shebang recipe is written to a
temporary file before it runs — so `$1` inside one is not what the caller passed to `just`,
which makes recipes a poor place for anything that branches or takes input.

## The development stack

```bash
just server::up
```

| URL | What |
| --- | --- |
| <http://localhost:8000> | the web app, via nginx — static files plus the `/api/` proxy |
| <http://localhost:8080> | the API directly, bypassing nginx |
| `localhost:5433` | Postgres — user/password/db `trackr` / `trackr_dev` / `trackr` |

Port 5433 rather than 5432 so it does not collide with a local Postgres. The credentials are
throwaway by design; never point this stack at real data.

`/openapi/v1.json` is served in Development only.

### Hot reload

For web and backend work, run only Postgres in a container and the app from the SDK:

```bash
just server::watch
```

In `Debug` the API also serves the Blazor app, so you get the same single origin as
production on one port — <http://localhost:5277>.

## Disk usage

Two things worth knowing before wondering where the space went:

- **Most of it is not in Docker.** Host-side build output is around 1.4 GB — `Trackr.Mobile`
  alone is ~865 MB — against ~230 MB of images. Removing images does not touch it.
  `just clean-all` takes both, along with the dev database. (`just clean` is the cheap tier —
  a plain `dotnet clean`, which leaves `obj/` and so reclaims almost none of this.)
- **Empty `obj/` directories reappear seconds after a clean** if VS Code is open. The C# Dev
  Kit watches the workspace and re-runs a design-time restore. That is a few MB of
  `project.assets.json`, not build output, and nothing has gone wrong.
