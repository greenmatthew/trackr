---
id: 23
title: An admin page on the web app
milestone: M14
record: -
depends_on: []
---

## Goal

A page on the website for administering the server. Filed coarse: what it administers is not
written down anywhere, so the first step is agreeing that with the user, then splitting.

## Done when

- [ ] what the page does is agreed with the user and written under Notes
- [ ] split into session-sized tasks, and this one moved to `rejected/` with "split into NNN–MMM"

## Notes

From the open list in milestone 14's build-order entry (task 018): "an admin page on the web app".
CLAUDE.md §3 promises it ("an admin page later"), and §10 keeps administration web-only.

- **There is no admin role yet.** Any account may log in to the website; nothing distinguishes one
  that may administer.
- One job is already waiting for it: a shared catalog item cannot be deleted by an ordinary account,
  and `FoodEndpoints.cs` says "Removing one is left to a future admin surface".
