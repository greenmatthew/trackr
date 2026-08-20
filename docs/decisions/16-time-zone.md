# Milestone 13 (part) — The per-user time zone

§9.13 is a late milestone with several parts. This is the one it calls load-bearing rather than
cosmetic, and asks to be respected earlier than the rest:

> **Time zone decides what "today" means.** … keep the day boundary in exactly one helper …
> so making it per-user later is one change rather than a rewrite of every aggregate.

This is that change, and the useful thing to record is that **the claim held**. `ZoneFor` stopped
returning UTC unconditionally, and the log range, the stats views and the goal progress all
followed with no other call site touched. A test logs a meal at 23:30 UTC and watches it move from
the 19th to the 20th when the account moves to Tokyo, through both the stats route and the goals
route; on the emulator, moving to `America/Chicago` moved Home from *Thursday 20 August* back to
*Wednesday 19 August*, with the day's total following.

Nothing else from §9.13 is built. Display name, unit preferences, body metrics, account deletion
and export remain open.

## The zone lives on the account

Not sent with each request. §9.13 says why and it is worth restating: **the server aggregates**. A
total has to agree with itself between the chat, the stats views, the goals and whatever asks next,
and one client being in an airport must not move somebody's midnight.

The phone knowing its own zone is the tempting shortcut. It is offered as an *answer* on the
profile screen — a button reading "Use this phone: America/Chicago" — because the alternative is
asking somebody to type an IANA id from memory. It is never used as one.

Nullable rather than defaulted to the string `"UTC"`, so "never set" stays distinguishable from
"deliberately UTC".

## The trap the code had already flagged, and finding it

`DayBoundary` carried this warning from milestone 6:

> One thing for milestone 13 to check rather than discover: this project sets
> `InvariantGlobalization`, and resolving a named zone needs the tz database to be present in the
> runtime image.

Checking found it. **The alpine runtime image ships no tz database**, so
`TimeZoneInfo.FindSystemTimeZoneById` throws for every named zone — on the deployed server only,
while working on every developer machine, which is the worst shape a bug can have. The runtime
stage now installs `tzdata`, about 2 MB.

`InvariantGlobalization` turns out to be a red herring and is recorded as one: it governs ICU and
culture data, not the zone database, despite both sounding like locale support. It stays on.

## Validated when set, forgiving when read

A zone is checked against **this server's** database at the moment it is chosen, because that is
the only database that matters — a zone the phone knows and the server does not would silently
become UTC, and the day would run on the wrong clock with nobody told. It is also the moment a
person is present to be told.

Reading is deliberately forgiving: an unrecognised zone falls back to UTC rather than throwing. The
value came from a client and the tz database moves underneath a deployment, so a zone renamed or
dropped between releases must not turn every request for a total into a 500.

## Two smaller things

- **A failed save does not redraw as though it worked.** A day quietly running on the wrong clock
  is precisely what storing the zone server-side prevents.
- **`AuthSession.NoteAccountChanged`**, alongside the existing `NoteAvatarChanged` and for the same
  reason: sign-in state has not moved, and raising `Changed` would swap the shell and — since the
  chat became a singleton — empty the transcript, for a time zone edit.
- **A XAML footnote worth keeping:** a single-quoted attribute cuts its value at an apostrophe even
  when escaped as `&apos;`, which reached the device as a button reading "Use this phone". Found by
  looking at it.

## Verified

Tier 1: 378 API tests, 173 view-model tests, 104 documentation tests.

Tier 2 (emulator, API 36): the phone's zone applied from the profile, landing in Postgres as
`America/Chicago`, and Home's heading and totals moving to the previous day as a result. The
`tzdata` fix is verified in the running container rather than only on the host, which is the whole
point of it.

## Left open

- **The rest of §9.13** — display name, units, body metrics, changing the account email, export,
  deletion.
- **No zone picker.** The phone's zone and UTC are the two offers. A list of six hundred IANA ids
  is a worse screen than a button that already knows the answer, and somebody who wants a third
  option can be given one when somebody wants a third option.
- **Existing accounts stay on UTC** until they choose, which is the same behaviour they had.
