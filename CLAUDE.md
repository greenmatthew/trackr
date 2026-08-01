# CLAUDE.md — Self-Hosted Nutrition Tracker

This file is the project brief and working agreement for Claude Code. Read it fully before generating code. When in doubt about scope or a design decision, prefer the choices recorded here; if something here is ambiguous, ask before inventing a new pattern.

---

## 1. What we are building

A **self-hosted personal nutrition / calorie tracker** that runs on the user's home server. It is for the user (and possibly a small number of household members) only — not a public product.

**The product is an Android app.** Logging a meal happens on the phone, because that is where you are when you eat. The server is the backend — database, AI, cascade orchestration — and the website is an account and administration surface, not a place to log food. See §3 for what each surface owns.

**The interface is a chat.** This is the defining UX decision and everything else serves it. The app should feel like Claude chat or Google Health: the user starts a new chat, types what they ate in plain language ("two eggs and a slice of toast", "I had 2 of these"), and — optionally — attaches a photo using a **`+` button in the bottom-left of the text box**. They do **not** type barcode numbers, pick from dropdowns, or fill out nutrition forms. The AI figures it out from the text and/or image. Any barcode work happens invisibly behind the scenes (see the cascade in §5) — the user never sees or enters a barcode. Structured confirmation (calories/macros to approve before save) appears *inside the chat flow* as a card the user can correct, not as a separate data-entry screen.

**But the chat is not the whole app — it's paired with an always-visible stats view.** The user needs a clear, glanceable picture of their nutrition, not just a place to log it. One tap from the chat there must be:

- **Today so far — REQUIRED, core.** A running total of the current day's nutrition: calories and full macro/micro breakdown (see §7a), updating as entries are logged and confirmed. This is a necessary part of the basic app, not a polish item.
- **Week / month stats — REQUIRED (basic form).** Rolling summaries and simple trends over the past week and month (daily averages, totals, basic charts). "Basic" is fine — it doesn't need to be elaborate analytics, but it must exist.
- **Goals — LATER milestone.** Ability to set targets (calorie goal, macro targets, specific nutrient goals) and see progress against them. This is explicitly a *late* feature — build the stats views first; goals layer on top of them once the numbers exist.

Layout intent: the chat is the *input* surface and the stats view is the *output* surface, both first-class. On the phone they are two tabs in the app's `Shell`. Don't bury the day's totals behind a menu — logging a meal and immediately seeing the day update is the core loop.

**Design mobile-first and mean it.** One-handed reach, thumb-sized targets, the text box within reach of the bottom of the screen. Do not port a desktop layout onto a phone; the phone *is* the layout.

Core idea: the user logs meals by typing a short description and/or attaching one or more images in the chat. The system:

1. Tries to **decode a barcode** from each uploaded image locally.
2. On a successful barcode decode, looks the product up in the **Open Food Facts public API** to get structured nutrition data.
3. Sends the user's text plus **either** the Open Food Facts structured data (if a barcode matched) **or** the raw image (if no barcode / no match) to a **local LLM served by Ollama** for interpretation (including serving-count math like "I ate 2 of these").
4. Saves the resulting structured entry to a **Postgres** database and grows a personal food **catalog** over time.

There is **no pre-loaded food/ingredient database**. The catalog is built up gradually from Open Food Facts hits and AI reads as the user logs meals.

---

## 2. Guiding principles

- **Privacy first.** Images are decoded for barcodes locally and, when needed, sent only to the *local* Ollama container. The only thing that ever leaves the server is a barcode *number* string sent to Open Food Facts. Raw images never go to any third party.
- **Cascade, not competition.** Barcode → Open Food Facts → local AI fallback → manual entry. Each stage only runs if the previous one didn't resolve the item.
- **Cheap by default.** Prefer the barcode+OFF path (a tiny number lookup) over sending images to the model. Only fall back to the model when necessary.
- **Confirm before save.** The system must show the user what it parsed (calories, macros, servings) and let them correct it *before* writing to the database. Silent wrong numbers are the main failure mode to avoid.
- **Swappable stages.** Barcode decode, OFF lookup, and the AI parse step must each sit behind a clean interface so any one can be swapped (e.g. local Ollama model ↔ a cloud API fallback; SQLite ↔ Postgres) without touching the rest.
- **Maintainable by the user.** The user wants to maintain this themselves in C#. Favor clear, conventional ASP.NET Core / Blazor patterns over clever abstractions.

---

