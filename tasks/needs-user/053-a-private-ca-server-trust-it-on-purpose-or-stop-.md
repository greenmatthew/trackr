---
id: 53
title: A private-CA server: trust it on purpose, or stop advising it
milestone: M3
record: -
depends_on: []
---

## Goal

Decide what the app does about a server whose certificate is self-signed or from a private CA. Today
it gives advice that cannot work.

## Done when

- [ ] the answer is under Notes, the message in `TrackrApiClient.cs` and `wiki/Troubleshooting.md`
      match it, and any build work is filed

## Waiting on you

Choose one:
1. **Stop advising it** — tell people to use a publicly trusted certificate at the reverse proxy
   (Let's Encrypt). Likely right for you, now that Trackr will be public.
2. **Trust user-installed CAs** in Release builds, accepting that anything the phone's owner installs
   can then read the traffic.
3. **Let a self-hoster build their own CA into their own APK**, and say so in the wiki.

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Prompted by otter 387 on TLS trust.

- Today `TrackrApiClient.cs:846` says "install its certificate on this phone first", and
  `wiki/Troubleshooting.md:24-25` says the same. But no `network_security_config` in the app has
  `<trust-anchors>`, and an app targeting API 24+ trusts only system CAs, so an installed user CA is
  ignored. The advice fails every time it is followed.
- Trusting user CAs in a Release build widens what can intercept the traffic, which is health data
  (CLAUDE.md §8).
