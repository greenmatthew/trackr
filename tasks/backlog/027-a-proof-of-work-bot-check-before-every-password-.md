---
id: 27
title: A proof-of-work bot check before every password and new account
milestone: M2
record: -
depends_on: []
---

## Goal

Every request that tries a password or a two-factor or recovery code, or makes an account, first
answers a puzzle the server hands out. It is a self-hosted proof-of-work check, ALTCHA's idea: a
script pays real CPU for every guess and every account, and no sign-in visit goes to a third party.
Nobody sees a puzzle: the web app and the phone app solve it while the person types.

The design is already built twice. Copy it rather than inventing a new one:
- ~/programs/nautilus/docs/specs/bot-check.md (nautilus D74)
- ~/programs/dnd/dnd-near/docs/specs/bot-check.md (dnd-near D20, tasks 218 and 219)

This reverses CLAUDE.md §8's note that dropped reCAPTCHA. That note was about Google's service on
a login kept to a VPN or LAN, and it ends "If the login is ever exposed publicly, revisit this";
this check calls no one. The user's reason (2026-10-26): **Trackr will be open to the public
internet through the reverse proxy, so it can be used away from home without a VPN.**

## Done when

- [ ] The puzzle and its DTOs are in Trackr.Shared, which stays dependency-free. The server picks a
      random salt and a random number up to `maxNumber`, and hands out SHA-256(salt + number),
      HMAC-signed under a key made at each start and kept only in memory, good for 5 minutes.
      Trackr.Web, Trackr.Mobile and every test client share one solver.
- [ ] `GET /api/auth/bot-check` is open to anyone, sends `Cache-Control: no-store` and logs nothing.
- [ ] The server's check refuses in a fixed order, each refusal in plain words: not an answer, not
      from this start, expired, wrong number, used already. A right answer works once. Past a cap
      on kept answers it returns 503, logged once. A check from before a restart is refused.
- [ ] Every route that takes a password or a code, or makes an account, requires it. Start from
      /api/auth/login, /login/2fa, /login/recovery-code, /token, /register, /forgot-password and
      /reset-password, plus /api/account/password, /2fa/disable, /2fa/recovery-codes and
      DELETE /api/account. Read `src/Trackr.Api/Endpoints/AuthEndpoints.cs` and
      `AccountEndpoints.cs` for the real list and say why each route is in or out.
- [ ] The check runs before Identity looks at the password, so a request without a right answer
      never runs the hash and never counts toward lockout. Record where it sits relative to the
      rate limiter.
- [ ] Trackr.Web answers without freezing the page. Blazor WebAssembly has one thread, so measure
      the C# solver against a small JS Web Worker over crypto.subtle and keep the faster one that
      does not block. The sign-in button shows it is busy until the answer is ready. A stale or
      refused check is fetched and answered again once, by itself. A page older than the server
      is told to reload.
- [ ] Trackr.Mobile answers off the UI thread, from the view models in Trackr.Mobile.Core so the
      flow is covered by view-model tests (CLAUDE.md §10).
- [ ] `maxNumber` is measured, not guessed: about a second on an old phone. nautilus started at
      400,000; dnd-near measured that at a third of a second in Chromium with the CPU slowed six
      times, and raised it to 1,000,000. A timing on the user's own phone waits on the user.
- [ ] Tests cover: the hash against System.Security.Cryptography over many numbers; every refusal
      and their order; an answer spent once; expiry on a test clock; the cap; the route over HTTP;
      every test client answering; and that a request without an answer never reaches the lockout
      counter.
- [ ] A decision record says why, and why not reCAPTCHA, Turnstile, hCaptcha or a separate shared
      service. CLAUDE.md §8 is updated: the reCAPTCHA note, and point 5, which still recommends a
      login kept off the public internet. wiki/Accounts-and-2FA.md says what the check is, in a
      person's words, and wiki/Self-Hosting.md § "The strongest protection is not exposing it" says
      what guards a login that is exposed. The docs tests are green.
- [ ] It is our own code over System.Security.Cryptography: no package unless one earns it under
      CLAUDE.md's dependency and licence rules.

## Notes

- What it does not do: it cannot tell a person from a bot, and a script with a CPU to spare pays
  only milliseconds a try. One account stays guarded by lockout, rate limiting and 2FA.
- Related, not this task: Identity's lockout (5 failures, then 15 minutes — trackr's settings in
  `Program.cs` match) lets anyone who knows a username keep that person locked out on purpose.
  dnd-near decided against a hard lock for that reason (its D18). Once the login is public this is
  a real exposure rather than a theoretical one, so it deserves its own task — now task 030.
- Routes the list above does not name, checked at filing — decide each:
  `POST /api/account/email` checks the password (`CheckPasswordAsync`), so it looks in;
  `POST /api/account/2fa/enable` verifies a TOTP code; `POST /api/account/email/confirm` takes an
  emailed token and `POST /api/auth/token/refresh` a refresh token, neither of which a person types.
- Existing rate limiting: `RateLimitPolicies.Login` and `.Sensitive` (`Security/RateLimitPolicies.cs`)
  on every route above, with `UseRateLimiter()` in `Program.cs`.
- The phone timing can ride along with task 026, the physical-phone run, rather than waiting on a
  separate ask.
- Drafted in another session from the nautilus and dnd-near work, filed here at the user's request.
