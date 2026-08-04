# Milestone 7a — Composite / recipe items

Barcode plus Open Food Facts covers packaged food and fails completely on home cooking, which is the
case the AI fallback handles worst. A recipe answers it by being assembled from things the catalog
already knows: two hundred grams of the beans somebody scanned, plus the rice, divided by however
many bowls it makes.

The shape was settled in [07-data-layer.md](07-data-layer.md) and did not move. A composite is
**still a `FoodItem`**, plus a `FoodItemComponent` join and a `FoodItem.Yield`; nutrition is
**materialised on write**, so nothing downstream learns that composites exist. No backfill, no
change to an existing table beyond one nullable column.

What the slice actually consists of is the two things that record named: the cycle check, and the
fan-out recompute. Everything below is about those, or about a question the sketch did not answer.

## Materialised, and what that costs

A recipe's own columns and nutrient rows always hold the answer. `GET /api/foods`, the log, milestone
11's aggregates and the whole cascade read a recipe exactly as they read a scanned package, and none
of them contains the word "composite". Recursive aggregation at read time was the alternative and
would have put a tree walk in front of every catalog list and every dashboard.

The price is that **the numbers are a cache, and a cache has to be invalidated**. Correcting an
ingredient has to push new numbers up through every recipe made of it, transitively, or the recipe
reports the old figure forever. That is `CompositeNutrition.RecomputeAncestorsAsync`, and it runs in
the transaction of the edit that caused it — a fan-out that could fail independently would leave
exactly the silent wrong number CLAUDE.md §2 is written against.

**The recompute is deliberately not scoped to what the editing account can see.** A global
ingredient may be in recipes belonging to several household members; fixing only the editor's would
leave somebody else's dinner quietly wrong, and they would have no way of knowing. This is the first
place the shared catalog's wiki-style editing reaches across an account boundary and changes data the
editor cannot see, which is worth knowing before changing either.

It bumps `UpdatedUtc` on every recipe it touches so a client refetches, and leaves
`UpdatedByUserId` alone. The person edited the *ingredient*, and that is where the attribution
belongs; stamping them on a recipe of somebody else's they have never opened would be a worse lie
than a stale field.

## The cycle check, and the lock that makes it true

`WouldFormACycleAsync` walks *down* from the proposed ingredients looking for the recipe. Walking
down rather than up is what makes the recipe's existing edges irrelevant: they lead away from it, so
the walk only meets them after having already arrived — and arriving is the answer.

Only `PUT` needs it. Nothing can already contain an item that does not exist yet, so a create has no
cycle to form, which is also why a create only takes the transaction when it has ingredients at all.

**A transaction-scoped advisory lock (`pg_advisory_xact_lock`) is what makes the check a guarantee
rather than advice.** Without it two requests could each add one edge of a two-edge loop, both having
looked at a graph in which neither edge existed yet; the result is a recipe whose numbers can never
be recomputed correctly, and a walk that has to be written defensively to terminate at all. One
global key rather than a per-item one, because a cycle is a property of the whole graph and cannot be
locked piecewise. `SERIALIZABLE` would also have worked and would have needed a retry loop for a
conflict that, on a household server, will essentially never happen.

The cost is that catalog *writes* serialise. Reads are untouched, and writes here are human-paced —
somebody typing in a recipe, or the cascade saving one confirmed item. `DELETE` takes it too, so a
recipe cannot pick an item up between the check below and the delete.

Both walks carry a visited set and the topological sort drops rather than loops. That is belt and
braces on top of the lock, and it is there so that a graph which somehow *did* acquire a cycle
produces stale numbers rather than a hung request.

## Two rules that fell out of the shared catalog

Neither was in the sketch; both are forced.

**A global recipe may hold only global ingredients**, checked on create and again on
`POST /{id}/share`. Otherwise the household sees a recipe whose ingredient list it cannot open, and —
worse — the private item underneath it disappears the day its owner deletes their account, taking the
recipe's numbers with it. Enforcing this is also what makes the database-level `Cascade` on
`FoodItemComponent.ChildFoodItemId` safe: no recipe can outlive an account whose personal item it
depends on, so no cascade can leave a surviving recipe short an ingredient.

**Deleting an item a recipe uses is refused, 409, naming the recipes.** The database would cascade
happily and leave the recipe reporting numbers it can no longer justify. Recomputing it without the
ingredient would be worse, because the answer would look exactly as confident as before. Refusing is
the only option that tells somebody something true. Only personal items reach the check — a global
item is already a 403 — so no name in that message can belong to another account's recipe.

## Missing is not zero, all the way through the arithmetic

**A nutrient survives into a recipe only if every one of its ingredients reports it.**

