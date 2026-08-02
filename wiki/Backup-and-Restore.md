# Backup and Restore

Everything that matters is in Postgres. There is no state in the container filesystems worth
keeping — which is deliberate, and makes backup a single command.

## What to back up

| What | Where | If you lose it |
| --- | --- | --- |
| The database | The `db-data` volume | Everything: accounts, invites, and later your entire food log. |
| Data-protection keys | **Also in the database**, in `DataProtectionKeys` | Every session is signed out, every pending password-reset link stops working, and every phone has to sign in again. Recoverable, but disruptive. |
| `docker/.env` | Your machine, gitignored | `POSTGRES_PASSWORD` — without it the restored database is unreadable. **Back this up separately from the database dump.** |

The important subtlety: **the data-protection keys are inside the database**, not on disk.
That is on purpose — the container has no volume for them, so on disk they would be
regenerated on every restart and silently sign everyone out. It also means a database dump
already contains them, so there is no second thing to remember.

It also means the reverse: restoring an *older* dump restores older keys, and any session or
reset link issued since is invalidated.

## Taking a backup

A logical dump is simplest and survives Postgres upgrades:

```bash
docker compose -f docker/docker-compose.yml exec -T db \
  pg_dump -U trackr -d trackr --clean --if-exists \
  | gzip > trackr-$(date +%F).sql.gz
```

Store `docker/.env` — or at least `POSTGRES_PASSWORD` — somewhere separate. A dump you cannot
decrypt the keys of is not a backup.

### Meal photos are in the dump, and they are what makes it big

Photos attached to log entries are stored in Postgres, not on a separate volume, so the command
above already includes them and there is nothing else to remember. That is the point of storing
them there.

The cost is size. A phone photo lands around 3 MB, so an account logging five photographed meals a
day adds roughly **5.5 GB a year** to the dump — and the dump takes proportionally longer to write
and to restore. Nothing else in the database is remotely that large.

If that becomes awkward, the options in increasing order of effort are: dump less often but keep
more generations, use the volume snapshot below (which is a file copy rather than a re-encode), or
delete old entries you no longer want, which takes their photos with them.

### Volume snapshot instead

Backing up the `db-data` volume directly also works, but only with the stack **stopped** —
copying a live Postgres data directory produces a torn, possibly unrestorable copy.

```bash
docker compose -f docker/docker-compose.yml down
docker run --rm -v trackr_db-data:/data -v "$PWD":/backup alpine \
  tar czf /backup/trackr-volume-$(date +%F).tar.gz -C /data .
docker compose -f docker/docker-compose.yml up -d
```

## Restoring

```bash
docker compose -f docker/docker-compose.yml up -d db
gunzip -c trackr-2026-08-01.sql.gz \
  | docker compose -f docker/docker-compose.yml exec -T db psql -U trackr -d trackr
docker compose -f docker/docker-compose.yml up -d
```

`POSTGRES_PASSWORD` in `.env` must match the one the dump was taken under. Migrations run at
startup, so a dump from an older schema is brought forward automatically — but restore into a
build **at least as new** as the dump. Going backwards is not supported.

After restoring, expect to sign in again everywhere, including on the phone.

## Testing that it works

An untested backup is a guess. The dev stack is the ideal place to prove it:

```bash
just server::reset                      # empty dev database
gunzip -c trackr-2026-08-01.sql.gz \
  | docker compose -f docker/docker-compose.dev.yml exec -T db psql -U trackr -d trackr
just server::up
```

Then open <http://localhost:8000> and check you can sign in. Note the dev stack uses the
throwaway password `trackr_dev`, so a production dump restored there will have
data-protection keys it cannot decrypt — accounts and data will be intact, but sessions will
not carry over. That is expected, and does not indicate a bad backup.
