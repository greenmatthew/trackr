# Trackr

A self-hosted personal nutrition tracker with a chat-first interface. The product is an
**Android app**; the website is account and administration only. See [CLAUDE.md](CLAUDE.md)
for the full project brief and [docs/.claude/decisions/](docs/.claude/decisions/) for why things are the way
they are.

**Status: milestone 2 (auth) complete; milestone 3 (mobile foundation) in progress.** The
server stack runs end to end and everything is behind a login: ASP.NET Core Identity with
invite-only registration, authenticator-app 2FA, account lockout and rate limiting. There is
no food data model and no chat UI yet.

## Layout

```
src/Trackr.Api/            ASP.NET Core Web API          + Dockerfile
src/Trackr.Web/            Blazor WebAssembly, accounts  + Dockerfile + nginx.conf
src/Trackr.Mobile/         .NET MAUI Android app          (the product)
src/Trackr.Mobile.Core/    Its view models and API client (testable without Android)
src/Trackr.Shared/         DTOs referenced by all of the above
tests/Trackr.Api.Tests/    Integration tests against a throwaway Postgres
tests/Trackr.Mobile.Tests/ View-model tests, no device needed
docker/                    Compose stacks and .env.example
docs/.claude/decisions/    Why each milestone was built the way it was
docs/wiki/                 The project wiki — edited here, published from here
```

The wiki pages live in this repository, so a change to a flag and a change to the page
documenting it are one commit. `just docs::publish` copies them to the GitHub and Gitea
wikis; editing a page in either web UI will be overwritten on the next publish.

One repository on purpose: `Trackr.Shared` is a project reference rather than a published
package or a generated client, so a contract change is one commit rather than three.

Requires the .NET 10 SDK and Docker. The Android app additionally needs the `maui-android`
workload, the Android SDK and **JDK 17** — see [CLAUDE.md §11](CLAUDE.md).

## Trying the app

The short version, from a clean machine to signed in on the emulator.

**Once, ever:** the emulator boots a real VM, so your user needs access to `/dev/kvm`.
Building an APK does not — only running one does.

```bash
sudo usermod -aG kvm $USER     # then `wsl --shutdown` from Windows PowerShell
just mobile::doctor            # checks JDK 17, the SDK, the workload, kvm and the AVD
```

**Every session:**

```bash
just dev
```

That starts the dev stack, boots the emulator with a window on your desktop, then builds,
installs and launches the app. Safe to re-run — it's a no-op for anything already running.

**Then, in this order — the app has no sign-up screen, so the account must exist first:**

1. **Create an account in the browser** at <http://localhost:8000>. On an empty database
   registration is open and the first account claims the server; after that it needs an
   invite (Settings → Invites). *Skipping this is the usual reason the app's login fails.*
2. **In the emulator, enter the server address:**

   ```
   http://10.0.2.2:8000
   ```

   `10.0.2.2` is the emulator's fixed alias for your machine's loopback — `localhost` inside
   the emulator means the emulator itself. Plain `http` works here only because Debug builds
   ship a narrow cleartext exception for exactly this address; Release builds refuse it.
3. **Sign in** with the account from step 1. That's the whole slice: the app stores a bearer
   token in Android's keystore and shows the email the server returned.

**Finishing up:**

```bash
just stop     # stop the stack and the emulator, keep data, images and build output
just nuke     # also delete the dev database, this project's images and ~1.4GB of bin/obj
```

### While testing

| | |
|---|---|
| `just mobile::logs` | the app's own log output, and any crash |
| `just mobile::ui` | every text on screen — better than a screenshot for checking a label |
| `just mobile::shot` | capture a PNG |
| `just mobile::reset-app` | forget the server address and tokens, back to first-run setup |
| `just mobile::run` | rebuild, reinstall and relaunch after a code change |
| `just server::logs backend` | server-side logs |

**To test the 2FA path**, enable it in the browser under Settings → Two-factor
authentication, then sign in on the app again — it will ask for a code on the second attempt.
Recovery codes work in the same field via the checkbox.

