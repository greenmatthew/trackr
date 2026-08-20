# Milestone 10 — Catalog growth

CLAUDE.md §9.10 asks for two things: "upsert items from OFF/AI into the catalog; let the user
pick from previously logged items for fast re-logging." Both are built. What is worth recording
is how much of each was deliberately *not* built, because the catalog is shared between
accounts, editable by any of them and in one direction permanent — so an automatic writer into
it is a thing to keep on a short leash.

Three of the choices below reverse an answer given when this milestone was planned. They are
recorded as reversals rather than quietly corrected, because in each case the argument that
changed them is an argument somebody could reasonably make again.

## A barcode is the only key

An item with a barcode is filed. An item without one is logged exactly as before and files
nothing — which is most of what the model returns.

The planned rule was to match model-read items on name, brand and serving unit and give them
personal catalog rows. That is wrong in **both** directions, which is what settled it:

- *If it matches* — "chicken breast" at 165 kcal and "chicken breast" at 195 kcal are two
  readings of two different foods. Attaching one to the other's row is the silent wrong number
  §2 names as the failure to avoid, and it is structurally the same mistake as the UPC-E false
  positives in [08-barcode-off.md](08-barcode-off.md): a cheap-looking accuracy improvement
  that manufactures confident wrong answers.
- *If it does not match* — a new row per meal. Not the per-*user* duplicate §7 forbids, but a
  per-*meal* one, and the catalog fills with fifty near-identical "Toast" rows.

The positive case is stronger than either. **What is the catalog for?** Both
[08-barcode-off.md](08-barcode-off.md) and [10-ollama.md](10-ollama.md) close by naming
milestone 10 as the real fix for re-looking-up a product, and that value belongs entirely to
barcode items: a barcode already in the catalog is a product you can find again. A model's read
of "two boiled eggs" has no key to find it by, so a row for it buys nothing.

`MealAnalysisItem.Barcode` is set only in `MealCascade.FromProduct` — a barcode that decoded
*and* that Open Food Facts answered for. `FromModel` sets it null. So the rule needed no new
plumbing at all; the cascade had already sorted the items into the two piles.

The guard beside it: an item is only filed if it also has a serving size and unit.
`FoodItem.ServingSize` and `ServingUnit` are not nullable, and inventing "one serving" would
record a measurement nobody took.

## Everything filed is personal. Nothing here is ever global

The planned rule was that a barcode item, being an objective packaged product, should become a
global row — the household scans a can of beans once between them, which is
[07-data-layer.md](07-data-layer.md)'s own argument for a shared catalog.

Two facts beat it:

- **A global item cannot be deleted.** `FoodEndpoints` answers 403, and 07-data-layer calls
  removal "a future admin surface". Automatic promotion is therefore the one irreversible thing
  the catalog does, performed without being asked. A partial match where the model guessed the
  fat becomes a household fixture nobody can remove — only correct.
- **07-data-layer already considered deriving visibility from source, and rejected it**: *"it
  would forbid sharing a hand-typed staple and force sharing a barcode scan of something
  private."* Auto-promotion is that rejected rule, reintroduced under another name. It is also,
  for a health tracker, a broadcast of what each member of a household eats.

§7's "do not build per-user duplicates of a shared product" still holds, because **a hit on an
existing global row inserts nothing**. Once anybody shares a product deliberately, the household
stops duplicating it. And a personal row remains deletable, which is what being personal buys.

Sharing stays `POST /api/foods/{id}/share`: a deliberate act, which is what an irreversible one
should be.

## A hit links and writes nothing

Insert if absent, link if present. `UpdatedUtc` and `UpdatedByUserId` are stamped only on
insert. The planned rule was to fill in nutrients the row was missing; even that is gone.

Tapping **Save** consents to logging a meal, not to editing the household's catalog. The
corrections a card collects are as often about the portion as about the label — "I had half of
it", a rounded figure — and writing those onto a stored product degrades it. The edit path
exists and is deliberate: `PUT /api/foods/{id}`, attributed, which is the trade 07-data-layer
already made when it accepted that one person's mistake reaches everyone's future logs.

Mechanically it also avoids a trap: `ReplaceFoodAsync` takes a transaction, an advisory lock and
a cross-account `RecomputeAncestorsAsync`. An update-on-confirm would fire a recipe recompute
across other people's accounts on every meal.

**A personal row beats a shared one** when both carry the barcode. It exists precisely because
its owner disagreed with the shared figures, and preferring the shared row would overrule them
without saying so. `SharedCatalogTests` already proves the pair can coexist.

## Where the write happens, and what happens when it fails

Server-side, inside `POST /api/log`. A client doing `POST /api/foods` then `POST /api/log` has a
failure with no recovery: the catalog row lands, the log post fails, and the retry is refused
with a 409 by our own half-finished write — so the user cannot save the meal they already
approved. Deciding what counts as the same food is also cascade-adjacent logic, and §10 keeps
that server-side where it changes without shipping an APK.

The failure rule is the part worth reading. `CatalogUpsert` saves its own rows and
`LogEndpoints` swallows anything that goes wrong: **the meal is what the user confirmed and the
catalog row is a convenience**, so losing the first to the second would be the wrong trade. The
reverse costs nothing — a row for a meal that then failed to save is *found and linked* by the
retry rather than collided with, because a hit links.

The swallow detaches what it added. Without that, the entry's own `SaveChanges` would try to
insert the same row again and turn a swallowed failure into a lost meal.

Two devices on one account confirming the same product at the same moment is settled by the
partial unique index, which is what actually enforces one row per account per barcode. The loser
drops what it built and re-reads what the winner wrote — the same answer, reached without the
meal noticing.

