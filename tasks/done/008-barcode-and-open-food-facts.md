---
id: 8
title: Barcode and Open Food Facts
milestone: M7
record: docs/decisions/08-barcode-off.md
depends_on: []
---

## Goal

Decode barcodes on the server, look them up in Open Food Facts behind `IProductLookup`, and map the
answer onto the per-serving `FoodItem` shape across all 29 nutrients, covering all four branches of
the §5 cascade.

## Done when

- [x] ZXing + SkiaSharp on the server, with UPC-E excluded for the false positives it made
- [x] failures carried in `ProductLookupResult.Warnings` rather than swallowed
- [x] `/api/lookup/barcode/{barcode}` and `/api/lookup/image/{id}`, which write nothing
- [x] run against the live API and the photographs in `media/examples`: 3 of 4 barcodes read, 0 invented
- [x] recorded in [08-barcode-off.md](../../docs/decisions/08-barcode-off.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> 7. ~~**Barcode + Open Food Facts**~~ ✅ — [08-barcode-off.md](../../docs/decisions/08-barcode-off.md).
>    Server-side decode (ZXing + SkiaSharp), OFF v2 lookup behind `IProductLookup`, and a pure mapper
>    onto the per-serving `FoodItem` shape across all 29 nutrients. All four §5 branches, with failure
>    reasons carried in `ProductLookupResult.Warnings` rather than swallowed. Two read-only routes,
>    `/api/lookup/barcode/{barcode}` and `/api/lookup/image/{id}` — **not** a barcode-entry surface,
>    and they write nothing.
>    **Verified at two tiers** (§11): unit tests plus a live run of the compose stack against the real
>    API, and then against real photographs in `media/examples` — which changed the design. **UPC-E is
>    excluded from the decoder because it manufactured checksum-valid false positives** out of
>    ingredients paragraphs and nutrition tables, and a false positive is the cascade's worst outcome:
>    it is reported as a full match, which is the one branch that never sends the photo to the model.
>    A second decode pass at 2× resolution earned its place on a curved can. 3 of 4 barcodes read, 0
>    invented. **Left open:** ingredients are not requested from OFF yet (milestone 10a), and no lookup
>    caching, deliberately — milestone 10 putting items in the catalog is the real fix.