This is the one decision here that a reasonable person would make differently, so the reasoning is
worth stating plainly. Every other recipe tracker sums what it has and treats a missing value as
zero. That is wrong under this project's own rules: the flour's silence about iron means *not
measured*, not *no iron*, and summing it as zero puts a confident understated number on the
confirmation card. wiki/Nutrient-Reference.md's rule — distinguish "known to be zero" from "not
measured" — only means something if the arithmetic respects it.

The visible cost is real: a recipe reports few micronutrients until every ingredient is well
described, so a chilli made from one thorough Open Food Facts hit and one hand-typed "rice, 50 kcal"
will show macros and almost nothing else. That is the honest answer to a question nobody has the data
for, and it improves on its own as the catalog fills.

The core four are exempt, because they are non-nullable columns on every item — a recipe always has
calories and macros. Rounding happens **once, at the end**: rounding each ingredient's contribution
first would drift by up to half a unit in the last place per ingredient, which over a twelve
ingredient recipe stops being a rounding artefact and becomes a number.

## Quantities are in servings

`FoodItemComponent.Quantity` counts **servings of the ingredient**, not grams. A serving is the only
unit every catalog item is guaranteed to have, and it is the unit the ingredient's own nutrition is
already expressed in, so composing is a multiplication rather than a unit conversion nobody has the
data to make. "150 g of flour" is entered as however many flour servings that comes to.

Grams would have been friendlier to type and would have required a mass per serving on every item,
which Open Food Facts supplies for some products and not others, and which "1 slice" does not have at
all. Milestone 9 is where a human-friendly quantity gets converted, because that is where there is a
model to do it.

`Yield` is the other half: components add up to a whole batch, and dividing by the yield puts the
result back on the same footing as a scanned package. **Non-null `Yield` is what makes an item a
composite** — the API refuses a yield without ingredients and ingredients without a yield, so the two
can never be out of step, and `GET /api/foods` can mark recipes in a list without loading a single
edge.

## Smaller calls

- **The same routes, not new ones.** A composite is a `FoodItem`, so it is created and corrected
  through `POST` and `PUT /api/foods`. A separate `/api/recipes` would have been a second write path
  into one table with the same visibility, barcode and validation rules to keep in step.
- **A recipe's own nutrient fields are ignored, not rejected.** They are computed, so a client that
  fetched an item, renamed it and sent the whole thing back would otherwise have to strip them out
  first. `A_recipes_own_numbers_are_ignored` pins the behaviour down.
- **A recipe may not carry a barcode.** A barcode identifies one manufacturer's product and the
  catalog treats it as a uniqueness key; a dish cooked at home has none.
- **The ingredient list is replaced wholesale by `PUT`**, exactly like the nutrient map, which is
  what makes "this was never really a recipe" a correction rather than a delete.
- **50 ingredients is a sanity limit, not a view about cooking.** Nesting is how a large recipe is
  expressed — a sauce is one ingredient of the dish — so a flat list that long is far more likely to
  be a client looping than a person.
- **`GET` returns one level.** A recipe made of recipes shows its own ingredients; opening one of
  those is another request, because the whole tree is a screen nobody has asked for.
- **No `FoodSource.Recipe`.** `Source` records where an item's *numbers* came from, and a composite's
  came from its ingredients — each of which still carries its own provenance. `Yield` already marks a
  recipe, and a new enum value would have changed the wire contract for nothing.

## Verifying a change to any of this

- **`just server::test`** — 216 tests, 16 of them `CompositeRecipeTests`. The acceptance criterion is
  `A_recipe_gets_its_nutrition_from_its_ingredients`; the two that would be easiest to break without
  noticing are `Correcting_a_shared_ingredient_recomputes_another_accounts_recipe` (the cross-account
  fan-out) and `A_nutrient_one_ingredient_is_silent_about_is_left_out`.
- **The compose stack over HTTP**, which is the tier every claim here rests on: two accounts, two
  shared ingredients, a nested recipe belonging to the *other* account, the owner correcting an
  ingredient and both levels of the member's recipe following, a logged entry that did not follow, a
  refused cycle, a refused delete and a refused share.

## Left open

- **Nothing writes a recipe yet but a person with an HTTP client.** Milestone 9's confirmation card is
  where "I made this from these" becomes possible to say, and milestone 10 is where the catalog fills
  up enough for it to be worth saying.
- **No recipe scaling and no unit conversion.** "Double the batch" is editing the yield and every
  quantity by hand.
- **A recipe still reports the ingredients it was built from, not the ingredients of those.**
  §9.10a's ingredient text derives from components rather than being stored, and that derivation is
  not written — it belongs with the milestone that introduces the field.
