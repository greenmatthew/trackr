# CLAUDE.md — Self-Hosted Nutrition Tracker

This file is the project brief and working agreement for Claude Code. Read it fully before
generating code. When in doubt about scope or a design decision, prefer the choices recorded
here; if something here is ambiguous, ask before inventing a new pattern.

---

## 0. Where documentation lives

Three homes, split by **when something is read** rather than by who reads it.

| Home | Holds | Read |
| --- | --- | --- |
| **`CLAUDE.md`** (this file) | Decisions and constraints. What we are building, what is locked in, what not to do. | Every session, unprompted. |
| **`docs/`** | Claude-facing working material. `decisions/` holds one record per milestone — the *why* behind past choices. | Before changing anything a record covers. |
| **`wiki/`** | Reference and how-to, for the self-hoster and for Claude alike: installation, configuration, troubleshooting, dev environment, testing. Tracked here, published to the GitHub/Gitea wiki. | On demand, when the question actually arises. |

**The test for this file:** *would Claude make a worse decision without this in context?* If
yes, it belongs here. If it merely answers a question — "which port", "what are the nutrient
keys", "how do I start the emulator" — it belongs in the wiki, where it can be looked up at
the moment it is needed instead of costing context in every unrelated session.

**One exception, and it matters: constraints stay here even when they read like reference.**
Anything whose staleness would be *dangerous* — the cleartext-HTTP rule, the auth schemes,
the security posture in §8 — stays in `CLAUDE.md`. A wiki page that drifts out of step with
the code is a mild annoyance for a how-to and a security misunderstanding for a constraint.

**The wiki is published from here, not cloned into here** (`just docs::publish`), so a change
to a flag and a change to the page documenting it land in **one commit**. Consequence worth
stating out loud: editing a page in the GitHub or Gitea wiki UI is pointless, because the next
publish overwrites it — say so if asked to.

**Documentation is kept honest by tests, not by discipline.** `API-Reference.md` is generated
(`just docs::api`) — never hand-edit it. `tests/Trackr.Docs.Tests` fails when a `TRACKR_*`
variable is undocumented or a documented `just` recipe does not exist. Adding a configuration
knob is *meant* to fail that suite until the wiki is updated — the mechanism working, not an
obstacle to route around.

Rationale and rejected alternatives: [05-documentation.md](docs/decisions/05-documentation.md).

---

## 1. What we are building

A **self-hosted personal nutrition / calorie tracker** that runs on the user's home server. It
is for the user (and possibly a small number of household members) only — not a public product.

**The product is an Android app.** Logging a meal happens on the phone, because that is where
you are when you eat. The server is the backend — database, AI, cascade orchestration — and
the website is an account and administration surface, not a place to log food. See §3.

**The interface is a chat.** This is the defining UX decision and everything else serves it.
The app should feel like Claude chat or Google Health: the user starts a new chat, types what
they ate in plain language ("two eggs and a slice of toast", "I had 2 of these") and —
optionally — attaches a photo using a **`+` button in the bottom-left of the text box**. They
do **not** type barcode numbers, pick from dropdowns, or fill out nutrition forms. The AI
figures it out from the text and/or image. Any barcode work happens invisibly behind the
scenes (see the cascade in §5) — the user never sees or enters a barcode. Structured
confirmation (calories/macros to approve before save) appears *inside the chat flow* as a card
the user can correct, not as a separate data-entry screen.

**But the chat is not the whole app — it's paired with an always-visible stats view.** The
user needs a clear, glanceable picture of their nutrition, not just a place to log it. One tap
from the chat there must be:

- **Today so far — REQUIRED, core.** A running total of the current day: calories and the full
  nutrient breakdown, updating as entries are confirmed. Basic app, not polish.
- **Week / month stats — REQUIRED (basic form).** Rolling summaries and simple trends. "Basic"
  is fine — it needn't be elaborate analytics, but it must exist.
- **Goals — LATER milestone.** Targets and progress against them. Explicitly *late*: build the
  stats views first; goals layer on top once the numbers exist.

Layout intent: the chat is the *input* surface and the stats view is the *output* surface, both
first-class, two tabs in the app's `Shell`. Don't bury the day's totals behind a menu — logging
a meal and immediately seeing the day update is the core loop.

**Design mobile-first and mean it.** One-handed reach, thumb-sized targets, the text box within
reach of the bottom of the screen. Do not port a desktop layout onto a phone; the phone *is*
the layout.

