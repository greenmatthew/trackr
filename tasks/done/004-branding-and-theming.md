---
id: 4
title: Branding and theming
milestone: M3
record: docs/decisions/04-branding.md
depends_on: []
---

## Goal

Give Trackr an identity — a `trackr` wordmark on a cyan-to-teal gradient and a ten-colour palette —
drawn from one token set by the web app and the phone, following the system light/dark setting.

## Done when

- [x] the icon and palette replace MAUI's defaults on both surfaces
- [x] dark-first, with a derived light theme; each brand colour ships as a light/dark triple
- [x] recorded in [04-branding.md](../../docs/decisions/04-branding.md)

## Notes

Built before the task board existed; filed at the migration on 2026-10-26 to carry the milestone's
history.

Not in the build order: a cross-cutting change taken between milestones 3 and 4, as the record says.
Labelled M3 because it landed with the mobile foundation.

The record accepts one known limitation, the launcher icon clipped by circular masks, and describes
the file swap that fixes it. Not filed: accepted is not agreed work.
