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

`TRACKR_LOOKUP_RATE_LIMIT` is the odd one out: it is not protecting your server. Every lookup
becomes a request to Open Food Facts, a free service run by volunteers, and this cap keeps a
looping client from spending their bandwidth and getting your address throttled. It sits well
above what a person logging meals could reach, so in practice it only ever catches a bug.

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