There is **no pre-loaded food/ingredient database**. The catalog is built up gradually from
Open Food Facts hits and AI reads as the user logs meals. The full flow is §5.

---

## 2. Guiding principles

- **Privacy first.** Images are decoded for barcodes locally and, when needed, sent only to
  the *local* Ollama container. The only thing that ever leaves the server is a barcode
  *number* string sent to Open Food Facts. Raw images never go to any third party.
- **Cascade, not competition.** Barcode → Open Food Facts → local AI fallback → manual entry.
  Each stage only runs if the previous one didn't resolve the item.
- **Cheap by default.** Prefer the barcode+OFF path (a tiny number lookup) over sending images
  to the model. Only fall back to the model when necessary.
- **Confirm before save.** The system must show the user what it parsed (calories, macros,
  servings) and let them correct it *before* writing to the database. Silent wrong numbers are
  the main failure mode to avoid.
- **Swappable stages.** Barcode decode, OFF lookup, and the AI parse step must each sit behind
  a clean interface so any one can be swapped (local Ollama ↔ a cloud API fallback; SQLite ↔
  Postgres) without touching the rest.
- **Maintainable by the user.** The user wants to maintain this themselves in C#. Favor clear,
  conventional ASP.NET Core / Blazor patterns over clever abstractions.

---

## 3. Tech stack (locked in)

Versions and package lists live in `Directory.Packages.props`, commented with the reasoning
for each. Toolchain setup is in [Development-Environment](wiki/Development-Environment.md).

- **Mobile (the product):** **.NET MAUI** targeting **Android**, UI in **XAML**, MVVM via
  `CommunityToolkit.Mvvm`. Not Blazor Hybrid — it would reuse the Razor components but render
  them in a WebView, the thing this design moved away from
  ([03-android-pivot.md](docs/decisions/03-android-pivot.md)). MAUI replaces WPF/WinUI rather
  than complementing them: one project, native controls, its own XAML dialect.
  - **Two projects, and the split is load-bearing.** `Trackr.Mobile.Core` (plain `net10.0` —
    view models, API client, platform abstractions) and `Trackr.Mobile` (the MAUI app and its
    Android implementations). Core exists so view models are testable with plain xUnit, with no
    Android SDK and no emulator. **Keep logic in Core**; `Trackr.Mobile` is XAML, `Shell`
    wiring and platform glue.
  - **Do not add packages for DI, logging or configuration** — `MauiAppBuilder` already has
    all three. And deliberately **not** `Microsoft.Extensions.Hosting`: `MauiAppBuilder` is
    modelled on `HostApplicationBuilder` but is not an `IHostBuilder`, and MAUI never runs
    `IHostedService`, so it would bolt on a host nothing drives. Likewise no `appsettings.json`
    — the only real configuration is the server URL, which the user types and which belongs in
    `SecureStorage`.
  - **Two kinds of local storage, and the split is a security boundary.** Tokens go in
    `SecureStorage`, which is Keystore-backed. Everything else — the cached account, the
    profile picture, and the offline log queue when it arrives — goes in a **SQLite database**
    in app-private storage, via `Microsoft.Data.Sqlite` in `Trackr.Mobile.Core` (hand-written
    SQL, *not* EF Core; that is the server's ORM and the phone's schema is a handful of tables).
    Core rather than the MAUI project so the real SQL is testable against `:memory:`. Never put
    a credential in the database, and never put bulk data in `SecureStorage`.
  - **Cleartext HTTP is blocked, except for the emulator in Debug.** Android has forbidden it
    by default since API 28 and the app targets 36. The Release network security config forbids
    it everywhere; the Debug one opens it for `10.0.2.2`, `localhost` and `127.0.0.1` only —
    addresses that can never be a real server. The csproj chooses which file supplies the
    resource, so the exception cannot reach a release build by being forgotten. **Verify by
    dumping the manifest out of a built APK, not by reading the source** —
    [Building](wiki/Building.md).
  - iOS is out of scope. Nothing should *prevent* it later, but do not spend effort on it.
- **Web:** Blazor WebAssembly (`Trackr.Web`), served by nginx — not Razor Pages, which the user
  first suggested and which is server-rendered HTML. Scope is **account self-service and
  administration only**: login, password change, 2FA enrolment, invites, an admin page later.
  Any user may log in. Deliberately **not** a food-logging surface — do not build the chat or
  stats views here.
  - *Onboarding is the one exception, and it goes both ways.* **Registration exists on the
    phone as well as the web**, because the case the invite system serves is a household member
    who may own nothing but a phone. Everything else stays web-only.
