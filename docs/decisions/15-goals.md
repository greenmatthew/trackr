# Milestone 12 — Goals

§9 puts goals after the stats views rather than instead of them, and the ordering is the point: a
target is only meaningful once the number it is a target for exists and is trusted. §9.12 also
calls it LATE, so this is deliberately the small version — targets, and progress against them.

## Keyed by nutrient, like everything else

A `Goal` is `(user, nutrient key, target, direction)`. Not a fixed set of columns, for the reason
§7 gives about nutrients generally: a target for selenium should be a row, not a migration.

This is the second thing [07-data-layer.md](07-data-layer.md)'s "core four as columns *and*
catalog rows" bought. The core keys are in the same vocabulary as the rest, so a calorie target and
a selenium target are the same kind of object and neither needs a special case.

## A target carries a direction

"At least 100 g of protein" is met by passing it. "At most 2 000 kcal" is broken by the same. A
single figure with no direction would have to guess which, and would guess wrong for about half of
them.

That decision propagates further than it looks:

- **The fraction on the wire is uncapped.** Past a floor is the point; past a ceiling is the
  problem. A client that could not see beyond 100% could not draw the difference. The phone clamps
  it for the bar and carries "over" as a flag.
- **On screen, a ceiling passed is a warning and a floor reached is done.** Drawing both as
  complete would congratulate somebody for going over their calories.
- **A floor not yet reached is neither.** It is a day in progress, not a failure — which is what
  keeps a progress bar from being an accusation at breakfast, and is the nearest thing this
  codebase has to acting on CLAUDE.md's closing note about anxiety.

## One target per nutrient, and a wholesale replace

Two targets for one nutrient is a contradiction rather than a refinement — "at least 100 g" and "at
most 80 g" cannot both be satisfied — so a unique index enforces it and the endpoint refuses it
with a message rather than letting the index produce a 500.

`PUT /api/goals` replaces the whole set, like the catalog's nutrient map and the log's items. A
merge leaves "stop tracking this one" inexpressible without a second route whose only job is
deletion.

## Progress is measured on the server

`GET /api/goals/progress` totals a local day and compares. Measured server-side for the reason
milestone 11 learned the hard way: the day boundary follows the account's time zone, so a client
naming its own day reports against the wrong one for most of every evening.

**A nutrient nothing reported counts as nothing eaten, and that is not "missing is not zero" being
broken.** A target asks how much was eaten. A nutrient nobody recorded contributed nothing to the
day whether or not the food contained it, and saying so is the honest answer — the dishonest
version would be reporting a target as met on the strength of food nobody described.

## The editor is a form, and is allowed to be one

§10 forbids forms as the way food is *logged*. This is a settings screen, and §9.12 requires a
hand-typed target to work whether or not anything ever suggests one — which matters because the
BMR/TDEE suggestion it mentions belongs to §9.13's body metrics and is explicitly not a hard
dependency.

Reached by a route off Home rather than made a fourth tab, like the profile: three tabs are the
shape of the app, and a target is set occasionally and then left alone.

Amounts are bound as **text** and parsed, the way milestone 9's confirmation card does it, so an
unreadable one blocks the save rather than quietly becoming zero. Adding a second row picks a
nutrient not already spoken for, because the server refuses duplicates and a rejected save is a
confusing way to learn that.

## No targets is a perfectly good state

Home draws nothing when there are none, and nothing anywhere asks for one. An app that demanded
targets before it would show a number would be the version of this that drives anxiety rather than
helping.

## Verified

Tier 1: 371 API tests, 171 view-model tests, 104 documentation tests.

Tier 2 (emulator, API 36), the whole loop: a 2 000 kcal ceiling typed on the targets screen, landing
in Postgres as `energy_kcal | 2000.0000 | AtMost`, and Home then reading
*Energy — 43 380 of 2 000 kcal — over*.

## Left open

- **Nothing suggests a target.** §9.12 mentions BMR/TDEE, which needs §9.13's body metrics, and
  §9.12 is explicit that a hand-typed target must work regardless. The hand-typed path is the whole
  of this milestone and the suggestion can layer on it.
- **Targets are per day only.** A weekly target is a different feature with different arithmetic;
  §9.12 asks for progress against a day, which is also the only window the stats views total by
  default.
- **Trends shows no targets.** The bar chart still scales against its own tallest day rather than
  against a ceiling, which is the obvious next thing and was left rather than guessed at.
- **No physical-phone run.**
