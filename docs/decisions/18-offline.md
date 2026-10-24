# Milestone 14 (part) — Logging with no connection

§9.14 lists offline logging first among the polish items, and it is the one that changes what the
app can do rather than how it looks: a phone in a lift, on the underground, or in a kitchen at the
far end of the house is a phone that cannot reach a home server, and until now that meant the meal
was not logged.

The rest of §9.14 — editing and deleting entries, richer charts, per-nutrient views, a weight
series, the admin page — is untouched.

## What is queued, and what deliberately is not

**Only a meal the user has already confirmed.** There is no queue of things to analyse later.

That falls out of the two rules the project already had rather than out of caution. The cascade
runs on the server (CLAUDE.md §10) — barcode decode, Open Food Facts and the model are all there,
and duplicating any of it on the phone was ruled out in milestone 3 — so with no network there are
no numbers to be had. And confirm-before-save (§2) means numbers nobody has seen may not be
stored. A queue of unanalysed meals would have to break one of those: either it saves what the
model says without anybody reading it, or it raises a confirmation card hours later, out of
context, for a plate that is long gone.

A confirmed meal has neither problem. The numbers are settled, a person approved them, and sending
them later stores exactly what was agreed.

## The list of foods eaten before is what makes it useful

Left there, the queue would only catch the narrow case where the network died between the analysis
and the tap on *Save*. What makes it a feature is that **re-logging needs no server at all** — the
numbers were confirmed once already — so `GET /api/log/recent` is cached on the phone and the
*Had it again* list opens without a network.

`RecentItemsCache` is a cache in the sense `AccountCache` is: it degrades rather than throwing, and
losing it costs an offline list rather than data. What it must not do is pretend. A stale list says
so, in words, above the rows:

> Offline - showing what this phone last saw, a moment ago. Anything you log now is sent when the
> server can be reached.

Passing yesterday's answer off as the server's current one would be the quiet kind of wrong the
whole cascade is arranged against, and it costs one sentence to avoid.

## The queue is not a cache, and the difference is the point

`LogOutbox` sits beside `AccountCache` in `Trackr.Mobile.Core/Storage` and follows the opposite
rule. The account cache swallows a `SqliteException` and carries on, because the worst case is a
network request. A swallowed failure here would silently discard food somebody logged, so
`EnqueueAsync` returns a bool and the caller has to care: a card whose meal could be neither sent
nor stored stays on screen, unsaved, saying so.

What is stored is the `SaveLogEntryRequest` as JSON — the bytes that would have been posted —
rather than a row of columns. A request taken apart and put back together is a second place for a
number to change between what the user approved and what is stored, which is precisely what
milestone 9 kept out of the confirmation path on the way in.

## A retry is safe because the server de-duplicates it

The dangerous failure is not the one where nothing arrives. It is the one where **the write
succeeded and the reply was lost**, which from the phone is indistinguishable from a request that
never landed. An unguarded retry logs the meal twice, and an automatic queue retries without
anybody watching.

So `SaveLogEntryRequest` gained a `ClientRequestId`, `LogEntry` a nullable column, and the two a
unique index filtered to `WHERE "ClientRequestId" IS NOT NULL` — null being every entry written
before this milestone and every client that does not ask to be de-duplicated. `POST /api/log` looks
the key up and answers a repeat with the original entry and **200 rather than 201**, because the
second request created nothing and saying otherwise is a claim a client could act on.

Three things about that are worth keeping.

- **The look-up runs before validation.** A replay carries the image ids the successful attempt
  attached, and attached photos are refused — correctly, since that is what keeps one meal's
  picture out of another's. Validating first answers every retry with a 400 about the photos it had
  itself stored. `A_replay_carrying_photos_the_first_attempt_attached_still_returns_its_entry`
  pins that order down.
- **The index is what makes it true, not the look-up.** Look-then-write is two statements, so two
  requests can both find nothing; the unique index turns that into a losing race, and the handler
  catches the `23505` on that constraint by name and answers with the winner's entry. Which path a
  given request takes cannot be pinned from outside the process, so the test fires eight at once
  and asserts the part that matters either way: one `201`, seven `200`, one meal.
- **The key is unique per account, not globally.** It is a `Guid` a phone invents offline, and a
  collision between two accounts would hand one household member another's meal — worse than the
  duplicate it was there to prevent.

## The meal keeps the time it was approved

The client now sets `LoggedUtc` on **every** save, queued or not.

Without it the server stamps its own "now" when the queue finally arrives, which turns a breakfast
confirmed in a tunnel into an evening meal and, past midnight, into one on the wrong day. One rule
for both paths rather than a special case for the queued one, so a meal's time never depends on how
long it waited.

This does not hand the day boundary to the phone, and the distinction is the same one milestone 11
and milestone 12 both had to make: the client sends an **instant**, never a date. Which local day
that instant falls in is still decided by the account's time zone on the server, in the one helper
that has always decided it.

## Once anything is queued, everything queues

A save that jumped the queue would store tonight's dinner while this morning's breakfast was still
waiting. The log would be out of order, and "did that go through?" would stop being answerable by
looking.

So `SubmitAsync` sends directly only when nothing is waiting; otherwise the meal joins the back of
the queue and the flush takes them in order. A flush stops at the first row it cannot deliver, for
the same reason: whatever stopped that one will stop the next, and working past it reorders the log
to spend a phone's radio on a queue that is going nowhere.

