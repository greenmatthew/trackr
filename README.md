# Trackr

A self-hosted personal nutrition tracker with a chat-first interface. You log meals by typing
what you ate — and optionally attaching a photo — into a chat on your phone; the server works
out the nutrition and asks you to confirm before saving anything.

The product is an **Android app**. The website is account and administration only.

Food photos never leave your server: barcodes are decoded locally and images go only to an AI
model running in a container of your own. The only thing that goes out to the internet is a
barcode number, looked up against [Open Food Facts](https://world.openfoodfacts.org/).

> **Status: early.** Accounts, 2FA and the Android app's sign-in work today. Meal logging does
> not exist yet — see the build order in [CLAUDE.md](CLAUDE.md).

## Layout

```
src/Trackr.Api/            ASP.NET Core Web API          + Dockerfile
src/Trackr.Web/            Blazor WebAssembly, accounts  + Dockerfile + nginx.conf
src/Trackr.Mobile/         .NET MAUI Android app          (the product)
src/Trackr.Mobile.Core/    Its view models and API client (testable without Android)
src/Trackr.Shared/         DTOs referenced by all of the above
tests/                     API integration tests, and view-model tests
docker/                    Compose stacks and .env.example
just/                      Task-runner recipes
wiki/                      The project wiki — edited here, published from here
docs/decisions/            Why each milestone was built the way it was
```

One repository on purpose: `Trackr.Shared` is a project reference rather than a published
package or a generated client, so a contract change is one commit rather than three. The wiki
is here for the same reason — a flag and the page documenting it change together.

## Getting started

Requires the .NET 10 SDK and Docker. The Android app also needs the `maui-android` workload,
the Android SDK, **JDK 17**, and membership of the `kvm` group to run the emulator —
`just mobile::doctor` checks all of it.

```bash
just dev      # dev stack + emulator + the app, built and launched
just stop     # stop both, keeping data and build output
just nuke     # also delete the dev database, images and build output
```

`just dev` is safe to re-run, so it doubles as "make sure everything is up". Then:

1. **Create an account** at <http://localhost:8000>. On an empty database the first account
   claims the server. Do this first — **the app has no sign-up screen**, and skipping it is
   the usual reason a first login fails.
2. **In the emulator, enter the server address `http://10.0.2.2:8000`.** That is the
   emulator's alias for your machine; `localhost` there means the emulator itself.
3. **Sign in.**

`just` on its own lists every recipe.

## Documentation

| | |
| --- | --- |
| [Self-Hosting](wiki/Self-Hosting.md) | Deploying behind your reverse proxy |
| [Configuration](wiki/Configuration.md) | Every environment variable |
| [Accounts and 2FA](wiki/Accounts-and-2FA.md) | Invites, authenticator apps, recovery codes |
| [Backup and Restore](wiki/Backup-and-Restore.md) | What to back up, and what breaks if you miss part |
| [Troubleshooting](wiki/Troubleshooting.md) | Symptoms and their usual causes |
| [Development Environment](wiki/Development-Environment.md) | Toolchain and the dev stack |
| [Building](wiki/Building.md) | Backend, web app and the Android APK |
| [Testing the Android App](wiki/Testing-the-Android-App.md) | Emulator and physical device |

These pages are published to the project wiki with `just docs::publish`. Edit them here —
changes made in the wiki's web UI are overwritten on the next publish.

[CLAUDE.md](CLAUDE.md) is the project brief: what is being built, what is locked in, and why.
[docs/decisions/](docs/decisions/) records the reasoning behind each milestone.
