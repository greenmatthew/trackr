---
id: 7
title: Data layer
milestone: M6
record: docs/decisions/07-data-layer.md
depends_on: []
---

## Goal

Seven entities, the relational nutrient store, and CRUD for the catalog, the log and meal photos —
API only. The acceptance criterion: store and read back a full multi-nutrient item.

## Done when

- [x] the nutrient store seeded with 29 nutrients
- [x] CRUD for the catalog, the log and meal photos
- [x] `FoodCatalogTests.A_food_item_keeps_every_nutrient_it_was_given` passes
- [x] recorded in [07-data-layer.md](../../docs/decisions/07-data-layer.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> 6. ~~**Data layer**~~ ✅ — [07-data-layer.md](../../docs/decisions/07-data-layer.md). Seven entities,
>    the relational nutrient store seeded with 29 nutrients, and CRUD for catalog, log and meal
>    photos. The acceptance criterion — store and read back a full multi-nutrient item — is
>    `FoodCatalogTests.A_food_item_keeps_every_nutrient_it_was_given`.
>    **API only: it has no UI and is not meant to acquire one** (§10 still forbids a food-logging
>    surface on the web; the chat is milestone 9). Two things it settled beyond the original scope:
>    the **catalog is shared across accounts** (see §7) and meal photos are stored now, because
>    milestone 9 needs somewhere to put them before a confirmation card exists.
