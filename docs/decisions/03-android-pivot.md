# Android-first pivot

Not a milestone — a change of direction taken between milestones 2 and 3, before any of the
food-logging code existed.

## What changed

The original brief made Blazor WASM, installed as a PWA, *the* interface on both desktop and
phone. That is reversed. **The Android app is the product.** Meals are logged and stats are
read there. The website keeps doing exactly what milestone 2 built — login, password change,
2FA, invites — and gains an admin page later. It is not the food-logging surface.

## Why now

Milestones 3–9 did not exist. There was no chat UI, no stats view, no data layer, no
confirmation card. Nothing built had to be discarded: the API, Identity, Postgres, migrations,
invites, 2FA and rate limiting all survive untouched, and the Blazor app already *was* the
account surface the new shape calls for. The pivot would only get more expensive from here.

## Decisions

- **.NET MAUI with XAML, not MAUI Blazor Hybrid.** Blazor Hybrid would have reused the
  existing Razor components, but it renders them in a WebView — which is the thing being
  moved away from. XAML gets real Android controls. The maintainer already knows WPF XAML,
  `CommunityToolkit.Mvvm` and `Microsoft.Extensions.DependencyInjection`/`Hosting`, and
  `MauiApp.CreateBuilder()` *is* that same generic-host pattern, so the transferable part of
  the skill set is most of it. What is genuinely new: `Shell` navigation, the mobile control
  set (`CollectionView`, no `DataGrid`), handlers instead of control templates, and no visual
  designer. MAUI XAML is a subset of WPF XAML — expect gaps around triggers and templating.
- **Avalonia was considered.** Its XAML is closer to WPF's than MAUI's is, which would have
  been a smaller learning curve. MAUI wins on native mobile integration — camera, share
  intents, `SecureStorage` — which is precisely what the pivot is *for*.
- **One repository, not three.** The proposal was `trackr` + `trackr-backend` + a shared repo
  as a submodule. Rejected: `Trackr.Shared` is the coupling point, and a `ProjectReference` is
  free where a submodule costs detached HEADs and two-step commits, and a NuGet package costs
  a build-publish-bump cycle on every DTO change. Adding a nutrient field touches API, Shared
  and app — one commit here, three ordered PRs otherwise. Immich, the cited model for having a
  separate mobile app, is itself a monorepo.
- **No OpenAPI-generated client.** Immich generates TypeScript and Dart clients from
  `open-api/immich-openapi-specs.json` because its server, web and mobile are three languages.
  Trackr is C# throughout, so a project reference to `Trackr.Shared` is strictly better:
  compile-time checked, refactor-safe, no generation step, no spec/code drift. The API still
  serves an OpenAPI document (`AddOpenApi()`) for documentation and any future non-.NET
  client, just not as the source of truth.
- **Layout keeps `src/` + `tests/`.** Immich's top-level `server/ web/ mobile/` split exists
  because each is a different toolchain — pnpm, Flutter/Gradle, Python/uv. Every Trackr
  project is `dotnet` in one solution, so the split would buy little and cost the convention
  every .NET template and CI example assumes. The parts of that layout that do pay for
  themselves — `docs/` and `docker/` — were adopted.
- **Bearer tokens alongside the session cookie.** Milestone 2 recorded "cookie, not JWT" and
  reasoned that JWTs only matter for "cross-origin / multi-service / native-mobile setups,
  none of which apply." A MAUI app is exactly that, so the premise is now false. The web keeps
  cookies with every hardening decision intact; the API additionally exposes
  `IdentityConstants.BearerScheme`. This is additive — no cookie behaviour changes.
- **The app's 2FA flow is one endpoint, not two.** The web posts to `/api/auth/login`, gets
  `RequiresTwoFactor`, then posts to `/api/auth/login/2fa` — a handshake carried by the
  short-lived `TwoFactorUserId` *cookie*, which a native client has no good way to hold.
  `/api/auth/token` therefore takes an optional `twoFactorCode` and is posted twice.
- **No CORS for the app.** MAUI uses a native `HttpClient`; CORS is a browser mechanism.
  Adding CORS config to accommodate the app would be pure attack surface.
- **`Trackr.Mobile.Core` is a separate `net10.0` library.** View models, the API client and
  the platform abstractions live there so they can be unit-tested with plain xUnit — no
  Android SDK, no emulator, no `maui-android` workload. `Trackr.Mobile` holds only the MAUI
  app and the Android implementations. This is the standard MVVM split and the reason most
  logic stays testable.
