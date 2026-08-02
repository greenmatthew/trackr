# Milestone 6 — Data layer

The tables everything downstream writes into: the food catalog, the log, meal photos, and the
extensible nutrient store CLAUDE.md §7 asks for. Plus the CRUD API over them.

Scoped as schema and API. **There is no user interface in this milestone and it is not meant to
acquire one** — that sentence is here because "basic CRUD API for catalog and log" is the phrase
most likely to be misread later as licence to build the nutrition data-entry form §10 forbids.
Food is logged by describing it in the chat (milestone 9); these routes are what the chat, the
cascade and the stats views call.

Everything downstream was blocked on it: milestone 7 needs a `FoodItem` shape to map Open Food
Facts onto, 8 needs somewhere to put a parsed result, 9 needs write endpoints and a home for
photos, 11 aggregates the snapshots defined here.

## Nutrient storage

- **Relational, not JSONB.** §7 permits either and prefers the reference table; the deciding
  argument is milestone 11's query. Totalling a nutrient across a month is
  `SUM(Amount) WHERE NutrientKey = …` against a btree either way, but JSONB would need a GIN index
  and expression predicates to get there, and a code-side registry to mean anything at all. The
  reference table also makes "every nutrient carries an explicit unit" a foreign key rather than a
  convention.
- **`Nutrient.Key` is the primary key**, not an int surrogate. The wire format is keyed by
  `vitamin_c` anyway, so an amount row's foreign key already *is* the wire key: projecting an
  item's nutrient map needs no join to `Nutrients` at all, and that projection is the hot path for
  every catalog GET, every log GET and every future aggregate. Seeding also needs stable keys, and
  a human-chosen one is stable by construction. The cost is about 12 bytes per amount row instead
  of 2, which at household scale is nothing.
- **Composite primary keys on the amount tables**, `(FoodItemId, NutrientKey)`. The pair is the
  identity; a surrogate would make "one row per nutrient per item" a convention rather than a
  constraint.
- **`decimal` mapped `numeric(12,4)`, never `double`.** Milestone 11 sums thousands of label
  values and binary floating point accumulates representation error; §5's 4/4/9 kcal
  reconciliation is also far easier to reason about in exact arithmetic. `FoodCatalogTests` pins
  it with an amount of `12.3456`, which a `double precision` column fails.
- **`StoredPrecision` rounds before writing.** `numeric(12,4)` silently rounds what it is given, so
  a handler echoing the request's own value back would report a number the next GET disagrees
  with. This is the same class of bug as the avatar's timestamp precision, in a different currency,
  and it bites hardest in the log where `2.5 × 3.3333` routinely produces a fifth decimal.

### The four core nutrients

Energy, fat, carbohydrate and protein are **columns** on `FoodItem` and `LogItem` (§7: first-class,
no join, no parse), **also rows in `Nutrients`** flagged `IsCore`, and **never rows in the amount
tables** — enforced by a `CHECK` constraint and, more helpfully, by a 400 naming the offending key.

Duplicated storage would be the problem; duplicated *metadata* is what this avoids. Key, display
name, unit, group and sort order for all 29 live in one place, so `GET /api/nutrients` returns a
complete ordered catalog a client renders uniformly.

The consequence is a wire-format rule worth stating plainly: **the `nutrients` map contains exactly
the other 25.** A client that summed the map and then added the typed fields would double count.

**One deliberate exception to "missing is not zero":** all four columns are non-nullable. §7 calls
them always-present and §5's validator refuses to save without them, so "the source could not
determine the protein" is a cascade problem — ask the user, or refuse the save — not a schema
problem.

## The log

