# Milestone 9 — Chat UI, cascade and confirm

The core loop, on the phone: say what you ate, attach a photo, approve the numbers. Every server
piece already existed — milestone 8 collapsed the whole cascade into one `POST /api/analyze` that
writes nothing — so this milestone is the Android client and the confirmation card, plus the one
piece of plumbing without which none of it works.

## The client fix that had to come first

`Trackr.Mobile.Core/ServiceCollectionExtensions.cs` configured a single `HttpClient` with a
30-second timeout and `AddStandardResilienceHandler()`. Against a two-minute analysis that is not
slow, it is **four inference jobs queued for one meal**: the timeout fires, the pipeline retries,
and the user is shown a failure while the server burns CPU on three requests nobody awaits.

So analysis gets a **second, named client** — long timeout, no resilience pipeline:

- **Five minutes**, deliberately looser than the server's own 240-second model timeout plus the
  barcode and Open Food Facts stages in front of it. When an analysis runs long, the useful outcome
  is the server's explanation; a client that gave up first would replace it with "the server took
  too long to answer".
- **No resilience handler at all.** A retry here has to be the user's decision, because each one
  costs minutes of somebody's CPU. `BearerTokenHandler` stays, because its single retry is on a 401
  — a request that never reached the model.

`GET /api/nutrients`, `POST /api/images` and `POST /api/log` are ordinary short calls and stay on
the original client.

## Decisions

### The transcript is four types, not one with a flag

`UserMessage`, `NoteMessage`, `WarningMessage` and `ConfirmationCard` are separate classes under a
`ChatMessage` base, selected by a `DataTemplateSelector`. They are drawn completely differently — a
bubble, a sentence, a warning strip and an editable card — and a template selector needs a type to
switch on. Nothing is ever removed from the transcript: a card stays in whatever state it ended in,
so scrolling back shows what was approved rather than a gap where a decision used to be.

**The model's note and the server's warnings are separate messages.** CLAUDE.md §5 requires the
warnings to reach the user *independently* of what the model says, so they are rendered as their own
strip rather than folded into the note. A failed analysis shows both and produces no card at all —
a card is a thing that can be saved.

### Confirming is a copy, and the edits happen upstream of it

`MealAnalysisItem` and `SaveLogItemRequest` are kept field-for-field identical on purpose (there is a
drift test). This milestone adds `ToSaveLogItemRequest`, which is a copy and **deliberately nothing
else** — no rounding, no defaulting, no arithmetic — because anything clever there would be a second
place for a number to change between what the user approved and what was stored.

Edits therefore live on `ConfirmableItem`, which holds the analysed record and produces
`analysed with { … }`. Everything the user did not touch — the nutrient map, the barcode, which
photo it came from — carries through untouched, which is what keeps the copy a copy rather than a
reconstruction.

**Editable numbers are exposed as text, not as decimals.** Binding an `Entry` to a `decimal` makes a
half-typed value ("1." on the way to "1.5") either an exception or a silent revert, and gives no way
to say a field is wrong. Text in, parsed here, and `IsValid` is what the confirm button depends on —
so a number that could not be read stops the save rather than quietly becoming zero. Quantity must
be above zero; the rest may be zero, because "known to be zero" is a real measurement.

### Values are per serving and the server multiplies

The card shows per-serving figures with the total spelled out beneath ("2 x 78 kcal = 156 kcal"),
because that is the shape both contracts use and milestone 8 moved the multiplication to the server
on purpose. The meal total on the card is a second computation of the same thing, for reading only;
nothing computed in the client is ever sent.

### Two rules inherited from earlier milestones, and where they landed

- **A confident wrong answer is the expensive failure** (milestone 7), which milestone 8 answered
  with `MealAnalysisReader`. The card is the last place it applies, so a `Confidence: Low` item gets
  a bordered warning strip of its own and its `Warnings` are rendered **without a tap**. Rendering
  them as decoration would waste the whole validator.
- **Absent means not measured, never zero** (7a, 8). The card lists only the nutrients the source
  actually reported, in the catalog's label order. A card padded with "-" rows for the two dozen
  nobody measured would quietly teach the reader the opposite.

The nutrient names and units come from `GET /api/nutrients` through a `NutrientCatalogCache`, held
in memory for the process. The server owns that vocabulary and adding to it is a data change, so a
list hardcoded on the phone would render a new nutrient against the wrong name or in the wrong unit.
**A failed catalog fetch is not a failed analysis**: the card still shows its calories and macros,
which are typed fields, and simply omits the micronutrient rows — raw keys would be worse than
nothing.

### Photos: uploaded on send, kept by id, camera first

- **Uploaded on send rather than on pick**, so backing out of a half-composed message leaves nothing
  on the server. The analysis behind the upload is far longer than the upload, so it is not the part
  anybody notices.
- **A failed analysis keeps the uploaded ids** and offers "Try again", which re-analyses without a
  second upload and without a second copy. This is CLAUDE.md §5's retry, made free.
