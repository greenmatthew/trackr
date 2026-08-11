# Milestone 8 — Ollama

Stage three of the cascade CLAUDE.md §5 describes: when the barcode path cannot answer, show the
photograph to a model running on the user's own hardware. That is most home cooking, everything
unpackaged, and every product Open Food Facts has never heard of.

The shape is the same as milestone 7's — a swappable stage behind an interface, transport separated
from meaning, and a read-only route so the thing can be run rather than only unit-tested. What is
different is where the risk sits. Milestone 7's risk was in a decoder that either read a number or
did not. This milestone's risk is a component that is *always* willing to produce a confident
number, so nearly everything below is about not believing it.

`POST /api/analyze` takes text and photo ids and returns a parsed, validated, **unsaved** result.
Milestone 9 puts a chat around it and a confirmation card in front of the save.

## The arithmetic moved, and that is a deviation from §5

**The model reports nutrition per serving and a quantity in servings; the server multiplies.**

§5 says the prompt should compute the serving math — "2 of these" = 2 × serving × per-serving
macros. The intent is preserved and the multiplication is not. Two reasons:

- Small models are bad at arithmetic and good at "two eggs". Asking for the part it can do and
  doing the part it cannot removes a whole class of wrong answer rather than detecting it.
- `SaveLogItemRequest` already takes per-serving values plus a quantity and multiplies server-side
  (`LogEndpoints.Snapshot`). Matching that shape means milestone 9's confirmation card hands a
  corrected item straight back to `POST /api/log` with no mapping layer in between — and a mapping
  layer is a second place for a number to change. `MealAnalysisContractTests` fails if the two types
  drift apart.

## Open Food Facts beats the model, and the serving is what makes that safe

Where both have a figure, the database wins: one was read off a printed label by a project with two
decades of corrections behind it, the other guessed from a photograph by a model small enough to run
on a home server. The model's job is to say *which product* and *how much of it*.

**The subtle part is the serving.** `ProductDraft` may be on `ReferenceQuantityAsServing` — Open
Food Facts had no serving size, so one serving is taken to be 100 g — while the label in the
photograph says 28 g. Merging one side's protein with the other's fat produces a card whose numbers
each came from somewhere defensible and whose total is nonsense. That is the failure
[08-barcode-off.md](08-barcode-off.md) already had to solve once, for one product's internal
consistency; this milestone re-introduces it across a different seam.

So the prompt **pins each identified product's serving** and asks for the amount as a fraction of
it. 15 g of a 100 g serving is `0.15`. The model never restates a known product's serving size, and
`MealCascade` never takes one from it.

That is also why the plan's original exception — let the model's serving win when the basis is
`ReferenceQuantityAsServing` — was dropped during implementation. Expressing the amount as a
fraction of a pinned serving is strictly safer than re-basing every figure the database supplied
onto a serving the model read, and it is the unit `FoodItemComponent.Quantity` already counts in.

## Constrained decoding, and what it does not buy

Ollama compiles a JSON Schema into a sampling grammar, so the reply cannot be malformed and cannot
use a nutrient key the schema does not list. Both the schema and the prompt's nutrient table are
**generated from `NutrientCatalog`**, so adding selenium later cannot leave the model being told
about a nutrient the server would reject, or told the wrong unit for one.

Three things about that are worth writing down, because two of them are counter-intuitive.

**Unsupported schema keywords are skipped silently.** Not rejected — skipped, leaving that part of
the answer unconstrained with nothing anywhere to indicate it. So the schema uses no `$ref`, no
`oneOf`, no `additionalProperties`, no `pattern` and no `uniqueItems`, and
`The_schema_uses_no_construct_the_grammar_skips_silently` is the test that keeps it that way. It is
the highest-value test in the milestone precisely because the failure it prevents has no runtime
symptom.

**The nutrient `enum` converts a detectable error into an undetectable one.** It makes an invented
key impossible — but a model that was about to report selenium must now pick one of the 25 permitted
keys, and it will attach selenium's number to whichever it picks. A rejected key has become a
misattributed amount, which looks exactly like a measurement. The enum is still right; it just does
not buy what it appears to. The prompt says "if a nutrient is not in this list, leave it out", and
the reader's cross-checks are what actually catch it.

**Required means the model cannot omit it, so requiring a micronutrient is a fabrication order.**
The core four are required because they are non-nullable columns and every item has them; everything
else is optional. That is what keeps [09-composites.md](09-composites.md)'s "missing is not zero"
true through the model, and `Only_the_core_four_and_the_identity_fields_are_required` pins it.

