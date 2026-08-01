# Trackr

A self-hosted personal nutrition tracker. You log meals by typing what you ate — and
optionally attaching a photo — into a chat on your phone; the server works out the nutrition
and asks you to confirm before saving anything.

Everything runs on your own hardware. Food photos are never sent to a third party: barcodes
are decoded locally, and images go only to an AI model running in a container on your server.
The only thing that ever leaves the machine is a barcode number, looked up against
[Open Food Facts](https://world.openfoodfacts.org/).

> **Status: early.** Accounts, 2FA and the Android app's sign-in work today. Meal logging
> does not exist yet. See the build order in the repository's `CLAUDE.md`.

## Running your own instance

| Page | What it covers |
| --- | --- |
| [Self-Hosting](Self-Hosting) | Deploying the stack with Docker Compose or Portainer, behind your reverse proxy |
| [Configuration](Configuration) | Every environment variable |
| [Accounts and 2FA](Accounts-and-2FA) | The first account, invites, authenticator apps, recovery codes |
| [Backup and Restore](Backup-and-Restore) | What to back up, and what breaks if you miss part of it |
| [Troubleshooting](Troubleshooting) | Symptoms and their usual causes |

## Working on Trackr

| Page | What it covers |
| --- | --- |
| [Development Environment](Development-Environment) | Toolchain, the `just` recipes, the dev stack |
| [Building](Building) | Backend, web app, and the Android APK |
| [Testing the Android App](Testing-the-Android-App) | Emulator and physical device |
| [Nutrient Reference](Nutrient-Reference) | The tracked nutrient set, keys and units |

---

*This wiki is generated from `wiki/` in the [main repository](https://github.com/greenmatthew/trackr).
Edits made here in the web UI are overwritten on the next publish — change the files in the
repository instead.*

*Copyright © 2026 Matthew Green, licensed under the
[Elastic License 2.0](https://github.com/greenmatthew/trackr/blob/master/LICENSE). Running,
modifying and self-hosting Trackr is free for anyone, including businesses. Selling accounts
on an instance you host for other people is not permitted.*