**No migration.** `FoodItemId`, `FoodItem.Barcode` and both partial unique indexes were already
there; `TrackrDbContext` even carries a comment saying the `(UserId, Name)` index was for this
milestone. The only wire change is `SaveLogItemRequest.Barcode`, and it is the one field that
could be added without touching `MealAnalysisContractTests` — `MealAnalysisItem.Barcode` is
already `string?` on both sides. `Source` was deliberately not added: it would fail that test's
type check (`FoodSource` vs `AnalyzedItemSource`) and would let a client *assert* provenance.
The server derives `FoodSource.Off`, because only stage one of the cascade can produce a
barcode — true even where the model filled gaps, since the identity still came off the label.

## Re-logging reads the log, not the catalog

`GET /api/log/recent`, surfaced as a third choice behind the chat's `+` button.

The planned design was a text match against the catalog *before* the model, skipping a
two-minute inference. It was rejected on two grounds, and the first is the serious one:

- **A match reported as `Database` is the most trusted badge the card has**, and the stage that
  would have caught the error is exactly the one that did not run. That is milestone 7's lesson
  in a new place.
- After the barcode-only rule above, the catalog holds packaged products and nothing else. "I
  had that again" is overwhelmingly home cooking, which never carries a barcode — so the text
  match would be searching the one table that cannot answer.

The log can. It holds every item ever confirmed, including everything the model read off a
plate, and it is the user's own history, so no inference is involved anywhere: they tap a row
they logged before and still see a card before anything is saved.

**The distinction worth keeping:** grouping duplicates in a list somebody chooses from is safe;
matching text to decide what a meal *was* is not. The worst a wrong group can do is offer one
row where it should have offered two.

### What it returns, and the arithmetic underneath

`RecentItemResponse` carries a `MealAnalysisItem`. The chat already renders one, corrects one
and confirms one into `POST /api/log`, so re-logging costs no rendering code and — the real
point — no mapping layer between what was stored and what is offered back, which is the same
instinct that keeps `MealAnalysisItem` and `SaveLogItemRequest` field-identical.

`LogItem` stores **totals**; a save request wants **per serving**. The values are recovered by
dividing by the stored quantity. Two alternatives were rejected: reading them from the linked
`FoodItem` is forbidden outright (nothing joins through `FoodItemId` for a number — that rule is
what stops a later correction from rewriting a confirmed log), and offering the whole portion as
`Quantity = 1` would destroy the serving semantics that make the card readable.

The cost, stated rather than hidden: the quotient is rounded to what the column keeps, so three
servings of 100 kcal come back as 99.9999. A tenth of a kilocalorie across a meal, and the
standing price of storing totals.

Two fields are set deliberately rather than copied:

- **`Source = PreviouslyLogged`**, a new enum member. None of the three existing values is true,
  and `Database` would badge it with trust it has not earned — these numbers are as good as
  whatever produced them the first time, which may well have been the model. What is true is
  that a person has already looked at them. `ConfirmableItem.SourceDescription` ends in a
  catch-all reading "estimated", so a new member would otherwise have become a silent lie; all
  four arms are now pinned by a theory.
- **`MealImageId = null`**, and this is load-bearing. The photo belongs to the entry it was taken
  for, and `ValidateImagesAsync` refuses one already attached elsewhere — so carrying it forward
  would make every re-log of a photographed meal a 400.

The **barcode is** carried, through the catalog link, so a re-logged packaged product files
itself away exactly as the first one did. That is provenance rather than a number, which is what
`LogItem.FoodItemId` is for.

A bounded window — the 200 newest log items, grouped, at most 20 returned — rather than a scan
of all history: one index scan, one round trip, and a cost that does not grow with how long the
log has been kept. Something eaten once six months ago will not appear, which for this question
is the right answer rather than a limitation. No `search` parameter: 20 rows is a scroll, and a
search box in the chat is §10's forbidden dropdown wearing a hat.

## One bug fixed in passing

`NormaliseBarcode` checked that a barcode was digits and never that it would fit its
`varchar(32)` column. The `StringLength` attributes on these DTOs are documentation — nothing
calls `AddValidation` — so a forty-digit barcode passed validation and came back from Postgres
as a 500. Pre-existing, in the catalog rather than in this milestone, and committed separately.

## Verified

Tier 1: 324 API tests, 151 view-model tests, 104 documentation tests.

Tier 2 (emulator, API 36) against the compose stack, with Postgres inspected directly:

- A text-only meal saved, and the catalog gained **nothing**; the log row's `FoodItemId` is null.
- `+` → **Had it again** listed both foods from an earlier entry with "150 kcal · 8 days ago".
- Tapping one produced a card sourced *logged before*, which saved as a second entry with the
  numbers the card showed.
- **A bug the emulator found that no test could:** the first attempt reported "Could not reach
  the server". The container was running the image built before any of this — a reminder that
  `just server::up` builds once and `./scripts/server.sh rebuild backend` is what a code change
  needs. Worth knowing, because the client's honest failure message reads exactly like a bug in
  the endpoint.

## Left open

- **No lookup cache off the new rows.** 08-barcode-off.md names milestone 10 as "the real fix -
  a barcode already in the catalog needs no lookup at all", and this deliberately does not do it.
  Skipping the lookup would make a stale or mistyped row the permanent answer for that product,
  badged `Database`, on the one branch that never sends the photo to the model — to save one
  small HTTP GET. It needs a staleness rule before it is worth having.
- **`PUT /api/log/{id}` files nothing.** Editing history is milestone 14, and a second silent
  writer with no user-facing trigger is how a table fills with rows nobody chose.
- **Ingredients are still not requested from Open Food Facts** — milestone 10a.
- **No physical-phone run.** Every claim here is tier 2.