- **`TargetFramework` moved out of `Directory.Build.props`** into each project, sourced from a
  new `$(TrackrTargetFramework)` property. A value inherited directly could not be suffixed to
  `net10.0-android`. A .NET upgrade is still a one-line change.
- **Risk-first ordering.** The Android toolchain and the auth change were the two unknowns, so
  a thin end-to-end mobile slice — server URL, login, 2FA, one placeholder screen, on a real
  phone — comes *before* milestones 3–5 rather than after.

## Mobile package choices

- **`CommunityToolkit.Mvvm`** — source-generated `[ObservableProperty]` / `[RelayCommand]`.
  Lives in `Trackr.Mobile.Core`, which is a plain `net10.0` library, so nothing about the
  MVVM layer is MAUI-specific or needs a device to compile.
- **`CommunityToolkit.Maui`** — converters, behaviors, toast/snackbar, popups. Note it needs
  an explicit `.UseMauiCommunityToolkit()` call in `MauiProgram`; unlike the MVVM toolkit it
  is not purely additive.
- **`Microsoft.Extensions.Http`** — taken chiefly for `AddHttpMessageHandler`, which is how
  the bearer-token attach-and-refresh handler gets wired, mirroring the web app's
  `UnauthorizedResponseHandler`.
- **`Microsoft.Extensions.Http.Resilience`** — retry, timeout, circuit breaker. A phone roams
  between wifi, cellular and VPN, so transient failure is the normal case here rather than an
  exceptional one.
- **`NSubstitute`** in `Trackr.Mobile.Tests` only. `Trackr.Api.Tests` deliberately has no
  mocking library because it drives the real application against a real Postgres; the mobile
  view-model tests have no equivalent real thing to drive, so substitutes earn their place.

### Explicitly not taken

- **DI, logging and configuration packages.** All three are already on `MauiAppBuilder` as
  `builder.Services`, `builder.Logging` and `builder.Configuration`. Adding
  `Microsoft.Extensions.DependencyInjection` or `.Logging` would duplicate what MAUI ships.
- **`Microsoft.Extensions.Hosting`.** `MauiAppBuilder` is modelled on `HostApplicationBuilder`
  but is not an `IHostBuilder`, and MAUI never runs `IHostedService` — adding it bolts on a
  host that nothing drives.
- **`appsettings.json`.** The only configuration that matters on a phone is the server URL,
  which the user types and which belongs in `SecureStorage`, not a file in the APK.

## Decided while building the thin slice

- **The app talks to `frontend`, not `backend`.** It points at the same URL a browser would,
  and nginx proxies `/api/` exactly as it already does for the web app. No compose change,
  no second published port, and `backend` stays on the internal network.
- **Android auto-backup is off** (`allowBackup="false"` plus empty `data_extraction_rules`).
  Otherwise the encrypted token store, and later the nutrition history, would be copied to
  Google Drive — which rather defeats self-hosting. The keystore key is device-bound and is
  not backed up anyway, so a restore would only ever yield undecryptable ciphertext.
- **`TrackrApiClient` builds absolute URIs** rather than setting `HttpClient.BaseAddress`.
  The server is unknown until first-run setup and can change afterwards, and `BaseAddress`
  cannot be reassigned once a client has sent a request.
- **Token refresh is serialised behind a semaphore**, and the refresh call deliberately uses
  a bare `HttpClient` rather than the one `BearerTokenHandler` is installed in — routing it
  back through itself would recurse. Without the lock, several screens resuming at once each
  spend the single-use refresh token and all but one gets signed out.
- **A failed refresh does not clear the tokens when the cause was a network error.** Only an
  actual rejection from the server ends the session. Signing someone out because their train
  entered a tunnel would be its own bug.
- **URL normalisation treats `host:8000` as a port and `mailto:x` as a scheme.** The naive
  "does it contain `://`" test gets this wrong: prefixing `https://` onto the mailto form
  yields a URL that parses perfectly well with `mailto:someone` as its userinfo, and would be
  saved as a server address. Non-http(s) schemes are rejected, and a missing scheme defaults
  to https — never http, since silently downgrading would ship a password in plaintext.
- **Navigation is provisional.** `INavigationService` has three named methods rather than a
  route-shaped API, and `AppShell` has three flat routes with no tabs. Milestone 5 replaces
  both; they exist now only because the slice needs to move between three screens.

  *(Counts are as this milestone shipped. A fourth route and method — registration — arrived
  afterwards, and the milestone that replaces them became 5 when the documentation migration
  was inserted ahead of it.)*

## Decided while setting up testing

