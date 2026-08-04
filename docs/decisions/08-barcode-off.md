# Milestone 7 — Barcode and Open Food Facts

Stages one and two of the cascade CLAUDE.md §5 describes: read a barcode out of an attached photo,
and ask Open Food Facts what it is. The model is milestone 8; nothing here talks to it, and nothing
here writes to the database.

The shape of the milestone is two swappable services (`IBarcodeDecoder`, `IProductLookup`), a pure
mapper between Open Food Facts' vocabulary and Trackr's, and two read-only routes so the thing can
actually be run rather than only unit-tested.

## Server-side decoding, which §9 asked to have recorded

**Decoding happens on the server.** §3 defaulted to this and asked for the choice to be recorded
once made, so: the deciding argument is that the backend needs the code anyway. Images can reach it
without a decode — a share target, a future non-MAUI client, a re-run of the cascade over a photo
already stored — so on-device decoding could only ever be an *addition*, never a replacement. Two
implementations of one thing, one of which needs an APK release to fix, is the wrong trade for
saving one HTTP round trip on a photo that is being uploaded regardless.

The user attaches stills rather than live-scanning, which is what makes this cheap. A live camera
preview would push hard the other way — a viewfinder that decodes on-device at 30 fps is a
completely different design — and that is explicitly not the interaction §1 describes.

On-device decoding remains available later as an optimisation. It would not change this code.

## Libraries, and one licence trap

**ZXing.Net (Apache-2.0) for decoding, SkiaSharp (MIT) for the image codec underneath.** Both are
AGPL-compatible per §10.

**`SixLabors.ImageSharp` is the obvious alternative and cannot be used.** Since 3.0 it ships under
the Six Labors Split License, which is source-available rather than open source, and §10 rules out
source-available dependencies. Worth knowing before reaching for it: it is still the most commonly
recommended .NET imaging package, and its 2.x releases *were* Apache-2.0, so a search will turn up
plenty of advice that was true once.

**SkiaSharp needs a native library, and the API image is Alpine.** `aspnet:10.0-alpine` is musl, not
glibc, so `SkiaSharp.NativeAssets.Linux.NoDependencies` is referenced explicitly — it carries a
`linux-musl-x64` build, and the "NoDependencies" variant needs no fontconfig, which suits a decoder
that never draws text. That package is referenced by no code at all, which makes it exactly the kind
of thing a later tidy-up removes; both the csproj and `Directory.Packages.props` say so in place.
The failure mode is a green build and a `DllNotFoundException` on the first photo.

SkiaSharp is pinned to 3.119.1 rather than the newest release, because that is the major the ZXing
binding is built against.

## What the data actually looks like

Everything below was verified against the live API before the mapper was written, not inferred from
documentation. Each item is something that would have produced silently wrong numbers.

- **`_100g` and `_serving` values are normalised to grams**, whatever unit the label used;
  `energy-kcal_*` is in kilocalories. The sibling `_unit` and `_value` fields carry the label's own
  units and are **ignored**. A US tin of Pringles declares iron in milligrams and comes back as
  `iron_100g: 0.00107` with `iron_unit: "g"` — reading `_value` would put milligrams in a grams
  column and be a thousandfold wrong with no symptom.
- **`nutrition_data_per` can be `100ml`**, and the keys are *still* named `_100g`. Diet Coke reports
  `nutrition_data_per: "100ml"` alongside `carbohydrates_100g`. Deriving the key name from that field
  would find nothing for every liquid in the database. It is read for the unit only.
- **US labels add a second `carbohydrates-total`** that disagrees with `carbohydrates` — 57.1 g
  against 50 g on the same tin. `carbohydrates` wins because it is the key populated worldwide;
  `carbohydrates-total` is a fallback so US-only entries are not downgraded to partial matches.
- **`status` is an integer**, 1 or 0, in an API called v2. An unknown barcode is sometimes a 404 and
  sometimes a 200 with `status: 0`; both are treated as not-found.
- **Numbers arrive as JSON strings** on some products. This is a crowd-edited database with two
  decades of history, and losing a whole product over one quoted field would be the wrong response.
- **Blank strings mean absent** (`"quantity": ""`), not empty.
- **`nutriments_estimated` arrives whether or not it is requested, and is never read.** Those figures
  are inferred from the ingredient list rather than read off a label. Storing them as though a source
  had measured them is precisely the silent-wrong-number failure §2 names — and estimating is the
  model's job, where the estimate arrives flagged as one.

## Mapping decisions

- **The serving basis is chosen once per product, not per nutrient.** Mixing a per-serving protein
  with a per-100 g fat would produce a card whose numbers each came from somewhere defensible and
  whose totals were nonsense. `ServingBasis` records which of the three ways it went, because the
  assumption is invisible in the numbers: `LabelServing`, `ScaledFromReferenceQuantity`, or
  `ReferenceQuantityAsServing` when there is no serving size anywhere and one serving is taken to be
  100 g. That last case raises a warning, per §5.
