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
