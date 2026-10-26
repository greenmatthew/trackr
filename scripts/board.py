#!/usr/bin/env python3
"""Trackr task board.

Tasks are Markdown files with a small front-matter block, living in one of five directories under
tasks/: backlog, in-progress, needs-user, done, rejected. The directory IS the status; the file
never records it.

The MILESTONE is a label, not an order. tasks/README.md holds the milestone table; `validate`
rejects a label that is not in it and `list` groups by it, but the table is the history of the
product's areas rather than a plan, so `ready` and `next` order by id. Reorder work by editing
depends_on - ids never change. (Lifted from the millionaire board, which orders by milestone
because its table is a plan.)

needs-user is the lane for a task that cannot proceed until the USER acts - a decision only they
can make, a phone only they hold, a deploy only they can run. It is not for a task blocked by
another task (that is depends_on). Every task in it carries a '## Waiting on you' section, and a
task sitting there does not count against the three in-progress slots.

    board.py validate                          check every task (`just board`, and the first step of `just test`)
    board.py list [--status S] [--milestone M] table of tasks, grouped by milestone
    board.py ready                             backlog tasks nothing blocks, lowest id first
    board.py next                              the one task to pick up now
    board.py milestone M9                      what is done and what is left under one label (`9` works too)
    board.py waiting                           what is waiting on the user, and what they must do
    board.py new "Title" --milestone M14 [--record docs/decisions/x.md] [--depends 12,14] [--goal "..."]
    board.py move ID STATUS                    git mv a task between directories
    board.py show ID                           print a task

Only the standard library is used so this runs anywhere Python 3 does.
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TASKS = ROOT / "tasks"
README = TASKS / "README.md"
# Lifecycle order, which is also the order `list` sorts by inside a milestone.
STATUSES = ("backlog", "in-progress", "needs-user", "done", "rejected")
# The lanes that stop a dependency from blocking: finished, or decided against. A task parked in
# needs-user/ blocks its dependents exactly as a backlog one does, because the answer that unparks
# it may change what they are.
SETTLED = ("done", "rejected")
WAITING_HEADING = "## Waiting on you"
REQUIRED = ("id", "title", "milestone", "record")
MAX_IN_PROGRESS = 3

TEMPLATE = """---
id: {id}
title: {title}
milestone: {milestone}
record: {record}
depends_on: [{depends}]
---

## Goal

{goal}

## Done when

- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