- **A nutrient stored only under the *other* suffix is converted rather than dropped**, when a factor
  exists to do it honestly. Without that, a product whose macros came per serving would silently lose
  a micronutrient OFF happened to store per 100 g.
- **Per-serving figures are judged by the core stems, not by "any key ending in `_serving`".** OFF
  attaches `nova-group_serving` — a processing score, not a measurement — to products with no
  per-serving nutrition at all, which would pick the per-serving basis and then read nothing but
  nulls.
- **`ProductDraft` has nullable core four, unlike `SaveFoodItemRequest`.** Non-nullable is right for
  something being written and wrong for something being reported: a product whose protein OFF does
  not know would arrive as `0`, and "zero protein" is a claim nobody made. The distinction §7 insists
  on — known-zero versus not-measured — is easiest to lose exactly here, which is why a diet drink's
  genuine zeroes have their own test.
- **A missing name makes a match partial**, alongside the macro checks §5 names. A nameless product
  gives the confirmation card nothing to show and the catalog nothing to file it under.
- **Bad values become null rather than being clamped.** A negative amount, an out-of-range number,
  something unparseable: all "not measured". Zero would assert known-to-be-zero, which is a different
  claim. Rounding to the stored four decimal places can turn a trace into `0.0000`, which is accepted
  — no label distinguishes a tenth of a microgram either.

## No retry, on purpose

The client has **no retry policy and no resilience handler**, which for an outbound HTTP call is
unusual enough to write down. Three reasons:

1. **The cascade already has a fallback, and it is a good one.** A failed lookup sends the photo to
   the model. That is a worse answer than a label hit and a much better one than a spinner.
2. **A 429 is a request to stop.** Open Food Facts is free and volunteer-run. Retrying into a rate
   limit is how a client gets blocked, and the number-one thing their API guidance asks of callers is
   not to hammer them.
3. **`AddStandardResilienceHandler` is not free.** Its timeout and circuit-breaker options validate
   against each other at startup, so a misconfigured combination fails the process rather than one
   request — machinery to maintain in exchange for a retry that is not wanted.

The timeout is short (10 s, configurable) for the same reason: someone is watching a chat message
spin, and falling through to the model is an acceptable outcome.

A descriptive `User-Agent` is sent — app name, version, contact — which §9 calls out for this
milestone. `TRACKR_OFF_CONTACT_EMAIL` is optional but encouraged; the header identifies Trackr
either way.

## Two things beyond the original scope

- **A `TRACKR_OFF_ENABLED=false` switch.** The barcode number leaving the server is the single
  exception to §2's "nothing leaves this machine", and a self-hoster who wants literally no outbound
  traffic should be able to say so in configuration rather than at their firewall. Off, every lookup
  reports not-found and the cascade falls through to the model.
- **A rate limit on the lookup routes** (`TRACKR_LOOKUP_RATE_LIMIT`, 60/minute). The other two
  policies protect this server's accounts; this one protects somebody else's service from a loop in
  ours. It sits well above what a person logging meals could reach, so it only ever catches a bug.

## The image-decoder question milestone 6 deferred

`ImageEndpoints` recorded that the server had no image library, that decoders are a well-known RCE
and DoS surface, and that milestone 7 would be the moment to revisit normalising photos on ingest.
Revisited, and the answer is **no re-encoding on ingest**:

- Re-encoding would degrade every photo for every user, and the photo is what the model reads. §7's
  note that meal photos are kept at full sensor resolution so a better model can be re-run over them
  later is the same argument.
- It would be protecting a decoder that exactly one class runs, on a code path that is already
  optional.

**The guard lives in the decoder instead, and the important half is that it reads the header.** A
byte cap is not a pixel cap: 12 MB of JPEG can describe a 20000×20000 image, which is over a gigabyte
of RGBA — one request, one exhausted home server. `SKCodec` reports the dimensions before any pixels
are allocated, and anything over 30 megapixels is refused. A phone camera produces about 12.

The decoder also **restricts the formats it will accept to EAN-13, EAN-8, UPC-A and UPC-E**. This is
for accuracy rather than speed: ITF and Code 39 in particular happily find spurious barcodes in the
stripes of real packaging, and a wrong number gets looked up and answered confidently — worse than no
number, because the wrong product *skips* the image-to-model step that would have caught it. A decode
whose text is not 8–14 digits is discarded for the same reason.

UPC-A comes back as 12 digits and is **not** padded to 13. Open Food Facts normalises the length
itself, and a wrong guess about a leading zero turns a findable product into a miss.

## Routes, and what they must not become

