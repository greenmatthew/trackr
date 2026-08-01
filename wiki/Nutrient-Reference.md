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

## Always present

Calories and the three macros are first-class columns rather than rows in the extensible
store — they drive every dashboard and all goal maths, and should not need a join.

| Nutrient | Unit |
| --- | --- |
| Energy | kcal (kJ optional) |
| Protein | g |
| Total carbohydrate | g |
| Total fat | g |

## Captured when available

### Fat breakdown

| Nutrient | Unit |
| --- | --- |
| Saturated fat | g |
| Trans fat | g |
| Monounsaturated fat | g |
| Polyunsaturated fat | g |

### Carbohydrate breakdown

| Nutrient | Unit |
| --- | --- |
| Dietary fibre | g |
| Total sugars | g |
| Added sugars | g |

### Sterols and electrolytes

| Nutrient | Unit |
| --- | --- |
| Cholesterol | mg |
| Sodium | mg |
| Potassium | mg |

### Vitamins

| Nutrient | Unit |
| --- | --- |
| Vitamin A | µg |
| Vitamin C | mg |
| Vitamin D | µg |
| Vitamin E | mg |
| Vitamin K | µg |
| B1 (thiamin) | mg |
| B2 (riboflavin) | mg |
| B3 (niacin) | mg |
| B6 | mg |
| B9 (folate) | µg |
| B12 | µg |

### Minerals

| Nutrient | Unit |
| --- | --- |
| Calcium | mg |
| Iron | mg |
| Magnesium | mg |
| Zinc | mg |

Others are added as sources turn out to provide them — that is the point of keeping the set
data-driven.

## How the display should behave

Render in label order, and show only what the source actually provided. A confirmation card
padded with twenty "—" rows is worse than a short one: it buries the numbers that matter
under the ones nobody has.
