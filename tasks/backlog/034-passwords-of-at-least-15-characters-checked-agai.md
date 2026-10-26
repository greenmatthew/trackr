---
id: 34
title: Passwords of at least 15 characters, checked against common ones
milestone: M2
record: -
depends_on: [35]
---

## Goal

A password that is the only factor — which it is while 2FA is optional — is long enough by NIST's
measure and is not on a list of common passwords.

## Done when

- [ ] a custom `IPasswordValidator<TrackrUser>`: at least 15 characters after NFC normalisation, and
      not on a bundled common-password list
- [ ] the list's licence notice is shown where the licences are
- [ ] every hint and DTO says the same: `Program.cs:153`, the three `MinimumLength = 12` in
      Trackr.Shared, `RegisterPage.xaml`, `Register.razor`, `ResetPassword.razor`,
      `ChangePasswordSection.razor`
- [ ] existing passwords keep working until they are changed
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Source: dnd-near 184 (`~/programs/dnd/dnd-near/tasks/*/184-*`).

- Today the minimum is 12, and no decision record says why.
- Depends on 035: 15 is NIST's figure for a single-factor password. If 035 makes 2FA required, the
  case for 15 weakens, so the answer there decides the number here.
- SecLists, dnd-near's source for the list, is MIT — compatible, and its notice must be shown.