"""


@dataclass
class Task:
    path: Path
    status: str
    meta: dict[str, str] = field(default_factory=dict)
    body: str = ""

    @property
    def id(self) -> int:
        return int(self.meta["id"])

    @property
    def title(self) -> str:
        return self.meta.get("title", "")

    @property
    def milestone(self) -> str:
        return self.meta.get("milestone", "")

    @property
    def depends(self) -> list[int]:
        raw = self.meta.get("depends_on", "[]").strip("[] ")
        return [int(x) for x in re.split(r"[,\s]+", raw) if x]


def label(text: str) -> str:
    """'9', 'm9' and 'M9' all name M9; '10A' names M10a. Code comments say "milestone 9"."""
    text = text.strip()
    if text[:1] in ("m", "M"):
        text = text[1:]
    return "M" + text.lower()


def milestones() -> list[tuple[str, str]]:
    """The (label, area) rows of the table under '## Milestones' in tasks/README.md."""
    rows: list[tuple[str, str]] = []
    inside = False
    for line in README.read_text(encoding="utf-8").splitlines():
        if line.startswith("## "):
            inside = line.strip() == "## Milestones"
            continue
        m = re.match(r"^\|\s*`?(M\d+[a-z]?)`?\s*\|\s*(.+?)\s*\|", line) if inside else None
        if m:
            rows.append((m.group(1), m.group(2)))
    return rows


def milestone_rank() -> dict[str, int]:
    return {m: i for i, (m, _) in enumerate(milestones())}


def parse(path: Path, status: str) -> Task:
    text = path.read_text(encoding="utf-8")
    m = re.match(r"^---\n(.*?)\n---\n?(.*)$", text, re.S)
    if not m:
        raise ValueError(f"{path}: missing front matter")
    meta: dict[str, str] = {}
    for line in m.group(1).splitlines():
        if ":" in line:
            k, v = line.split(":", 1)
            meta[k.strip()] = v.strip()
    return Task(path, status, meta, m.group(2))


def load() -> list[Task]:
    tasks: list[Task] = []
    for status in STATUSES:
        for path in sorted((TASKS / status).glob("*.md")):
            tasks.append(parse(path, status))
    return tasks


def validate(tasks: list[Task]) -> int:
    problems: list[str] = []
    rank = milestone_rank()
    if not rank:
        problems.append("tasks/README.md: no '## Milestones' table found")
    seen: dict[int, Path] = {}
    for t in tasks:
        rel = t.path.relative_to(ROOT)
        for key in REQUIRED:
            if key not in t.meta or not t.meta[key]:
                problems.append(f"{rel}: missing '{key}'")
        if "id" in t.meta:
            try:
                tid = t.id
            except ValueError:
                problems.append(f"{rel}: id is not a number")
                continue
            if tid in seen:
                problems.append(f"{rel}: duplicate id {tid} (also {seen[tid].relative_to(ROOT)})")
            seen[tid] = t.path
            if not t.path.name.startswith(f"{tid:03d}-"):
                problems.append(f"{rel}: file name should start with {tid:03d}-")
        if rank and t.milestone not in rank:
            problems.append(f"{rel}: milestone '{t.milestone}' is not in the tasks/README.md table")
        record = t.meta.get("record", "")
        if record and record != "-" and not (ROOT / record).exists():
            problems.append(f"{rel}: record '{record}' does not exist")
        if "## Goal" not in t.body or "## Done when" not in t.body:
            problems.append(f"{rel}: body needs '## Goal' and '## Done when' sections")
        if t.status == "rejected" and "## Why rejected" not in t.body:
            problems.append(f"{rel}: rejected tasks need a '## Why rejected' section")
        if t.status == "needs-user" and WAITING_HEADING not in t.body:
            problems.append(
                f"{rel}: needs-user tasks need a '{WAITING_HEADING}' section saying exactly what "
                "the user has to do or decide"
            )
    ids = set(seen)
    for t in tasks:
        for d in t.depends:
            if d not in ids:
                problems.append(f"{t.path.relative_to(ROOT)}: depends on unknown task {d}")
            elif d == t.id:
                problems.append(f"{t.path.relative_to(ROOT)}: depends on itself")
    # needs-user is deliberately NOT counted here: parking a task there is how a slot is freed.
    in_progress = [t for t in tasks if t.status == "in-progress"]
    if len(in_progress) > MAX_IN_PROGRESS:
        problems.append(f"{len(in_progress)} tasks in progress; finish some before starting more")
    for p in problems:
        print("error:", p)
    counts = {s: sum(1 for t in tasks if t.status == s) for s in STATUSES}
    summary = ", ".join(f"{s} {n}" for s, n in counts.items())
    print("board:", summary, "-", "OK" if not problems else f"{len(problems)} problem(s)")
    return 1 if problems else 0


def ready(tasks: list[Task]) -> list[Task]:
    """Backlog tasks whose dependencies are all settled, lowest id first."""
    settled = {t.id for t in tasks if t.status in SETTLED}
    rows = [t for t in tasks if t.status == "backlog" and all(d in settled for d in t.depends)]
    return sorted(rows, key=lambda t: t.id)


def row(t: Task, width: int) -> str:
    deps = ",".join(str(d) for d in t.depends) or "-"
    return f"{t.id:03d}  {t.status:<11} {t.milestone:<4} {t.title:<{width}}  deps {deps}"


def print_grouped(rows: list[Task]) -> None:
    table = dict(milestones())
    rank = milestone_rank()
    width = max((len(t.title) for t in rows), default=5)
    current = None
    for t in sorted(rows, key=lambda t: (rank.get(t.milestone, len(rank)), STATUSES.index(t.status), t.id)):
        if t.milestone != current:
            current = t.milestone
            print(f"\n{current}  {table.get(current, '(not in the milestone table)')}")
        print("  " + row(t, width))


def list_tasks(tasks: list[Task], status: str | None, milestone: str | None) -> None:
    rows = [
        t
        for t in tasks
        if (not status or t.status == status) and (not milestone or t.milestone == milestone)
    ]
    print_grouped(rows)
    print(f"\n{len(rows)} task(s)")


def print_ready(tasks: list[Task]) -> None:
    rows = ready(tasks)
    width = max((len(t.title) for t in rows), default=5)
    for t in rows:
        print(row(t, width))
    print(f"{len(rows)} task(s) ready to start")


def print_next(tasks: list[Task]) -> int:
    in_progress = sorted((t for t in tasks if t.status == "in-progress"), key=lambda t: t.id)
    for t in in_progress:
        print(f"in progress: {t.id:03d} {t.title}")
    rows = ready(tasks)
    if not rows:
        print("Nothing is ready: every backlog task waits on another task or on the user (`just waiting`).")
        return 0
    t = rows[0]
    print(f"next: {t.id:03d} {t.title}  ({t.milestone}, {t.path.relative_to(ROOT)})")
    return 0


def milestone_report(tasks: list[Task], milestone: str) -> int:
    table = dict(milestones())
    if milestone not in table:
        sys.exit(f"{milestone} is not in the tasks/README.md milestone table: {', '.join(table)}")
    rows = sorted((t for t in tasks if t.milestone == milestone), key=lambda t: t.id)
    counts = {s: sum(1 for t in rows if t.status == s) for s in STATUSES}
    print(f"{milestone}  {table[milestone]}")
    print("  " + (", ".join(f"{s} {n}" for s, n in counts.items() if n) or "no tasks"))
    width = max((len(t.title) for t in rows), default=5)
    for t in rows:
        print("  " + row(t, width))
    if rows and all(t.status in SETTLED for t in rows):
        print("  Nothing left open under this label.")
    return 0


def waiting(tasks: list[Task]) -> int:
    """What is waiting on the user, with the ask spelled out.

    Prints the '## Waiting on you' section of every needs-user task, because the whole point of
    the lane is that the ask should be readable without opening the task.
    """
    rows = sorted((t for t in tasks if t.status == "needs-user"), key=lambda t: t.id)
    if not rows:
        print("Nothing is waiting on you.")
        return 0
    for t in rows:
        print(f"{t.id:03d}  {t.title}")
        print(f"      {t.path.relative_to(ROOT)}")
        for line in section(t.body, WAITING_HEADING):
            print(f"      {line}" if line else "")
        print()
    print(f"{len(rows)} task(s) waiting on you.")
    return 0


def section(body: str, heading: str) -> list[str]:
    """The lines under one '## ' heading, up to the next one."""
    lines = body.splitlines()
    try:
        start = lines.index(heading) + 1
    except ValueError:
        return []
    out: list[str] = []
    for line in lines[start:]:
        if line.startswith("## "):
            break
        out.append(line.rstrip())
    while out and not out[0]:
        out.pop(0)
    while out and not out[-1]:
        out.pop()
    return out


def slugify(title: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", title.lower()).strip("-")[:48]


def new_task(tasks: list[Task], title: str, milestone: str, record: str, depends: str, goal: str) -> Path:
    table = dict(milestones())
    if milestone not in table:
        sys.exit(f"--milestone must be one of {', '.join(table)} (tasks/README.md)")
    if record != "-" and not (ROOT / record).exists():
        sys.exit(f"--record {record} does not exist; pass '-' and set it once the record is written")
    next_id = max((t.id for t in tasks), default=0) + 1
    path = TASKS / "backlog" / f"{next_id:03d}-{slugify(title)}.md"
    deps = ", ".join(x for x in re.split(r"[,\s]+", depends) if x)
    path.write_text(
        TEMPLATE.format(id=next_id, title=title, milestone=milestone, record=record, depends=deps, goal=goal or title),
        encoding="utf-8",
    )
    print(path.relative_to(ROOT))
    return path


def move(tasks: list[Task], task_id: int, status: str) -> None:
    for t in tasks:
        if t.id == task_id:
            dest = TASKS / status / t.path.name
            dest.parent.mkdir(parents=True, exist_ok=True)
            tracked = (
                subprocess.run(
                    ["git", "ls-files", "--error-unmatch", str(t.path)], cwd=ROOT, capture_output=True
                ).returncode
                == 0
            )
            if tracked:
                subprocess.run(["git", "mv", str(t.path), str(dest)], check=True, cwd=ROOT)
            else:
                t.path.rename(dest)
            print(f"{t.path.relative_to(ROOT)} -> {dest.relative_to(ROOT)}")
            return
    sys.exit(f"no task with id {task_id}")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd")
    sub.add_parser("validate")
    p_list = sub.add_parser("list")
    p_list.add_argument("--status", choices=STATUSES)
    p_list.add_argument("--milestone")
    sub.add_parser("ready")
    sub.add_parser("next")
    p_ms = sub.add_parser("milestone")
    p_ms.add_argument("milestone")
    sub.add_parser("waiting")
    p_new = sub.add_parser("new")
    p_new.add_argument("title")
    p_new.add_argument("--milestone", required=True)
    p_new.add_argument("--record", default="-")
    p_new.add_argument("--depends", default="")
    p_new.add_argument("--goal", default="")
    p_move = sub.add_parser("move")
    p_move.add_argument("id", type=int)
    p_move.add_argument("status", choices=STATUSES)
    p_show = sub.add_parser("show")
    p_show.add_argument("id", type=int)
    args = ap.parse_args()

    # Every script in scripts/ prints its own help when run bare (CLAUDE.md §11). `just board`
    # passes `validate` explicitly, so the recipe still checks the board with no arguments.
    if args.cmd is None:
        ap.print_help()
        return 0

    tasks = load()
    if args.cmd == "validate":
        return validate(tasks)
    if args.cmd == "list":
        list_tasks(tasks, args.status, label(args.milestone) if args.milestone else None)
    elif args.cmd == "ready":
        print_ready(tasks)
    elif args.cmd == "next":
        return print_next(tasks)
    elif args.cmd == "milestone":
        return milestone_report(tasks, label(args.milestone))
    elif args.cmd == "waiting":
        return waiting(tasks)
    elif args.cmd == "new":
        new_task(tasks, args.title, label(args.milestone), args.record, args.depends, args.goal)
    elif args.cmd == "move":
        move(tasks, args.id, args.status)
    elif args.cmd == "show":
        for t in tasks:
            if t.id == args.id:
                print(t.path.read_text(encoding="utf-8"))
                return 0
        sys.exit(f"no task with id {args.id}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
