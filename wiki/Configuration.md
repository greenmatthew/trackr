# Configuration

Every setting is an environment variable on the `backend` or `db` service. Set them in
`docker/.env` next to the compose file, or as stack environment variables in Portainer.

`docker/.env` is gitignored. Never commit real credentials.

> The development stack (`docker/docker-compose.dev.yml`) is self-contained and needs none of
> this — it ships throwaway credentials on purpose.

## Required

| Variable | Notes |
| --- | --- |
| `POSTGRES_PASSWORD` | The database password. **The stack refuses to start without it.** Generate one with `openssl rand -base64 32`. |

## Database

| Variable | Default | Notes |
| --- | --- | --- |
| `POSTGRES_DB` | `trackr` | Database name. |
| `POSTGRES_USER` | `trackr` | Database user. |

## Deployment

| Variable | Default | Notes |
| --- | --- | --- |
| `PROXY_NETWORK` | `proxy` | Name of the **pre-existing** external Docker network your reverse proxy is on. The stack does not create it. |
| `TRACKR_TAG` | `latest` | Tag applied to the images this stack builds. |

## Password-reset delivery

Left alone, Trackr writes reset links to the backend log instead of emailing them:

```bash
docker compose logs backend | grep -A3 "was not sent"
```

That is the deliberate default — it needs no SMTP credentials. **The trade-off is explicit:
anyone who can read the backend logs can take over an account.** On a private server whose
logs only you can read, that is the same trust boundary as the database itself.

To send real email instead, set `TRACKR_EMAIL_PROVIDER=Smtp` and fill in the rest.

| Variable | Default | Notes |
| --- | --- | --- |
| `TRACKR_EMAIL_PROVIDER` | `Log` | `Log` or `Smtp`. |
| `TRACKR_SMTP_HOST` | — | Required when the provider is `Smtp`. |
| `TRACKR_SMTP_PORT` | `587` | |
| `TRACKR_SMTP_USE_SSL` | `true` | |
| `TRACKR_SMTP_USER` | — | |
| `TRACKR_SMTP_PASSWORD` | — | |
| `TRACKR_SMTP_FROM` | — | The From address on reset and invite mail. |
| `TRACKR_PUBLIC_BASE_URL` | — | Base URL used in reset and invite links. Only needed if they come out with the wrong host; normally it is derived from the request via `X-Forwarded-Host`. |

## Auth rate limits

Per-window request budgets on the auth endpoints. These are **shared by everyone behind the
same address**, so a household behind one public IP shares one budget. Raise them if that
keeps tripping.

| Variable | Default | Notes |
| --- | --- | --- |
| `TRACKR_LOGIN_RATE_LIMIT` | `10` | Requests per minute across login, 2FA and recovery-code endpoints. |
| `TRACKR_SENSITIVE_RATE_LIMIT` | `5` | Per 15 minutes, across register, password reset and change, 2FA changes and invite creation. |

Rate limiting is the second line of defence. Per-account lockout — see
[Accounts and 2FA](Accounts-and-2FA) — is the first, and is not configurable by environment
variable.

| Variable | Default | Notes |
| --- | --- | --- |
| `TRACKR_LOOKUP_RATE_LIMIT` | `60` | Barcode lookups per minute. |
| `TRACKR_ANALYSIS_RATE_LIMIT` | `30` | Meal analyses per **five** minutes. |

`TRACKR_LOOKUP_RATE_LIMIT` is the odd one out: it is not protecting your server. Every lookup
becomes a request to Open Food Facts, a free service run by volunteers, and this cap keeps a
looping client from spending their bandwidth and getting your address throttled. It sits well
above what a person logging meals could reach, so in practice it only ever catches a bug.

`TRACKR_ANALYSIS_RATE_LIMIT` is a third kind again: it protects your **processor**. One meal
analysis can occupy the vision model for a minute or more, so a per-minute budget would be the
wrong unit — thirty per five minutes is far more than a household eats and few enough to catch a
client stuck in a loop.

## Open Food Facts

The nutrition database behind barcode lookups. When you photograph something packaged, the server
reads the barcode out of the picture and asks Open Food Facts what it is; a hit means the real
label numbers instead of an AI estimate.

**A barcode number is the only thing Trackr ever sends off your server.** Not the photo, not the
meal, not anything about you. That is the single exception to everything staying on your machine,
and it is a number with no account attached to it.

