---
id: 25
title: Make a recipe from the chat
milestone: M7a
record: -
depends_on: []
---

## Goal

Somebody can say "I made this from these" in the chat and save a recipe they can log again. Today
only an HTTP client can create one.

## Done when

- [ ] a recipe can be made from the chat and logged again from the `+` button
- [ ] `just test` is green
- [ ] seen working on the emulator (`Verified: emulator`)
- [ ] the decision record and the wiki still match the code

## Notes

Milestone 7a's build-order entry (task 009): "**Left open:** nothing writes a recipe yet but a
person with an HTTP client — milestone 9 is where saying 'I made this from these' becomes possible".
Neither milestone 9 nor milestone 10 built it, and nothing in `Trackr.Mobile.Core` references the
recipe contracts.

Settled already, and not to be re-decided here:
- **The chat is the interface**, not a form (CLAUDE.md §10).
- **Quantities are servings of the ingredient**, never grams
  ([09-composites.md](../../docs/decisions/09-composites.md)).
- **What the chat files is personal**
  ([12-catalog-growth.md](../../docs/decisions/12-catalog-growth.md)), and a global recipe may hold
  only global ingredients.
- **A recipe derives its ingredients and unions its allergens**
  ([13-ingredients.md](../../docs/decisions/13-ingredients.md)).
