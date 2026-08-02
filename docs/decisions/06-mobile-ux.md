# Milestone 5 — Mobile UX & architecture

Making properly the navigation, styling and storage choices that milestone 3 made minimally
and labelled provisional — *before* the chat UI is built on top of them.

Scoped as a planning milestone. It grew structural code because three of the four questions
could not be answered honestly on paper: whether a shell swap removes the launch flicker,
whether `{x:Static}` binds a route, and whether SQLite links into an APK are all facts about
a build, not opinions about a design. No feature code: nothing here logs a meal.

## The three symptoms this milestone was named for

All three came from milestone 3 and are gone:

- **Auth routing was a post-launch correction.** `App.xaml.cs` opened `AppShell`, Shell landed
  on whichever `ShellContent` was declared first, and a `window.Created` handler navigated away
  a moment later. Every launch flickered through server setup by design.
- **`AuthSession.Changed` had no subscribers.** It was added for a shell swap that never
  happened; grep confirmed zero consumers.
- **Route names were duplicated** as unrelated literals in `AppShell.xaml` and a `Routes` class.
  Renaming either side compiled and failed at runtime inside a fire-and-forget handler — so it
  surfaced as a dead button, not a crash.

## Decisions

### Navigation

- **Two shells, swapped on the `Window`, rather than one shell holding every route.** `AuthShell`
  (server setup, login, register) and `AppShell` (the tabs plus profile) are separate objects, and
  `App` assigns `Window.Page` when `AuthSession.Changed` fires. One shell would keep auth and app
  routes in a single namespace where an absolute `//route` can cross the boundary, and would keep
  the launch correction. The rejected alternative is not hypothetical: it is what shipped in
  milestone 3.
- **The window opens on a neutral loading page that continues the splash colour.** Which shell to
  open depends on whether the stored token still works, and the only honest way to know is to ask
  the server. Showing a blank brand-coloured screen for a moment is better than showing a *wrong*
  screen and correcting it.
- **View models make no navigation call across the auth boundary, and the tests assert it.**
  `INavigationService` lost `GoToHomeAsync` entirely. A view model that navigated on sign-in
  would race the swap, and `Assert.Empty(navigation.ReceivedCalls())` is what keeps that from
  creeping back.
- **`Routes` moved to `Trackr.Mobile.Core` and the XAML binds `{x:Static}`.** Confirmed working:
  the XAML source generator emits `shellContent.Route = Routes.Home` as a direct assignment. The
  guard test written instead covers the opposite gap — a route constant with no `ShellContent`,
  which `{x:Static}` cannot catch.

### Layout: three tabs and an avatar

- **Home | Chat | Trends, and no more.** Goals layer onto Trends rather than earning a tab
  (CLAUDE.md §9.12), re-logging a previous item belongs in the chat rather than in a
  browse-and-pick screen (§10), and history is a drill-down from a day in Trends.
- **The profile is reached from a circular avatar in the title bar, not a fourth tab.** Three tabs
  is already the width a thumb wants, and the profile is somewhere you go occasionally rather than
  one of the two surfaces the app exists for. It is the app's only *pushed* route, so it gets a
  back arrow and returns to the tab it was opened from.
- **The account details moved from Home to Profile.** Email, server and 2FA state were never about
  the home screen; they were there because milestone 3 had one screen. Home becomes the Today
  surface — for now an honest empty state shaped like the target, since nothing can be logged
  until milestone 9.

### Styling

- **Prune the template, then add a thin Trackr layer over Material.** `Styles.xaml` was still the
  untouched MAUI template. Deleted what cannot fire on Android — `TitleBar`, `NavigationPage`,
  `TabbedPage`, `SearchHandler`, the `PointerOver` visual states — plus `Headline` and
  `SubHeadline`, which nothing referenced. Added a five-step type scale and keyed
  `Card` / `ErrorText` / `AvatarCircle` / `PrimaryAction` / `LinkAction` styles, and migrated the
  pages' inline `FontSize` literals onto them. Per-screen hero titles were deliberately left as
  literals: they are one-offs, not a scale step.
- **Dark text on every brand fill, never light.** Not a preference — the brand colours are
  1.8–2.4:1 against `#F2FBFD` and fail contrast the other way round. See
  [04-branding.md](04-branding.md).

### The profile picture

- **Stored on the server, in a table of its own.** It follows the account across devices, which a
  device-local picture would not. `UserAvatar` is separate from the user row so avatar bytes never
  load with an ordinary user query, and the bytes live in Postgres rather than a volume so
  [Backup-and-Restore](../../wiki/Backup-and-Restore.md) stays a single-thing story.
