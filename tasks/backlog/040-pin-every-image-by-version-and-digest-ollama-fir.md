---
id: 40
title: Pin every image by version and digest, Ollama first
milestone: M15
record: -
depends_on: []
---

## Goal

A rebuild gets exactly the images the last one did, and an upgrade is a commit someone chose.

## Done when

- [ ] every `FROM` and `image:` pinned to a version and a `@sha256` digest
- [ ] a script lists newer versions on request, so pins do not quietly go stale
- [ ] the wiki says how to bump one
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: dnd-near 196, 221, 233 and its D22; nautilus 019.

- Today: `sdk:10.0`, `aspnet:10.0-alpine`, `nginx:1.29-alpine`, `postgres:18-alpine`, and
  `ollama/ollama:latest` twice (`docker/docker-compose.yml:116,149`).
- **Ollama matters most:** [10-ollama.md](../../docs/decisions/10-ollama.md) records that an older
  Ollama silently truncated an oversized prompt where the version in use refuses it. A `latest` pull
  can bring the silent failure back.
- A new Postgres major is a dump and restore, not a pin bump.
