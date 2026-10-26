---
id: 38
title: Nightly backups, and a restore drill under Portainer
milestone: M15
record: -
depends_on: []
---

## Goal

The stack dumps the database every night, prunes old dumps only after a good one is written, and a
restore has been rehearsed on the real deployment.

## Done when

- [ ] a backup service in `docker/docker-compose.yml`, built from the db image, writing compressed
      dumps to a named volume
- [ ] an empty or failed dump never prunes an older one
- [ ] retention decided for trackr's size, and recorded
- [ ] `wiki/Backup-and-Restore.md` rewritten for a Portainer git stack
- [ ] a restore drilled on the homelab — the needs-user step
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: dnd-near 048 and its D21; nautilus 021, 270, 278 (`~/programs/nautilus/tasks/*/278-*`).

- Today the page is hand-run `docker compose -f docker/docker-compose.yml` commands reading
  `docker/.env`; a Portainer git stack has neither on the host. Nothing is scheduled or rehearsed.
- **Retention is a fresh decision:** dnd-near keeps 14 dumps of kilobytes; trackr's carry meal photos
  as `bytea`, about 5.5 GB a year per account that photographs meals (`Backup-and-Restore.md`).
- Pitfalls: Portainer CE cannot bind-mount relative paths (dnd-near builds `FROM` the db image through
  a YAML anchor instead); "Stop this stack" removes the containers; nautilus's drill printed success
  on an empty dump until it was chained with `test -s`; nautilus's cron was never rehearsed.
- Until task 029 lands, every dump holds the key ring and the 2FA secrets in clear: guard the volume.