- **Backend:** ASP.NET Core **Web API** (REST), serving both front ends. **EF Core** over
  **PostgreSQL** (containerized).
- **Shared contracts:** `Trackr.Shared` — DTOs referenced by the API, the web app and the
  mobile app alike. Sharing types by **project reference** rather than generating a client from
  the OpenAPI document is the main reason everything lives in one repository. Keep it
  dependency-free: it is trimmed into the browser bundle and linked into the APK.
- **Auth:** ASP.NET Core **Identity** (do NOT hand-roll auth) — hashing, login, session
  handling, TOTP 2FA and lockout all come from it. §8 has the hardening list.
  **Two schemes, by client:**
  - *Web → HttpOnly cookie.* Same origin (nginx serves the app and proxies `/api/`), so the
    browser handles the session and JavaScript cannot read it — an XSS bug cannot steal it.
    Hardening decisions in [02-auth.md](docs/decisions/02-auth.md) stand unchanged.
  - *Android → Identity bearer token.* A native cross-origin client cannot hold a cookie
    usefully, so it gets `IdentityConstants.BearerScheme` with a refresh token in
    `SecureStorage`. **Additive** — it changes nothing about the cookie path. (This file once
    read "cookies, not JWTs" because tokens only matter for native/cross-origin clients, "none
    of which apply". The app made that premise false; the conclusion still holds for the
    browser, which is why both coexist.)
  - *No CORS.* MAUI uses a native `HttpClient` and CORS is browser-only. Adding it for the app
    would be pure attack surface.
- **Local AI:** **Ollama** container serving a small multimodal (vision) model over its HTTP
  API on the Docker network. See [Ollama-Setup](wiki/Ollama-Setup.md).
- **Barcode decode:** **invisible** — an internal optimization on any attached photo, never a
  user-facing feature. **Prefer server-side** (a .NET library decoding in the backend): the
  user attaches stills rather than live-scanning, and it keeps the decode in one place.
  On-device decoding is available if wanted but is an optimisation that duplicates logic the
  backend needs anyway for images arriving without a decode. Record the choice when made. A
  decode failure is not a dead end — the vision model can often read the barcode, or just
  recognise the product, from the image.
- **Nutrition data:** the Open Food Facts **public API**, not a self-hosted copy — a lookup is a
  small HTTP GET by barcode number. OFF returns a rich `nutriments` object (per-serving and
  per-100g), so **map through the full nutrient set**, not just macros —
  [Nutrient-Reference](wiki/Nutrient-Reference.md).
- **Orchestration:** Docker Compose, deployed via **Portainer** behind the user's existing
  **reverse proxy**, which terminates TLS. Keep `docker/docker-compose.yml` stack-friendly:
  named services, env vars for config and secrets, named volumes, an external network for the
  proxy to attach to.

---

## 4. Container / service layout

Four services on a shared Docker network, orchestrated by `docker/docker-compose.yml`:
**frontend** (nginx serving `Trackr.Web` and proxying `/api/` to the backend — also the address
the Android app points at), **backend** (the Web API: auth, cascade orchestration, DB access,
OFF and Ollama calls), **ollama**, and **db** (Postgres with a persistent volume). Detail is in
[Self-Hosting](wiki/Self-Hosting.md).

Open Food Facts is **not** a container — it's an external public API the backend calls. The
Android app is **not** a container either; it ships as an APK and talks to `frontend` over the
network like any other client.

Keep the data layer behind EF Core so the concrete provider stays swappable.

### Decisions made so far

Recorded per milestone in [`docs/decisions/`](docs/decisions/), which is where the *why*
lives — read the relevant one before changing anything it covers.

- [01-scaffold.md](docs/decisions/01-scaffold.md) — nginx also proxying `/api/`, plain HTTP
  inside the containers, the two compose files, the `dotnet watch` inner loop.
- [02-auth.md](docs/decisions/02-auth.md) — `AddIdentityCore` with hand-written endpoints,
  bootstrap-then-invite registration, the fail-safe fallback policy, cookie hardening,
  data-protection keys in Postgres, `Guid` user keys, startup migrations.
- [03-android-pivot.md](docs/decisions/03-android-pivot.md) — why the phone became the
  product, why MAUI XAML over Blazor Hybrid, why one repository, and why the API now issues
  bearer tokens as well as cookies.
