# Decision records

The choices made while building Trackr and — more importantly — *why*, so a later change can
tell a deliberate decision from an accident. One file per milestone through record 18; since the
task board, one per task that settles something worth not re-deciding.

`CLAUDE.md` holds the brief: what we are building, the locked-in stack and the principles.
[`tasks/`](../../tasks/README.md) holds the work. These files hold the archaeology. When they
disagree, `CLAUDE.md` is the intent and these are the history.

**Each record has a task on the board with the same number** — `07-data-layer.md` is task 007 —
carrying that milestone's entry from the build order `CLAUDE.md` §9 held until 2026-10-26. So a
"§9.N" in these records means milestone N's entry, and `just milestone N` finds it.

`CLAUDE.md` and these records are Claude-facing, which is why the records live under `docs/`
rather than the wiki. Reference and how-to material — installation, configuration,
troubleshooting, the dev environment — belongs in the wiki at `wiki/` instead, where the
self-hoster can read it too. See CLAUDE.md §0.

| Milestone | Record | Task |
| --- | --- | --- |
| 1 — Scaffold | [01-scaffold.md](01-scaffold.md) | 001 |
| 2 — Auth | [02-auth.md](02-auth.md) | 002 |
| — Android-first pivot | [03-android-pivot.md](03-android-pivot.md) | 003 |
| — Branding and theming | [04-branding.md](04-branding.md) | 004 |
| 4 — Documentation migration | [05-documentation.md](05-documentation.md) | 005 |
| 5 — Mobile UX & architecture | [06-mobile-ux.md](06-mobile-ux.md) | 006 |
| 6 — Data layer | [07-data-layer.md](07-data-layer.md) | 007 |
| 7 — Barcode and Open Food Facts | [08-barcode-off.md](08-barcode-off.md) | 008 |
| 7a — Composite / recipe items | [09-composites.md](09-composites.md) | 009 |
| 8 — Ollama | [10-ollama.md](10-ollama.md) | 010 |
| 9 — Chat UI, cascade and confirm | [11-chat.md](11-chat.md) | 011 |
| 10 — Catalog growth | [12-catalog-growth.md](12-catalog-growth.md) | 012 |
| 10a — Ingredients | [13-ingredients.md](13-ingredients.md) | 013 |
| 11 — Stats views | [14-stats.md](14-stats.md) | 014 |
| 12 — Goals | [15-goals.md](15-goals.md) | 015 |
| 13 (part) — Per-user time zone | [16-time-zone.md](16-time-zone.md) | 016 |
| 13 — The rest of the profile | [17-profile.md](17-profile.md) | 017 |
| 14 (part) — Logging with no connection | [18-offline.md](18-offline.md) | 018 |

From record 19 on, the Task column names the task that wrote the record, which no longer shares
its number.

A decision that is later reversed stays in its original file, with a note pointing at the
record that superseded it. Deleting it would hide the reasoning that made the reversal
necessary.