- **An upload failure stops the send** rather than analysing what did upload. Half a plate read as
  the whole of it is a confidently wrong number, which is the failure everything here is arranged
  around. The already-uploaded photos are left unreferenced, which costs a few megabytes.
- **Camera capture is new** (`IPhotoPicker.CaptureAsync`) and comes first in the `+` menu, because
  §1 puts logging on the phone precisely because that is where you are when you eat. It brings
  `android.permission.CAMERA` with it — verified by dumping the built APK, not by reading the
  manifest — with `uses-feature required="false"` so the app still installs on a device without one.
  Still **no `READ_MEDIA_IMAGES`**: the gallery picker hands back a URI the system has already
  granted for the one chosen file.
- **The choice is two buttons bound to two commands**, not a platform action sheet. "Camera or
  gallery" stays a decision the view model exposes and a test can drive, rather than logic in the
  MAUI project.

**Photos are re-encoded at 2560px on the longest edge.** `MealImageRules` deliberately has no such
limit — a meal photo is stored at full resolution so re-running a better model later is never
foreclosed — and this bounds what "full" can mean rather than contradicting it: MAUI's picker hands
over a stream with no content type, so the phone re-encodes every image anyway, both to produce
something the server's allow-list accepts and to drop the EXIF block, which on a camera photo
carries the coordinates the meal was eaten at. 2560 is several times what the server itself
downsizes to before inference (1280), so the stored copy stays better than anything the current
model sees.

### No client-side cap on the number of photos

The server caps it (`Trackr:Ollama:MaxImages`, 4 by default) and says so in a validation message
naming the real number. That is server configuration, so a constant on the phone could only ever be
wrong; the cost of not having one is that a sixth photo is uploaded before being refused.

### The composer, and the one thing the view model cannot own

`SafeAreaEdges="Container, Container, Container, SoftInput"` — system bars respected on three edges,
the keyboard on the fourth, so the composer rises above the IME instead of being typed into blind.
This is the screen the edge-to-edge work that preceded this milestone was done for.

Scrolling to the newest message lives in the page's code-behind, dispatched, because a freshly added
item has not been measured yet and scrolling to something with no height lands short. Where a list
is scrolled is not something Core can have an opinion about.

## Verified

Tier 2 (emulator, API 36) end to end against the real compose stack, plus 17 view-model tests:

- **Text only.** "two boiled eggs and a slice of wholemeal toast" → a card whose toast item was
  flagged `Low` with both of the validator's complaints readable on it. Saved, and the rows in
  Postgres carried the totals the card showed (`2 x 150 = 300`).
- **Photo.** A real barcode photograph from `media/examples` → decoded, matched in Open Food Facts,
  and drawn as "Ben & Jerry's / Chocolate Therapy", source *from Open Food Facts*, with seven
  micronutrients in catalog order and units — saturated fat 11 g, cholesterol 45 mg, vitamin A
  120 µg — and nothing for the ones it did not report.
- **The gallery picker, the attachment strip and its remove badge**, the busy state and Cancel.

**Two bugs the device found that the tests had not.** A photo with no text left `Send` greyed out,
because a `Button` takes its enabled state from `ICommand.CanExecute` rather than from the property
the command's method happens to read — and a wordless photo log is exactly what the camera is for.
And the remove badge was a `Button`, which Material gives a minimum touch size that swallowed most
of the thumbnail it annotated; it is now a tap on the thumbnail with a drawn badge. Both now have
tests.

## Left open

*(Amended: the first and second are fixed, in the branch recorded by
[12-catalog-growth.md](12-catalog-growth.md). The fourth is what that milestone is.)*

- **A quantity the validator does not catch.** **Fixed, and the first attempt was in the wrong
  place** - the check was added to `MealAnalysisReader`, which judges what the model said, and this
  item was a full Open Food Facts match whose figures had all been replaced. It now runs on the
  assembled item, in `PortionCheck`. On the Open Food Facts run the model returned
  `Quantity: 131` — the serving's gram weight echoed as a count — giving a cheerful *43230 kcal*
  meal total with **no** low-confidence flag. The card did its job, in that the number is enormous
  and sits in an editable box, but this is a `MealAnalysisReader` gap rather than a client one: a
  quantity far above the plausible range, or a total energy far above a day's worth, should be a
  `Low` verdict. Worth fixing where the other cross-checks live.
- **The transcript does not survive leaving the tab.** **Fixed:** the view model is a singleton
  and empties itself on a sign-out, the way `AvatarStore` already did. `ChatViewModel` is transient, so switching to
  Home and back is a new conversation. The right time to change that is milestone 14's offline
  queue, which is when a conversation gets somewhere to live that a phone call cannot destroy.
- **No physical-phone run.** The device was locked for the whole session, so every claim here is
  tier 2. The layout is the part most worth re-checking on real hardware.
- **Nothing is upserted into the catalog** on confirm, and `FoodItemId` stays null. That is
  milestone 10, which is also what makes `MealAnalysisItem.Barcode` — carried through this whole
  flow and currently unused — earn its place.
