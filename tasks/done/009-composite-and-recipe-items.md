---
id: 9
title: Composite and recipe items
milestone: M7a
record: docs/decisions/09-composites.md
depends_on: []
---

## Goal

A recipe is a `FoodItem` plus a `FoodItemComponent` join and a `Yield`, with its nutrition
materialised on write so nothing downstream learns composites exist.

## Done when

- [x] quantities count servings of the ingredient, never grams
- [x] a cycle check behind a transaction-scoped advisory lock
- [x] an ingredient change recomputes every recipe using it, across accounts
- [x] deleting an ingredient a recipe uses is refused, never cascaded
- [x] recorded in [09-composites.md](../../docs/decisions/09-composites.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

Nothing on the phone creates a recipe yet; only an HTTP client can. Task 025.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> - ~~**7a. Composite / recipe items**~~ ✅ — [09-composites.md](../../docs/decisions/09-composites.md).
>   A composite is still a `FoodItem`, plus a `FoodItemComponent` join and a `Yield`; nutrition is
>   **materialized on write**, so nothing downstream learns composites exist. Quantities count
>   **servings of the ingredient**, never grams. Two rules the original sketch did not settle and
>   this milestone did: **a global recipe may hold only global ingredients** — which is also what
>   makes the delete cascade safe — and **a nutrient survives into a recipe only if every
>   ingredient reports it**, because summing a missing micronutrient as zero is a confident
>   understatement, and "missing is not zero" has to hold in the arithmetic or it holds nowhere.
>   The cycle check is backed by a **transaction-scoped advisory lock**, without which it is advice
>   rather than a guarantee; the fan-out recompute deliberately **crosses account boundaries**,
>   since a shared ingredient sits in other people's recipes. Deleting an item a recipe uses is
>   refused, never cascaded.
>   **Left open:** nothing writes a recipe yet but a person with an HTTP client — milestone 9 is
>   where saying "I made this from these" becomes possible — and there is no scaling or unit
>   conversion.