## 3. Tech stack (locked in)

- **Mobile (the product):** **.NET MAUI** targeting **Android**, UI in **XAML**, MVVM via **`CommunityToolkit.Mvvm`**. This is where meals get logged and stats get read.
  - *MAUI is an alternative to WPF/WinUI, not a companion to them* — one project, native controls, its own XAML dialect. `MauiApp.CreateBuilder()` is the same `Microsoft.Extensions.Hosting` generic-host pattern used elsewhere, so DI and `IOptions` work as expected.
  - Split into two projects: **`Trackr.Mobile.Core`** (plain `net10.0` — view models, API client, platform abstractions) and **`Trackr.Mobile`** (the MAUI app and its Android implementations). Core exists so view models are unit-testable with plain xUnit, without the Android SDK or an emulator. **Keep logic in Core**; `Trackr.Mobile` should be XAML, `Shell` wiring and platform glue.
  - *Packages:* `CommunityToolkit.Mvvm` (MVVM, source-generated), `CommunityToolkit.Maui` (converters, behaviors, toast/snackbar, popups), `Microsoft.Extensions.Http` (`IHttpClientFactory` — chiefly for `AddHttpMessageHandler` to attach and refresh the bearer token), `Microsoft.Extensions.Http.Resilience` (retry/timeout, because a phone roams between wifi, cellular and VPN in a way a desktop never does).
  - *Already built into MAUI — do not add packages for these:* dependency injection (`builder.Services` on `MauiAppBuilder`), logging (`builder.Logging`) and configuration (`builder.Configuration`).
  - *Deliberately NOT `Microsoft.Extensions.Hosting`.* `MauiAppBuilder` is modelled on `HostApplicationBuilder` but is not an `IHostBuilder`, and MAUI never runs `IHostedService` — adding it bolts on a host nothing drives. Likewise no `appsettings.json`: the only real configuration is the server URL, which the user types and which belongs in `SecureStorage`.
  - *Cleartext HTTP is blocked, except for the emulator in Debug.* Android has forbidden cleartext by default since API 28 and the app targets 36, so a plain-HTTP server is simply unreachable — which the dev compose stack is, by design. `Platforms/Android/Resources/xml/network_security_config.xml` (Release) forbids it everywhere; `network_security_config.debug.xml` opens it for `10.0.2.2`, `localhost` and `127.0.0.1` only — addresses that can never be a real server. `Trackr.Mobile.csproj` decides which file supplies the resource, so the exception cannot reach a release build by being forgotten. **Verify this by dumping the manifest out of a built APK** (`aapt2 dump xmltree <apk> --file res/xml/network_security_config.xml`), not by reading the source.
  - *Not MAUI Blazor Hybrid.* It would reuse the Razor components, but renders them in a WebView — the thing this design moved away from. See [03-android-pivot.md](docs/decisions/03-android-pivot.md).
  - iOS is out of scope. Nothing should *prevent* it later, but do not spend effort on it.
- **Web:** Blazor WebAssembly (`Trackr.Web`), served by nginx. Scope is **account self-service and administration only** — login, password change, 2FA enrolment, invites, and an admin page later. Any user may log in. It is deliberately **not** a food-logging surface; do not build the chat or stats views here.
  - *Note:* the user initially thought of "Razor Pages." That is server-rendered HTML; Blazor WASM was chosen instead and the choice still stands now that the scope has narrowed.
- **Backend:** ASP.NET Core **Web API** (REST). Serves both front ends.
- **ORM:** Entity Framework Core.
- **Database:** PostgreSQL (containerized).
- **Shared contracts:** `Trackr.Shared` — request/response DTOs referenced by the API, the web app and the mobile app alike. Sharing types by **project reference** rather than generating a client from the OpenAPI document is the main reason everything lives in one repository. Keep it dependency-free: it is trimmed into the browser bundle and linked into the APK.
- **Auth:** ASP.NET Core **Identity** (do NOT hand-roll auth). Password hashing, login, session handling, **2FA (TOTP authenticator apps)**, and account **lockout** all come from Identity. See §8 for the full auth-hardening list. **Two schemes, by client:**
  - *Web → HttpOnly cookie.* Same origin (nginx serves the app and proxies `/api/`), so the browser handles the session itself and JavaScript cannot read it — an XSS bug cannot steal the session. Every hardening decision in [02-auth.md](docs/decisions/02-auth.md) stands unchanged.
  - *Android → Identity bearer token.* The app is a native cross-origin client and cannot hold a cookie usefully, so it gets `IdentityConstants.BearerScheme` with a refresh token, stored in Android `SecureStorage`. This is **additive** — it changes nothing about the cookie path.
  - *Historical note:* §3 previously read "cookies, not JWTs" on the reasoning that tokens only matter for "cross-origin / multi-service / native-mobile setups, none of which apply." The Android app made that premise false. The conclusion still holds for the browser, which is why both schemes coexist rather than one replacing the other.
  - *No CORS.* MAUI uses a native `HttpClient`, and CORS is a browser-only mechanism. Do not add CORS configuration for the app — it would be pure attack surface.