- **`LogItem` stores computed totals, not per-serving values.** §7 says "full *computed* nutrient
  snapshot at time of logging". It gives confirm-before-save an integrity property worth having
  (the number approved is the number stored), lets an ad-hoc item — "a bowl of chili, 480 kcal" —
  exist without inventing `serving = 1`, and makes milestone 11 a plain `SUM`. The honest cost:
  milestone 14's "change the quantity" edit must rescale every nutrient row rather than update one
  column.
  - Requests carry **per-serving** values plus a quantity, the same shape a catalog item has, and
    the server multiplies. The arithmetic is exact decimal, so what the confirmation card showed is
    what lands in the database.
- **`LogItem.FoodItemId` is nullable and `SetNull`.** Cascade would let someone erase their own
  history by tidying their catalog — precisely the failure the snapshot rule exists to prevent.
  Restrict would make any logged catalog item permanently undeletable. SetNull keeps the row and
  its numbers and loses only the back-link. The rule stated on the entity: **the foreign key is
  provenance, never a source of values — nothing may join through it to compute a total.** That is
  what stops milestone 11 from quietly reintroducing the bug, and it is what makes the shared
  catalog safe.
- **`LoggedUtc` is separate from `CreatedUtc`.** Correcting "I actually ate this at 8am" has to
  move an entry between days without lying about when the row was written.
- **One index, `(UserId, LoggedUtc)`.** Postgres scans a btree backwards, so day, week and month
  all come off it and no descending variant is needed.
- **Entry, items and image attachments are written in one request.** An entry with no items is
  meaningless, and a two-step create orphans entries whenever the second call fails.

## The shared catalog

This **amends CLAUDE.md §7**, which described a `FoodItem` as having an owning user. A household
sharing one server should scan a can of beans once between them, so `FoodItem.UserId` is nullable
and null means global.

- **Visibility is chosen at creation, not derived from `Source`.** Deriving it was considered and
  rejected: it would forbid sharing a hand-typed staple and force sharing a barcode scan of
  something private.
- **Promotion is one-way** (`POST /api/foods/{id}/share`). There is no unshare, because by the time
  anyone regrets it another account may already be logging the item.
- **Two partial unique indexes on `Barcode`**: unique among global items
  (`WHERE "UserId" IS NULL`), and unique per account among personal ones (`WHERE "UserId" IS NOT
  NULL`). Together they are what let a household scan a product once while an individual keeps a
  private override.
- **Delete behaviours differ on the same column, on purpose.** `UserId` cascades, so deleting an
  account takes its personal items (no cross-account value, same reasoning as `UserAvatar`) while
  global items have no owner to cascade from and survive — which is the entire point of sharing
  them. `UpdatedByUserId` is SetNull, because attribution must never block an account deletion.
- **404, not 403, for another account's personal item**, so the route cannot be used to probe which
  ids exist. **403 for deleting a global item** is deliberate and not a contradiction: a shared
  item's existence is not secret, so hiding it would only confuse. Removing one is left to a future
  admin surface.

### Wiki-style edits

Any account may correct a global item, and every write stamps `UpdatedByUserId` / `UpdatedUtc`.

The user's call, made knowing the trade: one person's mistake reaches everyone's *future* logs
until somebody corrects it. Two things bound the damage — the snapshot rule means **no already
logged number ever changes**, and every change is attributable.

Rejected: **fork-on-edit** (an account editing a shared item silently gets its own copy), which
gives the household five diverging copies of the same product and quietly defeats sharing; and a
full **`FoodItemRevision` history**, which is the right answer the day a mistake does real damage
and is speculative until then.

## Meal images

- **Stored per-user, never shared**, unlike catalog items. Another account's image is a 404.
- **`LogEntryId` is nullable, and that is what makes milestone 9 cheap.** The chat flow is upload →
  cascade → confirm, so the photo must exist server-side before the entry does — the model can be
  retried without a re-upload, and an abandoned confirmation leaves no orphan entry. `POST /api/log`
  adopts images by id. Deleting an entry cascades to its photos; cascade fires only for non-null
  foreign keys, so unattached ones are untouched.
  - A `PUT` that drops a photo **detaches** rather than deletes it. An item can be recreated from a
    request; a photograph cannot, and a client that sent no image ids because it does not know
    about images should not be able to destroy them. Milestone 14 sweeps what ends up attached to
    nothing.
