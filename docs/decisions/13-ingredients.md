# Milestone 10a — Ingredients

CLAUDE.md §9.10a describes three parts and says the value drops off sharply after the second.
Parts one and two are built; part three is not, and the section already argues that it may never
be worth building. Nothing here changes that judgement.

## Raw text, not the nutrient machinery

`FoodItem.IngredientsText` is one nullable column. §7 sets this out before the schema exists and
it holds up in the writing: a nutrient is an entry in a fixed, server-owned vocabulary with a
unit and a sort order, and the relational store exists to give it those. An ingredient list is
open-ended prose belonging to one brand's formulation. It has no unit, no order worth
maintaining and no vocabulary this server could own.

## Allergens and diet flags are a `text[]`, and that is the same argument from the other side

The structured subset is worth storing structured, because it is a short closed vocabulary and
"does this contain nuts" is a question with consequences. But it is stored as a Postgres array
rather than a join table, for exactly the reason the nutrients are *not*: an allergen tag carries
no unit, no display name and no order, and the list is five entries long. A join table here would
be machinery with nothing to hold.

They are **Open Food Facts' own tags** — `en:milk`, `en:palm-oil` — rather than a local
vocabulary. The taxonomy is maintained by somebody else and a translation into Trackr's own set
would be a mapping to keep correct forever. The consequence is accepted deliberately: nothing
validates a tag against a vocabulary, because OFF's grows without asking and a server that
dropped tags it had not heard of would discard a real allergen warning the first time one was
added.

**Empty means the source said nothing. It never means "contains no allergens."** The same
distinction the nutrient store draws, and the reason the card shows nothing at all rather than a
reassuring blank line.

GIN indexes on both arrays, which is what makes "everything containing palm oil" an index scan.

## An ingredient list is only true of one product

The constraint §9.10a asks to be settled before the schema. It is enforced at the API boundary:

- **A brand or a barcode is required.** "Chicken breast" has no formulation to describe, and the
  catalog is shared and editable by every account — so one person's guess at what a generic food
  contains would become everyone's.
- **A recipe is refused outright**, because it has a real answer already.

### What a recipe derives, and the asymmetry in it

A composite materialises its ingredients on write beside its nutrition, so nothing downstream
learns that composites exist — the rule [09-composites.md](09-composites.md) established.

**Allergens union where nutrients intersect**, and the asymmetry is deliberate rather than an
inconsistency. A nutrient missing from one ingredient means "not measured", so summing it as zero
understates a real number. An allergen present in one ingredient is present in the dish whatever
the others say. The safe direction for "how much iron" is to say less; the safe direction for
"does this contain nuts" is to say more.

**Diet flags are left empty on a recipe.** "Contains palm oil" unions safely and "vegan" does
not: one non-vegan ingredient makes the dish non-vegan, and a union would report `en:vegan` and
`en:non-vegan` at once. Doing it properly needs each tag's polarity, which is part three's
problem.

The text is the ingredients' *names* — "Butter, Flour" — not their own small print. Concatenating
a dozen paragraphs produces something nobody reads.

## Open Food Facts supplies it, and the model is not asked

For a barcode hit this was a field to map rather than a feature to build. Four fields added to
the `Fields` constant milestone 7 deliberately kept small; they cost a few hundred bytes.

`ingredients_text_en` first, then `ingredients_text` in whatever language the contributor used.
A French ingredient list is a worse answer to "what is in this" than an English one and a far
better answer than nothing — and the alternative, asking the model to read a label the database
already has, spends a photograph's worth of tokens to get the same words back.

`ingredients_analysis_tags` are carried **including the unknowns**. `en:vegan-status-unknown`
says nobody could tell, and dropping it turns that into silence, which reads as a stronger
statement than it is.

The list travels on the analysed item and is filed onto the catalog row a confirmation creates —
always the database's, on a partial match too, because what a product contains is a fact about a
formulation and OFF has the manufacturer's own words for it. **Nothing is stored on the log
row:** what a product contained is not a fact about a meal.

### The model reads one only where the database left a gap

§9.10a's other half — "where OFF has nothing, the model reads it off the photo" — is built, and
**much more narrowly than that sentence suggests**. The scope narrowed itself, out of milestone
10's rules rather than out of caution:

> A list is filed onto a catalog row. A row is only created for an item with a **barcode**. A
> barcode item came from Open Food Facts.

So the only gap a model can fill is a **partial match with no ingredients** — a product OFF knew,
with figures, and nothing about what is in it. And a partial match's photograph is already being
sent, which makes this cost nothing except the reply it lengthens. Asking on every request would
spend output tokens on a paragraph with nowhere to go.

The field is therefore **absent from the schema and the instruction absent from the prompt** unless
the request contains such a product. A full match never takes one either: its photograph was
deliberately withheld, so anything the model offers for it is about some other food it was told
about in text.

**Bounded at 600 characters against the column's 4 000, and the bound is about the output budget
rather than the column.** An ingredient paragraph is the longest thing a reply can hold, and a
reply that runs out of room fails *entirely* — `done_reason: length` loses the calories along with
the list.

Which makes the reader's rule for this one field the **opposite** of every other text field it
handles: an over-long list is **dropped, never truncated**. A name cut short is still recognisably
that food; an ingredient list cut short is a claim that the food contains only its first few
ingredients. The prompt tells the model the same thing — copy it or leave it out, never abridge —
which a small model will not always obey, but the alternative is that it never does.

## Verified

Tier 1: 360 API tests, 161 view-model tests, 104 documentation tests.

Tier 2 (emulator, API 36): a real barcode photograph through the chat, with the allergen line and
the ingredient list drawn on the confirmation card.

## Left open

- **A model-read list is only ever obtained for a partial barcode match.** A model-only item
  (a plate of food) carries one to the card if the model volunteers it, but it is filed nowhere,
  because nothing creates a catalog row for it. That is the rule working rather than a gap.
- **Part three, per-ingredient rows**, which §9.10a already argues may never be worth building.
  Nothing here forecloses it: OFF's parsed `ingredients` array and its canonical taxonomy are
  still there to be requested if a real question ever needs them.
- **Nothing queries the arrays yet.** The GIN indexes and the tags on the catalog list are what a
  "contains palm oil" filter would be built from; no endpoint offers one, because no screen asks.
