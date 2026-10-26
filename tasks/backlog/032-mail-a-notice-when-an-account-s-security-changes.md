---
id: 32
title: Mail a notice when an account's security changes
milestone: M2
record: -
depends_on: []
---

## Goal

The owner of an account hears about a new sign-in, a password change, two-factor turned on or off,
and new recovery codes — by mail to the address on the account, and to the old address for an
address change.

## Done when

- [ ] each event sends one plain notice, only after the change has committed
- [ ] a failed send is logged without the address and reaches the account through the API, never
      only the log
- [ ] a notice held by a rate limit is batched, never dropped
- [ ] works with the log sender as well as SMTP
- [ ] a real mail seen arriving in the user's own mailbox — the needs-user step
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: dnd-near 194; nautilus 086, 268 ("a serious notice is never dropped") and 320, the
needs-user check of a real mailbox (`~/programs/nautilus/tasks/*/086-*`).

- Today mail carries only reset links and address-change confirmations
  (`Identity/SmtpEmailSender.cs`, `AuthEndpoints.cs:518`, `AccountEndpoints.cs:426`), and a failed send
  is only logged.
