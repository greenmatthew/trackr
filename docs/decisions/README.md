# Decision records

One file per milestone, recording the choices made while building it and — more
importantly — *why*, so a later change can tell a deliberate decision from an accident.

`CLAUDE.md` holds the brief: what we are building, the locked-in stack, the principles and
the build order. These files hold the archaeology. When the two disagree, `CLAUDE.md` is the
intent and these are the history.

Both are Claude-facing, which is why they live under `docs/` rather than the wiki. Reference and how-to
material — installation, configuration, troubleshooting, the dev environment — belongs in
the wiki at `wiki/` instead, where the self-hoster can read it too. See CLAUDE.md §0.

| Milestone | Record |
| --- | --- |
| 1 — Scaffold | [01-scaffold.md](01-scaffold.md) |
| 2 — Auth | [02-auth.md](02-auth.md) |
| — Android-first pivot | [03-android-pivot.md](03-android-pivot.md) |
| — Branding and theming | [04-branding.md](04-branding.md) |
| 4 — Documentation migration | [05-documentation.md](05-documentation.md) |
| 5 — Mobile UX & architecture | [06-mobile-ux.md](06-mobile-ux.md) |
| 6 — Data layer | [07-data-layer.md](07-data-layer.md) |
| 7 — Barcode and Open Food Facts | [08-barcode-off.md](08-barcode-off.md) |

A decision that is later reversed stays in its original file, with a note pointing at the
record that superseded it. Deleting it would hide the reasoning that made the reversal
necessary.
