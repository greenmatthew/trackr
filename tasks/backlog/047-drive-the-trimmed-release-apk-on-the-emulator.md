---
id: 47
title: Drive the trimmed Release APK on the emulator
milestone: M5
record: -
depends_on: [46]
---

## Goal

Every on-device claim so far rests on a Debug build. Build Release — trimmed — install it, walk the
app, and fix what trimming broke.

## Done when

- [ ] trim analysis switched on and its warnings read, not suppressed
- [ ] the Release APK walked on the emulator: sign-in, chat with a photo, confirm, Home, Trends,
      profile
- [ ] whatever trimming broke is fixed (a JSON source-generation context if reflection-based
      serialisation fails)
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: otter 038; nudge `docs/decisions/01-foundation.md:86-90`, where Release trimmed something
and broke only there.

- The Android SDK sets `SuppressTrimAnalysisWarnings=true`, so a Release build looks clean while
  reporting nothing. Set `EnableTrimAnalyzer`.
- **Decide first:** the Release network config forbids cleartext everywhere (CLAUDE.md §3), so a
  Release APK cannot reach the dev stack at `10.0.2.2`. Point it at an HTTPS server, or give the dev
  stack one — and note that a self-signed one runs into task 053.