- **Full resolution, no downscaling, ever.** Re-running a better model over an old photo must stay
  possible. The phone encodes WebP q90 (Android's `Bitmap.Compress`, no new package), which is one
  extra lossy generation over the camera's JPEG in exchange for roughly 30% less storage.
- **The server never decodes image bytes.** It has no image library and this milestone deliberately
  does not give it one — decoders are a well-known remote-code-execution and denial-of-service
  surface, and the avatar path already set the precedent of checking the declared type and the
  length and nothing more. Server-side re-encoding was rejected mainly for this reason.
  Consequence: "stored images are WebP" is a **convention, not an invariant** — JPEG and PNG are
  accepted and stored as they arrive. Milestone 7 introduces decoding anyway for barcode reading,
  which is the moment normalising on ingest becomes cheap if it is ever wanted.
- **`bytea` in Postgres rather than a volume**, so `pg_dump` remains the single backup artifact:
  no second consistency window, no orphaned files, and `wiki/Backup-and-Restore.md` needed a
  warning rather than a rewrite. The cost, recorded honestly: about 5.5 GB a year for five
  photographed meals a day. If that becomes painful, moving the blobs out is contained, because
  every read already goes through one endpoint.

## Seeding

**An idempotent startup seeder, not `HasData`.** `HasData` was tempting since migrations already
run at startup, and it loses on three counts. The wiki promises that adding selenium later is "a
row, not a migration" — with `HasData` it is literally a migration file. The seed set will churn
more than the schema, because milestones 7 and 8 will meet nutrients this list lacks. And
decisively: **removing a `HasData` row generates `DELETE FROM "Nutrients"`**, which the amount
tables' `Restrict` foreign key turns into a runtime failure on any database where somebody has
measured that nutrient — a crash in the path that runs before the app serves. A seeder that only
inserts and updates cannot produce that failure.

A key in the database that the code no longer defines is logged, never deleted: it means a
downgrade has been deployed, and their data is worth more than a tidy table.

**Label order, not the wiki's old grouping.** Two changes to the page came with it, both
intentional: **potassium moved from "Sterols and electrolytes" to "Minerals"** (it is a mineral,
and sits in the mineral block of an FDA label), and the sections were reordered to read as a label
does. `Group` and `SortOrder` are independent — the label interleaves the core four with their
breakdowns, so groups are deliberately not contiguous in the ordering.

`NutrientUnit` is a **closed** enum. Adding a nutrient must be a data change; adding a *unit*
cannot be, because a unit code is meaningless without a conversion factor that lives in code. The
unit sits on the `Nutrient` row and never on an amount: two sodium values in different units would
make a `SUM` silently wrong, which the wiki names as worse than not recording the value.

## Time

`Time/DayBoundary` exists now rather than in milestone 11, per §9.13. **Every method takes a
`TrackrUser` even though it currently ignores one** — that is the whole point: when milestone 13
adds a per-user zone, the change is one line inside `ZoneFor` and every call site already passes
what it needs. The zone is a property of the user rather than of the request, because the *server*
aggregates and two devices in different places must not disagree about which day a meal belongs to.

UTC is hard-coded with **no configuration knob**. §9.13 permits "UTC or a single configured server
zone", and a `TRACKR_TIMEZONE` would oblige `wiki/Configuration.md` and `docker/.env.example` —
both enforced by `Trackr.Docs.Tests` — for a setting nothing can visibly use until the stats views
exist.

Two details live there because only that helper can get them wrong: **half-open intervals, always
`[from, to)`** (an inclusive end at `23:59:59.999999` drops the last microsecond, and Postgres
keeps microseconds), and the **DST guard** for a local midnight that does not exist, which is real
in Brazil, Chile and Cuba. Neither can fire while the zone is UTC, which is exactly why they are
handled now.

`Time/Timestamps` is `AccountEndpoints`' private `ToStorablePrecision` hoisted out. `LoggedUtc` has
the identical exposure the avatar marker had — a client caches what a POST returned and a later GET
disagrees in the last digit — and the existing avatar test covered the refactor for free.

## Deferred by design: composite / recipe items

Not built, and recorded so the later slice is additive: `FoodItemComponent` needs no backfill and
changes no existing table.

Barcode plus Open Food Facts covers packaged food and fails completely on home cooking, which is
the case the AI fallback handles worst — so recipes are a real feature rather than a nicety.

A composite is **still a `FoodItem`** with the same nutrient columns, plus `FoodItemComponent`
(`ParentFoodItemId`, `ChildFoodItemId`, `Quantity`, composite key on the pair) and `FoodItem.Yield`
so per-serving values stay comparable with everything else. **Nutrition is materialized on write**
— `Σ (child × quantity) ÷ yield` — which keeps the log, the aggregates and the whole cascade
ignorant that composites exist; recursive aggregation at read time would put a tree walk in front
of every catalog list and every dashboard.

Two things make it a slice rather than an add-on, and are why it was not folded in here: a **cycle
check** on write, and a **fan-out recompute** when an ingredient changes, since a shared ingredient
may appear in recipes belonging to several accounts. Sequenced after milestone 7, when the global
catalog holds real OFF-sourced ingredients worth composing.

## Found by running it

- **`Invite`'s `Restrict` foreign keys block account deletion outright.** Deleting an account that
  redeemed an invite — or minted one — fails with `23001`, because 02-auth.md deliberately chose
  never to erase the record of who invited whom. Nothing in this milestone depends on it, and
  `SharedCatalogTests` clears the invite rows to get at the catalog cascade it is actually testing.
  **Milestone 13 owns the decision**: anonymise the invite, or cascade it, or refuse to delete.
- **`just server::build` was broken** by the .NET 10 SDK, which rejects several projects in one
  `dotnet build` (`MSB1008: Only one project can be specified`). Split into one invocation per
  project; `Trackr.Shared` builds as a dependency of both. Pre-existing, unrelated to this
  milestone, and in the verification path for every step of it.

## What was deliberately not done

- **No aggregation endpoints.** `/api/stats/today` and the week/month rollups are milestone 11; the
  raw entries are already fetchable by date range, which is what those views will build on.
- **No upsert-from-cascade and no "log this again".** Milestone 10.
- **No paging.** `GET /api/foods` caps at 200 items. A household's catalog is hundreds, not
  millions, and paging is machinery to maintain for a scrollbar nobody reaches.
- **No rate limiting on these routes.** The two policies exist for §8.3's credential attacks; a
  limiter here would only throttle the legitimate owner of the data.
- **`FoodItemResponse.IsEditable` is always true today**, because what is visible is "mine or
  global" and what is editable is the same set. It ships anyway so the rule lives on the server: if
  a read-only form of sharing ever appears, the app renders the new answer without a new APK.

## Verifying a change to any of this

- **`just server::test`** — 104 tests over a real Postgres via Testcontainers, including the
  milestone's acceptance criterion (`A_food_item_keeps_every_nutrient_it_was_given`: 25 distinct
  amounts, exact round-trip), the snapshot rule under wiki-style edits, both partial unique
  indexes, the half-open day boundary, and the image projection rule.
- **`just docs::test`** and the `NutrientReferenceTests` in the API suite keep
  `wiki/Nutrient-Reference.md` and `wiki/API-Reference.md` from drifting.
- **The compose stack over HTTP** is the tier every claim in this record rests on: two accounts, a
  25-nutrient item shared and corrected, a photo uploaded and compared byte for byte, 2.5 servings
  logged, an account deleted. Not a `WebApplicationFactory`.
