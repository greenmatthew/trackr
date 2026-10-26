---
id: 26
title: The app on a physical phone
milestone: M14
record: -
depends_on: []
---

## Goal

Every mobile claim since milestone 9 rests on the emulator. Run the app on a real phone, against
the dev stack and then through the reverse proxy, and file what turns up.

## Done when

- [ ] installed on the phone with `just mobile::run`, reaching the dev stack through `./scripts/device.sh reverse`
- [ ] every tab walked, and a meal logged from text, from a barcode photo and from a plate photo taken with the phone's own camera
- [ ] a meal confirmed in airplane mode is sent once the connection is back
- [ ] the phone pointed at the deployed HTTPS server signs in (the reverse-proxy path)
- [ ] what was seen written under Notes, and anything wrong filed as a task

## Waiting on you

1. **Connect the phone**, either way:
   - USB: plug it in and run `./scripts/device.sh usb`. The first time, it prints a `usbipd bind`
     line to run in an admin PowerShell on Windows.
   - Wireless: *Developer options → Wireless debugging*, then
     `./scripts/device.sh pair <ip>:<pairing port>` and `./scripts/device.sh connect <ip>:<port>`.
     The two ports are different; the second is on the main Wireless debugging screen.
2. **Unlock it and keep it unlocked** — on USB, *Developer options → Stay awake*. The last attempt
   failed because the phone was locked for the whole session.
3. **Say it is connected.** The build, install, tunnel and walk-through happen from here. The camera
   needs your hands for two photos when asked: a packaged product's barcode, and a plate of food.
4. For the reverse-proxy check: the address of your deployed server, if Trackr is deployed yet.

## Notes

"No physical-phone run" is left open in every record from
[11-chat.md](../../docs/decisions/11-chat.md) to [18-offline.md](../../docs/decisions/18-offline.md).
11-chat.md: "The device was locked for the whole session, so every claim here is tier 2. The layout
is the part most worth re-checking on real hardware."

Setup and the two failures worth telling apart are in
[Testing-the-Android-App](../../wiki/Testing-the-Android-App.md) § A physical phone. With the
emulator running as well, export `ANDROID_SERIAL` first so nothing installs on the wrong device.
