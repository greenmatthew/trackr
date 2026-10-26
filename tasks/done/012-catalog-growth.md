---
id: 12
title: Catalog growth
milestone: M10
record: docs/decisions/12-catalog-growth.md
depends_on: []
---

## Goal

File confirmed barcode items into the catalog, and let somebody log again what they have eaten
before.

## Done when

- [x] a confirmed item with a barcode is filed as personal; a hit on an existing row links and writes nothing
- [x] `GET /api/log/recent` behind the chat's `+` button
- [x] a barcode checked against its `varchar(32)` column, and the portion check run on the assembled item
- [x] recorded in [12-catalog-growth.md](../../docs/decisions/12-catalog-growth.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

No physical-phone run: every mobile claim rests on the emulator. Task 026.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> 10. ~~**Catalog growth**~~ ✅ — [12-catalog-growth.md](../../docs/decisions/12-catalog-growth.md). Both
>     halves, and both are smaller than the wording above suggests, on purpose.
>     **A barcode is the only key**: an item without one is logged exactly as before and files
>     nothing, because matching a model's read of "chicken breast" on its name is wrong whether it
>     hits (two different foods merged) or misses (a near-duplicate row every meal). **Everything
>     filed is personal** — a global item cannot be deleted, so automatic promotion is the one
>     irreversible thing the catalog does, and §7's no-duplicates rule is honoured instead by a hit
>     on a shared row inserting nothing. **A hit links and writes nothing**: tapping save consents to
>     logging a meal, not to editing the household's catalog.
>     **Re-logging reads the log rather than the catalog** — `GET /api/log/recent`, behind the chat's
>     `+` button. The catalog holds packaged products; what people eat again is mostly home cooking,
>     which never carries a barcode. A pre-model text match was designed and rejected: it would be
>     badged *from Open Food Facts* while skipping the stage that would have caught the error.
>     **Two bugs fixed on the way**, both found by running it rather than by reading it: a barcode
>     was never checked against its `varchar(32)` column, and a quantity nobody could have eaten was
>     still unflagged on a *matched* product, because the reader checks the model's figures and a
>     full match keeps only the model's count. That check now runs on the assembled item.
>     **Left open:** no lookup cache off the new rows (deliberate — it needs a staleness rule first),
>     and `PUT /api/log/{id}` files nothing.
