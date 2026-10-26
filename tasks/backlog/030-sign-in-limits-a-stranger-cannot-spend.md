---
id: 30
title: Sign-in limits a stranger cannot spend
milestone: M2
record: -
depends_on: []
---

## Goal

Nobody on the internet can keep a household member from signing in. Today two things let them:
Identity's hard lockout (5 wrong passwords, then 15 minutes) can be set off by anyone who knows an
address, and every rate limit is keyed by IP, which behind the reverse proxy is one budget shared by
everybody.

## Done when

- [ ] failed attempts are limited per account name in a sliding window, never by a hard lock a
      stranger can trigger
- [ ] a place is taken before the password hash runs and given back on success, so a burst cannot
      slip through between the check and the count
- [ ] the IP-keyed limits either see the real client through the proxy or stop claiming to be per
      caller — the record says which
- [ ] a session already signed in is untouched by attempts against its account
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Source: dnd-near 043 and its D18 "no hard lock" (`~/programs/dnd/dnd-near/tasks/*/043-*`).

- Evidence: lockout at `Program.cs:160-162`; `RateLimitPolicies.cs:180` keys every policy on
  `Connection.RemoteIpAddress`; `Program.cs:22-27` clears the known proxies. `wiki/Troubleshooting.md:101`
  already admits it: "behind a reverse proxy that can resolve to the proxy — meaning everyone shares
  one budget. For a household that is usually fine". It is not fine once the login is public.
- Seeing the real client means trusting the outer proxy's address and setting `ForwardLimit`;
  trusting `X-Forwarded-For` from anyone instead lets a client choose its own bucket.
- Pitfall: dnd-near accepts that a window can still be held shut (one wrong guess about every 90 s)
  and limits the damage — sessions survive, passkey sign-in does not count. Task 027 (the bot check)
  makes each guess cost CPU; it does not remove the need for this.