**On a real phone** instead of the emulator, point it at your deployed HTTPS server rather
than `10.0.2.2`, which means nothing off the emulator. See [On a real phone](#on-a-real-phone).

## Task runner

Everything is wrapped in [`just`](https://just.systems) recipes. Most sessions need only the
three above; everything else lives in two modules, `just/server.just` and `just/mobile.just`:

```bash
just                       # list everything
just server::watch         # hot-reload loop for backend and web work
just server::logs backend
just server::reset         # drop the dev database only
just server::migration AddFoodItems

just mobile::show          # emulator with a window, via WSLg
just mobile::up            # ... or headless
just mobile::run           # build, install and launch
just mobile::doctor        # check the Android toolchain before blaming the code

just test                  # every suite
```

Two things worth knowing about cleanup:

- **Most of the disk is not in Docker.** Host-side `dotnet build` output is ~1.4 GB
  (`Trackr.Mobile` alone is ~865 MB), against ~230 MB of images. Deleting images does not
  touch it — `just nuke`, or `just clean` on its own, is what reclaims it.
- **Empty `obj/` folders reappear seconds after a clean** if VS Code is open: the C# Dev Kit
  re-runs a design-time restore. That is a few MB of metadata, not build output.

The raw commands are all documented below too, since `just` is a convenience rather than a
dependency.

## Running it

### Full stack in Docker

```bash
docker compose -f docker/docker-compose.dev.yml up -d --build
```

| URL | What |
|---|---|
| <http://localhost:8000> | the web app (nginx: static files + `/api/` proxy) |
| <http://localhost:8080> | the API directly, bypassing nginx |
| `localhost:5433` | Postgres (user/password/db: `trackr` / `trackr_dev` / `trackr`) |

```bash
docker compose -f docker/docker-compose.dev.yml down -v   # -v also drops the database
```

### Fast loop with hot reload

For web work, run only Postgres in Docker and the app from the SDK. In `Debug` the
API also serves the Blazor app, so you get the same single origin as production
on one port:

```bash
docker compose -f docker/docker-compose.dev.yml up -d db
dotnet watch --project src/Trackr.Api
```

Everything is then on <http://localhost:5277>, with `/openapi/v1.json` available
(Development only).

### Building

A bare `dotnet build` at the root needs the `maui-android` workload, since `Trackr.Mobile` is
in the solution. For backend and web work, name the projects instead:

```bash
dotnet build src/Trackr.Api src/Trackr.Web src/Trackr.Shared
```

The Dockerfiles restore and publish specific projects, so images are unaffected either way.

### The Android app

Needs the `maui-android` workload, the Android SDK, and **JDK 17** specifically — .NET for
Android rejects newer JDKs with a confusing error, so a machine with only JDK 21 or 25 will
fail. Both point at wherever you installed them:

```bash
export JAVA_HOME=$HOME/.jdks/microsoft-17
export ANDROID_HOME=$HOME/Android/Sdk
export PATH=$ANDROID_HOME/platform-tools:$JAVA_HOME/bin:$PATH

dotnet build src/Trackr.Mobile -c Debug -p:AndroidPackageFormat=apk
```

The APK lands at `src/Trackr.Mobile/bin/Debug/net10.0-android/dev.trackr.app-Signed.apk`,
signed with the local debug key. It is self-contained (`EmbedAssembliesIntoApk`), so it can
be installed anywhere — without that, a Debug APK installs fine and then aborts on launch
because Fast Deployment left the managed assemblies outside it.

On first launch the app asks for your server's address, then signs in. Point it at the same
URL you'd open in a browser — it uses the same `/api/` routes nginx already proxies.

#### In the emulator

Needs your user in the `kvm` group — the emulator boots a real VM, and without hardware
virtualization it is unusably slow. Building an APK does **not** need this:

```bash
sudo usermod -aG kvm $USER     # once; then `wsl --shutdown` from Windows PowerShell
```

Then everything goes through one script:

```bash
./scripts/emulator.sh start            # headless
./scripts/emulator.sh start --window   # with a window on your Windows desktop, via WSLg
./scripts/emulator.sh install          # build + adb install
./scripts/emulator.sh logcat
./scripts/emulator.sh stop
```

Inside the emulator, the dev stack is at **`http://10.0.2.2:8000`** — `10.0.2.2` is the
emulator's fixed alias for the host's loopback; `localhost` there means the emulator itself.
Plain HTTP works only because Debug builds ship a narrow cleartext exception for exactly that
address. Release builds refuse cleartext entirely.

#### On a real phone

Wireless debugging is simpler than USB — WSL's NAT handles outbound connections fine, so
`usbipd-win` is not needed. Enable *Developer options → Wireless debugging* on the phone:

```bash
adb pair <phone-ip>:<pairing-port>     # code shown on the phone
adb connect <phone-ip>:5555
adb install -r src/Trackr.Mobile/bin/Debug/net10.0-android/dev.trackr.app-Signed.apk
adb logcat -s DOTNET:V                 # the app's ILogger output
```

Point a real phone at your **deployed HTTPS server**, not the dev stack — `10.0.2.2` is
meaningless off the emulator, and the cleartext exception does not cover your LAN.

### Tests

```bash
dotnet test tests/Trackr.Api.Tests      # needs Docker
dotnet test tests/Trackr.Mobile.Tests   # no Docker, no Android SDK
```

The API tests start their own throwaway Postgres container (Testcontainers) and drive the
real application, migrations included, so Docker must be running. They do not touch the
dev stack's database.

## Accounts

**The first account claims the server.** On a fresh database `/register` is open; create
your account and registration closes permanently. After that, new accounts need a
single-use invite, which any signed-in user can mint under **Settings → Invites**. There
is no public sign-up at any point (CLAUDE.md §8.4).

**Two-factor authentication** is opt-in per account, under **Settings**. Scan the QR code
with an authenticator app, type one code to prove it works, then save the ten recovery
codes — they are shown once and only their hashes are stored.

**Lockout and rate limiting** are on by default: five failed attempts locks an account for
15 minutes, and the auth endpoints are rate limited per client address. Both are
configurable through the environment variables in `docker/.env.example`.

### If you forget your password

By default Trackr has no email server, so a reset link is written to the backend log
instead of being sent:

```bash
docker compose logs backend | grep -A3 "was not sent"
```

Open that link to set a new password. The trade-off is explicit: **anyone who can read the
backend logs can take over an account.** On a private server whose logs only you can read,
that is the same trust boundary as the database itself. To send real email instead, set
`TRACKR_EMAIL_PROVIDER=Smtp` and the `TRACKR_SMTP_*` variables — see `docker/.env.example`.

## Health endpoints

| Endpoint | Purpose |
|---|---|
| `GET /api/health` | Full report incl. database state. What the app's status page calls. 503 when unhealthy. |
| `GET /api/health/live` | Liveness. 200 whenever the process is up, **even if Postgres is down** — this is the container healthcheck, and a database blip must not trigger a restart loop. |
| `GET /api/health/ready` | Readiness. 200 only when Postgres is reachable too. |

## Deploying

`docker/docker-compose.yml` is the deployment stack, meant to be added to Portainer as a
**Git repository stack** so Portainer builds the images itself — there is no
registry in the loop. Point Portainer's compose path at `docker/docker-compose.yml`.

1. Copy `docker/.env.example` to `docker/.env` and set `POSTGRES_PASSWORD` (or set it as a
   stack environment variable in Portainer). The stack refuses to start without it.
2. Make sure the external network named in `PROXY_NETWORK` (default `proxy`)
   exists and your reverse proxy is on it.
3. Point the reverse proxy at the `frontend` service on port 80. It terminates TLS;
   nothing inside the stack does.

Only `frontend` joins the proxy network — `backend` and `db` stay on the internal
network and are unreachable from outside it.

> **Reach the deployed app over HTTPS.** Outside Development the session cookie is marked
> `Secure`, so a browser will accept it and then silently refuse to send it back over
> plain HTTP: logging in appears to work and then does not stick. Terminate TLS at your
> reverse proxy, and add HSTS and an http→https redirect **there** — deliberately not in
> these containers, which would break `http://localhost:8000` in development.

Strongest protection, per CLAUDE.md §8.5: do not expose the login to the public internet
at all. Reaching it over VPN or LAN only makes brute-force concerns nearly moot.

## Notes on the setup

- **One origin, for the browser.** nginx serves the WASM app and reverse-proxies `/api/` to
  the backend. That is what allows a plain HttpOnly session cookie instead of a token
  (CLAUDE.md §3), and it means the web app never needs CORS. The Android app is a native
  client on a different origin, so it authenticates with an Identity **bearer token**
  instead — and still needs no CORS, because CORS is a browser mechanism.
- **No HTTPS inside the containers.** TLS terminates at the reverse proxy; the app
  reads `X-Forwarded-Proto` instead.
- **Postgres 18** keeps its data in `/var/lib/postgresql/<major>/docker`, so the
  volume mounts `/var/lib/postgresql` — not the `/var/lib/postgresql/data` path used
  by Postgres 17 and earlier.
- **Migrations apply themselves at startup.** There is one replica, so there is no
  migration race, and a Portainer redeploy needs no extra step. To add one:

  ```bash
  dotnet tool restore                          # dotnet-ef, pinned in dotnet-tools.json
  ASPNETCORE_ENVIRONMENT=Development \
    dotnet dotnet-ef migrations add SomeName --project src/Trackr.Api --output-dir Migrations
  ```

  The environment prefix matters: `dotnet ef` ignores `launchSettings.json`, so without it
  the connection string is missing and startup throws.
- **Data-protection keys live in Postgres**, not in the container filesystem. They encrypt
  the session cookie and the bearer tokens, and the container has no volume for them — on
  disk they would be regenerated on every restart, silently signing everyone out on each
  redeploy.
- **The session cookie is `SameSite=Strict` on a single origin, with JSON-only request
  bodies.** That combination is the CSRF defence; there are deliberately no antiforgery
  tokens.
- **Data-protection keys are stored unencrypted** in the same database as the password
  hashes, on the internal-only network. Encrypting them would need a certificate and real
  key-management; on a private single-user server the database is already the trust
  boundary.
