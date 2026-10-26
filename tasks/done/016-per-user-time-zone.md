---
id: 16
title: Per-user time zone
milestone: M13
record: docs/decisions/16-time-zone.md
depends_on: []
---

## Goal

Make "today" the account's own day: a zone stored on the account, which the log range, the stats
views and goal progress all follow.

## Done when

- [x] `DayBoundary.ZoneFor` returns the account's zone, with no other call site touched
- [x] the phone's zone offered on the profile, never used unasked
- [x] the alpine runtime image installs `tzdata`, without which every named zone threw on the deployed server only
- [x] recorded in [16-time-zone.md](../../docs/decisions/16-time-zone.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

Built early, as milestone 13's entry asked. That entry — this half included — is quoted in task 017.
