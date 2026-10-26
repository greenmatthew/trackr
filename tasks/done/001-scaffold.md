---
id: 1
title: Scaffold
milestone: M1
record: docs/decisions/01-scaffold.md
depends_on: []
---

## Goal

A solution with a Blazor WebAssembly frontend, an ASP.NET Core Web API backend, EF Core over
Postgres, and Docker Compose bringing up db, backend and frontend behind a health check.

## Done when

- [x] the compose stack starts db, backend and frontend behind a health check
- [x] nginx serves the web app and proxies `/api/` to the backend, so the session can be a same-origin cookie
- [x] recorded in [01-scaffold.md](../../docs/decisions/01-scaffold.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> 1. ~~**Scaffold**~~ ✅ — [01-scaffold.md](../../docs/decisions/01-scaffold.md)
