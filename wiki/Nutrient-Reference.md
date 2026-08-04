# Nutrient Reference

Trackr aims to capture everything worth tracking that appears on a nutrition label — not just
calories and the carb/fat/protein split.

Full coverage is never required. Sources differ wildly in what they provide: a barcode lookup
against Open Food Facts may carry two dozen nutrients, while an AI estimate from a photo of a
home-cooked meal will reasonably have only calories and the big three.

## Design rules

These matter more than the list itself, because the list will grow.

- **Nutrients are data, not schema.** Adding selenium later is a row, not a migration. The
  set lives in a reference table with a code-side registry, so nothing is a fixed column.
- **Every nutrient carries an explicit unit.** Never assume grams — vitamins are usually mg
  or µg, and getting this wrong silently is worse than not recording the value.
- **Missing is not zero.** "Not measured" and "known to be zero" are different facts and are
  stored differently. A dashboard must render an absent micronutrient as absent, not as 0,
  or a photo estimate will look like a nutritional catastrophe.
- **Log entries store a snapshot.** The nutrients recorded against a logged item are frozen
  at the time of logging, so correcting a catalog item later never rewrites your history.

## The nutrients

Listed in **nutrition-label order**, which is the order everything renders in. `Order` values are
spaced by 10 so that adding a nutrient later slots between two numbers instead of renumbering the
set — and because the label interleaves the always-present four with their own breakdowns, the
sections below are deliberately not contiguous in that ordering.

`Key` is what the API uses: it is the key in every nutrient map a request or a response carries.

### Always present

Energy and the three macros are first-class columns rather than rows in the extensible store —
they drive every dashboard and all goal maths, and should not need a join.

| Key | Nutrient | Unit | Order |
| --- | --- | --- | --- |
| `energy_kcal` | Energy | kcal | 10 |
| `fat` | Total fat | g | 20 |
| `carbohydrate` | Total carbohydrate | g | 90 |
| `protein` | Protein | g | 130 |

Energy is stored in kilocalories only. Showing kJ instead is a display conversion (×4.184), not a
second stored value.

### Fat breakdown

| Key | Nutrient | Unit | Order |
| --- | --- | --- | --- |
| `saturated_fat` | Saturated fat | g | 30 |
| `trans_fat` | Trans fat | g | 40 |
| `monounsaturated_fat` | Monounsaturated fat | g | 50 |
| `polyunsaturated_fat` | Polyunsaturated fat | g | 60 |

### Sterols and electrolytes

| Key | Nutrient | Unit | Order |
| --- | --- | --- | --- |
| `cholesterol` | Cholesterol | mg | 70 |
| `sodium` | Sodium | mg | 80 |

### Carbohydrate breakdown

| Key | Nutrient | Unit | Order |
| --- | --- | --- | --- |
| `fibre` | Dietary fibre | g | 100 |
| `sugars` | Total sugars | g | 110 |
| `added_sugars` | Added sugars | g | 120 |

Note the British spelling in `fibre`. Open Food Facts uses `fiber` and `vitamin-pp`; those are
OFF's keys, and translating them into these is the barcode milestone's job.

### Vitamins

| Key | Nutrient | Unit | Order |
| --- | --- | --- | --- |
| `vitamin_d` | Vitamin D | µg | 140 |
| `vitamin_c` | Vitamin C | mg | 150 |
| `vitamin_a` | Vitamin A | µg | 160 |
| `vitamin_e` | Vitamin E | mg | 170 |
| `vitamin_k` | Vitamin K | µg | 180 |
| `vitamin_b1` | B1 (thiamin) | mg | 190 |
| `vitamin_b2` | B2 (riboflavin) | mg | 200 |
| `vitamin_b3` | B3 (niacin) | mg | 210 |
| `vitamin_b6` | B6 | mg | 220 |
| `vitamin_b9` | B9 (folate) | µg | 230 |
| `vitamin_b12` | B12 | µg | 240 |

The B vitamins are keyed by number rather than by name so that they sort and read together.

### Minerals

| Key | Nutrient | Unit | Order |
| --- | --- | --- | --- |
| `calcium` | Calcium | mg | 250 |
| `iron` | Iron | mg | 260 |
| `potassium` | Potassium | mg | 270 |
| `magnesium` | Magnesium | mg | 280 |
| `zinc` | Zinc | mg | 290 |

Others are added as sources turn out to provide them — that is the point of keeping the set
data-driven.

## How this maps to the database

The tables above are the `Nutrients` reference table, one row each:
`Nutrients(Key, DisplayName, Unit, Group, SortOrder, IsCore)`. Each section heading is the `Group`;
the `Order` column is `SortOrder`. Rows are inserted and updated when the server starts and are
never deleted, so amounts recorded against a nutrient stay readable even if a later version stops
listing it.

Amounts live in two join tables — `FoodItemNutrients` for catalog items and `LogItemNutrients` for
logged ones — each holding a key and an amount. **Amounts are always in the unit shown above**, and
the unit is a property of the nutrient rather than of the measurement: two sodium values recorded
in different units would make a total silently wrong.

The four always-present nutrients are the exception in one specific way. They are catalog rows
here *and* columns on the food item and the log item, but **never rows in the amount tables** — a
database check constraint forbids it. So the nutrient map in an API response contains exactly the
other 25: a client that summed the map and then added the four typed fields would otherwise count
them twice.

One more difference between the two amount tables: a catalog item's values are **per serving**,
while a logged item's are **totals for the quantity eaten**. The multiplication happens once, when
the entry is saved.

`GET /api/nutrients` returns this whole set, ordered, and is what a client caches to render display
names and units — see [API Reference](API-Reference).

### Recipes report less than you might expect, on purpose

A recipe — a catalog item assembled from other catalog items — does not store nutrition of its own.
Its values are computed from its ingredients when it is saved, and recomputed whenever one of those
ingredients is corrected, so what you read is always the sum of what is underneath it.

**A nutrient only appears on a recipe if every one of its ingredients reports it.** If the beans list
iron and the rice says nothing about iron, the chilli lists no iron at all — rather than the beans'
figure alone, which would look like the whole answer while being a fraction of it. Silence means "not
measured", not "none", and adding it up as zero would produce a confident understatement.

So a recipe built from one thorough barcode hit and one hand-typed ingredient will show calories and
macros and little else. That is the honest reading, and it fills in on its own as the ingredients
underneath it get better data. The four always-present nutrients are unaffected: every item has them,
so every recipe does.

## How the display should behave

Render in label order, and show only what the source actually provided. A confirmation card
padded with twenty "—" rows is worse than a short one: it buries the numbers that matter
under the ones nobody has.