- **Local AI:** **Ollama** container serving a small multimodal (vision) model, callable over the Docker network via its HTTP API (default port 11434).
- **Barcode decode:** happens **invisibly** — the user never types a barcode number and never sees one. It's an internal optimization on any attached photo, not a user-facing feature. A barcode library attempts to decode the image; if it succeeds, the cascade uses it, and if it doesn't, the image just goes to the AI as normal. Two acceptable approaches — pick during implementation and note the choice:
  - *Server-side* (simpler; good default for the chat-first design): a .NET barcode library decodes the uploaded image in the backend. Since the user is attaching stills in a chat rather than live-scanning, this fits cleanly. It also keeps the decode in one place rather than one per client.
  - *On-device* (optional): Android exposes real barcode scanning to a native app, so this is available whenever it's wanted — but it is an optimisation, not a requirement, and it duplicates logic the backend needs anyway for images that arrive without a decode. Prefer server-side first; revisit if the round trip proves slow.
  - *Note:* the vision model can often read a barcode (or just recognize the product) straight from the image anyway, so a decode failure is not a dead end — it's simply the AI-fallback path.
- **Nutrition data:** Open Food Facts **public API** over the internet (`https://world.openfoodfacts.org/...`). We do **not** self-host the Open Food Facts database — a lookup is just a small HTTP GET by barcode number. OFF returns a rich `nutriments` object (per-serving and per-100g), so **map through the full nutrient set from §7a**, not just macros — fiber, sugars, sodium, saturated fat, vitamins and minerals when present — into the extensible nutrient store.
- **Orchestration:** Docker Compose. The user deploys via **Portainer** and Docker Compose, and already runs a **reverse proxy** (TLS termination) in front of their services. Write `docker/docker-compose.yml` to be Portainer-stack-friendly (clean named services, env vars for config/secrets, named volumes, an external network for the reverse proxy to attach to). TLS is handled by the existing reverse proxy, not inside these containers.

---

## 4. Container / service layout

Services on a shared Docker network, orchestrated by `docker/docker-compose.yml`:

1. **frontend** — nginx serving the built `Trackr.Web` static assets, and reverse-proxying `/api/` to `backend`. This is also the address the Android app points at.
2. **backend** — the ASP.NET Core Web API (auth, cascade orchestration, DB access, OFF calls, Ollama calls). Barcode decode lives here if the server-side approach is chosen.
3. **ollama** — Ollama serving the local vision model. Always running, but configured so the *model* unloads from RAM when idle (see §6).
4. **db** — PostgreSQL, with a persistent volume.

Open Food Facts is **not** a container — it's an external public API the backend calls.

The database may be Postgres from day one (recommended, since we want a real user/catalog store). Keep the data layer behind EF Core so the concrete provider is swappable.

The Android app is **not** a container. It ships as an APK installed on the phone and talks
to `frontend` over the network like any other client.

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
    - Computes serving math (e.g. "2 of these" = 2 × serving size × per-serving macros).
    - Produces calories + the full nutrient set from §7a per item and a total —
      protein/carbs/fat always, plus fiber, sugars, saturated/trans fat, sodium,
      cholesterol, vitamins and minerals **whenever they can be determined**
      (from a visible label, or reasonable estimate from a described food).
      Each nutrient reported with its unit. Omit (or null) nutrients it can't
      determine rather than inventing them — a missing micro is fine, a
      hallucinated one is not.

Validate the model's JSON before trusting it (this is REQUIRED — small local
models will sometimes emit broken JSON or numbers that don't add up):
    - Parse defensively. If it's not valid JSON, or required fields are missing,
      do NOT save — show an error in the chat and let the user retry or enter
      values manually.
    - Sanity-check the numbers: calories should roughly reconcile with the macros
      (~4 kcal/g protein, ~4 kcal/g carbs, ~9 kcal/g fat). If they're wildly off,
      flag it on the confirmation card as low-confidence rather than presenting it
      as fact.
    - The validator is the main thing standing between the model and a wrong number
      in the database. It's cheap to write and worth it.

