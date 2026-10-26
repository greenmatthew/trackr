---
id: 55
title: Open Trackr to the public internet
milestone: M15
record: -
depends_on: [27, 28, 30, 37]
---

## Goal

The user wants Trackr reachable away from home without a VPN (2026-10-26). This task is the list of
what has to land first, and its last step — opening the reverse proxy — is the user's.

## Done when

- [ ] every task in `depends_on` is done
- [ ] CLAUDE.md §8 point 5 and `wiki/Self-Hosting.md` § "The strongest protection is not exposing it"
      describe the public posture (task 027 owns the words; check they landed)
- [ ] moved to `needs-user/` with a `## Waiting on you` for opening the proxy, and the address
      checked from outside once it is open
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Proposed gate, not settled — edit `depends_on` freely:
- **027** the bot check — the user's own call.
- **028** an emptied server is anyone's to claim.
- **030** today one script can lock the whole household out of signing in.
- **037** a public page with no frame or content policy.

Strongly advised before, not gating: 029 (dumps hold secrets in clear), 038 (no backups), 031 (no way
to sign out a lost phone), 043 (settle the signing key before anyone installs a build).
