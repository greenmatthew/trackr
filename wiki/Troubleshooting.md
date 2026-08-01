# Troubleshooting

Symptoms and their usual causes. Most of these are a specific, non-obvious mechanism rather
than something being broken.

## Logging in appears to work, then does not stick

You are reaching the deployed app over plain HTTP. Outside Development the session cookie is
marked `Secure`; the browser accepts it and then silently refuses to send it back over HTTP,
so you land back on the login page with no error.

Reach it over HTTPS. See [Self-Hosting](Self-Hosting).

## The Android app cannot sign in, but the website can

Almost always one of three things:

1. **The account does not exist.** The app has no sign-up screen. Create the account in a
   browser first — see [Accounts and 2FA](Accounts-and-2FA).
2. **Wrong address.** In the emulator the dev stack is `http://10.0.2.2:8000`; `localhost`
   there means the emulator itself. A real phone cannot use `10.0.2.2` at all and must point
   at your deployed HTTPS server.
3. **A certificate Android does not trust.** The app reports this specifically. If your
   reverse proxy uses a self-signed or private-CA certificate, install that CA on the phone.

## "Could not verify the server's HTTPS certificate"

Exactly what it says: the phone does not trust your certificate chain. Either install your CA
on the device, or use a publicly trusted certificate. Trackr will not offer an "ignore
certificate errors" switch — that would hand the bearer token to anyone on the path.

## The app cannot reach a plain-HTTP server

Android has forbidden cleartext HTTP by default since API 28, and Trackr targets 36. Debug
builds carry a narrow exception for `10.0.2.2`, `localhost` and `127.0.0.1` — the emulator's
loopback aliases, which can never be a real server. **Release builds refuse cleartext
entirely**, on purpose.

If you need to reach a plain-HTTP server from a real phone, put TLS in front of it. There is
no build flag for this.

## The stack will not start

`POSTGRES_PASSWORD` is unset. The stack refuses rather than starting with a default — see
[Configuration](Configuration).

If it starts and then restarts in a loop, check `docker compose logs backend`. The most
common cause is the database not being reachable, which the liveness probe deliberately
tolerates so a database blip does not restart the API.

## Everyone was signed out after a redeploy

The data-protection keys were regenerated. They live in Postgres precisely so this does not
happen — if it did, the database was reset or restored from a different dump. See
[Backup and Restore](Backup-and-Restore).

## Password reset emails never arrive

By default they are not sent at all — the link goes to the backend log:

```bash
docker compose logs backend | grep -A3 "was not sent"
```

Set `TRACKR_EMAIL_PROVIDER=Smtp` to send real mail. See [Configuration](Configuration).

## Reset or invite links have the wrong hostname

The link is built from the request, which needs your reverse proxy to forward `Host` and
`X-Forwarded-Proto`. If it will not, set `TRACKR_PUBLIC_BASE_URL` explicitly.

## "Too many attempts" when nothing looks wrong

Rate limits are keyed on the caller's address, and behind a reverse proxy that can resolve to
the proxy — meaning everyone shares one budget. For a household that is usually fine; raise
`TRACKR_LOGIN_RATE_LIMIT` if it is not.

An account lockout is different and says so, with the time it unlocks. Five failed attempts
locks an account for 15 minutes, and wrong 2FA codes count too.

## Locked out with no authenticator and no recovery codes

There is no back door. You need database access to clear the account's 2FA state directly.
See [Accounts and 2FA](Accounts-and-2FA).

---

## Development

### The emulator will not start

Your user is not in the `kvm` group. The emulator boots a real VM and without hardware
virtualization it is unusably slow, so it refuses rather than crawling:

```bash
sudo usermod -aG kvm $USER    # then `wsl --shutdown` from Windows PowerShell
```

`just mobile::doctor` checks this along with the rest of the toolchain.

### Typing on the emulator does nothing

The keyboard is not passed through: keystrokes go nowhere and only the on-screen keyboard
works. The Pixel 6 device profile ships `hw.keyboard = no`, and it comes back every time the
AVD is recreated.

`scripts/emulator.sh` now fixes this at every start, so `just mobile::up` or `just
mobile::show` is enough. To do it by hand, set `hw.keyboard = yes` in
`~/.android/avd/<name>.avd/config.ini` and restart the emulator — it is read at launch.

### A phone plugged into the PC does not show up

WSL2 passes no USB through to the VM, so a tethered phone is invisible until it is forwarded
in. See [Testing the Android App](Testing-the-Android-App#connecting--tethered).

### A device never appears in `adb devices`

Two `adb` binaries with different versions. Debian ships 34.x and the Android SDK has 37.x;
adb refuses to work across a client/server mismatch, and whichever starts the server first
kills the other. Put the SDK's first:

```bash
export PATH=$ANDROID_HOME/platform-tools:$PATH
```

The symptom looks nothing like a `PATH` problem, which is why it is worth knowing.

### The app installs and then crashes immediately

If the log says `No assemblies found in .../.__override__/x86_64`, the APK was built without
`EmbedAssembliesIntoApk`. Debug builds default to Fast Deployment, which leaves the managed
assemblies outside the APK for `dotnet run` to push separately — so an `adb install` of that
APK always aborts on launch. Trackr sets the property; if you removed it, put it back.

### A build fails with a confusing JDK error

.NET for Android needs **JDK 17** specifically. Newer JDKs fail with messages that do not
mention the version. See [Development Environment](Development-Environment).
