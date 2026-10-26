---
id: 18
title: Logging with no connection
milestone: M14
record: docs/decisions/18-offline.md
depends_on: []
---

## Goal

A confirmed meal is never lost to a network: it is queued on the phone and sent later, and the
re-log list is cached so logging still works from a tunnel.

## Done when

- [x] only a confirmed meal is queued, and `GET /api/log/recent` is cached and says when it is stale
- [x] `ClientRequestId` makes a retry safe: a repeat answers 200 with the original entry
- [x] the client stamps `LoggedUtc`, an instant, on every save
- [x] refused and undelivered are different answers (`SaveLogStatus`); discarding is the only way a meal is lost
- [x] recorded in [18-offline.md](../../docs/decisions/18-offline.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

No physical-phone run: every mobile claim rests on the emulator. Task 026.

The rest of milestone 14's entry — its "still open" list — became tasks 019–024.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> 14. **Polish** — a grab-bag with no fixed order. Still open: edit/delete entries, richer charts,
>     per-nutrient detail views, a weight history to chart, an admin page on the web app. (Exporting
>     an account's data landed in §9.13; an *import* to match it has not.)
>     - ~~**Offline / queued logging**~~ ✅ — [18-offline.md](../../docs/decisions/18-offline.md).
>       **Only a *confirmed* meal is queued**, and that falls out of rules the project already had
>       rather than out of caution: the cascade lives on the server (§10), so with no network there
>       are no numbers to be had, and confirm-before-save (§2) forbids storing numbers nobody has
>       seen. What makes the queue worth having is the other half — **`GET /api/log/recent` is cached
>       on the phone**, so something eaten before can still be logged from a tunnel, and the stale
>       list says in words that it is stale.
>       **A retry is safe because the server de-duplicates it.** `SaveLogEntryRequest` carries a
>       `ClientRequestId` and `POST /api/log` answers a repeat with the original entry and a 200 —
>       because the failure that matters is not the request that never arrived but the one whose
>       *reply* was lost, which from the phone looks identical. The unique index is what makes that
>       true rather than the look-up, which is two statements and can lose a race.
>       **The client stamps `LoggedUtc` on every save**, so a breakfast confirmed in a tunnel is not
>       stored as an evening meal — an instant, never a date, so §9.13's day boundary stays the
>       server's. A refusal and a non-delivery became different answers (`SaveLogStatus`), a rejected
>       row is kept and shown rather than retried or dropped, and **discarding is the only way a
>       confirmed meal is ever lost**. Home carries the banner, because Home holds the total the
>       queue is missing from.
>       **The trap, found by running it:** Android reports *no network* in airplane mode while the
>       emulator's host server is still reachable, so connectivity is a **trigger and never a
>       verdict** — a save is always attempted.
>       **Left open:** no physical-phone run; a background flush does not redraw a Home already on
>       screen; nothing but the save is queued, so a photo taken offline cannot be analysed later.
