---
id: 29
title: Hash the recovery codes, and seal the 2FA key and the key ring
milestone: M2
record: -
depends_on: []
---

## Goal

A copy of the database — a dump, a backup, the volume — stops being enough to pass anyone's
two-factor check or forge a session. Today it is: Identity's default store keeps recovery codes and
the authenticator key as plain tokens, and the data-protection key ring sits unencrypted in
`DataProtectionKeys`.

## Done when

- [ ] a test that reads `AspNetUserTokens` directly shows what is stored today, before anything changes
- [ ] recovery codes are stored as hashes and redeemed against them
- [ ] the authenticator key is sealed under a secret kept outside the database
- [ ] the key ring is protected with that secret (`ProtectKeysWith…`)
- [ ] accounts enrolled before the change keep working, or are told plainly to re-enrol
- [ ] a missing or wrong secret fails loudly at start, never silently turns 2FA off
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Source: dnd-near 185, under its D18 rule "backups hold no secret"
(`~/programs/dnd/dnd-near/tasks/*/185-*`).

- Evidence at filing: trackr sets no `Stores.ProtectPersonalData`, overrides no store, and
  `Program.cs:45-47` persists keys with no `ProtectKeysWith`. The survey read
  `UserStoreBase.ReplaceCodesAsync` in dotnet/aspnetcore: it joins the codes with `;` and stores them
  as a token. Not yet observed in a live row — hence the first Done-when line.
- **Two documents say the opposite today, and both mislead:** `wiki/Accounts-and-2FA.md:146` and the
  comment at `AccountEndpoints.cs:734` say only hashes are stored; `wiki/Backup-and-Restore.md` says
  `POSTGRES_PASSWORD` is needed to read a dump and "decrypt the keys". A `pg_dump` is plain SQL. Task
  054 corrects the words if this has not landed first.
- Identity's own route is `Stores.ProtectPersonalData` with an `ILookupProtector`; weigh it against
  overriding the token store before choosing.
- Pitfall: the secret must be backed up apart from the dumps, or a restore loses every enrolment and
  session. dnd-near turns 2FA off "with a reason" when its key is missing — decide trackr's answer
  deliberately and record it.