- **Cleartext HTTP is permitted in Debug builds, for three addresses.** Android has blocked
  cleartext by default since API 28 and this app targets 36, so the plain-HTTP dev compose
  stack was simply unreachable — failing with a connection error that looks like an app bug
  rather than a platform policy. The exception covers `10.0.2.2` (the emulator's alias for
  the host loopback), `localhost` and `127.0.0.1`: addresses that can never be a real Trackr
  server, so nothing an attacker controls becomes reachable. Release forbids cleartext
  everywhere.
  - The switch is in `Trackr.Mobile.csproj`, not in the manifest: one manifest always
    references `@xml/network_security_config`, and an `AndroidResource` item with `Link`
    metadata decides which file supplies it. This was chosen over a second per-configuration
    manifest because two manifests drift, and the thing that would drift is a security
    control. Verified in both directions by dumping `res/xml/network_security_config.xml` out
    of a built Debug APK and a built Release APK — not by reading the source.
- **`google_apis` system image, not `google_apis_playstore`.** The Play variant ships a locked
  `/system` that refuses `adb root`, and Play services are irrelevant to this app.
- **The emulator runs inside WSL rather than on Windows.** WSLg is available, so a
  WSL-launched emulator still puts a real window on the Windows desktop — meaning one
  emulator serves both interactive use and headless automation. A Windows-hosted emulator
  would bind its adb to Windows' loopback, which WSL cannot reach under the default NAT
  networking mode, so the APK would have to be copied across to install and nothing could be
  self-verified.
- **`$ANDROID_HOME/platform-tools` must precede `/usr/bin` on `PATH`.** Debian's `adb` is 34.x
  and the SDK's is 37.x; adb refuses to work across a version mismatch, and the symptom is a
  device that never appears — which looks nothing like a `PATH` problem.
- **`EmbedAssembliesIntoApk=true` in Debug.** Debug builds otherwise use Fast Deployment,
  which leaves the managed assemblies *out* of the APK for `dotnet run` to push separately
  over adb. The resulting APK installs without complaint and then aborts on launch with
  `No assemblies found in '/data/user/0/dev.trackr.app/files/.__override__/x86_64'` — which
  names Fast Deployment but reads like a corrupt build. Since every APK here is installed
  with `adb install`, onto an emulator or a real phone, a self-contained APK is worth far
  more than the seconds Fast Deployment saves. **This was not hypothetical**: the first APK
  handed over had exactly this defect.
- **Task running is `just`, split into two modules.** `just/server.just` and
  `just/mobile.just`, dispatched from a root `Justfile` via `mod`. Two modules rather than
  one file because the halves have genuinely different toolchains and inner loops; module
  *files* in `just/` rather than beside the projects because neither module maps to a single
  project — `server` spans the API, the web app and the compose stacks.
  - Both module files need `set working-directory := '..'`. A `just` module runs from the
    directory of its own file, so every repo-relative path in them is otherwise wrong.
  - `just` uses the **last** comment line above a recipe as its `--list` description, so
    longer explanations live inside the recipe body instead.

## Consequences

- `Trackr.Client` is now `Trackr.Web`; "Client" was ambiguous once there were two front ends.
- The compose files and `.env.example` moved to `docker/`, so their build context is `..` and
  `.dockerignore` needed globbed `**/.env` patterns — a root-anchored `.env` would no longer
  have matched, which is exactly how a password ends up in an image layer.
- A bare `dotnet build` at the repository root now requires the `maui-android` workload. The
  Dockerfiles target specific `.csproj` files and are unaffected; `README.md` documents
  backend-only commands.
- The PWA install/offline shell is dropped from the polish milestone. Live camera barcode
  scanning stops being a speculative future extension and becomes an ordinary native
  capability.

## Since superseded

- **"Navigation is provisional" was resolved by milestone 5**
  ([06-mobile-ux.md](06-mobile-ux.md)). `INavigationService` kept its named methods and lost
  `GoToHomeAsync`: reaching the signed-in shell stopped being navigation at all and became a swap
  of `Window.Page`. `AppShell`'s flat routes became a three-tab `TabBar` plus one pushed route, and
  the route names moved into `Trackr.Mobile.Core` where the XAML binds them with `{x:Static}`
  instead of repeating them as literals.
- **`SecureStorage` is no longer the app's only persistence.** Milestone 5 added a SQLite database
  in app-private storage for the account and the profile picture. The tokens have not moved — they
  are still Keystore-backed, and the asymmetry with the web app's HttpOnly cookie described above
  is unchanged.