Two smaller consequences of how the grammar is generated: `minimum`/`maximum` are honoured on an
integer and ignored on a number, so energy is an integer and gets a range check for free; and
required properties are emitted in declaration order, so `productRef` is declared first with a
`"none"` sentinel, forcing the model to decide *which product it is looking at* before it commits to
any numbers for it.

## The validator

§5 marks this REQUIRED and says why. Four things it can do, graded by how confident the result would
look:

- **Drop** — the value becomes "not measured". Never clamped to zero, because zero is a claim
  somebody made and this is the absence of one.
- **Warn** — kept, with a sentence saying what was done to it.
- **Flag** — the item is marked low-confidence and still returned. §5's explicit remedy.
- **Fail** — nothing returned. Reserved for replies whose *shape* means the grammar was not applied,
  which is a fault in this server rather than a bad reading.

Most of the rules are obvious once listed. Three are not.

**Mass conservation is the most valuable check here and was not in the original plan.** When the
serving is in grams or millilitres, fat + carbohydrate + protein + fibre must not exceed it. This
catches a model reading the *per 100 g* column of a dual-column label while reporting the label's
28 g serving — figures that are individually correct, reconcile against each other perfectly, and
are wrong by more than threefold. Nothing else catches it. Only those four are counted: saturated
fat is part of the fat and sugars are part of the carbohydrate, so adding them would double-count
and fire on good labels.

**The energy tolerance is a quarter, not a tenth, and it has a 50 kcal floor.** Atwater ignores
fibre (about 2 kcal/g), sugar alcohols and alcohol (7), none of which Trackr tracks, so a high-fibre
cereal or a beer misses by 10–40% while every number on it is correct. A check that fires on those
is one people learn to ignore. The floor exists because rounding each macro to the nearest gram —
which is what a label does — can move the prediction by 9 kcal on a 40 kcal item.

**Fibre is deliberately not checked against carbohydrate.** European labels exclude fibre from the
carbohydrate figure and US labels include it, so the obvious rule fires on nearly every European
product. `Fibre_above_carbohydrate_is_not_flagged` is a negative test whose only job is to stop
somebody adding it. [08-barcode-off.md](08-barcode-off.md) hit the same divergence from the other
direction, with `carbohydrates-total`.

One more that is invisible until it costs a 500: **a log item's amounts are `numeric(12,4)` and the
quantity is multiplied in before storing**, so a large enough pair does not fit. The grammar cannot
prevent it — an unconstrained JSON number has sixteen digits available either side of the point —
and the user would have already tapped confirm by the time Postgres refused it. The quantity is
reduced rather than the nutrition, because the nutrition may well have been read correctly.

## The context window, which is the trap that would have cost a week

**Ollama sizes the context from available video memory, so a CPU-only host gets the smallest tier —
around 4096 tokens.** Older versions respond to a prompt that does not fit by dropping the *oldest*
tokens and carrying on: the instructions and the schema, silently, with no error anywhere. The
symptom is a model that appears to ignore everything it was told.

So `num_ctx` is set explicitly, and photographs are scaled down to 1280 pixels on the long edge
before being sent.

Running it produced the useful number. **One 1280-pixel image measured at roughly 7 000 tokens**, so
the obvious default of 8192 fits the instructions and one picture and then fails on two. 16384 is
the default now. The version of Ollama in use turned out to *refuse* an oversized prompt rather than
truncating it, which is much better — and the analyzer turns that refusal into a sentence naming the
two settings that fix it, because "HTTP 400" tells nobody anything.

**Downscaling does not reverse milestone 7's decision.** [08-barcode-off.md](08-barcode-off.md)
refused to re-encode photos *on ingest*, so a better model can be run over the original later. This
resizes a copy on the way to one consumer, which is the other side of that same decision — the
decoder already resizes a copy to 2× when it helps. The barcode is still decoded from the original,
because its hit rate was measured there and one of its three successes needed the full resolution.

The copy is also always JPEG. `MealImageRules` accepts WebP, the phone may well send it, and the
image loader underneath llama.cpp does not read WebP at all — so a WebP passed through untouched
would be rejected by the model rather than by anything here.

The decompression-bomb guard moved out of `ZXingBarcodeDecoder` into `ImageGuard` for this, because
turning arbitrary uploaded bytes into a bitmap now happens in two places and the guard has to happen
in both.

## Sampler settings

`temperature: 0` and `top_k: 1`, because this is a measurement task. A fixed seed, which buys
*reproducible* rather than *deterministic* — llama.cpp still varies with batch size and thread count,
so two servers can disagree. It is enough to make a bad read investigable.

