---
id: 2
title: Auth
milestone: M2
record: docs/decisions/02-auth.md
depends_on: []
---

## Goal

ASP.NET Core Identity behind an HttpOnly cookie: bootstrap-then-invite registration, login, TOTP 2FA
with QR enrolment and recovery codes, account lockout, rate limiting, password change and
log-delivered reset, and a fully authed web app.

## Done when

- [x] registration open for the first account only, then by invite
- [x] TOTP 2FA: a QR code, one confirming code before it is switched on, recovery codes shown once
- [x] lockout, and rate limiting on the auth endpoints
- [x] recorded in [02-auth.md](../../docs/decisions/02-auth.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> 2. ~~**Auth**~~ ✅ — [02-auth.md](../../docs/decisions/02-auth.md)
