---
id: 13
title: Ingredients
milestone: M10a
record: docs/decisions/13-ingredients.md
depends_on: []
---

## Goal

Parts 1 and 2 of what a product is made of: raw ingredient text on the item, and allergens and diet
flags as Open Food Facts' own tags.

## Done when

- [x] ingredient text from Open Food Facts, and from the label only where a partial match left a gap
- [x] allergens and diet flags as `text[]` with GIN indexes, validated against no vocabulary
- [x] the brand-or-barcode rule enforced, and a recipe's allergens the union of its components'
- [x] an over-long list dropped rather than truncated
- [x] recorded in [13-ingredients.md](../../docs/decisions/13-ingredients.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

Part 3, per-ingredient rows, is deliberately unbuilt. The entry below is the guidance for whoever is
tempted: keep to parts 1 and 2 unless a real question needs part 3.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> - ~~**10a. Ingredients**~~ ✅ (parts 1–2) — [13-ingredients.md](../../docs/decisions/13-ingredients.md).
>   Raw text on the item; allergens and diet flags as a Postgres `text[]` with GIN indexes, using
>   **Open Food Facts' own tags** rather than a local vocabulary, and validating against no
>   vocabulary at all — theirs grows without asking, and dropping an unknown tag would discard a
>   real allergen warning. **Empty means the source said nothing, never "contains no allergens".**
>   The brand-or-barcode rule is enforced and **a recipe derives its own**: allergens *union*
>   where nutrients intersect, because the safe direction for "how much iron" is to say less and
>   for "does this contain nuts" is to say more. Diet flags are not derived — "vegan" does not
>   union.
>   **The model reads a list off a label only where the database left a gap**, and that scope fell
>   out of milestone 10's rules rather than out of caution: a list is filed onto a catalog row, a
>   row needs a barcode, and a barcode came from Open Food Facts — so the only gap is a *partial*
>   match with no ingredients, whose photo is already being sent. The field is absent from the
>   schema and the prompt otherwise. Bounded at 600 characters for the **output budget**, not the
>   column, and an over-long list is **dropped rather than truncated** — the opposite of every
>   other text field, because a shortened ingredient list reads as a complete one.
>   **Left open:** part 3 remains unbuilt as intended.
>   Lettered rather than renumbered because §9.10 and §9.13 are referenced by name from code
>   comments.
>   **Three parts, in this order, and the value drops off sharply after the second.**
>   1. **Raw ingredient text — cheap, do this first.** Open Food Facts already returns
>      `ingredients_text_en`, so for any barcode hit this is a field to map, not a feature to
>      build. Milestone 7 deliberately does not request it (see the `Fields` constant); adding it
>      is a small change. Where OFF has nothing, the **model reads it off the photo** in the same
>      vision call that reads the nutrition panel — no second pass, no OCR engine, and the image
>      never leaves the server. Store it as text on the item and show it; that alone answers "what
>      is in this?" and carries almost no design risk.
>   2. **Allergens and diet flags — the high-value structured subset.** OFF returns
>      `allergens_tags` (`en:milk`, `en:nuts`, `en:soybeans`) and `ingredients_analysis_tags`
>      (`en:palm-oil`, `en:non-vegan`). A closed, small vocabulary, unlike ingredients in general,
>      so it is queryable without solving the hard problem — and it is the part a household
>      actually needs, because "does this contain nuts" is a question with consequences.
>   3. **Per-ingredient rows and stats — the uncertain one.** *Deliberately last, and it may never
>      be worth building.* Two things make it more tractable than it looks, both worth knowing
>      before writing it off: OFF ships a **canonical ingredient taxonomy** (`en:sugar`), which is
>      the alias problem already solved by somebody else; and OFF's parsed `ingredients` array
>      **already nests sub-ingredients**, which is the "an ingredient made of ingredients" case
>      modelled for us, along with a `percent_estimate` per ingredient. So the aliases, the
>      synonyms and the parenthesised sub-lists are largely not ours to untangle *for packaged
>      products*. What stays genuinely hard is anything the model transcribed rather than OFF
>      supplying, because mapping free text onto that taxonomy is its own problem.
>   - **Ingredients belong to a specific product, and that constrains where they can live.** A
>     list is only true of one brand's formulation, so it attaches to an item carrying a brand or
>     a barcode. A generic item ("chicken breast") must **not** claim one, and a composite (7a)
>     derives its ingredients from its components instead of storing a list of its own. This is
>     the concern that makes ingredients a poor fit for the shared catalog's wiki-style editing —
>     settle it before writing the schema.
>   - Keep it to parts 1 and 2 unless a real question needs part 3. "Which of my regular foods
>     contain palm oil" is answerable from part 2; "how much sunflower lecithin did I eat in
>     March" is what part 3 buys, and is a question nobody has actually asked.
