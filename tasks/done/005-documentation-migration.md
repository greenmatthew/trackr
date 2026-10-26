---
id: 5
title: Documentation migration
milestone: M4
record: docs/decisions/05-documentation.md
depends_on: []
---

## Goal

Split documentation by when it is read, publish the wiki from this repository, and make the parts
that can go stale fail a test instead.

## Done when

- [x] `CLAUDE.md`, `docs/` and `wiki/` as the three homes
- [x] `just docs::publish` copies `wiki/` to every wiki remote
- [x] `tests/Trackr.Docs.Tests` fails on an undocumented `TRACKR_*` variable or a documented recipe that does not exist, and `API-Reference.md` is generated
- [x] recorded in [05-documentation.md](../../docs/decisions/05-documentation.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

`CLAUDE.md` was not cut to the 22–25 KB the milestone named, and the record says why: "If the file
needs to be smaller later, the honest lever is moving §5 and §9 into `docs/` and accepting that they
are read per-milestone rather than every session." The task board is that lever, pulled for §9.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> 4. ~~**Documentation migration**~~ ✅ — [05-documentation.md](../../docs/decisions/05-documentation.md)