- [04-branding.md](docs/decisions/04-branding.md) — the icon and brand palette, dark-first
  with a derived light theme, and why each brand colour ships as a light/dark triple.
- [05-documentation.md](docs/decisions/05-documentation.md) — the three homes, publishing the
  wiki from this repository, and generating/testing the reference material that drifts.
- [06-mobile-ux.md](docs/decisions/06-mobile-ux.md) — the two-shell swap, the three tabs and
  the avatar, pruning `Styles.xaml`, the server-stored profile picture, and SQLite on the
  phone with the account cache as its first job.
- [07-data-layer.md](docs/decisions/07-data-layer.md) — the relational nutrient store and
  key-as-primary-key, the core four as columns *and* catalog rows but never amounts, totals
  rather than per-serving values on `LogItem`, the **shared catalog** (which amends §7 below),
  wiki-style edits to global items, meal photos as `bytea`, the startup seeder over `HasData`,
  and the day-boundary helper.

---

## 5. The logging cascade (core flow)

For each log attempt (user text + zero or more images):

```
For each uploaded image:
    1. Attempt local barcode decode.
       ├─ Success → got a barcode number
       │     └─ Query Open Food Facts by that number.
       │           ├─ Full match (has calories + macros) → replace the
       │           │          image with the structured nutrition data
       │           │          (name, serving size, per-serving macros).
       │           ├─ Partial match (found, but calories/macros missing
       │           │          or null) → send the image AND whatever
       │           │          partial OFF data was found, so the AI can
       │           │          fill the gaps.
       │           └─ No match → keep the image; it will go to the AI.
       └─ Failure → keep the image; it will go to the AI.

Assemble the AI request:
    - The user's text.
    - For full barcode matches: structured OFF data (NOT the image).
    - For partial matches: the raw image(s) PLUS the partial OFF data.
    - For unmatched / no-barcode items: the raw image(s).
    - Any errors/exceptions from earlier stages (see error handling below).

Send to the local Ollama vision model with a prompt that:
    - Returns STRICT JSON only (no prose, no markdown fences).
    - Computes serving math ("2 of these" = 2 × serving × per-serving macros).
    - Produces calories + the full nutrient set per item AND a total. Macros
      always; fibre, sugars, saturated/trans fat, sodium, cholesterol,
      vitamins and minerals whenever determinable from a label or a
      reasonable estimate. Every nutrient carries its unit. Omit or null what
      it cannot determine — a missing micro is fine, a hallucinated one is not.

Validate the JSON before trusting it. REQUIRED: small local models do emit
broken JSON and numbers that don't add up, and this validator is the main thing
standing between the model and a wrong number in the database.
    - Parse defensively. Invalid JSON or missing required fields → do NOT save;
      show an error and let the user retry or enter values manually.
    - Reconcile calories against the macros (~4 kcal/g protein, ~4 carbs,
      ~9 fat). Wildly off → flag low-confidence on the card, don't present as fact.

Show the parsed result for confirmation / correction. On confirm: write the log
entry to Postgres, and upsert any new item into the personal catalog.
```

Key point to preserve in code: **fully-matched barcode items must NOT send their image to the
model** — they send the OFF structured data instead. This is the token/cost/accuracy win.
(Partial matches still send the image, alongside whatever data OFF returned.)

### Error / exception handling (surface problems, never fail silently)

Every stage can fail — OFF can rate-limit or time out, barcode decode can throw, Ollama can be
unreachable. These must never silently disappear or produce a wrong number.