`GET /api/lookup/barcode/{barcode}` and `POST /api/lookup/image/{id}`. Both read-only.

- **They are not a barcode-entry surface.** §10 forbids one and §1 keeps barcodes invisible. These
  exist because milestone 9's chat needs somewhere to send a photo, and because a milestone whose
  only evidence is a unit test is a milestone nobody has run.
- **They write nothing**, which has its own test. This is the obvious place for a well-meaning change
  to start filling the catalog automatically, and that would break confirm-before-save and pre-empt
  milestone 10.
- The image route takes an **id rather than bytes**, so a photo uploaded once can be re-examined
  without being sent again — the same reasoning that had `ImageEndpoints` accept an upload before the
  log entry it belongs to exists.
- Not merged into `/api/foods`: a lookup is a question about the outside world, not a resource in this
  server's catalog, and the two want different rate limits.

## Testing, and what the tests do not prove

The mapper is pure and separate from the client precisely so the interesting half needs no network.
It is tested against **three real captured responses**, each chosen because it breaks a different
assumption: Nutella (no serving size at all), Pringles (per-serving figures, mg iron, the duplicate
carbohydrate key), Diet Coke (per 100 ml, genuine zeroes). Every expected number was worked out by
hand from the fixture.

The client is tested against a stub, never the live service — a suite that called a volunteer-run API
would be both rude and flaky, and rate limits, timeouts and truncated responses are exactly what a
live service will not produce on demand.

A coverage test asserts every non-core nutrient in `NutrientSeed` has an Open Food Facts name
recorded, so adding "selenium" later fails the build until someone says what OFF calls it. This is
the §0 "kept honest by tests, not by discipline" pattern.

**A rendered barcode is a perfect barcode** — square-on, evenly lit, flat — so that suite proves the
library is wired up, the musl native build loaded, the format list is right and the digits survive.
It proves nothing about real packaging. `RealPhotoDecodeTests` is the suite that does, reading the
photographs in `media/examples`.

## What real photographs changed

The rendered-barcode suite passed everything. Real photographs immediately found two problems, and
the second is the important one.

**Baseline was 2 of 4.** A flat carton and a bottle decoded; a 500×500 shot of a curved can and a
2000×2000 carton back did not.

**Then measuring the misses found something worse than a miss.** Trying the obvious fixes — tiling
the image, downscaling, `DecodeMultiple` — appeared to crack a third photo, until the number was
checked against the label: it was wrong. Those strategies were manufacturing **checksum-valid
false positives**, and they were also firing on the two label photos that contain *no barcode at
all*, reading eight-digit codes out of an ingredients paragraph and a nutrition table.

**Every false positive was UPC-E, so UPC-E is gone from the format list.** It carries six digits of
data, so dense small print produces valid patterns by chance. Dropping it costs the small packets
that use it — they fall through to the model — and that is the right way round, because of how the
cascade treats a match: a false positive gets looked up, may match some unrelated product, and is
then reported as a *full* match, which is precisely the branch that does **not** send the photo to
the model. There is no second check. The user is shown someone else's food with no warning on it. A
miss costs a slower answer; a false positive costs a confidently wrong one.

**One real improvement survived: a second pass at double resolution.** The curved can decodes
correctly at 2× — `049000557695`, confirmed against its own filename. It is bounded to images of
4 MP or less, which both keeps the memory sane (4× the pixels) and keeps the retry away from phone
photographs, where bars are not sub-pixel thin and most photos have no barcode to find anyway.

**Final: 3 of 4 barcodes read correctly, 0 false positives on 3 barcode-free label photos.**

The remaining miss is recorded as a test rather than omitted — a flat, sharp UPC-A that occupies a
couple of hundred pixels of a 2000×2000 frame, so its bars approach the sampling grid. A failure of
that test is good news and the comment says so.

The general lesson, worth carrying into milestone 8: **for this cascade, a confident wrong answer is
the expensive failure, and cheap-looking accuracy improvements are exactly where they come from.**

## Left open

- **Ingredients are not requested**, though OFF returns `ingredients_text_en`, an `allergens_tags`
  list and a parsed ingredient taxonomy. The `Fields` constant is where that goes. Deferred to
  milestone 10a, which CLAUDE.md §9 now describes.
- **Four photographs is not a decode rate**, it is an anecdote with three data points and a known
  failure. It was enough to remove a whole barcode format, which suggests more photographs would
  earn their keep.
- **No caching.** Every lookup is a fresh request. A household re-logging the same six products will
  ask about them repeatedly. Milestone 10 puts items in the catalog, which is the real fix — a barcode
  already in the catalog needs no lookup at all — so a cache here would be solving it in the wrong
  place.
- **Composite items (7a) are untouched.** Still the next slice, and still wanting a global catalog of
  real OFF-sourced ingredients to compose, which this milestone is what starts filling.