- **`/me` reports a marker, not the bytes.** `TrackrUser.AvatarUpdatedUtc` is a column on the user
  row, so the identity endpoint needs no join, and any client can tell whether its copy is stale
  before asking for an image.
- **The phone resizes before uploading; the server never resizes.** A phone camera produces images
  several times the 512 KB cap, so without it every gallery upload would be rejected. Re-encoding
  also drops the EXIF block, which on a camera photo carries the coordinates it was taken at — not
  something to put on a server because someone picked a profile picture. And the server never has
  to decode attacker-controlled image bytes, which is a category of parser bug worth not having.
- **`AvatarStore` is a singleton that owns the bytes, the ETag and the marker arithmetic.** Two
  places draw the avatar — the title bar and the profile screen — and they must agree the instant
  it changes, without either knowing the other exists.
- **`AuthSession.NoteAvatarChanged` moves the marker with the upload that produced it.** The `PUT`
  response already carries the new marker; re-fetching `/me` to learn it would be a round trip to
  be told something we were just told.

### The local store

- **SQLite in `Trackr.Mobile.Core`, not in the MAUI project.** That is what lets the tests point
  `ILocalStorePath` at `:memory:` and run the *real* SQL — the schema, the upserts, the round-trip
  of a `DateTimeOffset` through a TEXT column — under plain `dotnet test`. A store whose SQL is
  only ever exercised on a phone is a store nobody can change confidently.
- **Hand-written SQL, not EF Core.** The server uses EF Core and should. Here the schema is a
  handful of tables the app owns end to end, and a reflection-driven ORM is a fight with the
  Android linker for nothing.
- **Versioned by `PRAGMA user_version` and an ordered array of migrations.** It is SQLite's own
  four-byte slot in the file header, so the schema version needs no table of its own and cannot
  drift from the file it describes.
- **One connection, held open for the process, behind a semaphore.** SQLite serialises writers
  regardless, so a pool would win nothing; a single connection is also what makes an unshared
  `:memory:` database survive between calls in a test.
- **It arrives with a real job, not empty tables.** The offline log queue it was really built for
  has no schema until milestone 9. Caching the account and the picture is genuine work that would
  otherwise be done twice, and it means the store is exercised the day it lands.
- **`GetMeAsync` reports a status rather than returning a nullable account.** "The server says this
  session is over" and "the server could not be reached" used to arrive as the same `null`, and
  only the first should end a session.
- **A launch with no network opens on the last known account.** Scoped to `RestoreAsync`, and
  resting on one invariant: the cached account is only ever written by a successful `/me` and is
  cleared on sign-out, so it always describes the owner of the stored token. `SignInAsync`
  deliberately does *not* fall back the same way — those tokens may belong to a different account
  than the one cached, and showing the previous user's details to this one would be worse than an
  error message.
- **Sign-out empties the cache, and the avatar row also carries its owner's id.** The second is
  belt and braces: sign-out clears the row, so a mismatch should be impossible. "Should be
  impossible" is a poor reason to hand one account's photograph to another.

### Rejected alternatives

Recorded so they are not revisited:

- **`SixLabors.ImageSharp` for the resize.** The obvious library, and excluded on licence grounds:
  the Six Labors Split License is source-available, which CLAUDE.md §10 rules out.
  `Microsoft.Maui.Graphics` ships with MAUI and adds no dependency at all.
- **On-device barcode-style decoding of the avatar, or any client-side crop UI.** Out of scope; the
  circle crops with `AspectFill` and the 512-pixel copy leaves room to add a crop later without a
  round trip to the original.
- **Declaring `READ_MEDIA_IMAGES`.** MAUI's `PickPhotosAsync` opens the Android system photo
  picker, which grants access to the one chosen file — confirmed on device, where no permission
  prompt appears. The manifest permission would be library-wide access the app never exercises.
  (`PickPhotoAsync`, which the plan named, is obsolete in .NET MAUI 10; `PickPhotosAsync` with
  `SelectionLimit = 1` replaces it, and the caller takes the first result because the limit is
  advisory on Android.)
- **Deriving the avatar ETag client-side from the marker.** It would work, and it would encode the
  server's tag format in the phone. After an upload the tag is simply left null, which means the
  next conditional request is unconditional — once.
