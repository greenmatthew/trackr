---
id: 15
title: Goals
milestone: M12
record: docs/decisions/15-goals.md
depends_on: []
---

## Goal

Daily targets keyed by nutrient, each with a direction, and the day measured against them on the
server.

## Done when

- [x] one target per nutrient; `PUT /api/goals` replaces them wholesale
- [x] a floor not yet reached is a day in progress rather than a failure; the fraction is uncapped on the wire
- [x] the editor is a settings form on a route off Home, not a fourth tab
- [x] recorded in [15-goals.md](../../docs/decisions/15-goals.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

No physical-phone run: every mobile claim rests on the emulator. Task 026.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> 12. ~~**Goals**~~ ✅ — [15-goals.md](../../docs/decisions/15-goals.md). Targets keyed by nutrient, so a
>     target for selenium is a row rather than a migration — the second thing "core four as columns
>     *and* catalog rows" bought. **A target carries a direction**, because "at least 100 g protein"
>     is met by passing it and "at most 2 000 kcal" is broken by the same; the fraction is uncapped on
>     the wire so a client can draw that difference, and **a floor not yet reached is a day in
>     progress rather than a failure**. One target per nutrient (two would be a contradiction);
>     `PUT /api/goals` replaces wholesale. Progress is measured server-side, for milestone 11's
>     reason. A nutrient nothing reported counts as nothing eaten — a target asks what was eaten, and
>     reporting one met on the strength of food nobody described would be the dishonest answer. The
>     editor is a settings form, which §10 permits, on a route off Home rather than a fourth tab.
>     **No targets is a perfectly good state** and draws nothing.
>     **Left open at the time, and two of the three closed since** by §9.13: a target can now be
>     suggested from body measurements (offered, never applied — a hand-typed one always works), and
>     Trends draws the energy target. Targets are still per-day only.
