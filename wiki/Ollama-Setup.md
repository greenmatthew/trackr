# Ollama Setup

The `ollama` service runs the vision model that reads a food photo when the barcode path does
not resolve it. It is the only AI in Trackr, it runs on your own hardware, and nothing it sees
leaves the machine.

> **Not built yet.** Milestone 8 wires the backend to Ollama. This page is the reference for
> doing it, and for choosing a model afterwards.

## Why the container stays up but the model does not

An idle Ollama container costs almost nothing. The *model* is what holds several GB of RAM,
and Ollama unloads it after an idle period on its own.

Set `keep_alive` short — via `OLLAMA_KEEP_ALIVE` on the service, or the `keep_alive` field on
each API request — so the model drops out of RAM between meals and reloads on the next one.
On CPU that reload costs a few seconds, which is fine: logging a meal is not latency-sensitive.

**Do not stop and start the container per request.** It adds orchestration complexity to save
the small part of the cost.

## Choosing a model

Two rules, and then it is an experiment.

**Keep the name in configuration, never in code.** Swapping models is expected, and it should
be an environment variable, not a rebuild.

**Start deliberately small.** For bringing the pipeline up, use a tiny model of 1–2 GB *even
though its answers will be bad*. The goal at that stage is image in → JSON out → validated →
confirmed → saved. Answer quality is a separate problem, solved by swapping the model once the
plumbing holds.

### Sizing for real use

A 7–12B-class VLM is comfortable on a server with plenty of RAM — the reference machine has
about 94 GiB of ECC — and reads labels appreciably better than a small one. CPU-only inference
is acceptable here for the same reason the reload cost is: nobody is waiting on a meal log the
way they wait on a chat reply.

*VLM = vision-language model: one that takes images and text together, which is the thing that
can look at the photo at all.*

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

## When the model gets it wrong

That is expected, and it is why nothing the model produces is saved without confirmation. The
backend validates the JSON before it ever reaches you — malformed output, missing fields, or
calories that do not reconcile with the macros (roughly 4 kcal/g of protein and carbohydrate,
9 kcal/g of fat) are flagged as low-confidence rather than presented as fact.

If Ollama is unreachable or its output cannot be parsed, the chat says so and offers a retry
or manual entry. It never saves a guess.

## See also

- [Configuration](Configuration) — environment variables, once this service has any
- [Self-Hosting](Self-Hosting) — what else is in the stack
