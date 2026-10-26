---
id: 31
title: Sign out everywhere else, then see and sign out each device
milestone: M2
record: -
depends_on: []
---

## Goal

A lost phone can be signed out without changing the password, and an account can see where it is
signed in. Filed coarse: split it when it is picked up — "sign out everywhere else" first, the list
of devices second.

## Done when

- [ ] split into session-sized tasks, and this one moved to `rejected/` with "split into NNN–MMM"

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: dnd-near 042; nautilus 057, 231, and 271 (browsers remembered for 2FA)
(`~/programs/dnd/dnd-near/tasks/*/042-*`, `~/programs/nautilus/tasks/*/057-*`).

- Today changing the password is the only way out (`wiki/Accounts-and-2FA.md:193-200`).
- **The wiki overstates how fast that is for a phone.** It says "about five minutes"
  (`Accounts-and-2FA.md:197`), but the five-minute stamp check (`Program.cs:183-184`) is the cookie's.
  A phone's bearer token lives an hour (`Program.cs:136`) and is not stamp-checked; only its refresh
  dies with the stamp (`TokenAuthTests.cs:121`).
- "Everywhere else" is cheap in Identity: roll the security stamp, re-sign the current session.
- A device list needs state the server does not keep: cookie and bearer tickets are stateless. An
  `ITicketStore` for cookies, and a per-device row named in the refresh ticket and checked on refresh.
