---
id: 19
title: Edit and delete a logged meal on the phone
milestone: M14
record: -
depends_on: []
---

## Goal

Somebody who logged the wrong thing, or the right thing twice, can correct it or remove it from the
phone, and the day's totals follow.

## Done when

- [ ] a logged meal's figures can be corrected and saved, and Home's totals and goal progress follow
- [ ] a logged meal can be deleted, after a confirmation, and the totals follow
- [ ] `just test` is green
- [ ] seen working on the emulator (`Verified: emulator`)
- [ ] the decision record and the wiki still match the code

## Notes

From the open list in milestone 14's build-order entry (task 018): "edit/delete entries".

- **The API half exists:** `PUT /api/log/{id}` and `DELETE /api/log/{id}` in
  `src/Trackr.Api/Endpoints/LogEndpoints.cs`. This is the phone's half.
- **`PUT` files nothing into the catalog, on purpose** — "a second silent writer with no user-facing
  trigger is how a table fills with rows nobody chose"
  ([12-catalog-growth.md](../../docs/decisions/12-catalog-growth.md)). Keep it that way.
- **`PUT` carries no client request id**, so an edit is not something the offline queue can retry
  ([18-offline.md](../../docs/decisions/18-offline.md) says where to look first if it ever must be).
- The confirmation card already binds editable figures as text and refuses an unreadable one
  ([11-chat.md](../../docs/decisions/11-chat.md)) — the obvious thing to reuse rather than a second
  editor.
