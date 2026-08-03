# The Android emulator.
#
# Verbs only, over scripts/emulator.sh - which also has `status`, `create` and `delete`. Run it
# with no arguments for its own help.
#
# Separate from `mobile` because the emulator is a machine to run the app ON, not part of the
# app - and because `mobile::up` read ambiguously once the app itself had a `run`.
#
# `just` shows the LAST comment line above a recipe as its `--list` description, and echoes any
# comment left inside a recipe body when it runs. So longer explanations sit above the recipe,
# separated from the one-line description by a blank line.

# Recipes in a module run from the module FILE's directory (just/), not the repository
# root - so every relative path below would be wrong without this.
set working-directory := '..'

EMULATOR := "./scripts/emulator.sh"

_default:
    @just --list emulator

# Boot the emulator headless.
up:
    {{ EMULATOR }} start

# Boot the emulator with a window on the Windows desktop, via WSLg.
show:
    {{ EMULATOR }} start --window

# Shut the emulator down.
down:
    {{ EMULATOR }} stop

# Keeps the AVD, so the next boot is a first-run device rather than a download.

# Factory-reset the emulator: apps, accounts and settings gone.
clean:
    {{ EMULATOR }} wipe

# Deletes about 2GB. `./scripts/emulator.sh create` gets it back, re-downloading the system
# image only if the SDK no longer has it.

# Delete the AVD itself.
[confirm("This deletes the trackr-test AVD (~2GB). Continue? (y/N)")]
clean-all:
    {{ EMULATOR }} delete
