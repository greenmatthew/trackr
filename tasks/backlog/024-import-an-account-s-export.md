---
id: 24
title: Import an account's export
milestone: M14
record: -
depends_on: []
---

## Goal

An account's export can be read back in — onto a fresh server, or into a new account after the old
one was deleted. Filed coarse: the design questions below come first.

## Done when

- [ ] the questions under Notes answered, and the answers recorded
- [ ] split into session-sized tasks, and this one moved to `rejected/` with "split into NNN–MMM"

## Notes

Milestone 14's build-order entry (task 018): "Exporting an account's data landed in §9.13; an
*import* to match it has not."

[17-profile.md](../../docs/decisions/17-profile.md) declined it as "a much larger question — what
happens to ids, what happens to shared items — than a milestone about self-service should answer".
Those are the questions, plus one more: the export **lists** photographs rather than embedding them,
so the file alone cannot restore them.
