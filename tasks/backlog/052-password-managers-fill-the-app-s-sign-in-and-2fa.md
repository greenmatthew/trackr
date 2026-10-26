---
id: 52
title: Password managers fill the app's sign-in and 2FA code
milestone: M5
record: -
depends_on: []
---

## Goal

A password manager offers to fill the phone's sign-in, registration and two-factor fields, as it
already does on the website.

## Done when

- [ ] the native fields carry autofill hints: username, password, new password, one-time code
- [ ] checked on the emulator with a stand-in autofill service
- [ ] checked with the user's own password manager on their phone — the needs-user step, which can
      ride along with task 026
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: nautilus 093 and 269 (needs-user there).

- The website is done (`Login.razor:32-69`, `one-time-code` included). The MAUI `Entry`s in
  `LoginPage.xaml` and `RegisterPage.xaml` carry no hints, and MAUI exposes none: a handler mapping
  sets them on the native `EditText`.
- These are native fields, not a canvas, so nautilus's Flutter problem — fields that are "not stable
  DOM inputs" — does not apply; only the hints are missing.
- Pitfalls: the emulator has no working autofill service and needs a stand-in; password managers
  offer to save passwords the server refused (nautilus 096, 342, 352, 357).
