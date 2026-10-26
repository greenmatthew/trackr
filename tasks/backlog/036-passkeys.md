---
id: 36
title: Passkeys
milestone: M2
record: -
depends_on: []
---

## Goal

Sign in with a passkey: on the website first, then on Android. Filed coarse: split it when it is
picked up.

## Done when

- [ ] split into session-sized tasks, and this one moved to `rejected/` with "split into NNN–MMM"

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: dnd-near 191-193; nautilus 069, 300-303, 344 (`~/programs/nautilus/tasks/*/069-*`).
Lowest value of the sign-in tasks: 030, 029 and 031 matter more.

- .NET 10 Identity has passkeys built in (`IdentityPasskeyOptions`,
  `SignInManager.MakePasskeyCreationOptionsAsync`, `PasskeySignInAsync`), needing
  `Stores.SchemaVersion = IdentitySchemaVersions.Version3` and a migration — per the survey's reading
  of Microsoft Learn; check the installed package before relying on it. Nautilus, on Dart, had
  to write its own CBOR, COSE and WebAuthn checks.
- Set `ServerDomain` explicitly: forwarded headers are trusted from any proxy (`Program.cs:22-27`), so
  the relying-party id would otherwise follow the `Host` header.
- `PasskeySignInAsync` issues the cookie, so the phone's token route needs its own assertion flow.
- Identity treats a passkey as a primary factor with no 2FA hook; nautilus D100 let a user-verified
  passkey stand for both factors.
- Android needs Credential Manager and `/.well-known/assetlinks.json` with the **release** certificate's
  hash — so the Android half waits on task 043 — and can only be tried for real at the public HTTPS
  address.
