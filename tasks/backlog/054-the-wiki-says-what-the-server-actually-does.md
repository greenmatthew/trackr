---
id: 54
title: The wiki says what the server actually does
milestone: M4
record: -
depends_on: []
---

## Goal

Correct the wiki pages the 2026-10-26 survey found describing something the code does not do.

## Done when

- [ ] `wiki/Building.md:39` names the APK `gg.matthewgreen.trackr-Signed.apk`
- [ ] `wiki/Accounts-and-2FA.md:155`: recovery codes regenerate from settings
      (`POST /api/account/2fa/recovery-codes`), not only by turning 2FA off and on
- [ ] `wiki/Accounts-and-2FA.md:197`: a phone is signed out within the hour, not "about five minutes"
      — unless task 031 lands first and makes five minutes true
- [ ] `wiki/Backup-and-Restore.md`: a dump is plain SQL, readable without `POSTGRES_PASSWORD`, and
      carries the key ring in clear, so it is guarded like a secret — unless task 029 lands first
- [ ] `wiki/Accounts-and-2FA.md:146` and the comment at `AccountEndpoints.cs:734` stop saying only
      hashes are stored — unless task 029 has made it true
- [ ] `just docs::test` is green

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

- `wiki/Testing-the-Android-App.md:183`, `adb uninstall dev.trackr.app`, is right as it stands: it
  removes the build from before the package was renamed.
- The certificate advice is task 053, which needs the user's answer first.
- The two security claims are the urgent ones: a reader currently believes their dumps and their
  recovery codes are safer than they are.
