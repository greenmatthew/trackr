---
id: 33
title: The server's owner can turn off an account's 2FA
milestone: M2
record: -
depends_on: []
---

## Goal

Someone who has lost both their phone and their recovery codes can be let back in without anyone
editing the database by hand: a command in the backend container, run from Portainer's console.

## Done when

- [ ] a command in the backend container turns an account's 2FA off, clears its recovery codes,
      rolls its security stamp, and logs the action without the address
- [ ] `wiki/Accounts-and-2FA.md` and `Troubleshooting.md` say how, replacing "there is no back door"
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: dnd-near 189 (in progress there), nautilus 236.

- Today the wiki says "no back door… you would need database access" (`Accounts-and-2FA.md:158-161`).
- There is no admin role, so the console is the trust boundary: whoever can exec into the container
  already owns the database. A button for it belongs to task 023's split, not here.
