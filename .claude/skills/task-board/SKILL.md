---
name: task-board
description: How to pick up, move, revise, close or reject tasks on the Trackr task board (tasks/ directory, scripts/board.py), how to commit one task with its Verified, Co-Authored-By and Claude-Session trailers in a tree other sessions may share, and what to do at the end of a run.
---

# Task board workflow

The board is `tasks/{backlog,in-progress,needs-user,done,rejected}/NNN-slug.md`. The directory is
the status. `scripts/board.py` (or `just board <cmd>`) manages it; `just board` alone validates,
`just next` names the task to pick up, and `just waiting` prints what is parked on the user. The
lanes, the milestone labels and the front matter are in `tasks/README.md`.

## Before the first task
Read, in this order: `.claude/state/handoff.md` if it exists (gitignored; where the last run
stopped; trust git where the two disagree), `CLAUDE.md`, the task, its `record:` and every decision
record its Notes cite, then the wiki page for anything you are about to run. Take only the ids you
were given, or `just next`: another session may file tasks in this same checkout while you work.

## One checkout, one index, several sessions
A manager may edit, file tasks and commit in the same working tree while you work, so `git status`
can show files that are not yours: leave them alone. `git commit` commits everything anyone has
staged, and `just board move` is a `git mv` that stages at once. So **every commit here is
`git commit --only -- <paths>`**, which takes those paths from the working tree and ignores the
rest of the index. A new file needs `git add <file>` first; `git add -A` never. Name only paths that
exist in HEAD or the working tree: a task picked up and finished in one session was never committed
under `tasks/in-progress/`, so list its `backlog/` and `done/` paths instead.

## Pick up a task
1. `just next` (or `just ready` for the whole queue). Its `depends_on` are all done.
2. `just board move <id> in-progress`. Keep at most three in progress.
3. Read the task and the records it cites. The records hold decisions already made; a task does not
   re-decide them, and if one looks wrong, say so rather than quietly building around it.
4. A coarse task ("split when it is picked up") is split first: file the parts, move the coarse one
   to `rejected/` with `## Why rejected` reading "split into NNN–MMM", and take the first part.

## While working
- A choice somebody would otherwise re-litigate goes in a decision record **in the same commit**:
  amend the record it changes, or add `docs/decisions/NN-slug.md` (next free number), list it in
  `docs/decisions/README.md` and CLAUDE.md §4, and name it in the task's `record:`. A discovery,
  measurement or deviation that is not a decision goes under the task's `## Notes`.
- Work that does not belong to this task becomes a new task:
  `python3 scripts/board.py new "Title" --milestone M14 --depends 12`. Do not silently widen the
  current one. (`just board new "Two words"` works too; the recipe keeps the quoting.)
- A task that turns out wrong or unnecessary goes to `rejected/` with a `## Why rejected` section.
- A task that cannot go further until the **user** acts goes to `needs-user/` — see below.

## Finish a task
1. **`just test` green** — it validates the board, then runs the docs, mobile and API suites. Then
   the tier the change needs (CLAUDE.md §11): anything under `src/Trackr.Mobile*` is built and seen
   on the emulator (`./scripts/app.sh ui` dumps the screen's text); anything the API serves is hit
   on the running dev stack (`just server::up`). Write what you saw under `## Notes`.
2. **The docs that are generated or tested move with the code** (CLAUDE.md §0): an endpoint change
   reruns `just docs::api`; a new `TRACKR_*` variable or `just` recipe is documented in `wiki/` in
   the same commit, or `just docs::test` fails.
3. Tick the `## Done when` boxes; add notes (measurements, deviations, follow-up task ids).
4. **One commit per task**, in one chain:
   `just board move <id> done && just board && git commit --only -F msg -- <every path this task touched, both task paths included>`.
   Subject `[<id>] <Title>`; body: what changed, deviations, follow-up ids; then three trailers:
   - `Verified:` the tier the claim rests on — `just test` | `stack` | `emulator` | `phone` |
     `none - needs phone`. Name the highest one actually reached, never the one it should pass.
   - `Co-Authored-By: <the model doing the work> <noreply@anthropic.com>`
   - `Claude-Session: <URL>` — both values are in the session's attribution note; never guess them.
5. **Never `git push`, and never `just docs::publish`.** Both publish, and the user does that.
6. If only the user's phone or server can confirm the change works, it does not sit in `done/`
   waiting to be forgotten: `just board move <id> needs-user` with a `## Waiting on you`, in a
   `[board]` commit.

## Waiting on the user (`needs-user/`)
A decision only the user can make, a check only their phone can make, a deploy to their server.
Not a task blocked by another task (that is `depends_on`), and **not anything the emulator can
answer** — it runs from this shell (CLAUDE.md §11).

**Getting in.** `just board move <id> needs-user` from `backlog/` (it needs an answer before it can
start), `in-progress/` (a question only the user can settle — this frees the slot, so pick up the
next task) or `done/` (shipped, gate green, but only the user's hardware can confirm the claim; the
`[NNN]` commit stays where it is).

**Every task in it carries a `## Waiting on you` section**, between `## Done when` and `## Notes`:
exactly what the user has to do or decide, in terms they can act on without reading the rest — the
commands, the taps, what to report back — not the design behind the question. `board.py validate`
fails without it.

**Getting out.** Confirmed, or it works → `done/` if the code shipped, else `backlog/`. Refuted, or
new work revealed → `backlog/`, or a new task carrying the finding. Write the answer into `## Notes`
either way so nobody asks twice. Lane moves are bookkeeping: a `[board]` commit with
`Verified: just test`, never a second `[NNN]` one.

## Revising the board
- Reorder by editing `depends_on`; ids never change. Milestones are labels, not gates or an order.
- A new area of work is a new row in the `tasks/README.md` milestone table, in the commit that files
  its first task.
- The "Left open" sections of the decision records are not the backlog. One becomes a task only
  when somebody decides to build it.

## End of a run
Rewrite `.claude/state/handoff.md` (gitignored; never `git add -f` it) — where things stand at your
final HEAD, what is in flight, what is next, gotchas, dead ends. Accurate beats long.
`manager-handoff.md` beside it belongs to a manager session, if one is running; do not touch it.