## Refused and undelivered are different answers

`SaveLogResult` grew a `SaveLogStatus` with three members instead of a bool, which is the
distinction `MeStatus` already draws for the session:

- **Saved.**
- **Rejected** — the server read it and said no. Retrying it unchanged will be refused again, so it
  is never retried automatically; the row stays, marked, with the server's own wording.
- **Deferred** — no verdict. Offline, timed out, a 5xx, a 429 — or a **401**, which is here on
  purpose: `BearerTokenHandler` has already tried to refresh, so this means the session ended while
  the meal waited, and the meal should wait for the next sign-in rather than be destroyed by it.

A rejected row is not deleted, because deleting it loses food somebody logged, and it is not
retried, because retrying a 400 forever is how a queue becomes a loop. It waits for the person, who
is the only one who can do anything about it, with *Send again* and *Discard* on the Home banner.
**Discarding is the only way a confirmed meal ever leaves the phone unsent**, and it takes a
deliberate tap.

One exception, and it is visible rather than quiet: a row that has waited **30 days** stops trying
by itself and says so. Long enough that a holiday away from the server still delivers, short enough
that a row for an account that no longer exists does not retry until the phone is replaced.

## Signing out keeps the queue and drops the list

`AuthSession.SignOutAsync` already cleared the cached account and the avatar, so that the next
person to hold the phone does not find the last one's email and photograph. The recents cache joins
them, at no cost: the server hands that list back on the next sign-in.

The queue deliberately does **not**. Those rows are meals the server has never seen, so clearing
them would make signing out a way to lose logged food. They carry the account that queued them, are
never read back for another, and go out on the next sign-in — which is itself a flush trigger, for
exactly this reason.

## Connectivity is a trigger, never a verdict — and the emulator proved it

`INetworkStatus` exists so the queue empties itself when a network returns instead of when somebody
remembers it. The first version also used it to *skip* work: no network, so do not attempt the save,
and do not flush.

The emulator killed that. In airplane mode Android reports `NetworkAccess.None` while
`10.0.2.2` — the host, and therefore the dev server — remains perfectly reachable, so the app
queued a meal that would have gone straight through and then refused to flush it. The hint is wrong
in the other direction too: a captive-portal wifi is reported as a connection and answers nothing.

So nothing decides anything on its word. A save is always attempted and always able to fail; the
hint only ever *starts* a delivery. The cost is one fast failure per trigger while genuinely
offline, which is the cheaper of the two mistakes by a long way.

This is CLAUDE.md §11's rule about actually running the thing catching a design error rather than a
bug, which is the third time it has paid for itself — [16-time-zone.md](16-time-zone.md) and
[17-profile.md](17-profile.md) are the other two.

## The queue is shown on Home, not in the chat

Home is where the number it contradicts is. A total that silently omits three queued meals is a
wrong figure on the screen the whole app is built around, so the banner says both halves:

> 1 meal is waiting to be sent, and is not in today's total.

The chat says its part too — a queued card reads *Saved on this phone - waiting to send*, which is
deliberately not *Saved to your log* — but the transcript does not survive a restart and the queue
does, so it cannot be the only place this is visible.

Home flushes before it reads its totals, since a queue that empties now is a total that should
already include what it delivered. A second flush arriving mid-way **joins** the first rather than
being turned away, which is also what stops a resume and a connectivity change from sending the
same row twice.

## Verified

Tier 1: 410 API tests, 223 view-model tests, 104 documentation tests. The queue's tests run the
real `LogOutbox` against a real in-memory SQLite database, so the SQL, the JSON round trip and the
ordering are under test rather than mocked.

Tier 2, the compose stack: the migration applied to a database that already had an account and a
log; the same save sent twice answered `201` then `200` with one entry stored; eight sent at once
answered with one `201`, seven `200` and one meal; a client-stamped `LoggedUtc` nine hours old kept
its time and landed on the previous local day.

Tier 2, the emulator: with the frontend container stopped, *Had it again* opened from the phone's
cache with the stale-list sentence above it, a confirmed card read *Saved on this phone - waiting
to send*, the row appeared in `outbox` with its whole request as JSON, and Home said *1 meal is
waiting to be sent, and is not in today's total* over the name of the meal. Starting the container
and returning to Home emptied the queue, removed the banner, and moved the day's total from 285 to
390 kcal — with the stored entry carrying **00:38:18**, the moment the card was approved, rather
than 00:41, when it was delivered.

## Left open

- **No physical-phone run.** Every mobile claim here is tier 2, as with milestones 9 to 13.
- **A background flush while Home is on screen does not redraw it.** The banner is read when Home
  appears and when it refreshes. Making it live would mean a singleton raising events at a
  transient view model, and either a leak or lifecycle glue in the MAUI project; the cheap version
  is the user switching tabs, which they are about to do anyway.
- **Nothing is queued but the save.** A photo cannot be uploaded offline, so a meal composed with
  no connection cannot be analysed later — see the first section for why that is a design decision
  rather than a gap, though a queue of *uploads* for photos taken offline would be a smaller and
  more honest version of it than a queue of analyses.
- **`PUT /api/log/{id}` has no client request id**, deliberately: an edit is already addressed by
  the entry's own id. If editing ever needs to work offline, that is where to look first.
