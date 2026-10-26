---
id: 50
title: Say which number is blocking Confirm or Save
milestone: M9
record: -
depends_on: []
---

## Goal

When a figure cannot be read, the person is told which one, instead of facing a dimmed button that
never says why.

## Done when

- [ ] the field that cannot be read is marked, and tapping Confirm or Save says which it is
- [ ] TalkBack reads the reason
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Source: otter 301.

- Today Confirm is `CanConfirm => Items.All(i => i.IsValid)` (`ConfirmationCard.cs:88`), and Goals and
  Body metrics bind `IsEnabled="{Binding CanSave}"`. An unreadable number dims the button and no field
  says anything (`ChatPage.xaml:160-190`).
- Blocking the save on an unreadable figure is [11-chat.md](../../docs/decisions/11-chat.md)'s rule
  and stays; this is about saying so.
- Pitfall: a tooltip on a disabled view cannot be reached by touch. Otter keeps the button enabled,
  answers the tap with the reason, and sets `SemanticProperties.Hint`.
