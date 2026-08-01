# Building

## Backend and web app

```bash
just server::build
```

Or by hand:

```bash
dotnet build src/Trackr.Api src/Trackr.Web src/Trackr.Shared
```

**Naming the projects is deliberate.** A bare `dotnet build` at the repository root also
tries to build `Trackr.Mobile`, which fails on any machine without the `maui-android`
workload. If you have the workload, `just build` does the whole solution.

The Dockerfiles restore and publish specific `.csproj` files, so container images are
unaffected either way.

## The Android app

```bash
just mobile::build            # Debug APK
just mobile::build-release    # Release APK
```

Or by hand, with the environment from
[Development Environment](Development-Environment) exported:

```bash
dotnet build src/Trackr.Mobile -c Debug -p:AndroidPackageFormat=apk
```

The APK lands at:

```
src/Trackr.Mobile/bin/Debug/net10.0-android/dev.trackr.app-Signed.apk
```

signed with the local debug key. Fine for sideloading onto your own phone; not for
distribution.

### Why the APK is self-contained

Debug builds set `EmbedAssembliesIntoApk`. Without it, .NET for Android uses **Fast
Deployment**, which leaves the managed assemblies *outside* the APK for `dotnet run` to push
separately over adb. The resulting file installs without complaint and then aborts on launch:

```
No assemblies found in '/data/user/0/dev.trackr.app/files/.__override__/x86_64'
```

which names Fast Deployment but reads like a corrupt build. Since every APK here is installed
with `adb install` — onto an emulator, a phone, or handed to someone else — a self-contained
APK is worth far more than the seconds Fast Deployment saves. Do not remove the property.

### Cleartext HTTP, and why Release differs

The manifest always points at `@xml/network_security_config`; which file supplies that
resource is chosen by build configuration in `Trackr.Mobile.csproj`.

| Configuration | Cleartext HTTP |
| --- | --- |
| Debug | Permitted to `10.0.2.2`, `localhost` and `127.0.0.1` only |
| Release | Forbidden everywhere |

Those three addresses are the emulator's loopback aliases and can never be a real Trackr
server, so nothing an attacker controls becomes reachable. The switch lives in the project
file rather than in a second manifest so the exception cannot reach a release build by being
forgotten.

**Verify it from a built APK rather than the source:**

```bash
aapt2 dump xmltree <apk> --file res/xml/network_security_config.xml
```

## Tests

```bash
just test                              # everything
dotnet test tests/Trackr.Api.Tests     # needs Docker
dotnet test tests/Trackr.Mobile.Tests  # needs nothing
```

The API tests start their own throwaway Postgres via Testcontainers and drive the real
application, migrations included. They never touch the development stack's database.

The mobile tests run against `Trackr.Mobile.Core`, which is plain `net10.0` — no Android SDK,
no emulator, no workload. If something is hard to test there, that is usually a sign logic has
leaked into the MAUI project.

## Database migrations

```bash
just server::migration AddFoodItems
```

By hand, the environment prefix is load-bearing — `dotnet ef` ignores `launchSettings.json`,
so without it the connection string is missing and startup throws:

```bash
dotnet tool restore
ASPNETCORE_ENVIRONMENT=Development \
  dotnet dotnet-ef migrations add SomeName --project src/Trackr.Api --output-dir Migrations
```

Migrations apply themselves at startup, so there is no separate step on deploy.