- **Pass errors along to the AI.** When assembling the AI request, include a short description
  of any exception from an earlier stage (e.g. "Open Food Facts returned HTTP 429 rate limit").
  The prompt should let the AI relay this in plain language when relevant ("I couldn't reach
  the food database, so I estimated from your photo instead").
- **Also surface a plain warning/error in the chat UI** regardless of what the AI says — e.g. a
  small "⚠ Open Food Facts rate-limited; used photo estimate" banner on the confirmation card.
  The user should always know when a fallback happened and why, so they can judge how much to
  trust the numbers.
- If the AI stage itself fails (Ollama down, unparseable output), show a clear error in the
  chat and let the user retry or enter values manually — never save a guessed or empty entry.

---

## 6. Ollama / RAM behavior

The user was concerned about the model holding several GB of RAM permanently. The resolution:

- The **Ollama container stays running** (it is lightweight when idle). The **model** is what
  consumes RAM, and Ollama unloads it after an idle period. Configure `keep_alive` (request
  field or `OLLAMA_KEEP_ALIVE`) to a short value so the model drops out of RAM between uses.
- Do **not** stop/start the whole container per request — complexity for little benefit.
- **Keep the model name in config, not code**, so it stays swappable. Start with a tiny model
  to get the pipeline working end to end, then swap up.

Sizing, model selection and how to test a candidate against real label photos:
[Ollama-Setup](wiki/Ollama-Setup.md).

---

## 7. Data model (initial sketch — refine in code)

- **User** — via ASP.NET Core Identity.
- **FoodItem (catalog)** — id, name, brand (nullable), barcode (nullable), serving size + unit,
  source (`off` | `ai` | `manual`), timestamps, **owning user (nullable)**, **plus a full set of
  per-serving nutrients**.
  - **A null owner means the item is global** — visible to every account on the server, and
    editable by any of them wiki-style. Non-null means personal to that account. The user chooses
    at creation; promotion to global is one-way. This amends the original "owning user" here, and
    the reasoning is in [07-data-layer.md](docs/decisions/07-data-layer.md). Do **not** build
    per-user duplicates of a shared product.
- **LogEntry** — id, user, timestamp/date, free-text note (nullable).
- **LogItem** — id, log entry id, food item id (nullable if ad-hoc), quantity (number of
  servings), **full computed nutrient snapshot** at time of logging.

### Nutrient storage — design for many nutrients, not just macros

Track **everything worth tracking that appears on a nutrition label**, not just calories and
the carb/fat/protein split. That's dozens of fields and the set will grow; the concrete target
set is in [Nutrient-Reference](wiki/Nutrient-Reference.md).

**Do NOT model this as one fixed column per nutrient** — that makes every new nutrient a schema
migration and leaves most rows full of nulls. Instead:

- Preferred: a **`Nutrient` reference table** (key e.g. `vitamin_c`, display name, unit,
  canonical sort order) + a **per-item `NutrientAmount`** join. Adding a nutrient = a row.
- Acceptable: a **JSONB column** holding a nutrient→amount map with a code-side registry for
  units and display order. Simpler to start, still schema-stable; weaker query ergonomics.
- Either way, **keep calories and protein/carbs/fat as first-class always-present fields** too
  — they drive the dashboard and goals math, and shouldn't need a join or a parse.
- Every nutrient carries an explicit **unit**; never assume grams (vitamins are often mg or µg).
- Nutrients are frequently **unknown**. Store null/absent, distinguish "known to be zero" from
  "not measured," and show missing micros gracefully rather than as 0.
- Keep the set **data-driven**, so adding "selenium" later is a data change, not a code change.

Store the **full nutrient snapshot** on the LogItem, so historical logs never change if a
catalog item is later edited.

---

## 8. Security posture

Treat the data as sensitive personal health information in terms of *care*, without claiming
formal regulatory compliance (this is a personal self-hosted tool for the user's own data;
formal HIPAA compliance is a heavy legal regime that does not meaningfully apply here).

Concretely:
- All access is behind login (ASP.NET Core Identity). No unauthenticated endpoints except
  login (and registration, if enabled at all).
- Enforce **HTTPS** in transit. TLS is terminated at the user's existing **reverse proxy**.
- Passwords hashed by Identity (never store plaintext).
- Use secure, HttpOnly cookies or properly-scoped tokens for the session.
- Data stays on the user's server; images are not sent to third parties; only barcode numbers
  go to Open Food Facts.
- Keep secrets (DB creds, any API keys for an optional cloud fallback) in environment variables
  / a secrets file, never committed.

### Auth hardening (wanted features)

The user explicitly wants a properly secured account system. Implement, roughly in order of
value:

1. **Two-factor authentication (2FA) — REQUIRED.** Identity's built-in TOTP support
   (authenticator apps). Flow: enable in settings → app displays a **QR code** → scan with the
   authenticator → enter the current rolling 6-digit code at login. Confirm enrollment by
   having the user type one valid code before turning 2FA on, and issue **recovery codes**
   (shown once) so they can still get in if they lose their phone. SMS 2FA is possible but not
   needed. All of this is built into Identity — render the QR and verify codes, don't build the
   TOTP algorithm yourself.
2. **Account lockout — REQUIRED.** Identity's lockout so N failed logins locks the account for
   a cooldown. The primary defense against brute-force / password guessing.
3. **Rate limiting — REQUIRED.** ASP.NET Core's built-in rate-limiting middleware on the auth
   endpoints (login, register, 2FA) to blunt automated attempts.
4. **Registration lockdown.** No open public sign-up. Either disable registration after the
   first account(s), or make it invite-only.
5. **Network-level protection (strongest, simplest).** The single most effective protection for
   a personal tool is to not expose the login to the public internet at all — VPN / LAN only,
   or gated at the reverse proxy. Note this to the user as the recommended posture; it makes
   bot/brute-force concerns nearly moot on its own.

*(reCAPTCHA was considered and deliberately dropped: on a VPN/LAN-only personal tool the login
isn't public, so it would add a Google dependency and login friction for essentially no
benefit. Lockout + rate limiting + network-level protection already cover brute-force. If the
login is ever exposed publicly, revisit this.)*

---

## 9. Build order (milestones)

Do each milestone as a working, testable slice before moving on. Keep the three cascade stages
(barcode, OFF, AI) behind interfaces from the start so they stay swappable.

1. ~~**Scaffold**~~ ✅ — [01-scaffold.md](docs/decisions/01-scaffold.md)
2. ~~**Auth**~~ ✅ — [02-auth.md](docs/decisions/02-auth.md)
3. ~~**Mobile foundation**~~ ✅ — the thin end-to-end slice, deliberately *before* the backend
   milestones because the Android toolchain and token auth were the two unknowns in the pivot.
   [03-android-pivot.md](docs/decisions/03-android-pivot.md)
4. ~~**Documentation migration**~~ ✅ — [05-documentation.md](docs/decisions/05-documentation.md)
5. ~~**Mobile UX & architecture**~~ ✅ — [06-mobile-ux.md](docs/decisions/06-mobile-ux.md).
   Scoped as planning; it grew structural code because the questions were facts about a build,
   not opinions about a design. Two shells swapped on the window, three tabs (Home | Chat |
   Trends) with the profile behind a title-bar avatar, `Styles.xaml` pruned to a Trackr layer
   over Material, a server-stored profile picture, and SQLite on the phone.
   **Two things it pulled forward on purpose:** the avatar needed an EF entity, a migration and
   endpoints (milestone 6 work) and a profile screen (§9.13). Neither means those milestones
   partly shipped — the record says what was and was not taken.
   **Left open:** the Android status bar renders `colorPrimary` and clashes with the title bar.
   The fix is going edge-to-edge and handling insets, which is a layout change wanting its own
   slice; two cheaper approaches were tried and both fail structurally (see the record).
6. ~~**Data layer**~~ ✅ — [07-data-layer.md](docs/decisions/07-data-layer.md). Seven entities,
   the relational nutrient store seeded with 29 nutrients, and CRUD for catalog, log and meal
   photos. The acceptance criterion — store and read back a full multi-nutrient item — is
   `FoodCatalogTests.A_food_item_keeps_every_nutrient_it_was_given`.
   **API only: it has no UI and is not meant to acquire one** (§10 still forbids a food-logging
   surface on the web; the chat is milestone 9). Two things it settled beyond the original scope:
   the **catalog is shared across accounts** (see §7) and meal photos are stored now, because
   milestone 9 needs somewhere to put them before a confirmation card exists.
7. **Barcode + Open Food Facts** — invisible barcode decode (default to server-side; record the
   choice), OFF lookup by number, map OFF response → FoodItem shape. Send a proper descriptive
   **User-Agent** (app name + version + contact) — OFF asks for this and may throttle callers
   without it. Handle full-match, partial-match and no-match per §5, and surface
   rate-limit/timeout errors rather than swallowing them.
   - **7a. Composite / recipe items** — a slice of its own, sequenced here because it wants a
     global catalog holding real OFF-sourced ingredients to compose. Barcode+OFF covers packaged
     food and fails completely on home cooking, which is the case the AI fallback handles worst.
     A composite is still a `FoodItem`, plus a `FoodItemComponent` join and a `Yield`; nutrition
     is **materialized on write**, so nothing downstream learns that composites exist. The shape
     is settled in [07-data-layer.md](docs/decisions/07-data-layer.md) and the schema needs no
     backfill — the real work is the cycle check and the fan-out recompute.
8. **Ollama integration** — add the service, wire the backend to call it, define the
   strict-JSON prompt, implement the image-vs-structured-data swap, parse and validate the
   JSON. Configure `keep_alive`.
9. **Chat UI + cascade + confirm** — build the chat interface **in the Android app** (new-chat
   flow, message list, text box with a `+` button bottom-left to attach images) and wire the
   full cascade from §5 into it. The parsed result appears as an in-chat **confirmation card**
   (calories/macros/servings, editable) with any fallback warnings, and only writes to the DB
   on confirm. Include the serving-count math. Camera and photo-picker permissions land here.
10. **Catalog growth** — upsert items from OFF/AI into the catalog; let the user pick from
    previously logged items for fast re-logging.
11. **Stats views (REQUIRED)** — the output surface from §1, a tab in the app. "Today so far"
    running totals (calories + full nutrient breakdown, updating as entries are confirmed),
    then week/month summaries with basic trend charts. Aggregations read from the nutrient
    snapshots on LogItems. Core, not polish — build it before goals.
12. **Goals (LATE)** — calorie / macro / specific-nutrient targets and progress against them,
    layered on top of the stats views.
13. **User profile (LATE, potential)** — per-account settings beyond credentials: display name,
    **time zone**, unit preferences (metric/imperial, kcal vs kJ), and optional body metrics.
    Also the natural home for the account self-service milestone 2 left out — changing the
    account email, exporting the account's data, deleting the account. Two parts are
    load-bearing rather than cosmetic, so respect them earlier even though the milestone is late:
    - **Time zone decides what "today" means.** The stats views total a *local* day while the
      server stores UTC. Until a per-user zone exists, keep the day boundary in exactly one
      helper (defaulting to UTC or a single configured server zone) so making it per-user later
      is one change rather than a rewrite of every aggregate. The phone knows its own zone,
      which is a tempting shortcut — but the *server* aggregates, so the zone must be stored per
      user rather than sent per request.
    - **Body metrics feed the goal maths.** Goals can suggest a calorie target from BMR/TDEE
      instead of asking the user to invent a number. Goals must still accept a hand-typed
      target, so this is never a hard dependency.
14. **Polish** — offline/queued logging when the phone has no connection, edit/delete entries,
    richer charts, per-nutrient detail views, export, an admin page on the web app.

---

## 10. Explicit non-goals / cautions

- **Trackr is AGPL-3.0-or-later** — the same licence Immich, Nextcloud and Mastodon use, for
  the same reasons. Anyone may run, modify and self-host it, including commercially; anyone who
  modifies it and offers it to others *over a network* owes those users their source. A strong
  deterrent against a hosted commercial fork, **not a prohibition** — do not describe it as one.
  - **Dependencies must be AGPL-compatible.** Permissive (MIT, Apache-2.0, BSD) and
    GPLv3-family licences are fine. **`GPL-2.0-only` is not** — incompatible with v3;
    `GPL-2.0-or-later` is. Proprietary and source-available are out. Check *before* adding a
    package; a conflict found after it is woven in is expensive to undo.
  - **Copyright is the user's alone, and worth keeping that way.** As sole holder they can
    relicense or sell a commercial exception; accepting outside patches without a DCO or CLA
    would end that. Raise it if contributions ever start arriving.
- No pre-loaded global food database; catalog is user-built over time.
- No public/multi-tenant SaaS concerns; this is private and self-hosted.
- Don't hand-roll authentication or password hashing — use Identity.
- Don't send raw images to any third party. Only barcode numbers go to Open Food Facts.
- Don't hardcode the model name or DB provider — keep them swappable via config.
- Don't let the AI write to the DB directly or silently — always confirm-before-save.
- **The interface is a chat, not a form.** Don't build dropdowns, barcode-entry fields, or
  nutrition data-entry screens as the primary flow. The user describes food in natural language
  and optionally attaches a photo; the AI does the rest. Barcode decoding is invisible plumbing.
- Don't let errors vanish. Rate limits, timeouts and parse failures must reach the user as a
  plain warning and/or via the AI's reply — never a silent wrong or empty entry.
- **Don't rebuild the food-logging UI on the web.** The website is accounts and administration.
  If a feature needs a screen, it belongs in the Android app unless it is specifically about
  managing an account or the server.
  - *Historical note:* this rule once implied the app had no sign-up screen. Onboarding was
    moved onto the phone because the premise did not hold for it — an invited household member
    may have no browser to be sent to, and redeeming an invite is the one account task that
    must work before the account exists. Password change, 2FA enrolment, invite minting and
    administration are unaffected and remain web-only; the rule stands for all of them.
- **Don't put logic in `Trackr.Mobile` that could live in `Trackr.Mobile.Core`.** Anything in
  the MAUI project needs the Android SDK to compile and a device to exercise; anything in Core
  is testable with plain `dotnet test`. Keep the MAUI project to XAML, `Shell` wiring and glue.
- Don't duplicate the cascade on the phone. The app sends text and images to the API and
  renders what comes back; barcode decode, OFF lookup and the AI call all stay server-side
  where they can be changed without shipping a new APK.
- A small, kind note for the humans maintaining this: tracking is a tool. If the app ever starts
  to feel like it's driving anxiety around numbers rather than helping, that's a reason to step
  back from it, not to add more tracking.

---

## 11. Working in this repository (for Claude Code)

Claude Code runs in **WSL**. How to build, run the stack, drive the emulator and connect a
phone is in the wiki — [Development-Environment](wiki/Development-Environment.md),
[Building](wiki/Building.md), [Testing-the-Android-App](wiki/Testing-the-Android-App.md).
**Prefer the `just` recipes**; they carry environment and flags that are easy to forget.

What is a constraint rather than a how-to:

- **`just` holds verbs; `scripts/` holds steps.** The justfiles carry build / run / test /
  clean / up / down and little else, deliberately — a task runner listing sixty recipes is one
  nobody reads. Everything finer-grained is a subcommand of a script, each of which prints its
  own help when run with no arguments: `app.sh` (build, install, launch, logs, `shot`, `ui`),
  `device.sh` (pair, connect, usb, reverse, `doctor`, and `ensure`, which finds or sets up a
  device), `emulator.sh` (the AVD), `server.sh` (the dev stack and EF migrations), `lib.sh`
  (sourced by the rest). `just help` lists everything.
  Two reasons this is a rule rather than a preference: recipe dependencies cannot be
  conditional, so "get a device, whatever that takes" is not expressible; and a shebang recipe
  is written to a temporary file before it runs, so `$1` inside one is **not** what the caller
  passed to `just` — a trap that has already caused a real bug here. **A recipe whose body runs
  past a few lines means the logic is in the wrong file**, and a new low-level operation is a
  script subcommand, not a new recipe.
- **Test by actually running it.** Build the images, bring up the compose stack, hit the
  endpoints, tear it down — as part of completing each milestone, not as an afterthought.
- **The WSL Docker engine is an empty, disposable sandbox.** The user's real infrastructure
  lives elsewhere, so containers here may be created and destroyed freely. **One precaution:
  it is intentionally logged out of Docker Hub.** Do NOT attempt to `docker login` or
  `docker push` anywhere. Pulling public base images still works, so builds are unaffected.
- Prefer ephemeral test data; never point dev runs at any real/production volume. Keep dev
  config (ports, test DB creds) separate from anything deployment-related, via env files.
- **Say which testing tier a claim of "it works" rests on.** There are three — view-model tests
  (no device), emulator, and a physical phone — and "the APK builds" and "the app runs" are
  very different statements. Say which one a claim rests on.
  - **The emulator is runnable from Claude Code's own shell on this machine.** It needs the
    invoking user in the `kvm` group and that user is; this file previously implied otherwise.
    So "I could not run it" is not an available excuse for anything short of a physical phone.
    `./scripts/app.sh ui` dumps the on-screen text, which beats a screenshot for asserting a label.
- If logic cannot be covered by a view-model test, that is a sign it has leaked out of
  `Trackr.Mobile.Core` and into the MAUI project.

---

## 12. Optional future extensions (not required)

- A cloud API fallback (e.g. a hosted model) for images the local model struggles with — slot
  it in behind the same AI-parse interface as a lower-confidence backstop.
- **Android share target** — "share" a photo from the gallery or camera straight into Trackr,
  landing in a new chat. A natural fit for the core loop and cheap once the app exists.
- **iOS.** MAUI can target it, but it needs a Mac to build and an Apple developer account to
  install on a device. Out of scope; nothing should actively prevent it.
- Deeper analytics on top of the core stats (long-range trends, nutrient-adequacy views,
  correlations), richer export formats. (Detailed micronutrient tracking and week/month
  summaries are **core** — see §7 and §9 — not future work.)
- Making the website a full second client. Deliberately not the plan (§10), but the API is
  client-agnostic and `Trackr.Shared` is already referenced by both, so nothing blocks it if
  the appetite appears.
