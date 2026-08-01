# Accounts and 2FA

Trackr has no public sign-up at any point. Accounts are created once by claiming the server,
and after that only by invitation.

## The first account claims the server

On an empty database, registration is open. Create your account at `/register` and
**registration closes permanently** — the endpoint starts refusing anyone without an invite.

Do this first, before signing in on the phone: **the Android app has no sign-up screen.**
Trying to sign in on a server with no accounts simply fails, and that is the most common
reason a first login does not work.

## Invites

Any signed-in user can mint an invite under **Settings → Invites**. Each is single-use, has
an expiry, and is shown to you exactly once — only a hash is stored, so a lost invite cannot
be recovered, only revoked and replaced.

The redemption is transactional: if account creation fails, the invite is not consumed.

## Two-factor authentication

Opt-in per account, under **Settings**. It uses TOTP — the rolling six-digit codes from an
authenticator app such as Aegis, Google Authenticator or 1Password. There is no SMS option.

1. Scan the QR code with your authenticator app.
2. Type one working code to prove it is set up. **2FA does not switch on until you do** —
   this is what stops you locking yourself out with a mis-scanned code.
3. Save the ten recovery codes. They are shown once and only their hashes are stored.

Once enabled, both the website and the app ask for a code after the password.

### Recovery codes

Each works once, in the same box as an authenticator code. On the app, tick *Use a recovery
code instead*; on the web, use the recovery-code link on the 2FA prompt.

Settings shows how many remain. They do not regenerate automatically — if you run low,
disable and re-enable 2FA to get a fresh set.

### If you lose both the phone and the codes

There is no back door, by design. You would need database access to clear the account's 2FA
state directly.

## Lockout and rate limiting

Five failed attempts locks the account for 15 minutes. Wrong 2FA codes count towards the same
counter as wrong passwords, so an attacker who has the password cannot brute-force the second
factor either. A correct password resets the count.

Wrong *recovery* codes deliberately do not count — they are high-entropy enough that guessing
is not a realistic attack, and counting them would give an attacker an easy way to lock you
out.

Rate limits sit in front of all of it — see [Configuration](Configuration).

## Forgotten passwords

By default the reset link is written to the backend log rather than emailed:

```bash
docker compose logs backend | grep -A3 "was not sent"
```

Configure SMTP to send real mail instead — see [Configuration](Configuration).

Two deliberate behaviours worth knowing:

- **The endpoint always reports success**, whether or not the address exists, so it cannot be
  used to discover which addresses are registered.
- **Using a reset link does not sign you in.** With the default provider that link came out of
  a log file, so proving you can read it should not by itself hand over a live session. You
  set the password, then log in normally, including 2FA.

## What changing a password does to your other sessions

Changing your password or your 2FA settings rolls the account's security stamp, which
invalidates every other session — browser cookies and phone tokens alike — usually within
about five minutes. The session you made the change from survives.

That is the mechanism to rely on if you think a device is compromised: change the password,
and every other device is signed out.
