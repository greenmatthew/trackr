# Branding: icon, palette and light/dark theming

Not a milestone — a cross-cutting change taken between milestones 3 and 4, prompted by the
app still shipping MAUI's `#512BD4` purple and a `.NET` wordmark icon.

## What changed

Trackr has an identity: a `trackr` wordmark on a cyan-to-teal gradient, and a ten-colour
palette. Both surfaces now draw from one token set, and both follow the system light/dark
setting. The source artwork lives in [`media/branding/`](../../media/branding/) and is
tracked — it is the master the derived assets come from, not scratch.

This **partly pre-empts milestone 5**, which owns "styling and theming" among other things.
What is settled here: the palette, the token names, and how light/dark is expressed on each
surface. What milestone 5 still owns: `Shell` versus plain navigation, the tab layout, local
storage, and whether to lean on Material styling or build a custom control theme.

## The palette is dark-first, and that is the whole reason this record exists

The supplied palette (`media/branding/trackr-palette.png`) defines **one** background
(`#122E42`) and **one** text colour (`#F2FBFD`). It is a dark theme. Both surfaces already
supported light and dark — `app.css` was light-first with `prefers-color-scheme` overrides,
`Styles.xaml` is 434 lines of `AppThemeBinding` pairs — so a straight adoption would have
meant dropping a mode or inventing values.

**Decision: keep both modes and derive the light theme.** Background and text swap (both
values already exist in the palette), and every accent is darkened until it clears 4.5:1 on
the light background. The derived values are recorded in `Colors.xaml` and `app.css` with
their measured ratios, so a later designer can see exactly which are brand and which are
computed.

The alternative — dark-only — was considered and rejected: it is a smaller change, but it
forces a phone in light mode into a dark app, which reads as a bug rather than a choice.

## Two contrast facts that shape everything

These are the load-bearing constraints. Both are properties of the palette, not opinions.

1. **Primary, Secondary, Tertiary, Success and Warning are all 1.8–2.4:1 against the light
   text colour.** Anything filled with them takes **dark** text (`PrimaryDarkText` /
   `--on-brand`), never white. The "vs text" ratios printed on the palette image are
   measuring exactly this.
2. **Muted (`#5B7D91`) and Error (`#E5484D`) are only 3.2:1 and 3.6:1 on the dark
   background** — under AA for body text. They stay as fill and border values; the `*OnLight`
   / `*OnDark` siblings carry contrast-corrected text colours.

Adopting the palette naively would have produced white-on-cyan buttons at 1.76:1 and cyan
body text on white at 1.85:1. Both existed in the codebase before this change — the template
`Button` style set `Light={White}` on `Light={Primary}`, and three pages used
`TextColor="{StaticResource Primary}"` directly.

## Decisions

- **A `Color` resource cannot be theme-aware; only an `AppThemeBinding` on a property can.**
  So each brand colour ships as a triple — the fill, an `*OnLight` variant, and an `*OnDark`
  / `PrimaryDark` variant — and callers bind the pair. This is why `Colors.xaml` has more
  keys than the palette has swatches.
- **Key *names* in `Colors.xaml` were preserved where `Styles.xaml` referenced them**, so the
  434-line style sheet did not need rewriting. Only 16 lines changed there, all of them
  either repointing a deleted key or fixing a contrast failure the new ramp exposed.
- **`Magenta`, `MidnightBlue` and `OffBlack` were deleted rather than repointed.** They were
  template names that would have ended up holding teal and navy — a trap for whoever reads
  this next. Their references now name what they mean (`PrimaryOnLight`, `Gray950`).
- **The neutral ramp is tinted toward the brand navy** rather than staying grey, so surfaces
  cohere instead of reading as grey pasted onto blue. `Gray600` *is* the palette's Muted and
  `Gray950` *is* its Background; the ramp and the palette are one thing, not two.
- **Web tokens are CSS custom properties on `:root`** with a single
  `prefers-color-scheme: dark` override. The two scattered dark blocks that were at
  `app.css:167` and `:364` were folded in, so the theme now lives in one place.
- **The web's primary action is styled off `button[type="submit"]`**, not a new class. Nine
  submit buttons already exist with no class attribute; inventing `.primary` would have meant
  dead CSS until someone touched the markup.
- **The splash background is `#122E42`, not the icon's cyan.** On Android 12+ the system
  always draws a splash — it cannot be disabled, only restyled — so the choice is what it
  looks like, and the brand Background sits closest to the app's own dark surface.

## Known limitation: the launcher icon is clipped

The wordmark measures 846×217 on the 1024 canvas — **82.6% of the width**. Android adaptive
icons guarantee only the centre **66.7%**; launchers crop the rest to a circle, squircle or
rounded square. Circular-mask launchers will lose the leading `t` and the trailing `r`.

This is accepted for now and is a **file swap to fix**: re-export the artwork with the
wordmark inside a centred box ~66% of the canvas width, re-run the extraction below, and
nothing in the project needs to change. Web favicons are not masked and are unaffected.

The two icon layers are derived from the square master, because an adaptive icon needs the
gradient and the wordmark as separate layers:

```sh
# foreground: isolate the near-white wordmark as an alpha mask
magick media/branding/trackr-icon-square-1024.png -grayscale Rec709Luma -level 76%,94% mask.png
magick -size 1024x1024 xc:'#F2FBFD' mask.png -alpha Off -compose CopyOpacity -composite \
       src/Trackr.Mobile/Resources/AppIcon/appiconfg.png
# background: synthesise the gradient clean, so the wordmark is not baked in twice
magick -size 1024x1024 gradient:'#38D7E5-#17B8A0' -depth 8 \
       src/Trackr.Mobile/Resources/AppIcon/appicon.png
```

## Verifying a change to any of this

`MauiIcon` and `MauiSplashScreen` output is cached in `obj/`, so `just mobile::clean` first
or the old artwork survives the build. And **Android caches launcher icons per package** —
`just mobile::uninstall` before reinstalling, otherwise the old icon keeps showing and reads
as a failure that is not one. `adb shell cmd uimode night yes|no` flips the theme on device.

## Since superseded

- **Everything this record left to milestone 5 has been decided**
  ([06-mobile-ux.md](06-mobile-ux.md)): `Shell` versus plain navigation (two shells, swapped on the
  window), the tab layout (Home | Chat | Trends, with the profile behind an avatar rather than a
  fourth tab), local storage (SQLite on the phone now), and Material versus a custom theme (prune
  the template, then a thin Trackr layer over Material).
- **The contrast rule stated here is now load-bearing in code.** Every brand fill in the app —
  the avatar circle, the primary action — takes `PrimaryDarkText`, because the brand colours are
  1.8–2.4:1 against `#F2FBFD` and fail the other way round.
