---
id: 41
title: Harden the containers
milestone: M15
record: -
depends_on: []
---

## Goal

Each container runs with the least it needs: not as root where avoidable, a read-only filesystem,
capabilities dropped, no new privileges.

## Done when

- [ ] the compose file sets `read_only`, `cap_drop` and `no-new-privileges` wherever each works
- [ ] nginx runs unprivileged, or with the tmpfs mounts it needs
- [ ] the stack comes up and passes its health check with all of it on (`Verified: stack`)
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: nautilus 019, its D21 and D87, and 281.

- Today only the backend drops root (`USER $APP_UID`); nothing else is set, and nginx runs as root.
- Pitfall from the user's own TrueNAS (nautilus 281): containers run in a user namespace and
  `CAP_CHOWN` fails on inodes it cannot map. The postgres entrypoint chowns as root — leave the db
  container's capabilities alone, and check Ollama's volume the same way.