Show the parsed result to the user for confirmation / correction.

On confirm:
    - Write the log entry to Postgres.
    - Upsert any new item into the personal food catalog so it's reusable later.
```

Key point to preserve in code: **fully-matched barcode items must NOT send their image to the model** — they send the OFF structured data instead. This is the token/cost/accuracy win. (Partial matches still send the image, alongside whatever data OFF returned.)

### Error / exception handling (surface problems, never fail silently)

Every stage can fail — OFF can rate-limit or time out, barcode decode can throw, Ollama can be unreachable. These must never silently disappear or produce a wrong number.

- **Pass errors along to the AI.** When assembling the AI request, include a short description of any exception from an earlier stage (e.g. "Open Food Facts returned HTTP 429 rate limit"). The prompt should let the AI relay this to the user in plain language when relevant ("I couldn't reach the food database, so I estimated from your photo instead").
- **Also surface a plain warning/error in the chat UI** regardless of what the AI says — e.g. a small "⚠ Open Food Facts rate-limited; used photo estimate" banner on the confirmation card. The user should always know when a fallback happened and why, so they can judge how much to trust the numbers.
- If the AI stage itself fails (Ollama down, unparseable output), show a clear error in the chat and let the user retry or enter values manually — never save a guessed or empty entry.

---

## 6. Ollama / RAM behavior

The user was concerned about the model holding several GB of RAM permanently.

- The **Ollama container stays running** (it is lightweight when idle).
- The **model** is what consumes RAM. Ollama unloads it after an idle period. Configure `keep_alive` (via the API request field or `OLLAMA_KEEP_ALIVE` env var) to a short value so the model drops out of RAM between uses and reloads (a few seconds on CPU) on the next request.
- Do **not** try to stop/start the whole container per request — that adds complexity for little benefit.
- The server has ample RAM (~94 GiB, ECC), so a **mid-size vision model** (a 7–12B-class VLM) is viable for production for better label reading. CPU-only inference is fine here because meal logging is not latency-sensitive — a few seconds per image is acceptable. (*VLM = Vision-Language Model — a model that takes images + text together, i.e. the thing that reads the food photo.*)
- **For initial development, start with a tiny model (~1–2 GB) even if its answers are bad.** The goal early on is to get the whole pipeline working end to end — image in, JSON out, validated, confirmed, saved. Answer quality doesn't matter yet. Swap up to a larger/better model once the plumbing works. This is why the model name lives in config, not code.
- **Test the model before committing to it for real use.** Label OCR is exactly where small VLMs struggle most, and CPU inference on a high-res image can be slower than "a few seconds." Before settling on the production model, try it against a handful of real nutrition-label photos and check it actually reads them acceptably. Treat model selection as a quick experiment, not an afterthought. Fine-tuning is a possible *later* step if no off-the-shelf model is good enough — not needed now.
- Pick the concrete model during implementation and record it here. Keep the model name in config, not hardcoded, so it's swappable. AI's discretion on the specific choice until there's a reason to fine-tune.

---

## 7. Data model (initial sketch — refine in code)

- **User** — via ASP.NET Core Identity (Id, credentials, etc.).
- **FoodItem (catalog)** — id, name, brand (nullable), barcode (nullable), serving size + unit, source (`off` | `ai` | `manual`), created/updated timestamps, owning user, **plus a full set of per-serving nutrients** (see below).
- **LogEntry** — id, user, timestamp/date, free-text note (nullable).
- **LogItem** — id, log entry id, food item id (nullable if ad-hoc), quantity (number of servings), **full computed nutrient snapshot** at time of logging.

### Nutrient storage — design for many nutrients, not just macros

The user wants to track **everything worth tracking that appears on a nutrition label** — not only calories and the carb/fat/protein split, but fiber, sugars, saturated/trans fat, sodium, cholesterol, and vitamins/minerals. That's dozens of possible fields, and the set will grow.

**Do NOT model this as one fixed column per nutrient** — that turns every new nutrient into a schema migration and leaves most rows full of nulls. Instead use an extensible shape:

- Preferred: a **`Nutrient` reference table** (id, key e.g. `vitamin_c`, display name, unit e.g. `mg`/`µg`/`g`, canonical sort order for label-style display) + a **per-item `NutrientAmount`** join (item/snapshot id, nutrient id, amount). Adding a nutrient = inserting a row, not altering the schema.
- Acceptable alternative: a **JSONB column** on FoodItem / LogItem holding a nutrient→amount map, with a small code-side registry defining each nutrient's unit and display order. Simpler to start, still schema-stable; trade-off is weaker query ergonomics for aggregation.
- Either way, **keep calories and the big three macros (protein / carbs / fat) as first-class, always-present fields** too, since they drive the main dashboard and goals math and you don't want to join/parse for the common case. The extensible store covers everything beyond those.
- Every nutrient carries an explicit **unit**; never assume grams. Vitamins are often mg or µg.
- Nutrients are frequently **unknown/missing** (a photo estimate won't have vitamin C). Store null/absent, distinguish "known to be zero" from "not measured," and have the dashboard show missing micros gracefully rather than as 0.

Store the **full nutrient snapshot** on the LogItem (via `NutrientAmount` rows or the JSON map), not just macros, so historical logs never change if a catalog item is later edited.

### 7a. What to track (target nutrient set)

Aim to capture, per item and per serving, as available from OFF or the AI read. Full presence is not required — capture what the source provides:

- **Energy:** calories (kcal). (kJ optional.)
- **Macros:** protein, total carbohydrate, total fat. (Always present / first-class.)
- **Fat breakdown:** saturated fat, trans fat, (mono/polyunsaturated if available).
- **Carb breakdown:** dietary fiber, total sugars, added sugars.
- **Sterols/electrolytes:** cholesterol, sodium, potassium.
- **Vitamins:** A, C, D, E, K, and B-complex (B1/thiamin, B2/riboflavin, B3/niacin, B6, B9/folate, B12) as available.
- **Minerals:** calcium, iron, magnesium, zinc, and others when present.

Keep the set **config-/data-driven** (that's the point of the `Nutrient` table / registry) so adding "selenium" later is a data change, not a code change. The confirmation card and dashboard should render whatever nutrients are present in label order, and not clutter the view with ones the source didn't provide.

---

## 8. Security posture

Treat the data as sensitive personal health information in terms of *care*, without claiming formal regulatory compliance (this is a personal self-hosted tool for the user's own data; formal HIPAA compliance is a heavy legal regime that does not meaningfully apply here).

Concretely:
- All access is behind login (ASP.NET Core Identity). No unauthenticated endpoints except login (and registration, if enabled at all).
- Enforce **HTTPS** in transit. TLS is terminated at the user's existing **reverse proxy** in front of the app.
- Passwords hashed by Identity (never store plaintext).
- Use secure, HttpOnly cookies or properly-scoped JWTs for the session.
- Data stays on the user's server; images are not sent to third parties; only barcode numbers go to Open Food Facts.
- Keep secrets (DB creds, any API keys for an optional cloud fallback) in environment variables / a secrets file, never committed.

### Auth hardening (wanted features)

The user explicitly wants a properly secured account system. Implement, roughly in order of value:

1. **Two-factor authentication (2FA) — REQUIRED.** Use Identity's built-in TOTP support (authenticator apps like Google Authenticator / Authy). Flow: the user enables 2FA in settings → the app displays a **QR code** → they scan it with their authenticator app → the app then generates a rolling 6-digit code every ~30 seconds, and the user enters the current code at login. Confirm enrollment by having the user type one valid code before turning 2FA on, and issue **recovery codes** (shown once) so they can still get in if they lose their phone. SMS 2FA is possible but not needed; authenticator-app TOTP is the target. All of this is built into Identity — render the QR and verify codes, don't build the TOTP algorithm yourself.
2. **Account lockout — REQUIRED.** Enable Identity's lockout so N failed login attempts locks the account for a cooldown. This is the primary defense against brute-force / password guessing.
3. **Rate limiting — REQUIRED.** Apply ASP.NET Core's built-in rate-limiting middleware to the auth endpoints (login, register, 2FA) to blunt automated attempts.
4. **Registration lockdown.** Since this is a private tool for the user (and maybe household), do NOT offer open public sign-up. Either disable registration after the first account(s) are created, or make it invite-only.
5. **Network-level protection (strongest, simplest).** The single most effective protection for a personal tool is to not expose the login to the public internet at all — reach it via VPN / LAN only, or gate it at the reverse proxy. Note this to the user as the recommended posture; it makes bot/brute-force concerns nearly moot on its own.

*(reCAPTCHA was considered and deliberately dropped: on a VPN/LAN-only personal tool the login isn't public, so it would add a Google dependency and login friction for essentially no benefit. Lockout + rate limiting + network-level protection already cover brute-force. If the login is ever exposed publicly, revisit this.)*

---

## 9. Build order (suggested milestones)

1. ~~**Scaffold**~~ — ✅ **DONE.** Solution with a Blazor WASM frontend, ASP.NET Core Web API backend, EF Core + Postgres, Docker Compose bringing up db + backend + frontend. Health-check endpoint, verified end to end in the browser. Decisions in [01-scaffold.md](docs/decisions/01-scaffold.md); how to run it is in `README.md`.
2. ~~**Auth**~~ — ✅ **DONE.** ASP.NET Core Identity behind an HttpOnly cookie: bootstrap-then-invite-only registration, login, TOTP 2FA with QR enrolment and recovery codes, account lockout, rate limiting, password change and log-delivered reset, and a fully authed web app (auth state, protected routes, login/settings pages). First EF migration and first test project. Decisions in [02-auth.md](docs/decisions/02-auth.md); account handling is in `README.md`.
3. **Mobile foundation** — the thin end-to-end slice, deliberately **before** the backend milestones because the Android toolchain and the token auth were the two unknowns in the pivot. Bearer-token scheme on the API alongside the cookie (`/api/auth/token`, refresh, optional 2FA code in one request); `Trackr.Mobile.Core` + `Trackr.Mobile`; a first-run **server-URL** screen, login, 2FA, and one placeholder page. Done when a signed APK installs on a real phone, points at the server and logs in through 2FA. No food logging.
4. **Mobile UX & architecture planning** — a *planning* milestone, no feature code. The thin slice in milestone 3 makes the minimum viable choices to get a screen on a phone; this is where they get made properly, before the chat UI is built on top of them. Cover at least:
    - **Navigation** — `Shell` versus plain navigation, how the chat and stats tabs are laid out, and where server-setup and login sit relative to the signed-in shell.
    - **Styling and theming** — resource dictionaries, light/dark handling, and whether to lean on default Material styling or build a custom theme.
    - **Local storage** — what the app persists beyond the token: cached stats, an offline log queue, and whether that needs SQLite now or waits for §9.13.
   Record the outcome in `docs/decisions/` and revise milestone 3's provisional choices to match.
5. **Data layer** — EF Core entities + migrations for FoodItem, LogEntry, LogItem, **and the extensible nutrient store** (`Nutrient` + `NutrientAmount`, or JSONB map) from §7, seeding the §7a nutrient set. Basic CRUD API for catalog and log. Confirm you can store and read back a full multi-nutrient item, not just macros.
6. **Barcode + Open Food Facts** — invisible barcode decode (default to server-side; record the choice), OFF lookup by number, map OFF response → FoodItem shape. Send a proper descriptive **User-Agent** on OFF requests (app name + version + contact) — OFF asks for this and may throttle callers without it. Handle full-match, partial-match, and no-match cases per §5, and surface rate-limit/timeout errors rather than swallowing them.
7. **Ollama integration** — add the ollama service, wire the backend to call it, define the strict-JSON prompt, implement the image-vs-structured-data swap, parse and validate the JSON. Configure `keep_alive`.
8. **Chat UI + cascade + confirm** — build the chat interface **in the Android app** (new-chat flow, message list, text box with a `+` button in the bottom-left to attach images) and wire the full cascade from §5 into it. The parsed result appears as an in-chat **confirmation card** (calories/macros/servings, editable) with any fallback warnings (e.g. rate-limited), and only writes to the DB on confirm. Include the serving-count math. This is also where the camera and photo-picker permissions land.
9. **Catalog growth** — upsert items from OFF/AI into the catalog; let the user pick from previously logged items for fast re-logging.
10. **Stats views (REQUIRED)** — the output surface from §1, a tab in the app. "Today so far" running totals (calories + full nutrient breakdown, updating as entries are confirmed), then week/month summaries with basic trend charts. Aggregations read from the nutrient snapshots on LogItems. This is core, not polish — build it before goals.
11. **Goals (LATE)** — let the user set calorie / macro / specific-nutrient targets and show progress against them, layered on top of the stats views. Explicitly a late milestone.
12. **User profile (LATE, potential)** — per-account settings beyond credentials: display name, **time zone**, unit preferences (metric/imperial, kcal vs kJ), and optional body metrics (height, weight, age, sex, activity level). Also the natural home for the account self-service milestone 2 deliberately left out — changing the account email, exporting the account's data, deleting the account. Two parts are load-bearing rather than cosmetic, so respect them earlier even though the milestone itself is late:
    - **Time zone decides what "today" means.** The stats views (§9.10) total a *local* day while the server stores UTC. Until a per-user zone exists, keep the day boundary in exactly one helper (defaulting to UTC or a single configured server zone) so making it per-user later is one change rather than a rewrite of every aggregate. Note the phone knows its own zone, which is a tempting shortcut — but the *server* does the aggregation, so the zone still has to be stored per user rather than sent per request.
    - **Body metrics feed the goal maths.** Goals (§9.11) can suggest a calorie target from BMR/TDEE instead of asking the user to invent a number. Goals must still accept a hand-typed target, so it never hard-depends on this milestone.
13. **Polish** — offline/queued logging when the phone has no connection to the server, edit/delete entries, richer charts, per-nutrient detail views, export, an admin page on the web app.

Do each milestone as a working, testable slice before moving on. Keep the three cascade stages (barcode, OFF, AI) behind interfaces from the start so they stay swappable.

---

## 10. Explicit non-goals / cautions

- No pre-loaded global food database; catalog is user-built over time.
- No public/multi-tenant SaaS concerns; this is private and self-hosted.
- Don't hand-roll authentication or password hashing — use Identity.
- Don't send raw images to any third party. Only barcode numbers go to Open Food Facts.
- Don't hardcode the model name or DB provider — keep them swappable via config.
- Don't let the AI write to the DB directly or silently — always confirm-before-save.
- **The interface is a chat, not a form.** Don't build dropdowns, barcode-entry fields, or nutrition data-entry screens as the primary flow. The user describes food in natural language and optionally attaches a photo (via the `+` button); the AI does the rest. Barcode decoding is invisible plumbing the user never sees.
- Don't let errors vanish. Rate limits, timeouts, and parse failures must reach the user as a plain warning and/or via the AI's reply — never a silent wrong or empty entry.
- **Don't rebuild the food-logging UI on the web.** The website is accounts and administration. If a feature needs a screen, it belongs in the Android app unless it is specifically about managing an account or the server.
- **Don't put logic in `Trackr.Mobile` that could live in `Trackr.Mobile.Core`.** Anything in the MAUI project needs the Android SDK to compile and a device to exercise; anything in Core is testable with plain `dotnet test`. Keep the MAUI project to XAML, `Shell` wiring and platform glue.
- Don't duplicate the cascade on the phone. The app sends text and images to the API and renders what comes back; barcode decode, OFF lookup and the AI call all stay server-side where they can be changed without shipping a new APK.
- A small, kind note for the humans maintaining this: tracking is a tool. If the app ever starts to feel like it's driving anxiety around numbers rather than helping, that's a reason to step back from it, not to add more tracking.

---

## 11. Development environment & self-testing (for Claude Code)

Claude Code runs in **WSL** and should **test the stack by actually running it** — building images, bringing up the compose stack, hitting endpoints, and tearing it down as part of completing each milestone.

- The WSL Docker engine here is an **empty, disposable sandbox** — the user has no other containers or images on it, and their real infrastructure (Portainer, reverse proxy, production stacks) lives elsewhere. So no special daemon isolation is needed; Claude Code may use this Docker engine directly and freely create/run/destroy this project's containers to test.
- **One precaution:** the environment is intentionally **logged out of Docker Hub** (`docker logout`) so nothing can be pushed to the user's registry account. Do NOT attempt to `docker login` or `docker push` anywhere. Pulling public base images still works fine while logged out, so builds are unaffected.
- Prefer ephemeral test data; never point dev runs at any real/production volume.
- Keep dev config (ports, test DB creds) separate from anything deployment-related, via env files.

### Building

A bare `dotnet build` at the repository root needs the `maui-android` workload, because
`Trackr.Mobile` is in the solution. For backend-only work, name the projects:

```
dotnet build src/Trackr.Api src/Trackr.Web src/Trackr.Shared
dotnet test  tests/Trackr.Api.Tests          # needs Docker (Testcontainers)
```

The Dockerfiles already restore and publish specific `.csproj` files, so images are unaffected.

**Prefer the `just` recipes** — they carry the environment and flags that are easy to forget:
`just server::up`, `just server::watch`, `just mobile::run`, `just mobile::doctor`, `just test`.
Recipes live in `just/server.just` and `just/mobile.just`, dispatched from the root `Justfile`.
Two gotchas if you edit them: a module runs from **its own file's directory**, hence
`set working-directory := '..'`; and `just` uses the **last** comment line above a recipe as
its `--list` description, so longer notes belong inside the recipe body.

Debug APKs set `EmbedAssembliesIntoApk=true`. Without it, Fast Deployment leaves the managed
assemblies outside the APK, and any APK installed by `adb install` rather than `dotnet run`
aborts on launch with `No assemblies found in .../.__override__/x86_64`. Do not remove it.

### Testing the Android app

Three tiers. Only the first two are automatable — **say which one a claim of "it works" rests
on**, because "the APK builds" and "the app runs" are very different statements.

1. **View models** — `tests/Trackr.Mobile.Tests`, plain xUnit against `Trackr.Mobile.Core`. No
   device, no emulator, no workload. Most logic should be coverable here; if it isn't, that's
   a sign logic has leaked into the MAUI project.
2. **Emulator** — the `trackr-test` AVD, driven through `scripts/emulator.sh`. Headless for
   automated checks, or `start --window` to put a real window on the Windows desktop via
   WSLg. What can be done from the command line here is more than it first appears:
   - `adb exec-out screencap -p > shot.png` — a PNG that can actually be looked at.
   - `adb shell uiautomator dump` — the whole view hierarchy as XML. Prefer this to
     screenshots for assertions: it can confirm a specific label's *text*, and it gives real
     coordinates instead of guessing where to tap.
   - `adb shell input text ... / input tap X Y / input keyevent` — drive the UI.
   - `./scripts/emulator.sh logcat` — the app's own `ILogger` output.
3. **Physical phone** — over wireless debugging (`adb pair`, then `adb connect <ip>:5555`).
   WSL's NAT handles outbound fine, so `usbipd-win` is **not** needed unless you specifically
   want USB. Required for camera work, and the only real check of the reverse-proxy path.

**Reaching the dev stack from an emulator:** use `http://10.0.2.2:8000` — `10.0.2.2` is the
emulator's fixed alias for the host's loopback. This works only because Debug builds ship a
narrow cleartext exception; see the network-security-config note in §3. A **physical phone**
cannot use that address at all, and should point at the real HTTPS server instead.

