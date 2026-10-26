# Task board

One Markdown file per task. The directory is the status:

| directory | meaning |
|---|---|
| `backlog/` | agreed work, not started |
| `in-progress/` | being worked on right now (one to three at most) |
| `needs-user/` | cannot proceed until **the user** acts: a decision only they can make, a phone only they hold, a deploy only they can run |
| `done/` | finished: `just test` green, seen working at the tier its commit names, committed with the `[NNN]` prefix and a `Verified:` trailer |
| `rejected/` | decided against, with a `## Why rejected` section so the idea is not re-litigated |

Files are named `NNN-slug.md`, where the slug is
`re.sub(r"[^a-z0-9]+", "-", title.lower()).strip("-")[:48]`. Front matter: `id`, `title`,
`milestone` (a row of the table below), `record` (the decision record under `docs/decisions/` the
task wrote or amended, or `-`), `depends_on` (a list of ids). The body has `## Goal`,
`## Done when` and `## Notes`. Notes is where discoveries, measurements, deviations and follow-up
ids go; the *why* of a choice worth keeping goes in the record (CLAUDE.md §9).

## Milestones

**Labels, not gates, and not an order.** A task's milestone groups it with related work.
`scripts/board.py` reads this table: `validate` rejects a label that is not in it and
`just board list` groups by it, but `just ready` and `just next` order by id, because this table is
the history of the product's areas rather than a plan.

M1–M14 are the numbers of the build order CLAUDE.md §9 held until 2026-10-26, which is what a
code comment means by "milestone 9" and a decision record by "§9.9". Tasks 001–018 are that build
order's history: one per decision record, numbered after it, each carrying its build-order entry
verbatim under `## Notes`. `just milestone 13` finds them.

| milestone | area |
|---|---|
| M1 | Scaffold: the API, the web app, Postgres and the compose stack |
| M2 | Auth: Identity, cookies, 2FA, lockout, rate limits, invites |
| M3 | Mobile foundation: the Android pivot, bearer tokens, and the brand |
| M4 | Documentation: CLAUDE.md, docs/ and the wiki, and the tests that keep them honest |
| M5 | Mobile UX and architecture: shells, tabs, styles, SQLite on the phone |
| M6 | Data layer: the catalog, the log, nutrients and meal photos |
| M7 | Barcode and Open Food Facts |
| M7a | Composite and recipe items |
| M8 | Ollama: the model, the prompt and the validator |
| M9 | Chat, cascade and confirm |
| M10 | Catalog growth and re-logging |
| M10a | Ingredients, allergens and diet flags |
| M11 | Stats views: Home and Trends |
| M12 | Goals |
| M13 | User profile: name, units, body metrics, time zone, account self-service |
| M14 | Polish: everything after, in no fixed order |
| M15 | Operations: backups, CI, signing and releases, pinned and hardened images, going public |

A new area of work is a new row (`M15`, ...), added in the commit that files its first task.
Renaming a row means moving its tasks in the same commit.

## Order and size

`just next` offers the lowest-id ready task. **Reorder by editing `depends_on`; ids never
change.**

**A task fits one session:** one area, a goal you can state in a sentence, and a Done-when list you
can check. If a task turns out bigger, finish a coherent slice and file the rest as new tasks rather
than widening it silently. A task filed coarse — "richer charts" — is **split when it is picked
up**: file the parts, and move the coarse one to `rejected/` with `## Why rejected` reading "split
into NNN–MMM".

## What is not the backlog

**The "Left open" sections of the decision records.** They record what a milestone chose not to
do, usually with the reason, and filing each one as a task would re-open decisions already made.
One becomes a task when somebody decides to build it, and the task cites the record.

**Ideas too vague to build** belong in CLAUDE.md §12, not here.

## Commands

`scripts/board.py` (or `just board <cmd>`):

| command | what it does |
|---|---|
| `just board` | validate every task (also the first step of `just test`) |
| `just board list [--status S] [--milestone M]` | table of tasks, grouped by milestone |
| `just ready` | backlog tasks nothing blocks, lowest id first |
| `just next` | the one task to pick up now |
| `just milestone 14` | what is done and what is left under one label |
| `just waiting` | what is parked on the user, with the ask spelled out |
| `python3 scripts/board.py new "Title" --milestone M14 --depends 12,14` | file a task (max id + 1) |
| `just board move ID STATUS` | `git mv` a task between lanes |
| `just board show ID` | print a task |

Revise tasks freely as the work teaches you things; add new ones rather than silently widening an
existing one. Never delete a done task.

## `needs-user/`: the one lane an agent cannot clear on its own

**What belongs here:** the task cannot proceed until the user acts. A decision only they can make,
a check only their phone can make, a deploy to their server, or anything that has to be pushed
first.

**What does not:**
- **Blocked by another task.** That is `depends_on`, which already works. Writing the same block in
  two places lets the two drift.
- **Anything the emulator can answer.** CLAUDE.md §11: the emulator runs from Claude Code's own
  shell, so "I could not run it" is not a reason to park a task here.

**Getting in.** A task may arrive from:
- `backlog/`: it turned out to need an answer before it can start.
- `in-progress/`: it ran into a question only the user can settle. **This frees one of the three
  in-progress slots** — pick up the next task rather than sitting on it.
- `done/`: the code shipped and `just test` was green, but only the user's phone or server can
  confirm the claim. The commit already exists and is not reopened.

**Every task here carries a `## Waiting on you` section** between `## Done when` and `## Notes`. It
says exactly what the user has to do or decide, in terms they can act on without reading the rest
of the task. `board.py validate` enforces the heading, and `just waiting` prints what is under it.

**Getting out.** Do not guess this. Once the user answers:
- **Confirmed, or it works:** `done/` if the code already shipped, otherwise `backlog/` to be built
  now that the answer is known.
- **Refuted, or the answer revealed new work:** `backlog/`, or a new task carrying the finding.

Record the answer in the task's `## Notes` either way, so the question is not asked twice. Moving
between lanes is bookkeeping, not a code change: a `[board]` commit with `Verified: just test`,
never a second `[NNN]` one.
