# Ollama Setup

The `ollama` service runs the vision model that reads a food photo when the barcode path does
not resolve it. It is the only AI in Trackr, it runs on your own hardware, and nothing it sees
leaves the machine.

Everything on this page is configured with the `TRACKR_OLLAMA_*` variables in
[Configuration](Configuration).

## Why the container stays up but the model does not

An idle Ollama container costs almost nothing. The *model* is what holds several GB of RAM,
and Ollama unloads it after an idle period on its own.

Set `keep_alive` short — via `OLLAMA_KEEP_ALIVE` on the service, or the `keep_alive` field on
each API request — so the model drops out of RAM between meals and reloads on the next one.
On CPU that reload costs a few seconds, which is fine: logging a meal is not latency-sensitive.

**Do not stop and start the container per request.** It adds orchestration complexity to save
the small part of the cost.

## How the model gets onto the server

A second container, `ollama-init`, runs `ollama pull` once and then exits, so a fresh deployment
works without anybody opening a shell. **Ollama does not download a model on demand** — until that
pull finishes, every meal log comes back saying the vision model is still downloading.

`ollama-init` shows as **exited** in Portainer once it has finished. That is success, not a failed
container.

The download is gigabytes and the backend deliberately does not wait for it, so the rest of Trackr
works normally while it runs. To change models later, change `TRACKR_OLLAMA_MODEL` and redeploy —
the init container pulls the new one. The models live in a named volume, so a redeploy does not
re-download them.

## Choosing a model

**Keep the name in configuration, never in code.** Swapping models is expected, and it should
be an environment variable, not a rebuild.

### The counter-intuitive part: on a processor, bigger can be faster

A **mixture-of-experts** model has a large number of parameters and only runs a fraction of them for
any given token. `gemma4:26b` has 25.2 billion parameters and activates about 3.8 billion, so your
processor reads roughly 2.7 GB per token. The **dense** `gemma4:12b` is less than half the size on
disk and reads all 7.6 GB of itself every token.

So on a machine without a graphics card the 26B is both faster and more accurate than the smaller
model. It just needs about 18 GB of RAM while it is loaded — which it gives back after
`TRACKR_OLLAMA_KEEP_ALIVE`. That is the default, and the reference deployment (CPU-only, about
94 GiB of ECC) is exactly the case it suits.

If you have a graphics card, `gemma4:31b` is dense and answers better; it is in `.env.example`,
commented out. If 18 GB is too much RAM, `gemma4:12b` is a good fallback — see the table below.

**Check the licence of the specific variant, not the family.** They are not always uniform:
`qwen2.5vl:7b` is Apache-2.0 while `qwen2.5vl:3b` is research-only, which makes the 3B unusable as a
shipped default for a project anyone may run commercially.

*VLM = vision-language model: one that takes images and text together, which is the thing that
can look at the photo at all.*

### What the candidates actually scored

Against one real US nutrition panel from `media/examples`, checked value by value by eye, on a
CPU-only machine:

| Model | Size | Result | Time |
| --- | --- | --- | --- |
| `gemma4:12b` | 7.6 GB | **16 of 16 values correct**, right units throughout, nothing flagged | 95 s |
| `qwen2.5vl:7b` | 6.0 GB | macros and 9 micronutrients correct, but the serving size wrong — 28 g for a 140 g serving | 123 s |
| `granite3.2-vision:2b` | 2.4 GB | read the **per-container** column instead of per-serving; no micronutrients | 104 s |

Gemma 4 also invented nothing: the label lists no vitamin A, C, E or K, no B vitamins, no magnesium
and no zinc, and it reported none of them. That is the behaviour that matters most — a missing
micronutrient is fine, a fabricated one is a wrong number in your health record.

The two that got it wrong were both caught by the server's own consistency checks and shown as
low-confidence rather than as fact. That is the system working, and it is also why the confirmation
card exists.

`gemma4:26b` is the shipped default on architectural grounds and was **not** in this test — 18 GB
does not fit the machine the test ran on.

### Test before you commit to one

Label OCR is precisely where small vision models fail, and a high-resolution photo on CPU can
be much slower than "a few seconds". Before settling on a production model:

1. Collect a handful of real nutrition-label photos — bad lighting, an angle, a curved
   package. Not clean stock images.
2. Run each through the candidate model with the actual prompt.
3. Check the numbers against the label by eye, and time it.

Treat this as a short experiment, not an afterthought. A model that reads a flat, well-lit
label perfectly and a curved one wrongly is worse than one that fails visibly, because the
cascade's confirmation card is the only thing between a wrong read and your database.

Fine-tuning is a possible later step if nothing off-the-shelf is good enough. It is not needed
to start.

## Photos are expensive, and the number is not intuitive

One 1280-pixel photo measures at roughly **7 000 tokens**. The instructions and the nutrient list
are about a thousand more. So the obvious context size of 8192 fits one picture and then fails on
two, which is why the default is 16384.

If an analysis comes back saying your photos needed more room than the model has, raise
`TRACKR_OLLAMA_CONTEXT_LENGTH` — which costs RAM — or lower `TRACKR_OLLAMA_MAX_IMAGE_EDGE`, which
costs the model's ability to read small print. Below about 768 pixels it cannot read a label at all.

Trackr scales a **copy** of each photo down before sending it. Your stored photo is never altered,
so a better model can be run over the original later.

## When the model gets it wrong

That is expected, and it is why nothing the model produces is saved without confirmation. The
backend validates every reply before it reaches you, and marks anything that does not hold together
as low-confidence rather than presenting it as fact:

- **Calories against the macros**, at roughly 4 kcal/g of protein and carbohydrate and 9 kcal/g of
  fat. The tolerance is wide on purpose — fibre and alcohol carry energy this sum ignores, so a
  high-fibre cereal legitimately misses by a good margin.
- **Whether the food weighs at least as much as the things in it.** This is the one that catches the
  most damaging mistake there is: reading the *per 100 g* column of a label while reporting its 28 g
  serving. Every figure is individually correct, they all reconcile against each other, and the
  answer is wrong by threefold.
- **Breakdowns against their totals** — saturated fat against total fat, added sugars against total
  sugars.

A value the server cannot trust is recorded as **not measured**, never as zero. Those are different
facts, and a dashboard showing a nutrient as absent is telling you the truth while one showing 0
is not.

If Ollama is unreachable or its output cannot be parsed, the chat says so and offers a retry
or manual entry. It never saves a guess. One exception is worth knowing about: if the barcode was
recognised but the model failed, you still get the label's real figures, with the amount assumed to
be one serving and a note saying so — those numbers came from a nutrition database, not from a
guess.

## See also

- [Configuration](Configuration) — the `TRACKR_OLLAMA_*` variables
- [Self-Hosting](Self-Hosting) — what else is in the stack
- [Troubleshooting](Troubleshooting) — analyses that time out or run out of room
