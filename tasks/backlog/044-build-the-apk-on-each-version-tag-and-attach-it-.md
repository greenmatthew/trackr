---
id: 44
title: Build the APK on each version tag and attach it to its release
milestone: M15
record: -
depends_on: [42, 43]
---

## Goal

Pushing a `v*` tag builds the release-signed APK on Gitea Actions and puts it on that tag's release
page, so nobody hand-copies a build.

## Done when

- [ ] a release workflow builds on `v*` tags and attaches the APK and its checksum
- [ ] the first release seen on the page — the needs-user step
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: otter task 295, 315, 316 and `~/programs/otter/.gitea/workflows/release.yml` (its D17);
nautilus 020.

- Reuse `git.matthewgreen.gg/mgreen/maui-android:net10-api36-1`, which otter's D17 says three Android
  apps share. It is built on the runner host and never pushed to the registry.
- Pitfalls: sdkmanager does not retry a failed download (otter 315); Cloudflare answers urllib's
  default User-Agent with a 403; pushing many tags at once starts that many builds.
