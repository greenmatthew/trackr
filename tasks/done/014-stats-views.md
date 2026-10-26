---
id: 14
title: Stats views
milestone: M11
record: docs/decisions/14-stats.md
depends_on: []
---

## Goal

Home is today's totals and Trends is the week and the month with a bar per day, from one route, `GET
/api/stats`.

## Done when

- [x] days grouped in memory on the server, and a client may not name a day (`?days=N`)
- [x] summed from snapshots; empty days come back as days; a nutrient nobody reported stays absent
- [x] the average divides by days logged, and the screen says so
- [x] recorded in [14-stats.md](../../docs/decisions/14-stats.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

No physical-phone run: every mobile claim rests on the emulator. Task 026.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> 11. ~~**Stats views**~~ ✅ — [14-stats.md](../../docs/decisions/14-stats.md). Home is today's totals,
>     Trends is the week and the month with a bar per day. One route, `GET /api/stats`, because the
>     three views differ only in how many days were asked for.
>     **The day boundary belongs to the server, and this milestone had to enforce that twice.**
>     Grouping into local days happens in memory rather than with `date_trunc`, so there is only one
>     answer to "which day is this instant in" — and **a client may not name a day at all**: Trends
>     computed its range from the phone's clock, which put Home on the 19th and the chart on the
>     20th, both faithful to different clocks. `?days=N` removes the phone's clock from the question
>     rather than correcting for it, which matters because a correction would have to be redone when
>     §9.13 lands. Home's heading had the same bug and now comes from the same answer as its numbers.
>     Summed from snapshots and never through `FoodItemId`; empty days come back as days; a nutrient
>     nobody reported stays absent. The average divides by **days logged**, not by the length of the
>     range, and the screen says so. The chart is scaled rectangles rather than a package.
>     **Left open:** the zone is still UTC (§9.13), and nothing compares a number to a target yet.