Toolchain, if it needs rebuilding: `dotnet workload install maui-android`, the Android SDK
command-line tools, and **JDK 17** specifically — .NET for Android does not accept newer JDKs,
so a machine with only JDK 21/25 installed will fail with a confusing error.

On this machine both are installed under `$HOME`, no root involved, and every mobile build
needs them exported:

```
export JAVA_HOME=$HOME/.jdks/microsoft-17
export ANDROID_HOME=$HOME/Android/Sdk
export PATH=$ANDROID_HOME/platform-tools:$JAVA_HOME/bin:$PATH
```

`platform-tools` must come **before** `/usr/bin`. Debian ships its own `adb` (34.x) while the
SDK has 37.x, and adb refuses to work across a client/server version mismatch — whichever
starts the server first wins and kills the other. The symptom is a device that never appears,
which looks nothing like a PATH problem. `scripts/emulator.sh` sets this itself.

The **emulator** additionally needs the invoking user in the `kvm` group
(`sudo usermod -aG kvm $USER`, then restart WSL) — that step needs the user's own shell.
Building and signing an APK does not, so a build can always be verified even when a run
cannot. Say which of the two a claim rests on.

---

## 12. Optional future extensions (not required)

- A cloud API fallback (e.g. a hosted model) for images the local model struggles with — slot it in behind the same AI-parse interface as a lower-confidence backstop.
- **Android share target** — "share" a photo from the gallery or camera straight into Trackr, landing in a new chat. A natural fit for the core loop and cheap once the app exists.
- **iOS.** MAUI can target it, but it needs a Mac to build and an Apple developer account to install on a device. Out of scope; nothing should actively prevent it.
- Deeper analytics on top of the core stats (long-range trends, nutrient-adequacy views, correlations), richer export formats. (Detailed micronutrient tracking and week/month summaries are now **core** — see §7a and §9 — not future work.)
- Making the website a full second client. Deliberately not the plan (§10), but the API is client-agnostic and `Trackr.Shared` is already referenced by both, so nothing blocks it if the appetite appears.
