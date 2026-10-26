---
id: 49
title: Survive a font, display-size or wallpaper change on the screen you were on
milestone: M5
record: -
depends_on: []
---

## Goal

Changing the font size, the display size, the wallpaper (which re-colours Material You) or the app's
language brings the person back to the screen they were on, not to a fresh start on the first tab.

## Done when

- [ ] each of the four changes, made on Goals and on Body metrics mid-edit, returns to that screen
- [ ] no handler is left on a singleton by the shell that was thrown away
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: otter 347, 348, 371 and `~/programs/otter/docs/api-notes.md:671+` and `:699+`.

- Today `MainActivity.cs:9` declares neither `assetsPaths` nor `locale`, so those changes recreate the
  activity, as a font-size change must. `App.CreateWindow` then starts over: LoadingPage, a network
  `RestoreAsync`, and a fresh AppShell on the first tab. A pushed page loses its typing; the chat
  survives only because `ChatViewModel` is a singleton.
- Trackr declares `Density`, which otter measured leaves MAUI drawn at the old size after a display
  size change.
- `AppShellViewModel.cs:28-36` says "a shell is only replaced by signing out" — wrong under recreate.
- Pitfalls: `ConfigChanges.AssetsPaths` is silently dropped by the manifest generator (otter used an
  `AndroidManifestOverlay`); `fontScale` cannot usefully be declared; MAUI leaks a replaced Shell
  three ways, which applies to trackr's sign-in/sign-out shell swap as well (dotnet/maui#33765).