| Variable | Default | Notes |
| --- | --- | --- |
| `TRACKR_OFF_CONTACT_EMAIL` | — | A contact address to include in the `User-Agent`. Optional, but see below. |
| `TRACKR_OFF_ENABLED` | `true` | Set to `false` for no outbound requests at all. |
| `TRACKR_OFF_BASE_ADDRESS` | `https://world.openfoodfacts.org/` | Point at a mirror, or at their staging server. |
| `TRACKR_OFF_TIMEOUT_SECONDS` | `10` | How long to wait for one lookup. |

**Setting a contact address is encouraged.** Open Food Facts asks API callers to identify
themselves, and anonymous callers are the ones that get throttled. Trackr always sends its name
and version; adding an address means someone can email you about a misbehaving client instead of
just blocking your server. It goes into a request header, so treat it as public.

Turning lookups off is a real option rather than a footgun, but know the trade: packaged food then
falls through to the AI, which is slower than a label lookup and worse at getting the numbers
right. Leaving it on is the recommended setting.

Trackr does not retry a failed lookup, deliberately — the reasoning is in
`docs/decisions/08-barcode-off.md` in the repository. A lookup that fails or times out falls
through to the AI, and the chat
tells you it happened rather than quietly showing you an estimate as though it were a label.

## Local AI

The vision model that reads a photo when the barcode path cannot — home cooking, anything
unpackaged, anything Open Food Facts has never heard of. It runs in the `ollama` container on your
own hardware, and **nothing it sees ever leaves the machine.**

| Variable | Default | Notes |
| --- | --- | --- |
| `TRACKR_OLLAMA_ENABLED` | `true` | Set to `false` to turn the model off. Barcode lookups keep working; everything else stops. |
| `TRACKR_OLLAMA_MODEL` | `gemma4:26b` | Which model to ask. See below — the obvious reading of this one is wrong. |
| `TRACKR_OLLAMA_KEEP_ALIVE` | `5m` | How long the model stays in RAM after answering. |
| `TRACKR_OLLAMA_TIMEOUT_SECONDS` | `240` | How long to wait for one analysis. |
| `TRACKR_OLLAMA_CONTEXT_LENGTH` | `16384` | The context window, in tokens. |
| `TRACKR_OLLAMA_MAX_IMAGES` | `4` | Photos one analysis may look at. |
| `TRACKR_OLLAMA_MAX_IMAGE_EDGE` | `1280` | Longest edge of the copy sent to the model. Your stored photo is never altered. |

### Choosing a model on a server with no graphics card

**Bigger can be faster, and that is not a typo.** `gemma4:26b` has 25.2 billion parameters but is
a mixture-of-experts model: only about 3.8 billion of them run for any one token, so your processor
reads roughly 2.7 GB per token instead of all 18. The dense `gemma4:12b` reads all 7.6 GB of itself
every token. On a machine without a graphics card the 26B is both **faster and more accurate** than
the smaller model — it just needs about 18 GB of RAM while it is loaded, which it releases again
after `TRACKR_OLLAMA_KEEP_ALIVE`.

If you do have a graphics card, `gemma4:31b` is dense and answers better. On a processor it is
several times slower for a modest gain.

If 18 GB of RAM is too much, `gemma4:12b` is the fallback and it is a good one — see
[Ollama Setup](Ollama-Setup) for what it actually scored against real label photographs.

### The three settings that are really one setting

`TRACKR_OLLAMA_CONTEXT_LENGTH`, `TRACKR_OLLAMA_MAX_IMAGES` and `TRACKR_OLLAMA_MAX_IMAGE_EDGE` trade
against each other, because **a photograph is expensive in tokens and the number is not intuitive**:
one 1280-pixel image measured at roughly 7 000 tokens. The obvious first guess of 8192 fits the
instructions and one picture, and then fails on two.

If an analysis comes back saying your photos needed more room than the model has, either raise the
context length — which costs RAM — or lower the edge size, which costs the model's ability to read
small print. Below about 768 pixels it stops being able to read a label at all, which is the whole
job, so raising the context is usually the better trade.

`TRACKR_OLLAMA_TIMEOUT_SECONDS` has a constraint of its own: it must stay **under** the read timeout
of your own reverse proxy. nginx defaults to 60 seconds, which will cut an analysis off long before
this fires — see [Troubleshooting](Troubleshooting).
