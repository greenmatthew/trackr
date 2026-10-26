---
id: 11
title: Chat, cascade and confirm
milestone: M9
record: docs/decisions/11-chat.md
depends_on: []
---

## Goal

The chat page, the `+` button with camera and gallery, and the in-chat confirmation card that turns
an analysis into a `POST /api/log`.

## Done when

- [x] analysis on its own named `HttpClient`: a five-minute timeout and no resilience pipeline
- [x] confirming is `items.Select(ToSaveLogItemRequest)`, with edits applied upstream
- [x] editable figures bound as text, an unreadable one blocking the save
- [x] a low-confidence item drawn differently, its warnings visible without a tap
- [x] recorded in [11-chat.md](../../docs/decisions/11-chat.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

No physical-phone run: every mobile claim rests on the emulator. Task 026.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> 9. ~~**Chat UI + cascade + confirm**~~ ✅ — [11-chat.md](../../docs/decisions/11-chat.md). The chat page,
>    the `+` button with camera and gallery, and the in-chat confirmation card that turns an analysis
>    into a `POST /api/log`. The cascade itself was already one server call, so this is MAUI work plus
>    the client fix without which none of it runs: **analysis needs its own named `HttpClient`** —
>    five-minute timeout, no resilience pipeline — because the shared 30-second client with retries
>    queued four inference jobs per meal.
>    **Three things it settled.** Confirming is `items.Select(ToSaveLogItemRequest)` and nothing else,
>    with edits applied upstream as `analysed with { … }`, so no mapping layer sits between the number
>    on screen and the number stored. Editable figures are bound as **text**, parsed here, and an
>    unreadable one blocks the save rather than becoming zero. And a photo is uploaded on send but
>    kept **by id**, so retrying a failed analysis costs no second upload.
>    **The two inherited rules landed where they had to:** a `Confidence: Low` item is drawn
>    differently with its warnings visible without a tap, and the card lists only nutrients the source
>    reported — never a "—" row for one nobody measured.
>    **Left open at the time, and closed since:** the quantity the validator missed and the transcript
>    dying on a tab switch are both fixed — see §9.10 and `12-catalog-growth.md` for the first, which
>    turned out to need fixing twice. No physical-phone run yet.
