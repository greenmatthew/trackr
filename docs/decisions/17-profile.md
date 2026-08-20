# Milestone 13 — The rest of the profile

[16-time-zone.md](16-time-zone.md) took the half of §9.13 that entry itself calls load-bearing and
asks to be built early. This is everything else it lists: a display name, unit preferences, body
metrics, and the account self-service milestone 2 left out — changing the email address, exporting
the account's data, deleting the account.

It also closes the one thing [15-goals.md](15-goals.md) named as the obvious next step, because
both ends of it landed here: **Trends draws its targets**, and **a target can be suggested** from
the measurements this milestone adds.

## Units are a display preference, and nothing else

The central decision, and everything else about units follows from it.

**The API stores and reports kilocalories and grams, always.** `EnergyUnit` travels with the
account and the client converts on the way to the screen and back on the way in. No total, no
target and no snapshot on a log item ever changes unit.

Converting on the server was the obvious alternative and it is wrong for a reason this codebase has
already paid for once. A snapshot on a `LogItem` exists precisely so that history does not move
when a catalog row is corrected ([07-data-layer.md](07-data-layer.md)); a stored figure that
changed meaning when a *setting* changed would move it by a different route. Somebody switching to
kilojoules out of curiosity would silently redraw every chart they had ever looked at.

So `DisplayUnits` on the phone is the whole of the conversion, and it has two rules worth keeping:

- **Nothing is rounded on the way out.** That is what makes a round trip exact: 2 000 kcal shown as
  8 368 kJ and saved untouched is 2 000 kcal again. Rounding for display would move numbers nobody
  edited, which is the failure CLAUDE.md §2 puts first, arriving by a route nobody would look at.
- **The way back in rounds to four decimal places**, and only because that is what the server's own
  columns keep. Dividing by 4.184 can otherwise hand back 1999.9999999, and rounding to what would
  be stored anyway cannot introduce a difference the server would not have made itself.

The conversion constant is 4.184 exactly, which is the definition of the thermochemical calorie
rather than a measurement. EU labelling rounds it to 4.2 in places; using the defined value is what
keeps the round trip exact.

### Imperial governs bodies, not food

`MeasurementSystem` applies to height and weight and stops there. Choosing imperial does **not**
turn grams of protein into ounces, and the profile screen says so.

Nutrition labels are metric the world over. Converting them would put a number on screen that
appears on no package the user can pick up, which is a strange thing for an app whose whole cascade
exists to read those packages accurately. Height and weight are the measurements people genuinely
hold in feet and pounds, and they are the ones this governs.

The conversion still happens on the client: the wire is always centimetres and kilograms. Two units
for one column is how a weight becomes ambiguous.

## One read, three writes

`GET /api/account/profile` returns the whole thing. Writing is split — `PUT /api/account/profile`
for the name and the units, `PUT /api/account/body` for the measurements, and the existing
`PUT /api/account/timezone` untouched.

Not one patch endpoint, and the reason is that **clearing has to stay expressible**. A request
whose fields are all optional cannot tell "leave my weight alone" from "I no longer want a weight
stored", and body measurements are exactly the data somebody may want gone. Each group is replaced
wholesale, so sending nulls is how they are removed — the same shape as `PUT /api/goals` and the
catalog's nutrient map, for the same reason.

`MeResponse` also grew the display name and both unit choices, because every screen with an avatar
or a calorie figure on it needs them and the phone renders those from its cached account before the
server has answered anything. That made a fourth place to construct a `MeResponse`, so the three
that already existed became one `ToMeResponse` — a field missing from one construction is a client
that renders correctly everywhere except immediately after registering, which is the kind of bug
nobody reproduces.

## The estimate is declined rather than guessed

Body measurements exist for exactly one purpose: so goals can suggest a calorie target instead of
asking somebody to invent a number. §9.12 is explicit that a hand-typed target must work
regardless, which is what makes every decision here cheap.

- **Mifflin-St Jeor**, rather than Harris-Benedict, which it superseded, or Katch-McArdle, which
  needs a body-fat percentage nobody has to hand. It is what clinical guidance uses without a
  metabolic cart, and it is still an estimate: two people matching on every input can differ by a
  couple of hundred kilocalories a day. The screen says so.
