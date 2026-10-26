---
id: 42
title: Run `just test` on every push
milestone: M15
record: -
depends_on: []
---

## Goal

Gitea Actions runs the gate on every push to master, so a red master is seen before Portainer
deploys it.

## Done when

- [ ] `.gitea/workflows/` runs `just test`, with the Docker socket the API tests' Testcontainers need
- [ ] no `uses:` steps that need Node in the job container
- [ ] the first real run seen green — the needs-user step, since an agent cannot see Gitea's run pages
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: otter's and nudge's `check.yml`; nautilus 008 and 276 (and rejected 103, 272, 273);
dnd-near 195, 197 and its D19, D23.

- Today trackr has no `.gitea/` at all. Portainer builds whatever master holds.
- Pitfalls:
  - Gitea's API and Actions pages sit behind a Cloudflare challenge, so an agent cannot read runs
    (nautilus 008).
  - The runner's cores are old and slow; about ten nautilus tasks chased timeouts there, and otter
    found tests that pass on 24 cores and fail on few (`taskset -c 0` finds them).
  - A Portainer prune deleted a host-only CI image and broke every job (nautilus 272, 274).
  - Two workflows from one push released before the gate finished (nautilus 276).
- Worth doing in the same breath: `just docs::publish` publishes the **working tree**
  (`just/docs.just`, `_sync`), so uncommitted pages can go live. Nautilus (D37) and nudge publish the
  wiki from a workflow on the pushed commit instead.