- **`Microsoft.Extensions.Hosting` or a configuration package to carry the database path.** Still
  not needed; the path is one property behind one interface, per CLAUDE.md §3.

## Bugs the milestone found by running things

Both were invisible to a build and to the unit tests, and are the argument for CLAUDE.md §11's
"test by actually running it".

- **.NET ticks are 100 ns; Postgres `timestamptz` is microseconds.** The avatar marker returned by
  `PUT` had one more digit than the same value read back through `/me`, so a client cache would
  never match and the phone would re-download the picture forever — the exact opposite of what the
  ETag is for. Fixed by truncating to storable precision on write, pinned by a test.
- **`SQLitePCLRaw` 2.1.11 ships SQLite 3.49.1, before the 3.50.2 that fixes CVE-2025-6965** — a
  memory-corruption bug reachable through a crafted query. `Microsoft.Data.Sqlite` 10.0.10 pulls
  it. The GitHub advisory lists *no* fixed version; 2.1.12 in fact carries SQLite 3.53.3, which was
  established by reading the version string out of the native library in each package rather than
  by trusting the metadata. Pinned via `CentralPackageTransitivePinningEnabled`, beside the
  existing `Microsoft.OpenApi` pin. The 3.0.x line restructures the packages around a separate
  `SQLite` package and is not a drop-in.

## Consequences

- **Milestone 6 and §9.13 work has been pulled forward, deliberately.** A server-stored avatar
  needs an EF entity, a migration and endpoints — data-layer work — and a profile screen is the
  user-profile milestone. This is stated so a later reader does not conclude that §9.13 partly
  shipped by accident. What it does *not* include: display name, time zone, unit preferences, or
  any of the account self-service. The time-zone decision in §9.13 is untouched and still
  load-bearing.
- **`SecureStorage` is no longer the app's only persistence.** CLAUDE.md §3 said it was, which was
  true when the token was the only thing worth keeping. Tokens stay in `SecureStorage`, which is
  Keystore-backed; the database holds the account and the picture and is ordinary app-private
  storage. `allowBackup` is false and `data_extraction_rules.xml` already excluded the `database`
  domain from cloud backup and device transfer — written in anticipation of exactly this file, and
  needing no change.
- **The APK now carries a native SQLite library.** Build times and package size grow accordingly.
- **CLAUDE.md §11 is wrong about the emulator.** It implies running one needs the user's own shell.
  It does not on this machine: the invoking user is in the `kvm` group, and every emulator claim in
  this record was verified from an agent shell. Corrected in §11.

## What was deliberately not done

- **The Android status bar still renders `colorPrimary` cyan and clashes with the white title
  bar.** Pre-existing from the branding change, not introduced here, and it is **not fixed**. Two
  approaches were tried and both fail for structural reasons worth recording:
  - *A theme override.* `MauiAppCompatActivity` switches to `Maui.MainTheme.NoActionBar` inside
    `base.OnCreate`, discarding it.
  - *`Window.SetStatusBarColor` in `OnCreate`.* Compiles with `CA1422` and is a **no-op on API
    35+**; the app targets 36 and the emulator runs 36.

  The real fix is going edge-to-edge — dropping `maui_edgetoedge_optout`, letting the shell paint
  behind the status bar, and handling insets. That is a layout change with its own testing, and it
  wants its own slice. Both attempts were reverted.
- **No offline log queue.** It has no schema until milestone 9, and inventing one now would be
  guessing at a shape the cascade has not settled.
- **No goals, no catalog browse, no chat.** Milestones 9, 10 and 12 respectively.
- **The avatar is not cropped or rotated by the user.** `AspectFill` centres it; EXIF orientation is
  whatever the platform decoder applies. If portrait photos start arriving sideways, that is the
  thing to look at.

## Verifying a change to any of this

Which tier a claim rests on, per CLAUDE.md §11:

- **View-model tests** (`just mobile::test`, no device) cover the route constants, the pick →
  resize → upload decisions, the marker arithmetic, the real SQL against `:memory:`, and the
  offline-restore rules.
- **The emulator** (`just mobile::run`, then `just mobile::ui` for a text dump of the screen) is
  the only way to check the shell swap, the tab bar, the system photo picker and the launch flicker.
  `just mobile::ui` beats a screenshot for asserting on a label.
- **Nothing here can be proved by `just mobile::build` alone.** The build says the APK links; it
  says nothing about whether SQLite's native library loads, which is a separate fact and was
  checked by reading `files/trackr.db` off the device with `adb shell run-as`.