- **On the server**, for the reason goal progress is: one answer. A phone and a browser computing
  their own would eventually disagree about somebody's maintenance calories, and the disagreement
  would be silent.
- **Null whenever a measurement it uses is missing**, which is most accounts and is not an error.
  An estimate built on a height nobody entered would be an invented number wearing the authority of
  a calculation.
- **Whole kilocalories.** The equation's own error bar is in the hundreds; decimal places would be
  precision it does not have.

`BiologicalSex` is named and documented as what the equation actually needs, which is not the same
as how anybody identifies: every published BMR formula has two constants and no third, so this is a
coefficient rather than a profile field. `Unspecified` is a first-class answer that costs an
estimate and nothing else, and nothing in Trackr reads it.

An age below fifteen yields no estimate rather than refusing the profile — the equation is not the
right one for children, and declining to guess is a stranger-free answer than telling somebody they
may not record their height. A birth date in the *future* is refused outright, being a typo with no
reading that makes sense.

### Where the columns live, and when that stops being right

Height, weight, sex, birth date and activity are columns on the account row rather than a table of
their own. One current weight is one value.

**A weight *history* is a series**, and the day somebody wants a chart of it (§9.14), it becomes
its own entity rather than a wider user row. That is written on the entity so the next person does
not have to infer it.

## Trends draws its targets

15-goals.md left this as "the obvious next thing", and it is small.

The chart asks for the **targets** rather than progress against them — progress is measured on one
day and this is a chart of thirty — and uses only the energy one, because the bars are kilocalories
and a target for sodium has nothing to scale them against.

The ceiling is **the taller of the target and the tallest day**, so both stay on screen. Scaling to
the target alone hides by how much a day went over; scaling to the tallest day alone puts the
target line at the very top of a week that never came near it, which reads as "target reached" — the
opposite of what happened.

Bars keep Home's vocabulary: a ceiling passed is a warning, a floor reached is done, and a day
nobody logged is neither, since nothing eaten sits under every ceiling. The caption says which way
the target reads, because the same picture is a good week or a bad one depending on it.

A null from the goals route is "could not ask", not "there are none", and draws the chart exactly as
it drew before targets existed.

On the goals screen, *Start from that* fills the energy row with the estimate and leaves it there as
an ordinary editable row. **Offered, never applied.**

## Changing the address is two steps, and that is the whole design

The email address is the login *and* the only route back in through a password reset. Applying a
typo immediately would lock somebody out of their own server with no way back, so:

1. `POST /api/account/email` re-checks the password and sends a confirmation link **to the new
   address**. Nothing changes.
2. `POST /api/account/email/confirm` applies it.

Two smaller decisions inside that:

- **Confirming needs the session as well as the link.** With the default email provider the link
  came out of a log file, and possession of it alone must not be able to move somebody else's
  account — the same reasoning that stops a password reset handing over a session
  ([02-auth.md](02-auth.md)). The web page confirms on a *button* rather than on load, so a mail
  client that prefetches links does not do it for you.
- **The user name moves with the address.** This application logs in by email and Identity keeps
  the two as separate columns; changing one alone leaves an account that reports the new address and
  only accepts the old one at the login screen. There is a test that says so.

An address another account already uses is refused in plain words rather than hidden behind a
generic 202. Unlike the forgot-password flow, the caller is signed in and can already see the invite
list, so refusing to say would hide nothing and would instead send a link that cannot work.

## The export describes what is stored

`GET /api/account/export` is a JSON download with its own shape rather than a reuse of the API's
response contracts. Those exist to be rendered by the app and change as screens do; an export is
read by a person or a script years later and should describe what is *stored*.

**Meal photographs are listed, not embedded** — id, type, size, which entry they belong to, and a
URL each. A few hundred meals of base64 would produce a file nothing can open, for bytes that are
already downloadable one at a time. The manifest says where they are rather than pretending they are
not there.

Only what belongs to the account. Global catalog items are the server's, which is the same rule the
delete follows.

## Deleting takes the account's data and leaves the server's