**`repeat_penalty: 1.0`, and this is the one that will get "tidied" back.** Ollama defaults it to
1.1, and unlike `top_k` and `top_p` it is not neutralised by a temperature of zero: it changes the
logits before anything is picked. A nutrition table is full of legitimate repetition — the same field
names on every entry, several zeroes in a row on a label — and penalising a repeated token there
pushes the model off the correct answer.

`think: false` is sent explicitly, because current models increasingly reason before answering and
that is either wasted output budget or a reply that is not the document it was asked for.

`num_predict` is bounded. Ollama's default is unlimited, and a model constrained to a JSON grammar
can fall into repeating a valid string forever — the grammar cannot stop it, because repeated words
are legal JSON. Bounded, the answer comes back marked truncated in bounded time and the reader says
so, instead of holding a core until the timeout.

## No retry, again

Same conclusion as milestone 7, different reasons. Retrying an inference that already cost a minute
of CPU multiplies load on the one resource the whole feature is bottlenecked on, to repeat a request
that is not idempotent and that failed for a reason a second attempt will not change. There is also
no resilience handler on the typed client, and the registration says so in place — its absence is
otherwise indistinguishable from an oversight.

The timeout is 240 s, long where Open Food Facts' is 10, because there is no cheaper fallback behind
this one: the model *is* the fallback. It has to stay under the reverse proxy in front of it or the
proxy answers first and the user gets a gateway error page instead of the explanation §5 requires.
Trackr's own nginx already allows 300 s; a self-hoster's proxy may well default to 60, which is a
Troubleshooting entry rather than something this can control.

## Two more deviations from §5, both in the same direction

§5 says an AI-stage failure must never save a guessed or empty entry. Nothing here saves anything,
and in two places the literal reading would throw away something better than a guess.

**A fully matched product survives the model failing.** Its photograph was deliberately withheld
*because* the database's figures were already better than a reading of it. Discarding them because a
different stage failed contradicts the cascade's own logic. The item is returned at quantity 1, with
warnings saying both that the model was unavailable and that the amount is an assumption.

**A fully matched product the model never mentioned is synthesised.** This is not hypothetical: the
model is working from text alone for that product, and may well describe it as "crisps" with no link
back. Dropping it would discard the best data in the cascade in favour of a guess made by a model
that was never shown the picture.

Related, and the reason the warning list is built where it is: **stage one and two warnings are
seeded before the analyzer runs.** §5 requires a stage failure to reach the user "regardless of what
the AI says", and a list built on the success path loses the Open Food Facts warnings the moment
Ollama also fails. Being told "the AI is unavailable" without "and the food database was
rate-limiting us" is materially less useful.

## Choosing a model, and a licence trap

**`qwen2.5vl:3b` was the first choice and cannot be used.** The 3B and 72B ship under the Qwen
Research License — non-commercial only — while the 7B is Apache-2.0. CLAUDE.md §10 promises anyone
may self-host Trackr *including commercially*, and the default model in the compose file is
downloaded onto every self-hoster's server. Same shape as milestone 7's ImageSharp trap, and worth
the same warning: check the licence of the *specific variant*, because a family is not uniform.

**On a processor, the bigger model is the faster one.** `gemma4:26b` is mixture-of-experts: 25.2B
parameters of which about 3.8B run per token, so a CPU reads roughly 2.7 GB per token against a
dense 12B's 7.6 GB. The reference deployment is CPU-only with about 94 GiB of RAM, which is
precisely the case MoE is for. `gemma4:31b` is dense and better, and wants a graphics card; it ships
commented out in `.env.example` rather than buried in the wiki, so the choice is visible where the
choice is made.

### What the label photographs showed

Against `media/examples`, one US nutrition panel, checked value by value by eye:

| Model | Result | Time |
| --- | --- | --- |
| `gemma4:12b` | **16 of 16 values correct**, right units throughout, no flags | 95 s |
| `qwen2.5vl:7b` | macros and 9 micronutrients correct; **serving size wrong** (28 g for a 140 g serving) | 123 s |
| `granite3.2-vision:2b` | read the **per-container** column; no micronutrients at all | 104 s |

Three things this settles.

**Gemma 4 behaves under constrained decoding**, which was the open risk in choosing a model four
months old. It also took the per-serving column rather than the per-container one — the exact trap
mass conservation exists for — and invented nothing: no vitamin A, C, E, K, no B vitamins, no
magnesium or zinc, none of which are on that label. "Missing is not zero" survived contact.

