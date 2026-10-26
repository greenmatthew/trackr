---
id: 28
title: The first account needs a code from the server's log
milestone: M2
record: -
depends_on: []
---

## Goal

An empty server — a fresh install, or one whose last account was deleted — cannot be claimed by
whoever reaches `/register` first. Until an account exists, each start prints a one-time setup code
to the log, and registering without an invite needs it.

## Done when

- [ ] while no account exists, each start logs a new code; once one exists, none is made
- [ ] `POST /api/auth/register` without an invite refuses anyone without the code, compared in
      constant time; the code is never stored, and never on a health or status route
- [ ] `GET /api/auth/registration-status` stops telling a stranger the server is unclaimed, or the
      record says why it still may
- [ ] the web Register page and the phone's `RegisterPage` both take the code
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources — copy the design, and read their Notes for the pitfalls: nautilus 061 (its D27,
`~/programs/nautilus/tasks/*/061-*`), dnd-near 035 (its D14, `~/programs/dnd/dnd-near/tasks/*/035-*`).

- Today `/api/auth/register` is open to anyone while the users table is empty
  (`AuthEndpoints.cs:126-130`), and its comment accepts the race "on the first boot of a private
  server" — which this one will no longer be. Deleting the last account reopens it
  (`wiki/Accounts-and-2FA.md:134`, [17-profile.md](../../docs/decisions/17-profile.md)).
- Amends [02-auth.md](../../docs/decisions/02-auth.md)'s bootstrap rule.
- Pitfalls (nautilus): an optional pinned code that is unusable turns setup **off**, never falls back
  to open. A new `TRACKR_*` variable fails `just docs::test` until `wiki/Configuration.md` lists it —
  the mechanism working, as CLAUDE.md §0 says.
