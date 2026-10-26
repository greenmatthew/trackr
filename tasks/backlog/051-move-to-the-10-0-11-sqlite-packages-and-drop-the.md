---
id: 51
title: Move to the 10.0.11 Sqlite packages and drop the SQLitePCLRaw pin
milestone: M5
record: -
depends_on: []
---

## Goal

Retire a package pin that nudge found is no longer needed.

## Done when

- [ ] `Microsoft.Data.Sqlite` on 10.0.11 or later, and the `SQLitePCLRaw.bundle_e_sqlite3` pin
      removed from `Directory.Packages.props`
- [ ] the APK still carries a 16 KB-aligned `libe_sqlite3.so`
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Source: nudge `docs/decisions/01-foundation.md:92-112`, which says it is "worth carrying back".

- Today `Directory.Packages.props:97-106` pins `SQLitePCLRaw.bundle_e_sqlite3` 2.1.12 under
  `Microsoft.Data.Sqlite` 10.0.10. The 10.0.11 nuspec depends on 2.1.12 itself (checked in
  `~/.nuget`).
- Never go below 10.0.11 again without putting the pin back.
