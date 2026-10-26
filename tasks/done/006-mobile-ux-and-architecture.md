---
id: 6
title: Mobile UX and architecture
milestone: M5
record: docs/decisions/06-mobile-ux.md
depends_on: []
---

## Goal

Make properly the navigation, styling and storage choices milestone 3 made minimally and labelled
provisional, before the chat UI is built on top of them.

## Done when

- [x] two shells swapped on the window; three tabs (Home | Chat | Trends), the profile behind a title-bar avatar
- [x] `Styles.xaml` pruned to a Trackr layer over Material
- [x] a server-stored profile picture, and SQLite on the phone with the account cache as its first job
- [x] the status bar clash fixed — by an amendment before milestone 9, not in the milestone itself
- [x] recorded in [06-mobile-ux.md](../../docs/decisions/06-mobile-ux.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

The build order in CLAUDE.md §9 described it as follows — verbatim, with links adjusted to
resolve from here:

> 5. ~~**Mobile UX & architecture**~~ ✅ — [06-mobile-ux.md](../../docs/decisions/06-mobile-ux.md).
>    Scoped as planning; it grew structural code because the questions were facts about a build,
>    not opinions about a design. Two shells swapped on the window, three tabs (Home | Chat |
>    Trends) with the profile behind a title-bar avatar, `Styles.xaml` pruned to a Trackr layer
>    over Material, a server-stored profile picture, and SQLite on the phone.
>    **Two things it pulled forward on purpose:** the avatar needed an EF entity, a migration and
>    endpoints (milestone 6 work) and a profile screen (§9.13). Neither means those milestones
>    partly shipped — the record says what was and was not taken.
>    **The status bar clash it left open is fixed** — before milestone 9, since the chat is the most
>    inset-sensitive screen in the app and building it against a layout about to change would mean
>    building it twice. It needed no slice: the app was already edge-to-edge (Android 15 enforces it
>    at API 35+), and the band was the Shell's `AppBarLayout` painting `colorPrimary` through the
>    status bar inset. An Android resource override fixes it; the amendment on the record has the
>    reasoning and the two things `ConfigChanges.UiMode` made necessary.
