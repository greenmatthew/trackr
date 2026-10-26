---
id: 17
title: User profile
milestone: M13
record: docs/decisions/17-profile.md
depends_on: []
---

## Goal

Display name, unit preferences, body metrics, and the account self-service milestone 2 left out.

## Done when

- [x] units a display preference that changes nothing the API returns; imperial governs height and weight only
- [x] a Mifflin-St Jeor estimate on the server, declined when an input is missing, offered as a target and never applied
- [x] changing an address in two steps, an export, and deleting the account
- [x] recorded in [17-profile.md](../../docs/decisions/17-profile.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

No physical-phone run: every mobile claim rests on the emulator. Task 026.

"No import to match the export" and "weight is a value rather than a series" are tasks 024 and 022.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> 13. ~~**User profile**~~ ✅ — [17-profile.md](../../docs/decisions/17-profile.md), and the time-zone
>     half early in [16-time-zone.md](../../docs/decisions/16-time-zone.md). Display name, unit
>     preferences, body metrics, and the account self-service milestone 2 left out.
>     **Units are a display preference and change nothing the API returns** — every total, target and
>     snapshot stays kilocalories, the client converts on the way to the screen, and nothing rounds on
>     the way out so an untouched figure round-trips exactly. **Imperial governs height and weight
>     only**: nutrition labels are metric everywhere, so ounces of protein would be a number on no
>     package. One read and three writes rather than a patch, because *clearing* a measurement has to
>     stay expressible. The estimate is Mifflin-St Jeor on the server (milestone 11's reason: one
>     answer) and is **declined rather than guessed** whenever an input is missing; `BiologicalSex` is
>     documented as the coefficient the equation has two of. **Changing an address is two steps** —
>     the address is the login and the only route back in through a reset — and confirming needs the
>     session as well as the link. The export describes what is *stored* and **lists photographs
>     rather than embedding them**. Deleting takes the account's own data and leaves the server's;
>     invite foreign keys became `SetNull` so the record of who invited whom survives the person,
>     while an unredeemed invite is revoked on the way out.
>     **Left open:** no physical-phone run, no import to match the export, weight is a value rather
>     than a series (§9.14), and the estimate suggests energy only.
>     Original wording, and the two parts it called load-bearing rather than cosmetic:
>     - ~~**Time zone decides what "today" means.**~~ ✅ **Built early, as this entry asked** —
>       [16-time-zone.md](../../docs/decisions/16-time-zone.md). The claim held: `ZoneFor` stopped returning
>       UTC and the log range, the stats views and the goal progress all followed, with no other call
>       site touched. The zone lives on the account, never travels with a request, and the phone's own
>       zone is *offered* on the profile rather than used. **The trap `DayBoundary` flagged was real:**
>       the alpine runtime image ships no tz database, so every named zone threw — on the deployed
>       server only. It now installs `tzdata`; `InvariantGlobalization` was a red herring and stays on.
>       The rest of this milestone is untouched. Original wording:
>       **Time zone decides what "today" means.** The stats views total a *local* day while the
>       server stores UTC. Until a per-user zone exists, keep the day boundary in exactly one
>       helper (defaulting to UTC or a single configured server zone) so making it per-user later
>       is one change rather than a rewrite of every aggregate. The phone knows its own zone,
>       which is a tempting shortcut — but the *server* aggregates, so the zone must be stored per
>       user rather than sent per request.
>     - ~~**Body metrics feed the goal maths.**~~ ✅ They do: *Start from that* on the targets
>       screen fills the energy row from the estimate, and a hand-typed target still works with or
>       without any measurements. Original wording:
>       **Body metrics feed the goal maths.** Goals can suggest a calorie target from BMR/TDEE
>       instead of asking the user to invent a number. Goals must still accept a hand-typed
>       target, so this is never a hard dependency.