**The validator earns its keep on the two that got it wrong.** Qwen's wrong serving was caught by
mass conservation and flagged; the collateral damage was two correct nutrients dropped for exceeding
a serving that was itself wrong, which is the right way round. Granite's per-container reading was
caught by both cross-checks at once. On a text-only request granite produced 150 g of carbohydrate
in an egg and both checks fired.

**`gemma4:26b` was not benchmarked here**, and that is stated rather than implied: 18 GB does not fit
the 15 GiB development machine. The architecture argument for it is sound and the 12B result shows
the family reads labels properly, but the deployment default's own accuracy rests on inference from
the 12B rather than on a measurement.

## The container

`ollama` is on the **internal network only, never the proxy one**. It has no authentication of any
kind and its API includes endpoints that download and create models — a far larger thing to expose
than the tracker in front of it. That belongs in §8's posture rather than being implicit in a
compose file.

`OLLAMA_NUM_PARALLEL=1` and `OLLAMA_MAX_LOADED_MODELS=1`, because without them two household members
logging at once can make Ollama load a second copy of the model, which is exactly the
several-gigabytes-of-RAM surprise §6 exists to prevent. The second request queues. The rate limit on
the route is the outer guard rather than the mechanism.

`ollama-init` is a one-shot container that pulls the model and exits, because **Ollama does not
download a model on demand** — until the pull finishes, every request is a 404. Without it a fresh
Portainer deployment answers every meal log with an error until somebody opens a shell, which is not
a reasonable thing to ask. It needs the healthcheck added to the `ollama` service: the stock image
ships none, so `depends_on: service_healthy` would otherwise be unenforceable and the init container
would race the server and exit having done nothing. It shows as *exited* afterwards, which reads as
a failed container and is not one; Self-Hosting says so.

The backend deliberately does **not** depend on it. The download is gigabytes and the rest of Trackr
works perfectly well while it runs — and the analyzer turns Ollama's model-not-found 404 into "the
vision model is still downloading", which is the first thing anybody hits and is a different message
from "the AI is unavailable".

## What running it changed

Two things, and neither was visible from the code.

**A fully matched item inherited the model's warnings about numbers that had just been replaced.**
The model described a drink as half a millilitre, the reader correctly warned that its figures
weighed more than its serving, the database's real 473 ml serving then replaced every figure — and
the warning stayed, sitting on a card whose numbers were entirely correct and describing figures
that were no longer on it. A full match now drops them; a partial match keeps them, because there
the model's figures partly survive.

**The context limit was a 400, not a truncation** — see above. Both are recorded as tests.

## Verifying a change to any of this

- **`just server::test`** — the acceptance criteria are `A_matched_products_numbers_beat_the_models`
  and `A_fully_matched_products_photograph_is_never_sent`. The two easiest to break without noticing
  are `The_schema_uses_no_construct_the_grammar_skips_silently` and
  `Fibre_above_carbohydrate_is_not_flagged`, both of which exist to stop a well-meant change.
- **The compose stack over HTTP**, which is the tier every claim in the tables above rests on: text
  only; a barcode photograph (assert the model never received the image and the numbers match the
  database exactly); a label photograph with no barcode; Ollama stopped mid-session, with and
  without a resolved barcode; and a model name that has not been pulled.

## Left open

- **Nothing calls this but a person with an HTTP client.** Milestone 9's chat and confirmation card
  are what turn an analysis into a saved entry, and the mobile client cannot call it yet: it sets a
  30-second timeout and a retry pipeline, which against a two-minute call is four inference jobs
  queued for one meal. That route needs its own `HttpClient` with a long timeout and no resilience
  handler.
- **`gemma4:26b` is unverified on real photographs**, for the reason above.
- **One label photograph is not an accuracy rate.** It was enough to choose between three models and
  to catch two bugs, which suggests more photographs would earn their keep — the same admission
  milestone 7 made about four barcodes.
- **No ingredients field**, though the photo is already in front of the model and §9.10a would save a
  second vision pass. A long free-text field is exactly where the grammar's repetition-collapse
  failure lives, and an ingredients paragraph is the longest string one could ask for: it needs a
  length cap, a truncation rule and its own tests, which is 10a's work done early and leaves this
  milestone's schema carrying a field nothing reads. The original photograph is kept at full
  resolution precisely so it can be re-run later.
- **Nothing is cached.** Re-analysing the same photograph asks the model again. Milestone 10's
  catalog is the real fix, exactly as it is for lookups.
