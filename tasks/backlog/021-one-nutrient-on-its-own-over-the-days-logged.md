---
id: 21
title: One nutrient on its own, over the days logged
milestone: M14
record: -
depends_on: []
---

## Goal

A per-nutrient detail view: pick one nutrient and see it over the day, the week and the month,
against its target when it has one.

## Done when

- [ ] reachable from where the nutrient is already shown, in one tap
- [ ] a day nobody reported the nutrient on reads as unknown, not as zero
- [ ] `just test` is green
- [ ] seen working on the emulator (`Verified: emulator`)
- [ ] the decision record and the wiki still match the code

## Notes

From the open list in milestone 14's build-order entry (task 018): "per-nutrient detail views";
[14-stats.md](../../docs/decisions/14-stats.md) left it open too.

- `GET /api/stats` already returns a nutrient map per day (`DayTotals.Nutrients` in
  `src/Trackr.Shared/Nutrition/StatsContracts.cs`). Check whether that is enough before adding a
  route.
- **Missing is not zero** (CLAUDE.md §7). The stats views already leave an unreported nutrient
  absent, and an average divides by days logged; a per-nutrient view has to keep both true.
