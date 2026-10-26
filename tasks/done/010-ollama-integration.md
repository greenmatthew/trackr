---
id: 10
title: Ollama integration
milestone: M8
record: docs/decisions/10-ollama.md
depends_on: []
---

## Goal

`IMealAnalyzer` behind a typed client: a prompt and JSON schema generated from `NutrientCatalog`,
and the validator §5 marks REQUIRED standing between the model and the database.

## Done when

- [x] a full Open Food Facts match sends its structured data instead of its photo (`MealPrompt.PhotosToSend`)
- [x] `MealAnalysisReader` validates every reply before anything trusts it
- [x] `POST /api/analyze` runs all three stages and writes nothing, with a test that says so
- [x] recorded in [10-ollama.md](../../docs/decisions/10-ollama.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> 8. ~~**Ollama integration**~~ ✅ — [10-ollama.md](../../docs/decisions/10-ollama.md). `IMealAnalyzer`
>    behind a typed client, a prompt and JSON schema **generated from `NutrientCatalog`**, the
>    image-vs-structured-data swap in `MealPrompt.PhotosToSend`, and `MealAnalysisReader` — the
>    validator §5 marks REQUIRED, which is where the milestone's weight sits. One read-only route,
>    `POST /api/analyze`, running all three stages; it **writes nothing**, with a test that says so.
>    **Two things it settled beyond the original scope.** The model reports *per serving* plus a
>    quantity and **the server does the multiplication** — a deviation from §5's "the prompt computes
>    serving math", because small models are bad at arithmetic and the result maps 1:1 onto
>    `SaveLogItemRequest`. And **the prompt pins each identified product's serving**, without which
>    merging Open Food Facts' figures with the model's re-creates milestone 7's "each number
>    defensible, total nonsense" failure across a new seam.
>    **Two traps worth knowing before touching any of it:** a JSON-schema keyword Ollama's grammar
>    converter does not support is **skipped in silence**, leaving that part of the reply
>    unconstrained with no runtime symptom — hence
>    `The_schema_uses_no_construct_the_grammar_skips_silently`. And a photograph costs about 7 000
>    tokens, so the context window is set explicitly rather than left to Ollama, which sizes it from
>    *video* memory and gives a CPU-only server far too little.
>    **Left open:** nothing calls the route but an HTTP client, and the mobile client cannot — its
>    30-second timeout and retry pipeline would queue four inference jobs for one meal.
