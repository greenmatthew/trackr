# Accounts and 2FA

Trackr has no public sign-up at any point. Accounts are created once by claiming the server,
and after that only by invitation.

## The first account claims the server

On an empty database, registration is open. Create your account at `/register` and
**registration closes permanently** — the endpoint starts refusing anyone without an invite.

Note that the root URL redirects to `/login`, not `/register`, even on a brand-new server:
every page behind it needs an account, and the redirect cannot know you have none. Follow the
*No account yet?* link on the login page, or go to `/register` directly.

The Android app can do this too — *No account yet? Create one*, on its sign-in screen. Either
way the effect is the same and registration closes afterwards.

## Invites

Any signed-in user can mint an invite under **Settings → Invites**. Each is single-use, has
an expiry, and is shown to you exactly once — only a hash is stored, so a lost invite cannot
be recovered, only revoked and replaced.

The redemption is transactional: if account creation fails, the invite is not consumed.

### Redeeming one on a phone

An invite can be redeemed entirely from the Android app, which is the point — someone you
invite may not have a browser pointed at your server, and may not own a desktop at all. On the
sign-in screen, tap *No account yet? Create one*, then paste either the invite code or the
**whole invite link**; the app picks the code out of a link so nobody has to select it by hand
on a phone keyboard.

Registration, the profile picture and your own preferences are what the app handles. Changing a
password, changing your email address, enrolling in 2FA, minting invites, exporting your data and
deleting your account all stay on the website.

## Your profile picture

Set it from the phone: tap the circle at the top right, then *Change picture*. It is stored on
the server against your account, so it follows you to another device, and *Remove* puts your
initials back.

The phone shrinks the image to 512 pixels on its longest edge and re-encodes it as JPEG before
uploading. That is not only about size — re-encoding drops the EXIF block, so a photo taken on
a phone does not carry the coordinates it was taken at up to the server.

Android's photo picker grants Trackr access to the one image you choose, so there is no
prompt asking for your whole photo library, and it never has it.

If the app reports that your account was created but signing in afterwards failed, the account
really does exist and the invite really has been spent. **Sign in with it — do not register
again**, which would consume a second invite and then fail on a duplicate email.

## Your name, your units and your time zone

Set from the phone, on the same screen as the picture.

- **Your name** is what greetings and the initials in the avatar circle use. It is not a second
  login — the account is still keyed by its email address.
- **Show energy as** switches every calorie figure in the app between kilocalories and kilojoules.
  Nothing on the server changes: totals, targets and the snapshot stored with each logged meal
  stay in kilocalories, and only the display converts. So switching back and forth costs nothing
  and moves no stored number.
- **Height and weight in** chooses metric or imperial for the body measurements below. It
  deliberately does **not** apply to food: nutrition labels are metric everywhere, so grams stay
  grams.
- **Your days are measured in** is the time zone every total is grouped by. The phone offers its
  own zone as an answer rather than using it, because the server is what adds up a day and every
  device has to agree with it.

## Body measurements

Optional, all of it, on **Profile → Body measurements**. Sex, date of birth, height, weight and
how active you are, kept for exactly one purpose: estimating what a day costs you, so a calorie
target can be suggested instead of invented.

Leaving anything blank stores nothing, and *Remove my measurements* takes back whatever is there.
An estimate only appears once sex, date of birth, height and weight are all present — a figure
built on measurements nobody entered would be a guess wearing the authority of a calculation.

The estimate is Mifflin-St Jeor, the equation clinical guidance uses when a metabolic cart is not
available, times an activity multiplier. It is rough: two people matching on every field can
differ by a couple of hundred kilocalories a day. On **Daily targets**, *Start from that* fills
the energy target with it and leaves it there to be changed — a hand-typed target always works,
with or without any of this.

## Changing your email address

**Settings → Email address**, on the website. Your address is also your login, so this happens in
two steps:

1. Enter the new address and your current password. A confirmation link is sent **to the new
   address**.
2. Follow it while signed in and confirm. Only then does the address change.

Nothing moves until step 2, which is the point — a typo would otherwise lock you out of your own
server, because the address you can no longer read is where a password reset would be sent. Being
signed in is required as well as holding the link, for the same reason a reset link does not sign
you in: by default that link is in a log file.

With the default email provider it is found the same way a reset link is:

```bash
docker compose logs backend | grep -A3 "was not sent"
```

## Exporting your data

**Settings → Your data → Download my data** gives you a JSON file holding everything the account
has: the profile, the targets, every logged meal with its full nutrient snapshot, and the catalog
items only you can see.

Meal photographs are **listed rather than embedded**, each with a link. A few hundred photos as
base64 text would produce a file nothing can open, and each is already downloadable on its own.
Fetch them before deleting the account if you want to keep them.

## Deleting your account

**Settings → Your data → Delete this account**. It asks for your password and for the word
`DELETE` typed by hand, and it cannot be undone.

What goes: the account, its log and every nutrient snapshot in it, its targets, its profile
picture, its meal photographs, and the catalog items only that account could see.

What stays:

- **Items shared with the household.** They belong to the server, and other people's recipes are
  made of them.
- **The record of who invited whom.** Invites survive, saying they were created by an account that
  no longer exists. Any invite that account minted and nobody has used yet is revoked on the way
  out, so a live invitation never outlives its issuer.

Deleting the last account on a server leaves an empty database, which means registration reopens
and the next person to visit `/register` claims it. That is the same state a fresh deployment is
in.

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
