---
id: 39
title: Copy the database before a startup migration
milestone: M15
record: -
depends_on: []
---

## Goal

An upgrade that changes the schema can be undone: before applying a pending migration the server
copies the database, and it refuses to start if the copy cannot be made.

## Done when

- [ ] a copy is made only when a migration is pending, before `MigrateAsync`
- [ ] a failed copy stops the start, before anything migrates
- [ ] pruning keeps the newest copy per schema version, so a crash-looping migration cannot push out
      the copy from before it
- [ ] `wiki/Self-Hosting.md`'s upgrade steps say how to go back
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Source: nautilus 295, built there and awaiting the user (`~/programs/nautilus/tasks/*/295-*`).

- Today `Data/DatabaseStartupExtensions.cs:17-45` migrates at every start with no copy, and Portainer
  rebuilds from the branch.
- The aspnet-alpine image has no `pg_dump`. Options: `CREATE DATABASE … TEMPLATE trackr` from the
  maintenance database (it needs no other open connection to `trackr`), or an init container on the
  postgres image.
- Copies are full size, photos included.
- Nautilus relied on an older server refusing a newer database. Check whether EF Core refuses unknown
  applied migrations before relying on the same.
