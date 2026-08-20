# Milestone 11 — Stats views

CLAUDE.md §1 pairs the chat with an always-visible picture of the day and calls it core rather
than polish: logging a meal and immediately seeing the day move is the loop the app exists for.
This is that half — Home became today's totals, Trends became the week and the month.

## One route, because the three views differ only in length

`GET /api/stats` answers for a run of local days. Today, this week and this month are the same
question asked for one day, seven or thirty, which is the reasoning that already gave
`GET /api/log` one `from`/`to` pair instead of three routes.

## The day boundary belongs to the server, and that had to be enforced twice

§9.13 says the stats views total a *local* day while the server stores UTC, and that the zone must
be stored per user rather than sent per request — because the server aggregates. `DayBoundary` has
been the single place that answers "which day is this instant in" since milestone 6.

Two things follow, and the second was learned the hard way.

**Grouping happens in memory, not in SQL.** A `date_trunc` in the query would be a second answer
to the same question, free to disagree the moment the zone becomes per-user, and free to disagree
*silently*. The window is bounded to a year, so the cost is a few thousand rows.

**A client must not name a day at all.** Home already obeyed this: it sends no dates and lets the
server decide what "today" means. Trends did not — it computed "seven days back from today" from
the phone's clock. On the emulator that produced Home reporting *Wednesday 19 August* beside a
chart running to the *20th*, both faithful, to different clocks. The phone and the account
disagree about the date for most of every evening for anybody west of UTC.

The fix is `?days=N` — the last N local days ending on the account's today — rather than teaching
the phone to correct for the offset. Correcting would have to be done again the moment §9.13 lands;
removing the phone's clock from the question does not.

The same bug had a second half worth recording separately: Home's *heading* still came from the
phone while its numbers came from the server, so it reported today's total under yesterday's name.
The date had been documented as decoration, and it was — right up until the numbers beside it came
from a different clock. It now comes out of the same answer.

## Summed from snapshots, never from the catalog

Every figure comes from the nutrient snapshot on a `LogItem`. Nothing joins through
`LogItem.FoodItemId` for a number — the rule milestone 6 set, and the reason those snapshots exist.
There is a test that edits a catalog item and asserts the total does not move.

**A nutrient nobody reported stays absent rather than becoming zero**, the way it does everywhere
else here. Seeding the map with every known key would turn a day of undescribed food into a
confident column of noughts.

## Every calendar day comes back, including the empty ones

A week with two days of nothing is a different picture from a week with two bars missing, and only
the client drawing it can tell those apart if the server sends both the same way. The chart draws
no bar at all for an empty day — a blank day and a small day are different facts — but the day
keeps its place, so the days either side do not close up over it.

## The average is per day logged, and the screen says so

The total divided by the days that have something on them, not by the length of the range. Three
days logged out of seven divided by seven reports a number nobody ate and makes a partly-filled
week look like a fast.

The screen carries "averaged over 3 days logged of 7" underneath, because the figure without that
sentence quietly implies a fuller week than there was. Verified on the device: switching from seven
days to thirty moved the headline from 43 380 to 21 915 as a second logged day came into range,
which is the arithmetic being visible rather than hidden.

## The chart is rectangles, not a package

Two dozen scaled `BoxView`s. A charting dependency would be a licence to check (§10) and a bundle
to carry, for something the layout already does, and §1 asks for "basic" in as many words. The view
model reports a **fraction** and a converter in the MAUI project turns it into a height, because
how tall a chart is belongs to the page — the same line `NutrientRow` draws.

Scaled against the tallest day in the range rather than against a target, because targets are
milestone 12 and a chart with no ceiling has to pick one.

## Smaller things

- Both tabs refetch on **every** appearance. Meals are confirmed on the tab next door, so a total
  loaded once is stale by exactly the moment somebody comes to look at what they just logged.
- An unreachable server is never drawn as an empty day. Those two states look identical if you let
  them and only one is the user's doing.
- `NutrientRows` moved out of `ConfirmableItem`: the card and both stats screens want the same
  thing, and the second copy would have been the one that drifted.
- Asking for both a window and a range is refused rather than resolved, since any resolution would
  be a guess about which the caller meant.

## Verified

Tier 1: 352 API tests, 161 view-model tests, 104 documentation tests.

Tier 2 (emulator, API 36) against the compose stack: Home showing the day's calories, macros and
seven micronutrients; Trends showing both ranges, the bars, and the average day's breakdown. Both
the day-boundary bugs above were found here and re-checked here after fixing.

## Left open

- **Goals are milestone 12.** Nothing on either screen compares a number to a target, which is why
  the chart scales against its own tallest day.
- **No per-nutrient detail view and no export** — milestone 14.
- **The zone is still UTC.** `DayBoundary.ZoneFor` returns it for every user, and this milestone
  deliberately did not add a `TRACKR_TIMEZONE` knob; what it did instead was make every caller ask
  the server, so §9.13 stays one change rather than a hunt.
- **No physical-phone run.**
