---
id: 48
title: The chat and its card with the soft keyboard up
milestone: M9
record: -
depends_on: []
---

## Goal

On a phone with no hardware keyboard, the on-screen keyboard must not cover the chat's text box or
the confirmation card's editable figures.

## Done when

- [ ] the emulator shows the soft keyboard (`adb shell settings put secure
      show_ime_with_hard_keyboard 1`)
- [ ] checked with the keyboard up: the chat's text box, the confirmation card, Goals, Body metrics,
      sign-in and registration; fixed where anything is covered
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: otter 345, 346 and `~/programs/otter/docs/api-notes.md:656-657`.

- Today `ChatPage.xaml:271` relies on `SafeAreaEdges` including `SoftInput`, and nothing sets a
  soft-input mode, so the activity pans — MAUI's default. Otter read the MAUI 10.0.100 source: under
  pan, `SafeAreaExtensions` returns `Consumed` with no padding, so `SoftInput` does nothing; and
  `adjustResize` does nothing under API 35+ edge-to-edge.
- **Never seen** because `scripts/emulator.sh:38` sets `hw.keyboard = yes`, which hides the soft
  keyboard, and there has been no phone run (026).
