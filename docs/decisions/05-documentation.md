# Milestone 4 — Documentation migration

Splitting documentation by *when it is read*, publishing the wiki from this repository, and
making the parts that can go stale fail a test instead.

No feature code. The problem was that `CLAUDE.md` had grown to ~50 KB and is loaded in full
every session, while most of its bulk had become reference — answers to "which port", "what
are the nutrient keys", "how do I start the emulator" — that cost context in every unrelated
session and was needed in almost none of them.

## Three homes, split by when something is read

| Home | Holds | Read |
| --- | --- | --- |
| `CLAUDE.md` | Decisions and constraints | Every session, unprompted |
| `docs/` | Claude-facing working material; one decision record per milestone | Before changing something a record covers |
| `wiki/` | Reference and how-to, for the self-hoster and Claude alike | On demand |

The split is deliberately **not** by audience. A self-hoster and Claude both need "how do I
point the app at my server"; the overlap is near-total. What actually differs is whether the
material must be in context *before* the question is asked.

**The test:** would Claude make a worse decision without this in context? If yes it stays in
`CLAUDE.md`. If it merely answers a question, it goes to the wiki, where it can be looked up
at the moment it is needed.

**One exception, and it is the important one: constraints stay in `CLAUDE.md` even when they
read like reference.** The cleartext-HTTP rule, the two auth schemes, the security posture.
A wiki page that drifts out of step with the code is a mild annoyance for a how-to and a
security misunderstanding for a constraint.

## The wiki is published from this repository, not cloned into it

`wiki/` holds ordinary tracked files, and `just docs::publish` copies them to
`<repo>.wiki.git` on each host. The wiki repositories are a **publishing target, not a source
of truth**.

The whole point is atomicity: a change to an environment variable and a change to the page
documenting it land in **one commit**, reviewed together and impossible to half-forget.

Two consequences worth holding on to:

- Editing a page in the GitHub or Gitea wiki UI is pointless — the next publish overwrites it.
- A wiki repository does not exist until its wiki has one page. If `just docs::publish` cannot
  clone, the fix is to create the first page through the web UI once per host, not to work
  around it in the recipe.

### Rejected alternatives

Recorded so they are not revisited:

- **Cloning `trackr.wiki.git` into `wiki/` and gitignoring it.** Editing works, but nothing is
  ever atomic, and a second repository is a second thing to forget to push.
- **A submodule.** Worse: still two commits, plus detached-HEAD friction.
- **`git subtree`.** A genuine contender — it *is* atomic — but its merge semantics are hard
  to reason about for something as low-stakes as a docs folder.

### Publishing targets are derived, not listed

The wiki URLs come from this repository's own `all` remote with `.git` swapped for
`.wiki.git`. There is deliberately no second list of wiki remotes to keep in step: adding a
host to `all` is enough, and two lists that must agree are exactly the kind of drift this
milestone is about.

## Keeping documentation honest

Prose cannot be generated — nothing writes "how to self-host behind a reverse proxy" from
source. Two things can be, and they cover the material that drifts most.

### `API-Reference.md` is generated

From the API's own OpenAPI document, by a test in `tests/Trackr.Api.Tests/Docs/`.

**Read through `IOpenApiDocumentProvider` from DI, not over HTTP.** `Program.cs` maps
`/openapi/v1.json` only in Development, and there was no reason to widen that for a test.
.NET 10 registers the provider in the container (keyed by document name), so the test resolves
it from `WebApplicationFactory.Services` and gets an `OpenApiDocument` directly. The
production gate is untouched.

**One test both generates and enforces.** With `TRACKR_UPDATE_DOCS=1` it writes the page;
without it, it asserts the committed page matches and fails pointing at `just docs::api`. A
separate generator plus a separate check would be two things that can disagree.

It boots the real application, so it needs Docker and lives in the API suite rather than the
fast one. That was the cost of using the real document instead of a hand-maintained list, and
it is worth it.

**Response bodies are not in the page, and the page says so.** The handlers return `IResult`
and declare no `Produces<T>()`, so the document genuinely does not know their shapes. The
renderer collapses the resulting one-row-of-nothing tables into a bare status code and prints
a note explaining why. Declaring response types on ~20 endpoints is real API work and belongs
to whichever milestone wants it; the table comes back on its own if it ever happens.

### Two drift tests, in a suite that needs nothing

`tests/Trackr.Docs.Tests` is plain xUnit with no project references, no Docker and no Android
SDK. It runs in about 50 ms, which is why `just test` runs it first — a stale wiki page is
reported before Docker has finished starting Postgres.

- **Every variable the deployment stack reads must be documented.** Wider than the section-9
  wording ("every `TRACKR_*` variable"): it covers every substituted variable, so
  `POSTGRES_PASSWORD` and `PROXY_NETWORK` are caught too, and it checks `docker/.env.example`
  as well as `Configuration.md`. Copying that file is the documented way to configure the
  stack, so an omission there is a knob nobody discovers.
- **Every `just` command the documentation names must exist.** Extended beyond the README to
  `CLAUDE.md` and the wiki, since a renamed recipe misleads a reader of any of them equally.

**Both scan only code spans and fenced blocks, never raw prose.** "just" is an ordinary
English word, and `CLAUDE.md` contains "just a", "just goes" and "just macros". Scanning raw
text reported half a dozen recipes that were never meant to be recipes.

**The justfiles are parsed, not queried.** Running `just --list` would need `just` on PATH to
run the test suite. The grammar being matched is small and stable, and the one trap — `set
working-directory := '..'` reading as a recipe named "set" — is handled with a negative
lookahead on `=`.

**Each suite has a guard-the-guard test.** A regex that stops matching would otherwise leave
the theories passing with nothing in them, which is worse than no test at all: it looks green.

## What was deliberately not done

- **`CLAUDE.md` was not cut to the 22–25 KB the milestone named.** It went from 49.6 KB to
  **35.2 KB** — a 29% cut, and short of the target by a wide margin.

  The two sections the milestone singled out did shrink as intended: §11 from ~4.5 KB to 1.8 KB
  and §3 from ~8 KB to 5.8 KB, with the dev-environment, build and Android-testing material
  deleted rather than duplicated, and the nutrient list and model-selection advice replaced by
  pointers. What the 22–25 KB estimate did not account for is that most of the *rest* is
  irreducible: §5 (the cascade spec), §8 (security posture), §9 (the 14-milestone build order)
  and §10 (non-goals) are ~15 KB between them and are decisions and constraints end to end.

  Cutting to 25 KB would mean deleting one of those, which the constraints-stay exception
  forbids. The rule was applied and the number was allowed to fall where it fell. If the file
  needs to be smaller later, the honest lever is moving §5 and §9 into `docs/` and accepting
  that they are read per-milestone rather than every session — a real decision, not a trim.
- **`Produces<T>()` was not added to the endpoints.** See above — real API work, wrong
  milestone.
