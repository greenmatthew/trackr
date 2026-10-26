---
id: 43
title: One signing key and one version number for every APK
milestone: M15
record: -
depends_on: []
---

## Goal

Every APK that leaves this machine is signed with one release key, so any build installs over any
other, and carries a version that says which build is on the phone. **Before the household installs
anything:** Android refuses an update signed by another key, and the uninstall that follows wipes
the tokens and the offline outbox — unsent meals included.

## Done when

- [ ] a release keystore, never committed, its passwords from the environment, its certificate's
      SHA-256 published in the wiki
- [ ] `ApplicationDisplayVersion` and `ApplicationVersion` move with a `just` recipe, and a tag is
      refused on a commit that did not move them
- [ ] the server and the web app report the same version as the APK
- [ ] the keystore and its passwords are in the user's password manager — the needs-user step
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: otter D16, D17 and tasks 294, 295; nautilus 007 and its D3, D5, D30, D36; dnd-near 199.

- Today: version `0.1` / `1`, never moved (`Trackr.Mobile.csproj:35-36`); no tags; the APK is signed
  by this machine's debug key, and `wiki/Building.md` calls it "not for distribution".
- Pitfalls (otter):
  - The SDK's `_Sign` target does not re-sign after a key change; otter added a stamp to force it.
  - apksigner reads one password file for both passwords — use `env:`.
  - `AndroidCreatePackagePerAbi` is silently ignored on .NET 10; choose the ABI per build instead.
    Trackr's Debug APK carries arm64 and x86_64 and weighs 89 MB.
  - In CI the keystore is a secret passed as a BuildKit secret, never a layer.
