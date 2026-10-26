---
id: 22
title: A weight history to chart
milestone: M14
record: -
depends_on: []
---

## Goal

Weight kept as a series rather than one current value, so it can be charted over time.

## Done when

- [ ] a weighing is a dated row of its own, and the profile's current weight is the latest of them
- [ ] the series is drawn on the phone, in the account's chosen units
- [ ] `just test` is green
- [ ] seen working on the emulator (`Verified: emulator`)
- [ ] the decision record and the wiki still match the code

## Notes

From the open list in milestone 14's build-order entry (task 018): "a weight history to chart".

- [17-profile.md](../../docs/decisions/17-profile.md) settled the shape in advance: "A weight
  *history* is a series, and the day somebody wants a chart of it, it becomes its own entity rather
  than a wider user row." `TrackrUser.cs` says the same on the column.
- Stored metric, always; imperial is how it is typed and drawn (CLAUDE.md §7).
- The energy estimate reads the current weight, so it must keep reading the latest one.
