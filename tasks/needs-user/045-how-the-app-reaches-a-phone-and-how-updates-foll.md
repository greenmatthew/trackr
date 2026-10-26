---
id: 45
title: How the app reaches a phone, and how updates follow
milestone: M15
record: -
depends_on: []
---

## Goal

Decide how a household member who owns only a phone gets Trackr, and how a new version reaches every
phone after that.

## Done when

- [ ] the answer is under Notes, and the work it asks for is filed as tasks

## Waiting on you

Choose one, or say what else you have in mind:
1. **A download on Trackr's own website**, behind sign-in, plus an in-app "a newer version is out".
2. **The Gitea release page** (task 044), installed by hand.
3. **Obtainium** on each phone, watching that release page — updates without you.
4. **A self-hosted F-Droid repository** — the most work, the most like an app store.

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: otter 313 (needs-user there: Obtainium or a self-hosted F-Droid repo); nautilus 028
(version check, in-app update) and 060 (first install from the web app); dnd-near 178, 180.

- Today the APK is installed with adb only (`wiki/Building.md`), while the invite flow assumes
  someone who may own nothing but a phone (CLAUDE.md §3).
- Trackr.Shared's DTOs change in lockstep with the server while installed phones lag; a version
  check is what tells an old phone to update rather than fail oddly.
- Pitfalls: a plain download link cannot carry the bearer token — nautilus 060 fetches, checks an
  `X-Checksum-Sha256` header, then saves. Changing which app installed Trackr costs one reinstall per
  phone, which wipes the outbox. Play Protect adds a second tap to every sideload.
