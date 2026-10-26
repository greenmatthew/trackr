---
id: 37
title: Security headers on the web app
milestone: M15
record: -
depends_on: []
---

## Goal

nginx sends a content security policy, `frame-ancestors`, `nosniff` and a referrer policy on every
response, now that the website is public.

## Done when

- [ ] every location carries the headers, not just the server block
- [ ] the policy allows only what Blazor WebAssembly needs, and a test checks each directive
- [ ] proved in a browser: no CSP violation on sign-in, 2FA enrolment with its QR code, or settings
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Source: nautilus 079 (`~/programs/nautilus/tasks/*/079-*`), which proved its policy in the browser
by listing resource entries and CSP violation events.

- Today `src/Trackr.Web/nginx.conf` sets only `Cache-Control` and `Content-Type`. The web app loads
  nothing from another origin (the survey found none), which makes a strict policy cheap.
- Pitfalls: nginx **drops** server-level `add_header` in any location that sets its own, and every
  location here sets `Cache-Control`. Blazor WebAssembly needs `'wasm-unsafe-eval'`; check
  `index.html` for an inline script or importmap that needs a hash, and how the QR code is drawn.
