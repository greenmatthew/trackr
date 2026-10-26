---
id: 3
title: Mobile foundation: the Android pivot
milestone: M3
record: docs/decisions/03-android-pivot.md
depends_on: []
---

## Goal

The thin end-to-end slice, deliberately before the backend milestones because the Android toolchain
and token auth were the two unknowns in the pivot: a MAUI Android app signing in with an Identity
bearer token, beside the web's unchanged cookie.

## Done when

- [x] the Android app signs in with a bearer token and keeps the refresh token in `SecureStorage`
- [x] the cookie path is unchanged, and no CORS was added
- [x] recorded in [03-android-pivot.md](../../docs/decisions/03-android-pivot.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> 3. ~~**Mobile foundation**~~ ✅ — the thin end-to-end slice, deliberately *before* the backend
>    milestones because the Android toolchain and token auth were the two unknowns in the pivot.
>    [03-android-pivot.md](../../docs/decisions/03-android-pivot.md)