`DELETE /api/account` wants the password *and* the word `DELETE` typed, so it cannot be reached by a
stray tap. The cascades already in the model do most of the work: the avatar, the goals, the log and
its snapshots, the meal photographs and the personal catalog items all go with the row.

A personal item can only ever appear in that account's own recipes — the catalog shows global plus
own, and a global recipe may hold only global ingredients ([09-composites.md](09-composites.md)) —
so nothing else can be holding one when it goes.

**Invites needed a decision rather than a cascade.** They were `Restrict` on both foreign keys,
deliberately, so that deleting an account could not silently erase the record of who invited whom.
That made an account undeletable the moment it minted one, which would have made §9.13's delete a
button that fails for exactly the households the invite system exists for. So:

- Both foreign keys become `SetNull`. The row survives saying it was created by an account that no
  longer exists, which is what actually happened. The original intent is kept; only the person is
  gone.
- **An invite nobody redeemed is revoked on the way out**, because it is a live credential and one
  of those must not outlive its issuer.

Nothing refuses to delete the last account on the server. It reopens registration the way an empty
database does, and the alternative is telling somebody they may not leave their own machine.

## Two smaller things

**`ICurrentAccount`** is the read-only sliver of `AuthSession` that things which only *render* the
account depend on. A view model wanting the account's units should not thereby acquire the ability
to sign somebody out, and a test should not have to stand up a token store and a cache to say "this
account reads kilojoules".

**Initials now come from the display name where there is one**, which is the point of setting one on
a server a household shares — and a space joined the separators, or "Sam Wilson" would give "S".
The local SQLite cache learned the profile columns at the same time, including the time zone it
never kept, which had an offline profile screen reporting UTC for an account that had chosen
otherwise.

## The migration trap, found by running it

`AddUserProfile` adds four text-converted enum columns to `AspNetUsers`, which already has rows. The
scaffolder writes `defaultValue: ""` for each, and an empty string parses as no enum member at all —
so **every existing account would have failed to load**, on a server that has been in use and only
there. The defaults are hand-edited to the member names, with a note on the migration class saying
why.

This is the same shape as the trap [16-time-zone.md](16-time-zone.md) records: a bug that works
perfectly on every developer machine. Both were caught by CLAUDE.md §11's rule about actually
running the thing.

## Verified

Tier 1: 404 API tests, 206 view-model tests, 104 documentation tests.

Tier 2, the compose stack: both migrations applied to a database that already had accounts in it,
backfilling the enum columns as `Kilocalories | Metric | Unspecified | Sedentary` rather than as
empty strings. Then the whole self-service loop against the running API — profile written and read
back, a body of 165 cm / 60 kg reporting 1 324 and 2 052 kcal, an export whose log carried the meal
that had just been posted, an address change that moved the login (old address 401, new address
200), and a delete that left zero users, zero log entries, an invite with a null creator and a
revocation stamp, and registration back in `Bootstrap`.

Tier 2, the emulator: name and kilojoules saved from the profile and landing in Postgres as
`Sam Wilson | Kilojoules | Metric`; the avatar's initials becoming *SW*; the body screen estimating
*5397.36 kJ* at rest and *6476.832 kJ* for a typical day; *Start from that* filling the target and
storing `energy_kcal | 1548.0000 | AtMost` — the exact round trip, from 6 476.832 kJ on screen back
to the kilocalories the server keeps; Home reading *2510.4 / kJ today* with grams untouched beside
it; and Trends drawing the target line with the day that went over it in the warning colour.

## Left open

- **No physical-phone run.** Every mobile claim here is tier 2.
- **Nothing on the phone changes an address or deletes an account.** Those stay on the website
  (CLAUDE.md §10), and only registration and preferences are the app's business.
- **The export is a download, not a restore.** Nothing reads one back in, and an import is a much
  larger question — what happens to ids, what happens to shared items — than a milestone about
  self-service should answer.
- **Weight is a value, not a series.** §9.14.
- **The estimate suggests energy only.** Protein per kilogram and the rest are nutrition advice
  rather than arithmetic, and this project has been careful not to give any.
