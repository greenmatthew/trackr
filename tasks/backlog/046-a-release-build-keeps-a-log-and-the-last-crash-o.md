---
id: 46
title: A Release build keeps a log, and the last crash, on the phone
milestone: M5
record: -
depends_on: []
---

## Goal

A Release build writes its log and its last crash somewhere on the phone, so a problem on a real
device can be read afterwards instead of guessed at.

## Done when

- [ ] a file logger registered so it works in Release, with a size cap
- [ ] an unhandled exception is written down before the process dies
- [ ] the last crash is offered for copying on the next launch, and nothing leaves the phone unasked
- [ ] `just test` is green
- [ ] seen working at the tier the commit's `Verified:` trailer names
- [ ] the decision record and the wiki still match the code

## Notes

Filed from the survey of the otter, nautilus, nudge and dnd-near boards on 2026-10-26.

Sources: otter 152, 153, 333; nautilus 286 (diagnostics to copy from the app).

- Today `MauiProgram.cs:37-38` adds only `AddDebug()`, inside `#if DEBUG`, so every `logger.Log…` in
  Release goes nowhere, and there is no unhandled-exception handler.
- Pitfalls (otter):
  - `AddSingleton<ILoggerProvider>` is silently inert in Release unless something reads
    `builder.Logging`; register through `builder.Logging.Services`.
  - `AppDomain.UnhandledException` also covers Java-thread exceptions; do not use
    `UnhandledExceptionRaiser`, which reports exceptions that are not crashes.
  - The crash handler must flush synchronously, within a time budget.
  - Native crashes and ANRs can only be read at the next launch through `ApplicationExitInfo`, which
    needs API 30; trackr's minimum is 21, so guard it.
- The log can carry the server address and an email: keep it on the phone.
