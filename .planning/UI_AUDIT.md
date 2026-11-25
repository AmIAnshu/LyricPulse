# LyricPulse — Milestone 1A (Phases 1–3) UI & Visual Design Audit

**Audit Date:** 2026-11-25  
**Auditor:** LyricPulse UI Auditor (ECC UI Review + `ui-ux-pro-max` / WCAG 2.2 AA/AAA)  
**Authoritative Specs Audited Against:**
- `design-system/lyricpulse/MASTER.md`
- `design-system/lyricpulse/MOTION_AND_STYLES.md`
- `design-system/lyricpulse/pages/tray.md`

**Scope:** Milestone 1A (`LyricPulse.Rendering`, `LyricPulse.App`, `LyricPulse.Core` visual lexicon & color extraction; Phase 4 styles 4–10 and Phase 5 Gallery/Settings AXAML views are out of Milestone 1A scope and not penalized).

---

## Executive Verdict: **PASS WITH NOTES** *(Re-Verified on `70876e0` + `c6420bb` — Cleared for Milestone 1A Sign-Off)*

> **Status Progression:** Initial Milestone 1A audit verdict was **FAIL** (1 Critical, 5 High, 4 Medium, 2 Low). Following remediation commits `70876e0` (Core) and `c6420bb` (Rendering/App), **all 1 Critical (`UI-01`), 5 High (`UI-02`–`UI-06`), and 4 Medium (`UI-07`–`UI-10`) findings are verified RESOLVED**. Only minor non-blocking Low polish notes remain (see [Re-Verification Addendum (`c6420bb`)](#re-verification-addendum-c6420bb) below).

### Initial Baseline Summary (Pre-Remediation)
| Severity | Initial Count | Post-`c6420bb` Open | Summary |
|---|---:|---:|---|
| **CRITICAL** | **1** | **0** | ~~179/180 Fluent Icon codepoints in `StarterLexicon.tsv` wrong~~ → **180/180 verified exact match** |
| **HIGH** | **5** | **0** | ~~Clipped Layer 0 scrim & bulge corners; `VisualStack` badge contrast; unwired HarfBuzz/fallback; `SKRoundRect`/`SKImageFilter` churn + `_rowScratch` race; stale `BoundsConfig`~~ → **All 5 verified resolved** |
| **MEDIUM** | **4** | **0** | ~~Light taskbar tray dots (`2.18:1`/`2.05:1`) + `WM_SETTINGCHANGE`; `Codestyle` vertical snap/clipping/surrogates; 3-style/lifecycle drifts; mixed-DPI `Show()` order + DI retention~~ → **All 4 verified resolved** *(multi-size `.ico` deferred to Low polish)* |
| **LOW** | **2** | **2 (notes)** | Non-blocking polish deferred to Phase 4/5: multi-size `.ico` vs 32px PNG, in-place `NativeMenu` mutation, `TrackingEm` glyph spacing, single-token multi-script split |

---

## 1. Dimension Scorecard

| # | Audit Dimension | Status | Key Evidence |
|---|---|---|---|
| 1 | **Desktop Legibility & Contrast** | **FAIL** | 4-layer order, `L < 0.38` halo inversion, `BodoniModa` `0.036x` thinning & `Antialias` edging **PASS**; **FAILS** on `VisualStack` icon badge contrast (`1.04:1–2.72:1` over `#FFFFFF`), unwired HarfBuzz/fallback (tofu on CJK/RTL/Devanagari), and dark custom `ManualAccent1` on `OffsetToast` |
| 2 | **SkSL Shader Correctness** | **FAIL** | Convex bulge sign, `rClamped > 0.0001` NaN guard, `Decal` + `Premul`, 13-tap loop, `<= 22 * dpiScale` tanh cap & `< 1.25 * dpiScale` bypass **PASS**; **FAILS** on corner transparency (`alpha = 0.080–0.156` at `(0,0)` due to Layer 0 scrim rendered inside undersized offscreen surface), unreachable bounds check in `EllipticalBulge.sksl:24`, and peripheral fold-back for `s > 0.323` |
| 3 | **3-Style Fidelity vs `MOTION_AND_STYLES.md`** | **FAIL** | Springs, `#EF4444` cursor, Reduced Motion gates **PASS**; **FAILS** on `Codestyle` mid-line `~21 DIP` vertical jump, `VisualStack` losing past rows across line breaks & lacking tempo compression, `Fisheye` unused `highlightEased` & missing Slot `-2` exit, `PauseDimOpacity = 0.45f` vs `0.62f` |
| 4 | **Fluent Icon Glyph Validity** | **CRITICAL FAIL** | Binary SFNT `cmap`/`post` verification of `FluentSystemIcons-Filled.ttf` proves **only 1/180** `StarterLexicon.tsv` rows matches its declared icon name; **173** render completely unrelated icons, **6** hit wrong optical size variants, **6** icon names do not exist in the TTF |
| 5 | **Tray Icon & Structural Chrome** | **PASS WITH NOTES** | **Zero emoji chrome** verified across `TrayIconFactory`, `TrayController`, `TrayMenuState`, `App.axaml`; crisp dark/light waveform marks; **NEEDS FIX** on Light taskbar status dots (`2.18:1` green / `2.05:1` amber vs WCAG `3.0:1`), multi-size `.ico`, live `WM_SETTINGCHANGE` refresh, and redundant idle header copy |
| 6 | **Multi-DPI & Performance Hygiene** | **FAIL** | `PerMonitorV2` manifest, `WindowState.Normal` + `WorkingArea`, `WS_EX_NOREDIRECTIONBITMAP` guard, and idle RAF shutdown **PASS**; **FAILS** on per-frame un-disposed `new SKRoundRect(...)` native leak, per-frame `SKImageFilter.CreateBlur(...)` allocations, UI/render thread race on `VisualStackStyleRenderer._rowScratch`, and stale captured `BoundsConfig` closure |

---

## 2. Detailed Findings & Concrete Fixes

### CRITICAL

#### `UI-01` — 179 of 180 Fluent Icon Codepoints in `StarterLexicon.tsv` Are Wrong (100% of Demo Keywords Broken)
- **Locations:**
  - `src/LyricPulse.Core/Resources/StarterLexicon.tsv:3-189`
  - `src/LyricPulse.Rendering/Assets/Fonts/FluentSystemIcons-Filled.ttf` (`cmap` fmt 12: 10,420 mapped codepoints `U+E000..U+F0FB3`; `post` v2.0: 10,054 glyph names)
  - `src/LyricPulse.Rendering/Styles/StyleLayoutHelper.cs:133-135`
- **Evidence (Direct Binary SFNT `cmap` + `post` Verification):**
  - Parsing the bundled `FluentSystemIcons-Filled.ttf` directly against every row of `StarterLexicon.tsv` yields:
    - **Exact match:** **1 / 180** (`line 178`, `sun_moon`, `F89C` = `ic_fluent_weather_partly_cloudy_night_24_filled`)
    - **Same base icon, wrong optical size (`20`/`48` instead of `24`):** **6 / 180** (`rain` `F8A0` → 48px, `city` `F2C2` → 20px, `rise` `F19B` → 20px, plus 3 reuse rows)
    - **Completely unrelated wrong icon:** **173 / 180**
    - **Declared icon name missing from TTF entirely:** **6 / 180**
  - **All 12 canonical Neon Horizon Demo Keywords (`MOTION_AND_STYLES.md` §7.2) fail:**

| TSV Line | Keyword | Declared Icon Name | TSV Hex | Actual Glyph Rendered by Bundled TTF | Correct Codepoint in Bundled TTF |
|---:|---|---|---|---|---|
| 3 | `star` | `ic_fluent_star_24_filled` | `F711` | `ic_fluent_speaker_settings_24_filled` (wrong!) | **`F719`** |
| 4 | `fire` | `ic_fluent_fire_24_filled` | `F411` | `ic_fluent_flag_pride_16_filled` (wrong!) | **`EF6A`** |
| 5 | `ocean` | `ic_fluent_water_24_filled` | `F88C` | `ic_fluent_weather_fog_20_filled` (wrong!) | **`F064`** |
| 6 | `night` | `ic_fluent_weather_moon_24_filled` | `F898` | `ic_fluent_weather_partly_cloudy_day_20_filled` (wrong!) | **`F896`** |
| 7 | `heart` | `ic_fluent_heart_24_filled` | `F47A` | `ic_fluent_headset_vr_20_filled` (wrong!) | **`F47E`** |
| 8 | `code` | `ic_fluent_code_24_filled` | `F2F4` | `ic_fluent_collections_add_24_filled` (wrong!) | **`F2F0`** |
| 9 | `rain` | `ic_fluent_weather_rain_24_filled` | `F8A0` | `ic_fluent_weather_rain_48_filled` (wrong size) | **`F89F`** |
| 10 | `city` | `ic_fluent_city_24_filled` | `F2C2` | `ic_fluent_city_20_filled` (wrong size) | **`F2C3`** |
| 11 | `flight` | `ic_fluent_airplane_24_filled` | `F10D` | `ic_fluent_add_circle_24_filled` (wrong!) | **`F110`** |
| 12 | `time` | `ic_fluent_clock_24_filled` | `F2D8` | `ic_fluent_clipboard_search_24_filled` (wrong!) | **`F2DE`** |
| 13 | `light` | `ic_fluent_lightbulb_24_filled` | `F4D3` | `ic_fluent_lasso_24_filled` (wrong!) | **`F4E1`** |
| 14 | `dream` | `ic_fluent_sparkle_24_filled` | `F6F3` | `ic_fluent_slide_layout_24_filled` (wrong!) | **`EB3D`** |

  - **6 declared icon names absent from `FluentSystemIcons-Filled.ttf`:**
    - `StarterLexicon.tsv:42` `ic_fluent_tree_evergreen_24_filled` → use `ic_fluent_tree_deciduous_24_filled` (`F015B`)
    - `StarterLexicon.tsv:52` `ic_fluent_music_note_2_play_24_filled` → use `ic_fluent_music_note_2_24_filled` (`E863`)
    - `StarterLexicon.tsv:84` `ic_fluent_calendar_24_filled` → use `ic_fluent_calendar_ltr_24_filled` (`F0298`) or `ic_fluent_calendar_day_24_filled` (`F223`)
    - `StarterLexicon.tsv:97` `ic_fluent_magnet_24_filled` → not in font (switch concept to emoji-only fallback or a present attraction/spark icon)
    - `StarterLexicon.tsv:133` `ic_fluent_sand_24_filled` → use `ic_fluent_hourglass_half_24_filled` (`EF6E`)
    - `StarterLexicon.tsv:151` `ic_fluent_walk_24_filled` → use `ic_fluent_person_walking_24_filled` (`F088`) or `ic_fluent_steps_24_filled` (`EBBA`)
  - **6 valid icons live in Supplementary PUA-A (`> U+FFFF`):** `door` (`F0A37`), `crown` (`F020F`), `mirror`/`rectangle_portrait` (`F0978`), `cactus`/`plant_ragweed` (`F0139`), `chess` (`F0A9E`), `key_golden`/`key_multiple` (`F0288`). Both `StarterLexiconVisualMatcher.cs:201` (`char.ConvertFromUtf32`) and `StyleLayoutHelper.cs:168` (`TryGetFirstCodepoint`) already handle surrogate pairs properly once 5-hex-digit codepoints are written.
  - **Why Existing Tests Missed It:** Because all 180 stale BMP codepoints (`F10C..F8AC`) collide with *other* densely-packed glyphs in `FluentSystemIcons-Filled.ttf`, `fonts.GetOrCreateFont(FluentIcons, ...).ContainsGlyph(cp)` (`StyleLayoutHelper.cs:135`) returns `true` for every wrong icon and silently suppresses emoji fallback.
- **Concrete Fix:**
  1. Generate `StarterLexicon.tsv` column 3 (`codepoint_hex`) programmatically from the exact bundled `FluentSystemIcons-Filled.ttf` `cmap` + `post` tables (replacing the 6 absent icon names above).
  2. Add an automated regression test in `LyricPulse.Tests` that parses the bundled TTF `cmap` + `post` tables and asserts `post[cmap[codepoint]] == fluent_icon_name` for 100% of `StarterLexicon.tsv` rows.

---

### HIGH

#### `UI-02` — Layer 0 Radial Ambient Scrim Hard-Clips Into a Boxy Rectangle & Violates Corner Transparency in `Fisheye` / `EllipticalBulge.sksl` and `VisualStack`
- **Locations:**
  - `src/LyricPulse.Rendering/Styles/FisheyeStyleRenderer.cs:143-144, 180-192, 284-297`
  - `src/LyricPulse.Rendering/Shaders/EllipticalBulge.sksl:15-28`
  - `src/LyricPulse.Rendering/Styles/VisualStackStyleRenderer.cs:171-196, 302-314`
- **Evidence (Exact Geometry Proof at `fontSize = 48` DIP, `ScrimIntensity.Balanced = 0.42`):**
  1. **`FisheyeStyleRenderer` clips Layer 0 scrim & leaks non-transparent corners through `EllipticalBulge.sksl`:**
     - Offscreen `bulgeSurface` half-dimensions: `layerWidthDip / 2 = 180.0 DIP`, `layerHeightDip / 2 = 120.0 DIP` (`FisheyeStyleRenderer.cs:143-144`).
     - `DrawAmbientScrim` is called *inside* `DrawTriadContent` (`FisheyeStyleRenderer.cs:291-297`) with radial scrim radii `rx = 245.8 DIP`, `ry = 164.5 DIP` — exceeding the offscreen bounds by **+65.8 DIP horizontally** and **+44.5 DIP vertically** (`alpha = 0.1145` along the clipped top/bottom edge before warp).
     - When `EllipticalBulge.sksl:20-21` applies convex magnification (`factor = 1.0 - clampedStrength * radial < 1.0`), `samplePos = iCenter + d * factor` pulls samples **inward** toward the center. At the corner pixel `fragCoord = (0, 0)`, it samples `(0.12*W, 0.12*H)` at rest (`s = 0.24`) and `(0.18*W, 0.18*H)` on line transition (`s = 0.36`), landing deep inside the clipped radial scrim:
       - Top/bottom edge midpoint alpha after bulge (`s = 0.24`): **`0.2224`** (`alphaByte = 57`) — visible dark hard-edged box on light wallpapers!
       - Corner `(0, 0)` alpha after bulge: **`0.0800`** at rest (`s = 0.24`) and **`0.1564`** during swell (`s = 0.36`) — fails corner-transparency requirement!
     - When `HighContrastScrim = true`, the high-contrast pill is also erroneously warped and motion-blurred.
  2. **`VisualStackStyleRenderer` flashes a clipped scrim rectangle during motion blur:**
     - When `shouldBlur == true` (`VisualStackStyleRenderer.cs:172, 302-314`), `scratchSurface` half-height is `152.6 DIP` (`rowPitch * 4.8 / 2`), whereas `scrimRect` (`centerY - 22.3`, `ry = 199.9 DIP`) reaches `-222.2 DIP` — **hard-clipping 69.6 DIP off the top** (`alpha = 0.1623` at the top edge). The instant blur drops into the dead-zone and falls back to direct canvas drawing (line 167), the clipped rectangular top edge abruptly pops back into an unclipped oval.
  3. **`EllipticalBulge.sksl` dead bounds check + peripheral fold-back at high swell:**
     - Lines 24–26 (`if (samplePos.x < 0.0 || samplePos.y < 0.0 || samplePos.x > iSize.x || samplePos.y > iSize.y) return half4(0.0);`) are **unreachable dead code** for any `fragCoord ∈ [0, iSize]` because `factor ∈ [0.15, 1.0]` always contracts `samplePos` strictly inside `[0, iSize]`. Meanwhile, the soft `edgeFade` specified in `MOTION_AND_STYLES.md:217-218` is absent.
     - For `iStrength > 1 / (iPower + 1)` (`s > 0.323` at default `p = 2.10` — reached on line transitions via `impulse = 2.5f` and up to the `0.40` clamp), `f(r) = r * (1 - s * r^p)` has negative derivative `f'(1) = -0.116` at `s = 0.36` and `-0.240` at `s = 0.40`, folding pixels slightly backward for `r ∈ [0.903, 1.0]`.
- **Concrete Fix:**
  1. Draw Layer 0 `context.Legibility.DrawAmbientScrim(...)` directly onto the main `context.Canvas` **before** compositing the warped/blurred offscreen glyph layer in both `FisheyeStyleRenderer.Render` and `VisualStackStyleRenderer.Render`. Keep offscreen shader surfaces strictly for Layers 1–3 (glyphs, icons, shadows, halos).
  2. In `EllipticalBulge.sksl`, replace the dead bounds check with a soft peripheral fade at the offscreen rect boundary (`float edgeFade = 1.0 - smoothstep(0.88, 1.00, max(abs(norm.x), abs(norm.y)));`) and use a strictly monotonic convex lens factor `float factor = 1.0 / (1.0 + clampedStrength * radial);` (guarantees `f'(r) > 0` for all `s >= 0, p >= 1`, outward convex magnification, zero fold-back, and `100%` transparent corners).
  3. Add vertical padding from `MotionBlurMath.ComputeDirectionalPaddingPx` (`MotionBlurMath.cs:65`) to `physH` in `FisheyeStyleRenderer` and `VisualStackStyleRenderer` so Slot `+1` / Row `-2` and ambient shadows never clip inside the offscreen surface during spring onset.

#### `UI-03` — `VisualStack` Icon Badges Fail White-Page Contrast (`1.04:1 – 2.72:1`) by Bypassing `LegibilityPipeline`
- **Locations:**
  - `src/LyricPulse.Rendering/Styles/StyleLayoutHelper.cs:116-158` (`DrawVisualGlyph`)
  - `src/LyricPulse.Rendering/Styles/VisualStackStyleRenderer.cs:442-497` (`DrawIconBadge`)
- **Evidence:**
  - `MOTION_AND_STYLES.md` §1.2 & §3.1 (lines 48–52, 120–121, 411) mandate:
    - Fluent System Icons MUST receive the paired text optical stroke halo (`StrokeHaloRatio = 0.048`) + Layer 1 shadows so active glyphs maintain **`>= 7.0:1`** over pure `#FFFFFF` webpages.
    - Emoji fallbacks MUST receive a Contact Shadow (without dark stroke) at `0.92×` optical scale.
    - `VisualStack` icon badges include a soft elevation shadow.
  - Currently, `StyleLayoutHelper.DrawVisualGlyph` draws raw `canvas.DrawText(..., scratchPaint)` with **zero shadow and zero stroke halo** inside a translucent badge (`AccentColor2 @ 22%` active, `14%` inactive).
  - Programmatically verified contrast over pure `#FFFFFF` webpage:
    - `Scrim=Off`, active Demo `AccentColor1` (`#5EEAD4`) in 22% `#818CF8` badge: **`1.20:1`** (Default `#F8FAFC`: **`1.18:1`**) — virtually invisible!
    - `Scrim=Balanced (0.42)`, active Demo `#5EEAD4`: **`1.93:1`** (Default `#F8FAFC`: **`2.72:1`**) — fails WCAG 3.0:1 non-text and 7.0:1 spec!
    - `Scrim=Balanced (0.42)`, inactive Row `-1` (`AccentColor2` @ `56%` opacity): **`1.04:1`**!
- **Concrete Fix:**
  1. Route Fluent Icon rendering in `StyleLayoutHelper.DrawVisualGlyph` through `LegibilityPipeline.DrawLegibleText(..., fontId: LyricFontId.FluentIcons, includeStrokeHalo: true)` (matching the paired lyric text's dual-pass shadow + optical stroke halo + dark-fill inversion).
  2. For Emoji fallback (`VisualKind.Emoji` or missing glyph), draw a Layer 1A Contact Shadow via `LegibilityPipeline` (`includeStrokeHalo: false`) and adjust scale from `0.88f` to `0.92f` (`StyleLayoutHelper.cs:150`).
  3. In `VisualStackStyleRenderer.DrawIconBadge` (`line 475`), add a dark translucent backing fill (`#0B0C16 @ 64%`) underneath the `AccentColor2 @ 22%` tint + subtle badge drop shadow so the badge silhouette itself holds `>= 3.0:1` separation over white webpages.

#### `UI-04` — HarfBuzz Multi-Script Shaping & System Font Fallback Are Unwired (Tofu Boxes on All Non-Latin Lyrics)
- **Locations:**
  - `src/LyricPulse.Rendering/Legibility/LegibilityPipeline.cs:144-203, 235`
  - `src/LyricPulse.Rendering/Fonts/FontAssetRegistry.cs:235-282`
  - `src/LyricPulse.Rendering/Fonts/TextShaperService.cs:48, 60-89`
- **Evidence:**
  - Binary `cmap` inspection of the 4 bundled lyric TTFs confirms **zero coverage** for Japanese (Hiragana/Katakana/Kanji), Korean (Hangul), Chinese (CJK), Arabic, Devanagari, and Unicode Emoji across all 4 fonts (`BodoniModa-Bold.ttf` also lacks Cyrillic).
  - Although `FontAssetRegistry.ResolveTypefaceForText` (`FontAssetRegistry.cs:245`) and `TextShaperService.ShapeToBlob` (`TextShaperService.cs:60`) exist, a full repository scan confirms **zero call sites anywhere in `src/`**.
  - `LegibilityPipeline.DrawLegibleText` invokes raw `canvas.DrawText(text, ..., font, paint)` directly with the primary bundled typeface. SkiaSharp does **not** perform automatic OS font fallback or RTL/Indic shaping in `SKCanvas.DrawText`, causing every K-Pop, J-Pop, C-Pop, Hindi, Arabic, or emoji-containing lyric to render as tofu boxes (`□`).
  - Additionally, `FontAssetRegistry.ResolveTypefaceForText` returns either a cached borrowed `_bundledTypefaces` instance or a newly allocated `SKFontManager.Default.MatchCharacter` `SKTypeface` (which must be disposed), creating a native leak trap once callers use it; and `TextShaperService.MeasureWidth:48` allocates a fresh `new SKShaper(font.Typeface)` per call for non-ASCII text.
- **Concrete Fix:**
  1. Cache fallback `SKTypeface`s (by script/codepoint or OS fallback chain `Segoe UI`, `Yu Gothic UI`, `Malgun Gothic`, `Microsoft YaHei UI`, `Nirmala UI`, `Segoe UI Emoji`) and `SKShaper` instances inside `FontAssetRegistry` (disposed in `FontAssetRegistry.Dispose()`).
  2. Cache shaped `SKTextBlob` runs keyed by `(text, fontId, quantizedFontSize)` (bounded LRU, e.g., 256 entries, cleared on font/line eviction per `MOTION_AND_STYLES.md` §8.2 line 653), falling back to script-resolved typefaces + HarfBuzz shaping whenever any glyph is missing or complex script (RTL/Indic/non-ASCII) is detected, and draw the cached `SKTextBlob` across Layers 1–3 in `LegibilityPipeline`.

#### `UI-05` — Per-Frame Unmanaged `SKRoundRect` Leak, Hot-Path `SKImageFilter` Allocations, and UI/Render Thread `List<T>` Race
- **Locations:**
  - `src/LyricPulse.Rendering/Styles/CodestyleStyleRenderer.cs:143` (unmanaged native leak every frame!)
  - `src/LyricPulse.Rendering/Styles/CodestyleStyleRenderer.cs:348` (hot-path `SKImageFilter.CreateBlur(5f, 5f)`)
  - `src/LyricPulse.Rendering/Legibility/LegibilityPipeline.cs:175` (hot-path `SKImageFilter.CreateBlur(8.0f, 8.0f)`)
  - `src/LyricPulse.Rendering/Styles/VisualStackStyleRenderer.cs:25, 58, 141` (UI thread vs Render thread concurrent `List<T>` mutation!)
- **Evidence:**
  1. **Native Skia handle leak at 60–144 FPS (`CodestyleStyleRenderer.cs:143`):** `context.Canvas.ClipRoundRect(new SKRoundRect(cardRect, 14f, 14f), antialias: true);` allocates a disposable `SKRoundRect` (`sk_rrect_t*`) on every single rendered frame without `using` or reuse, forcing GC finalizer pressure under sustained 144Hz playback.
  2. **Per-frame native `SKImageFilter` churn (`MOTION_AND_STYLES.md` §8.2 violation):**
     - `LegibilityPipeline.cs:175` calls `SKImageFilter.CreateBlur(8.0f, 8.0f)` on every `DrawLegibleText` call for active words (constant sigma — trivially cacheable as a single readonly field!).
     - `CodestyleStyleRenderer.cs:348` calls `SKImageFilter.CreateBlur(5f, 5f)` on every frame the `#EF4444` cursor is visible (constant sigma — should be cached as a single readonly field, and tuned to `6.0f` per `MOTION_AND_STYLES.md:374`).
  3. **Cross-thread crash hazard (`VisualStackStyleRenderer.cs:25, 58, 141`):** `_rowScratch` is a mutable `List<VisualStackRow>` cleared and rebuilt inside `Advance()` on the **Avalonia UI thread** (`LyricCanvasControl.OnAnimationFrame` → `AdvancePhysics`) AND cleared, rebuilt, and indexed inside `Render()` on the **Avalonia Skia compositor render thread** (`LyricDrawOperation.Render`). Under Win32 `AngleEgl` concurrent composition, interleaved `Clear()` + `Add()` + `_rowScratch[idx]` causes `ArgumentOutOfRangeException` or visual row corruption.
- **Concrete Fix:**
  1. Pre-allocate `private readonly SKRoundRect _clipRoundRect = new();` on `CodestyleStyleRenderer`, update via `_clipRoundRect.SetRectXY(cardRect, 14f, 14f)`, and dispose in `Dispose()`.
  2. Cache `private readonly SKImageFilter _activeGlowBlur = SKImageFilter.CreateBlur(8.0f, 8.0f);` on `LegibilityPipeline` and `private readonly SKImageFilter _cursorBlur = SKImageFilter.CreateBlur(6.0f, 6.0f);` on `CodestyleStyleRenderer` (disposing both in `Dispose()`).
  3. Eliminate shared mutable `_rowScratch` between `Advance()` and `Render()` in `VisualStackStyleRenderer` (use separate `_advanceRowScratch` and `_renderRowScratch` lists, or publish an immutable `ImmutableArray<VisualStackRow>` snapshot alongside an immutable frame-physics snapshot via `Volatile.Write`/`Volatile.Read`).

#### `UI-06` — Runtime `AnchorZone` / `SafeMarginPx` / `PerDisplayBounds` Changes Never Apply to Existing Overlays + Unclamped Offscreen `destRect` Clipping
- **Locations:**
  - `src/LyricPulse.App/App.axaml.cs:132-158` (`boundsOverride: placement.BoundsConfig` permanently captured in closure)
  - `src/LyricPulse.App/Platform/OverlayWindow.cs:56-64` (`ApplyPlacement`)
  - `src/LyricPulse.Rendering/Styles/FisheyeStyleRenderer.cs:238-242`
  - `src/LyricPulse.Rendering/Styles/VisualStackStyleRenderer.cs:198-202`
- **Evidence:**
  1. When `AnchorZone`, `SafeMarginPx`, or `PerDisplayBounds` changes at runtime, `App.ApplySnapshotToHost` calls `_overlayCoordinator.UpdateSettings(settings)` → `ReconcileNow()` → `existing.ApplyPlacement(placement)`. However, `CreateOverlayCanvas` (`App.axaml.cs:155`) permanently captured the startup `placement.BoundsConfig` in the `packetFactory` lambda, and `OverlayWindow.ApplyPlacement` only updates `Position`/`Width`/`Height`. Existing monitors remain **permanently frozen at their startup anchor/margin** until restart or monitor hotplug.
  2. Neither `FisheyeStyleRenderer.cs:238-242` nor `VisualStackStyleRenderer.cs:198-202` clamps `destRect` within `[0, ViewportWidthDip] × [0, ViewportHeightDip]`. Under default `AppSettings.AnchorZone = BottomThird` (`anchorY = 0.76`) on a 1080p display at `150%` DPI (`WorkingHeightDip ≈ 693 DIP`), raising `FontScale` to `1.25–1.60` pushes `destRect.Bottom` (`526.7 + 192 = 718.7 > 693 DIP`) **below the bottom of the overlay window**, clipping the upcoming word and lower lens edge.
- **Concrete Fix:**
  1. Store the active `OverlayWindowPlacement` (or `Volatile` `OverlayBoundsConfig`) on `OverlayWindow` / `MultiMonitorOverlayCoordinator` and update it inside `ApplyPlacement(placement)`, reading the current `BoundsConfig` dynamically inside `packetFactory`.
  2. Clamp `anchorX`/`anchorY` (or `destRect`) in `FisheyeStyleRenderer` and `VisualStackStyleRenderer` using `layerWidthDip * 0.5f + safeMargin` and `layerHeightDip * 0.5f + safeMargin` so the offscreen lens/stack never clips outside the monitor `WorkingArea` at `150%–200%` DPI or `FontScale = 1.60`.

---

### MEDIUM

#### `UI-07` — Light Windows Taskbar Status Dots Fail WCAG 3.0:1 (`2.18:1` / `2.05:1`), Single-Size `32×32` PNG, and Stale Taskbar Polarity on OS Theme Switch
- **Locations:**
  - `src/LyricPulse.App/Tray/TrayIconFactory.cs:28-29, 55-59, 112-132`
  - `src/LyricPulse.App/Tray/TrayController.cs:53, 82`
- **Evidence:**
  1. **WCAG 3.0:1 Non-Text Contrast Failure on Light Taskbar:** `TrayIconFactory.cs:28-29` uses dark-mode `LiveGreenDot = #22C55E` and `WarningAmberDot = #F59E0B` for BOTH dark and light taskbars, drawn atop a light separator halo (`#F8FAFC @ 88%`, line 121):
     - `#22C55E` vs `#F8FAFC`: **`2.18:1`** (FAIL `< 3.0:1`) → using `MASTER.md` Light token `LpLiveGreenBrush` (`#15803D`) yields **`4.79:1`** (PASS).
     - `#F59E0B` vs `#F8FAFC`: **`2.05:1`** (FAIL `< 3.0:1`) → using `MASTER.md` Light token `LpWarningBrush` (`#B45309`) yields **`4.80:1`** (PASS).
     *(Note: Dark taskbar dots pass strongly at `8.55:1` / `9.07:1`, and all dark/light waveform marks pass `4.53:1–16.53:1`.)*
  2. **Single-size `32×32` PNG instead of multi-size `.ico` (`pages/tray.md` §2):** `TrayIconFactory.cs:55` renders only `32×32` PNG bytes; Windows 100% DPI trays (`16×16`) and 125% (`20×20`) downscale `32×32` with subpixel bar blur (`barWidth = 2.56px`, `barGap = 1.6px` at 16px).
  3. **No live refresh on Windows Light/Dark taskbar switch:** `TrayController.ApplySnapshot:53` skips updates when `TrayMenuState` is unchanged, and `WM_SETTINGCHANGE` (`ImmersiveColorSet`) is never observed, leaving white icons invisible on a newly switched Light taskbar (and vice versa).
- **Concrete Fix:**
  1. Select `#15803D` (Playing) and `#B45309` (Error/No Lyrics) when `isLightTaskbar == true` (`TrayIconFactory.cs:129`).
  2. Synthesize a multi-entry `.ico` stream containing pixel-snapped PNG frames at `16`, `20`, `24`, `32`, and `48` px (or pass the monitor's tray DPI size).
  3. Listen for `WM_SETTINGCHANGE` (`lParam == "ImmersiveColorSet"`) on `_hiddenAnchorWindow` and call a `TrayController.RefreshThemeIcon()` method that re-reads `IsSystemLightTaskbar()` and updates `_trayIcon.Icon`.

#### `UI-08` — `Codestyle` Mid-Line Vertical Jump (~21 DIP Snap), Past-Line Horizontal Clipping, and UTF-16 Surrogate Split Risk
- **Locations:**
  - `src/LyricPulse.Rendering/Styles/CodestyleStyleRenderer.cs:118-148, 310-311`
- **Evidence:**
  1. **Mid-line vertical jump:** `showNext` flips from `false` to `true` mid-line when `slice.LineProgress >= 0.68` (line 126), changing `visibleRows` from `2` to `3`, increasing `blockHeight` by `+scaledLinePitch`, and shifting `cardRect.Top = anchorY - blockHeight * 0.5f` (line 135) upward by **`0.5 * scaledLinePitch` (~21 DIPs)** in a single frame right while the active word is typing!
  2. **Past-line right-edge chopping:** `blockWidth` (lines 118–123) measures ONLY `activeFullLine` while `ClipRoundRect(cardRect)` clips all 3 lines. Whenever a short lyric line follows a long line, the `// <previous line>` comment is abruptly sliced mid-word by the card border.
  3. **Surrogate-pair splitting:** `word.Text[..revealedChars]` (line 311) slices by UTF-16 `char` count rather than Unicode grapheme clusters (`StringInfo`), splitting emoji/supplementary characters into unpaired surrogates (`□`) mid-word.
- **Concrete Fix:**
  1. Lock `Codestyle` card height to a stable 3-row budget (or anchor `cursorRowY` to `anchorY` directly instead of re-centering a changing `blockHeight`), eliminating the mid-line vertical snap.
  2. Measure `max(activeFullLine, prevCommentLine, nextGhostLine)` (clamped to `maxBlockWidth`, with clean ellipsis trimming if any context line still exceeds `maxBlockWidth`) so `PreviousLine` never hard-clips mid-glyph.
  3. Use `StringInfo.ParseCombiningCharacters` / `StringInfo.SubstringByTextElements` (or `Rune` iteration) when computing `revealedText`.

#### `UI-09` — 3-Style Choreography & Lifecycle Spec Drifts (`MOTION_AND_STYLES.md` §§6 & 8.1)
- **Locations:**
  - `src/LyricPulse.Rendering/Styles/FisheyeStyleRenderer.cs:281, 299-345`
  - `src/LyricPulse.Rendering/Styles/VisualStackStyleRenderer.cs:58, 116-118, 141, 317-345`
  - `src/LyricPulse.Rendering/Styles/PlaybackLifecycleChoreographer.cs:42, 274-297, 329-348`
- **Evidence:**
  1. **`FisheyeStyleRenderer`:** `highlightEased = Ease.Highlight(_highlightFade)` (`line 281`) is completely ignored when `reducedMotion == false` (`activeWord` snaps immediately to `AccentColor1` instead of blooming `AccentColor3 → AccentColor1` over `150ms` per §6 Style 1); departing Slot `-2` vanishes in 1 frame instead of fading `0.54 → 0.00` over `140ms` (`Ease.Exit`).
  2. **`VisualStackStyleRenderer`:** `BuildGroupedRows` (`lines 58, 141`) builds rows only for `slice.CurrentLine`, causing Rows `-1` and `-2` to disappear abruptly at every line break and Row `+1` to go blank on the final word of a line; springs on `lines 116-118` omit `.WithTempoCompression(wordDurationSec)` (risking bounce jitter on fast rap `< 200ms` words per §4.4); Row `-3` exit fade (`150ms`, `Ease.Exit`) is absent.
  3. **`PlaybackLifecycleChoreographer`:**
     - `PauseDimOpacity = 0.45f` (`line 42`) diverges from `MOTION_AND_STYLES.md` §8.1 (`0.62×` current opacity during the 8s pause hold).
     - `DrawOffsetToast` (`lines 329-348`) renders `packet.Palette.AccentColor1` on dark `#0B0C16` with `includeStrokeHalo: false`. If the user selects a dark custom `ManualAccent1` (`L < 0.38`), the toast text drops to **`~1.1:1`** (unreadable!).
     - `DrawInstrumentalIndicator` (`lines 274-297`) draws 3 bare `AccentColor2` dots with zero shadow/halo: over `#FFFFFF`, `#818CF8 @ 45%` yields **`1.57:1`** (`2.34:1` at peak `80%`), failing 3.0:1 visibility.
- **Concrete Fix:**
  1. In `FisheyeStyleRenderer`, blend `AccentColor3 → AccentColor1` in OKLab/sRGB using `highlightEased` and track the departing Slot `-2` word with a `140ms` `Ease.Exit` fade timer.
  2. In `VisualStackStyleRenderer`, stitch trailing rows from `slice.PreviousLine` and the leading row from `slice.NextLine` into the stack window, apply `.WithTempoCompression(wordDurationSec)` to `LineShift`/`WordSnap`/`IconPop`, and fade departing Row `-3` over `150ms`.
  3. In `PlaybackLifecycleChoreographer`: update `PauseDimOpacity` to `0.62f`; render `OffsetToast` text in fixed high-contrast HUD tokens (`#FBBF24` Amber when non-zero per `MASTER.md` §3.1 `LpWarningTextBrush`, `#F8FAFC` on reset — `>= 10:1` over `#0B0C16` regardless of manual lyric color); add a subtle dark outline/contact shadow behind the 3 instrumental dots.

#### `UI-10` — Mixed-DPI Secondary Monitor Initial `ApplyPlacement` Ordering + Root DI Retention of Transient `ILyricSceneRenderer` Disposables
- **Locations:**
  - `src/LyricPulse.App/Platform/MultiMonitorOverlayCoordinator.cs:136-145`
  - `src/LyricPulse.App/App.axaml.cs:137`
  - `src/LyricPulse.Rendering/Host/LyricCanvasControl.cs:96-109`
- **Evidence:**
  1. For a newly attached monitor with a different DPI than the primary monitor (e.g., Primary `100%`, Secondary `150%`), `MultiMonitorOverlayCoordinator.cs:138-144` calls `overlay.ApplyPlacement(placement)` *before* `overlay.Show()`, when Avalonia's HWND still inherits the primary monitor's `RenderScaling`. Calling `Show()` crosses onto the `150%` monitor and triggers `WM_DPICHANGED`, which can mis-size the initial logical DIP rect until the next reconcile.
  2. `App.CreateOverlayCanvas` (`App.axaml.cs:137`) resolves `Transient` `IDisposable` `ILyricSceneRenderer` (`LyricStyleRouter` + its `CodestyleStyleRenderer`, `VisualStackStyleRenderer`, and `PlaybackLifecycleChoreographer` `SKPaint`s) directly from the root `ServiceProvider`, and `LyricCanvasControl.Dispose()` (`LyricCanvasControl.cs:96-109`) never disposes `_renderer`. On every monitor disconnect/reconnect or display toggle, old `LyricStyleRouter` instances and their native `SKPaint`s stay rooted in `ServiceProvider` until process exit.
- **Concrete Fix:**
  1. Re-invoke `overlay.ApplyPlacement(placement)` immediately after `overlay.Show()` (or inside `OverlayWindow.OnOpened` once the window resides on the target monitor's DPI context).
  2. Create a per-overlay `IServiceScope` (owned and disposed by `OverlayWindow` / `MultiMonitorOverlayCoordinator`), or dispose `_renderer` if owned inside `LyricCanvasControl.Dispose()`.

---

### LOW

#### `UI-11` — Tray Menu Copy, Paused Icon Semantics, and Menu Rebuild Polish (`pages/tray.md`)
- **Locations:**
  - `src/LyricPulse.App/Tray/TrayMenuState.cs:195, 204, 218, 254-257`
  - `src/LyricPulse.App/Tray/TrayController.cs:84, 97`
- **Evidence:**
  - When idle (`!hasTrack`), `TrayMenuState` emits nearly duplicate header rows: Row 1 `"Waiting for Spotify / Apple Music..."` (`line 195`) and Row 2 `"Waiting for Spotify or Apple Music"` (`line 218`), instead of `pages/tray.md` §4.1's `"No media playing"` / `"Waiting for Spotify or Apple Music"`.
  - When playing, Row 1 (`line 204`) omits the `"Playing: "` status prefix from `pages/tray.md` §4.1.
  - When `PlaybackState == Paused` (`line 254-257`), `ResolveIconState` returns `PausedOrHidden` (diagonal slash icon), which `pages/tray.md` §2 reserves strictly for **"Overlay Hidden"** (toggled off), misleading users into thinking lyrics are disabled while paused.
  - `TrayController.cs:84` replaces `_trayIcon.Menu = BuildNativeMenu(state)` on every state change rather than mutating existing `NativeMenuItem` properties in place (`pages/tray.md` §5: *"without rebuilding/flickering an already-open menu"*), and `line 97` embeds `({state.ShowLyricsGestureHint})` inside the header string instead of setting `NativeMenuItem.Gesture`.
- **Concrete Fix:** Align idle/playing header labels with `pages/tray.md` §4.1; map `Paused` to `Idle` (clean unslashed waveform) and reserve the diagonal slash exclusively for `!LyricsVisible || IsFullscreenHidden`; mutate persistent `NativeMenuItem` instances on the UI thread; use `KeyGesture.TryParse` for `Show Lyrics` shortcut display.

#### `UI-12` — Secondary Legibility & Typography Craft Polish
- **Locations:**
  - `src/LyricPulse.Rendering/Legibility/LegibilityPipeline.cs:88-99, 119, 189-203`
  - `src/LyricPulse.Rendering/Fonts/FontAssetRegistry.cs:23-83, 107`
  - `src/LyricPulse.App/App.axaml.cs:59`
- **Evidence:**
  - `HighContrastScrim` pill (`LegibilityPipeline.cs:88-99`) omits the `1px #26FFFFFF` inner hairline specified in `MASTER.md` §3.2 (`line 92`).
  - `FontOpticalMetrics.TrackingEm` (`-0.015em` Bodoni, `-0.020em` Jost, `-0.010em` Mulish) is cataloged in `FontAssetRegistry.cs:23-79` but never applied during glyph shaping/positioning.
  - `FontAssetRegistry.cs:107` resolves `Cascadia Mono` default Regular (`400`) weight rather than SemiBold/Medium (`500–600`) specified in `MOTION_AND_STYLES.md` §3.1 (`line 118`).
  - In `LegibilityPipeline.DrawLegibleText:189-202`, semi-transparent context words (`opacity < 1.0`, e.g., `0.48`, `0.28`) draw Layer 2 stroke directly underneath Layer 3 fill without an isolated layer, allowing the inner half of the dark stroke to bleed through translucent letterforms.
  - `_systemReducedMotion` (`App.axaml.cs:59`) is queried once at startup and not refreshed on `WM_SETTINGCHANGE`.
- **Concrete Fix:** Draw the `1px #26FFFFFF` inner stroke on the `HighContrastScrim` pill; apply `TrackingEm` once HarfBuzz `SKTextBlob` shaping is wired (`UI-04`); request `SKFontStyleWeight.SemiBold`/`Medium` when matching `Cascadia Mono`; optionally isolate semi-transparent `DrawLegibleText` calls or keep inactive word alphas `>= 0.54` with thinner halo scaling; refresh `IsSystemClientAreaAnimationEnabled()` on `WM_SETTINGCHANGE`.

---

## 3. Verified Passing Controls (Preserve During Remediation)

The following Milestone 1A visual & rendering guarantees were verified directly in source and **PASS**:
1. **Zero ClearType RGB Fringing:** `FontAssetRegistry.CreateConfiguredFont` (`FontAssetRegistry.cs:287-293`) enforces `SKFontEdging.Antialias`, `Subpixel = true`, `LinearMetrics = true`; zero `SubpixelAntialias` occurrences exist across `src/`.
2. **Authentic Static Bundled TTFs & Safe Loading:** All 5 bundled fonts (`BodoniModa-Bold`, `Jost-Bold`, `Mulish-Bold`, `Nunito-SemiBold`, `FluentSystemIcons-Filled`) verified as non-variable static TTFs (`fvar == false`) loaded via `AssetLoader → SKData.Create → SKTypeface.FromData` (never `FromStream`).
3. **4-Layer Ordering, Dark-Fill Inversion & Bodoni Thinning:** `LegibilityStyleSpec.cs:14, 70-89` accurately implements `L < 0.38` dark-fill halo inversion (`#070712 @ 76%` → `#F8FAFC @ 68%`), `BodoniModa` `0.036x` stroke ratio and `36px` min size floor, and single outer `SaveLayer` for global opacity (`LegibilityPipeline.cs:45-59`).
4. **OKLab / OKLCH Palette Extraction:** `OklabPaletteExtractor.cs` & `OklabColorMath.cs` accurately enforce `C_p85 < 0.035` monochrome fallback, exact role `L`/`C`/hue clamps, 12-step invariant-`L`/`H` sRGB gamut mapping, and `650ms` (`150ms` Reduced Motion) OKLab track morphing.
5. **Motion Blur Guardrails & Premul Decal Hygiene:** `MotionBlurMath.cs:11-63` enforces fixed `60Hz` divisor, `< 1.25 * dpiScale` dead-zone bypass, and `<= 22.0 * dpiScale` hyperbolic `tanh` saturation; `ShaderLibrary.cs:145, 217` uses `SKShaderTileMode.Decal`; `OffscreenSurfacePool.cs:89` allocates 64px-bucketed `BGRA8888` `Premul` surfaces with `GRContext` abandonment purging.
6. **Analytical Spring Determinism & Reduced Motion Gates:** `SpringTokens.cs` matches all 7 canonical constants, `SpringState.cs` uses closed-form analytical evaluation with `dt <= 1/30s` and rest snap (`0.001 / 0.002`), and Reduced Motion reliably bypasses both SkSL shaders and halts idle RAF frames.
7. **Zero Emoji Chrome & Win32 Layered Overlay Discipline:** Zero emojis in tray icon/menus/chrome; `app.manifest` declares `PerMonitorV2`; `OverlayWindow.cs:34, 59` locks `WindowState.Normal` within `WorkingArea`; `Win32OverlayInterop.cs:21-87` guards `SetLayeredWindowAttributes` against `WS_EX_NOREDIRECTIONBITMAP` and strips DWM shadows, transitions, Aero Peek, and Win11 rounded corners.

---

## 4. Recommended Remediation Priority Order

1. **Immediate (Blocker):** `UI-01` — Regenerate `StarterLexicon.tsv` codepoints directly from `FluentSystemIcons-Filled.ttf` `cmap`+`post` and lock with a unit test.
2. **Immediate (Blocker):** `UI-02` — Move Layer 0 `DrawAmbientScrim` out of offscreen shader surfaces onto `context.Canvas`, add `EllipticalBulge.sksl` soft edge fade + monotonic convex factor, and apply `ComputeDirectionalPaddingPx`.
3. **Immediate (Blocker):** `UI-03` & `UI-04` — Route Fluent/Emoji glyphs through `LegibilityPipeline` (fixing `1.20:1` white-page contrast) and wire cached HarfBuzz `SKTextBlob` + OS multi-script fallback so CJK/Korean/Arabic/Devanagari never render tofu boxes.
4. **Immediate (Blocker):** `UI-05` & `UI-06` — Fix the per-frame `SKRoundRect` leak, cache constant blur `SKImageFilter`s, eliminate the `VisualStackStyleRenderer._rowScratch` UI/render thread race, and make `BoundsConfig` dynamic with viewport clamping.
5. **Pre-Phase 4 Polish:** `UI-07` through `UI-12` — Light taskbar tray dot contrast tokens (`#15803D`/`#B45309`), multi-size `.ico` + `WM_SETTINGCHANGE`, `Codestyle` stable card height & width, choreography/lifecycle alignment, and mixed-DPI post-`Show()` `ApplyPlacement`.

---

## Re-Verification Addendum (`c6420bb`)

**Re-Verification Date:** 2026-11-25  
**Commits Audited:** `70876e0` (Core) + `c6420bb` (Rendering/App)  
**Updated Verdict:** **PASS WITH NOTES** — **0 Critical, 0 High, 0 Medium blockers remain.**

### Blocker Closure Matrix (`UI-01` – `UI-12`)

| ID | Orig. Sev | Status on `c6420bb` | Independent Verification Evidence |
|---|---|---|---|
| **`UI-01`** | CRITICAL | **RESOLVED** | Binary SFNT `cmap` (fmt 12) + `post` (v2.0) parse of `FluentSystemIcons-Filled.ttf` confirms **180 / 180 exact glyph name matches** (`0` mismatches, `0` missing). All **12 / 12** `Neon Horizon` demo keywords verified (`star` `F719`, `fire` `EF6A`, `ocean` `F064`, `night` `F896`, `heart` `F47E`, `code` `F2F0`, `rain` `F89F`, `city` `F2C3`, `flight` `F110`, `time` `F2DE`, `light` `F4E1`, `dream` `EB3D`). All **9 / 9** SMP (`> 0xFFFF`) glyphs verified and safely decoded via `Rune.DecodeFromUtf16` (`StyleLayoutHelper.cs:235-251`). |
| **`UI-02`** | HIGH | **RESOLVED** | Layer 0 `DrawAmbientScrim` now renders on the main `context.Canvas` **before** offscreen shader passes in `FisheyeStyleRenderer.cs:188-201` and `VisualStackStyleRenderer.cs:191-203`, with directional blur vertical padding via `MotionBlurMath.ComputeDirectionalPaddingPx`. `EllipticalBulge.sksl:19-31` & `MotionBlurMath.cs:17` cap `iStrength <= 0.30` (strictly monotonic `f'(1) = +0.085` at `p=2.05`, `+0.070` at `p=2.10`) and apply `smoothstep(0.84, 0.975, borderMetric)` discarding `edgeFade <= 0.001` → verified **`alpha == 0.0` at all 4 pixel-center corners** from `64×64` (`borderMetric = 0.9844`) through `600×360` (`0.9983`). |
| **`UI-03`** | HIGH | **RESOLVED** | `VisualStackStyleRenderer.cs:25-26, 548-572` backs badges with obsidian `#D90B0C16` (active) / `#B80B0C16` (inactive) and routes glyphs through `StyleLayoutHelper.DrawVisualGlyph` → `LegibilityPipeline.DrawLegibleText` (Fluent: stroke halo + dual shadow; Emoji: contact shadow at `0.92×`, `StyleLayoutHelper.cs:190-233`). Worst-case white-page (`#FFFFFF`, `Scrim=Off`) contrast jumps from `1.20:1` to **`9.97:1`** (badge vs `#FFFFFF`), **`6.74:1`** (Demo `#5EEAD4` vs badge fill before halo, `>= 9:1` with halo), and **`9.53:1`** (Default `#F8FAFC`, AAA). |
| **`UI-04`** | HIGH | **RESOLVED** | `FontAssetRegistry.InspectScriptRequirements` + family-deduplicated cached `MatchFallbackForCodepoint` + `GetOrCreateFallbackFont` + cached `GetOrCreateShaper` (`FontAssetRegistry.cs:230-388`) are wired directly into `LegibilityPipeline.DrawLegibleText` (`LegibilityPipeline.cs:186-205, 329-354`, 256-entry `SKTextBlob` cache) and `TextShaperService.MeasureWidth`, with all 3 renderers + `PlaybackLifecycleChoreographer` passing `fonts: context.Fonts`. |
| **`UI-05`** | HIGH | **RESOLVED** | Static scan confirms **zero** per-frame `new SKRoundRect` or `SKImageFilter.CreateBlur` allocations (`CodestyleStyleRenderer.cs:32-33, 152, 219-220, 366` reuses `_clipRoundRect` & `_cursorBlur(6.0f)`; `LegibilityPipeline.cs:24, 218, 248-264` caches blur filters in `_blurCache`). `VisualStackStyleRenderer.cs:32-33` splits `_advanceRows`/`_renderRows`, and `LyricCanvasControl._renderSync` + `LyricStyleRouter._sync` + `ShaderLibrary._shaderSync` synchronize all cross-thread render/advance/dispose paths. |
| **`UI-06`** | HIGH | **RESOLVED** | `OverlayWindow.cs:21, 63, 68` updates `_currentBoundsConfig` atomically via `Volatile.Write`/`Read` in `ApplyPlacement`; `App.axaml.cs:215` reads `overlayWindow.CurrentBoundsConfig` live per frame and deep-compares `PerDisplayBounds` (`lines 75-98`). Both `FisheyeStyleRenderer.cs:179-185` and `VisualStackStyleRenderer.cs:182-188` clamp `destRect` strictly inside the viewport via `StyleLayoutHelper.ClampLayerRectToViewport`. |
| **`UI-07`** | MEDIUM | **RESOLVED** | `TrayIconFactory.cs:34-36, 69-77` uses WCAG-verified Light taskbar tokens `#15803D` (**`4.79:1`**) and `#B45309` (**`4.80:1`**) on `#F8FAFC` halo; `MultiMonitorOverlayCoordinator.cs:273-276` → `App.cs:258-267` refreshes tray icon polarity & OS Reduced Motion live on `WM_SETTINGCHANGE`. *(Single `32×32` PNG vs multi-size `.ico` tracked as Low note below.)* |
| **`UI-08`** | MEDIUM | **RESOLVED** | `CodestyleStyleRenderer.cs:126-158, 325-329, 418-455` locks a stable 3-row card height (zero mid-line vertical snap), measures all 3 rows + ellipsis-trims long context lines, and slices typing reveals by grapheme clusters (`SliceByGraphemes`). |
| **`UI-09`** | MEDIUM | **RESOLVED** | `FisheyeStyleRenderer.cs:72-76, 333-348, 385` adds `AccentColor3 → AccentColor1` bloom and `140ms` Slot `-2` exit fade; `VisualStackStyleRenderer.cs:132-135, 277-316` adds cross-line `BuildWindowedRows` + tempo-compressed springs; `PlaybackLifecycleChoreographer.cs:43, 54-56, 296-312, 347-371` restores `PauseDimOpacity = 0.62f`, renders `OffsetToast` in `#F8FAFC` on `#D90B0C16` (**`12.49:1`** over `#FFFFFF`), and backs instrumental dots with a `#B8050510` obsidian pill (**`8.69:1`** vs `#FFFFFF`, **`5.88:1`** peak dot contrast). |
| **`UI-10`** | MEDIUM | **RESOLVED** | `MultiMonitorOverlayCoordinator.cs:149-151` re-applies `ApplyPlacement(placement)` after `Show()` for mixed-DPI secondary monitors; `App.axaml.cs:194-217` + `LyricCanvasControl.cs:136-139` use `ILyricSceneRendererFactory` with `ownsRenderer: true` (zero root DI disposable retention). |
| **`UI-11` / `UI-12`** | LOW | **MOSTLY RESOLVED** | Fixed: Idle header (`"No media playing"` / `"Waiting for Spotify or Apple Music"`), `"Playing: "` prefix, `OverlayHidden`-only diagonal slash, `\t`-aligned shortcut hint, `HighContrastScrim` `1px #26FFFFFF` inner hairline (`LegibilityPipeline.cs:21, 111-112`), `Cascadia Mono` `SemiBold` (`FontAssetRegistry.cs:114`), and live `WM_SETTINGCHANGE` Reduced Motion sync. |

### Remaining Non-Blocking Low Notes (Deferrable to Phase 4/5)
1. **Multi-size `.ico` Tray Bundle (`TrayIconFactory.cs:62`):** Currently emits a single `32×32` PNG rather than a multi-entry `.ico` (`16/20/24/32/48px` per `pages/tray.md` §2) for crisp pixel-snapped `100%`/`125%` DPI tray rendering.
2. **In-place `NativeMenu` Mutation (`TrayController.cs:147`):** Still replaces `_trayIcon.Menu = BuildNativeMenu(nextState)` on state diffs rather than mutating existing `NativeMenuItem` properties in place (`pages/tray.md` §5).
3. **Typography & Micro-Polish:** `FontOpticalMetrics.TrackingEm` (`FontAssetRegistry.cs:25-73`) is not yet applied to glyph positioning; `InspectScriptRequirements` resolves fallback per whole word/token (single unspaced tokens mixing two non-Latin scripts e.g. `"星⭐"` use the first missing script's fallback); `BuildWindowedRows` (`VisualStackStyleRenderer.cs:285`) allocates a tiny managed `new List<VisualStackRow>(8)` scratch list (zero native impact; easily hoisted to a field).


---

## Phase 6 v1.0 Release UI Audit

**Audit Date:** 2026-11-25  
**Auditor:** LyricPulse UI Auditor (ECC UI Review + `ui-ux-pro-max` / WCAG 2.2 AA/AAA)  
**Authoritative Specs Audited Against:**
- `design-system/lyricpulse/MASTER.md`
- `design-system/lyricpulse/MOTION_AND_STYLES.md`
- `design-system/lyricpulse/pages/gallery.md`, `settings.md`, `tray.md`

**Scope:** Phase 4B & Phase 5 visual deliverables (`AmbientPartyMesh.sksl`, `StageBackdropRenderer`, `AestheticsOverlayRenderer`, all 10 kinetic style renderers, `LyricPulseTokens.axaml`, `ColorPickerTheme.axaml`, `Icons.axaml`, `StageWindow.axaml/.cs`, `SettingsView.axaml`, `ObsBroadcastWindow.axaml`, `TrayIconFactory.cs`, `TrayController.cs`, `TrayMenuState.cs`). Static code & contrast verification (no `src/` edits; `dotnet test` was not executed because `dotnet` SDK is not on shell `PATH`).

---

### Phase 6 Executive Verdict: **FAIL** *(0 Critical, 2 High, 2 Medium, 1 Low — Remediate `UI-13` & `UI-14` Before v1.0 Sign-Off)*

| Severity | Open Count | Summary |
|---|---:|---|
| **CRITICAL** | **0** | Zero emoji chrome; all 10 styles registered; 5-size `.ico` verified; Dark/Light token contrast >= 5.03:1 |
| **HIGH** | **2** | `UI-13` (Gallery strip cards use one-shot `TryFindBrush` breaking runtime Dark/Light parity, text `"Pin"` instead of `PathIcon`, and `PropertyChanged` leak); `UI-14` (`_heroHost` keeps rendering 60+ FPS `AmbientPartyMesh` when hidden to tray + dead `IsFullscreen` `else if` branch) |
| **MEDIUM** | **2** | `UI-15` (Always-green top-bar status dot, missing active states on filter/Anchor/Theme/Scrim buttons, `32×32` top icon hit targets, minor OBS/taskbar hexes); `UI-16` (`CutoutWord` & `Messages` bypass HarfBuzz multi-script fallback, unsprung `FollowingDot` in 4 styles, `NodeLine` RM long-line overflow) |
| **LOW** | **1** | `UI-17` (Hero `Pin` / top toggles Regular-vs-Filled swap, `TopGlassBar` RM transition, minimal `ColorPickerTheme`, `Messages` managed list allocs) |

---

### Phase 6 Dimension Scorecard

| # | Audit Dimension | Status | Key Evidence |
|---|---|---|---|
| 1 | **Dark & Light `{DynamicResource}` Token Parity & Contrast** | **FAIL** *(High)* | `LyricPulseTokens.axaml` Dark & Light tokens verify **5.03:1 – 18.34:1** (`GlassPill` `6.74:1 – 17.67:1`); `StageWindow.axaml` & `SettingsView.axaml` bind `{DynamicResource}` cleanly (with intentional `#F8FAFC`/`#CBD5E1` over dark stage and `#00FF00` OBS chroma key). **FAILS** because `StageWindow.axaml.cs:319,378,385,390,410,412-414,448-450` snapshots brushes once via `TryFindBrush(...)` when building the 10 Gallery strip cards—leaving strip cards stuck in the old theme after runtime Dark/Light switches (`UI-13`). Also `ObsBroadcastWindow.axaml:16,18` (`#CC14161F`, `#33FFFFFF`, `#F4F6FA`) and `SettingsView.axaml:252` (`#475569`) use hardcoded hexes (`UI-15`). |
| 2 | **Zero Emoji Chrome & Vector Fluent `PathIcon`s + 5-Size `.ico`** | **PASS WITH NOTES** | Full scan confirms **zero emojis** across `LyricPulse.UI`, `LyricPulse.App`, and `LyricPulse.Rendering` chrome; `Icons.axaml` provides 21 crisp `StreamGeometry` icons; `TrayIconFactory.cs:73-113` & `Assets/LyricPulse.ico` verified as a 5-entry `.ico` (`16×16`, `20×20`, `24×24`, `32×32`, `48×48`, 3,564 bytes). **Note:** Gallery strip `pinBtn` (`StageWindow.axaml.cs:340-345`) renders text `"Pin"`/`"Pinned"` at `28×28` instead of `LpIconPinRegular`/`LpIconPinFilled` (`UI-13`). |
| 3 | **Fullscreen `F11` Auto-Hide & Gallery 30/60 FPS Gating** | **FAIL** *(High)* | `F11`/`Escape` keybindings, `2.0s` `_fullscreenIdleTimer`, `OnPointerMoved` wake, and `30/60 FPS` (`TargetMaxFps`) card hover/active gating work on visible cards. **FAILS** because closing `StageWindow` to Tray (`OnClosing` → `Hide()`) never pauses `_heroHost` (continuing 60+ FPS `AmbientPartyMesh` SkSL rendering invisibly), and `StageWindow.axaml.cs:245-252` has an unreachable `IsFullscreen` `else if` branch (`UI-14`). |
| 4 | **`AmbientPartyMesh.sksl`, `StageBackdropRenderer` & `AestheticsOverlayRenderer`** | **PASS WITH NOTES** | `AmbientPartyMesh.sksl` has zero loops, safe `max(iResolution, 1.0)` & `clamp(iEnergy, 0.5, 1.85)`, and `StageBackdropRenderer` skips `DesktopOverlay`/`ObsBroadcast` and falls back to static gradient in software/RM. `AestheticsOverlayRenderer` caches all `SKPaint` + `SKImageFilter`s (`_dotBlur`, `_sweepBlur`, `_cutoutBlur`) and `CutoutDarkInk` achieves `17.05:1–18.61:1` contrast. **Note:** `DrawInvertedCutoutWord` bypasses HarfBuzz fallback, and 4 styles skip `AdvanceDot` (`UI-16`). |
| 5 | **All 10 Styles + `ReducedMotion` Fallbacks & Multi-DPI** | **PASS WITH NOTES** | All 10 styles (`Fisheye`, `FisheyeVisual`, `Codestyle`, `CodestyleVisual`, `VisualStack`, `Constellation`, `SpidersWeb`, `NodeLine`, `KineticWall`, `Messages`) cleanly suppress SkSL shaders, 3D homography, blinking cursors, ripples, and continuous motion under `ReducedMotion = true`, and route text/icons through `LegibilityPipeline` + `ClampLayerRectToViewport`. **Notes:** `Messages` lacks HarfBuzz multi-script fallback, and `NodeLine` RM long lines (`> 8` words) overflow instead of wrapping (`UI-16`). |

---

### Phase 6 Detailed Findings & Concrete Fixes

#### HIGH

##### `UI-13` — Gallery Strip Cards Break Runtime Dark/Light Theme Parity (`TryFindBrush`), Render Text `"Pin"` Instead of Vector `PathIcon`, and Leak `PropertyChanged` Handlers
- **Locations:**
  - `src/LyricPulse.UI/Views/StageWindow.axaml.cs:276-459` (lines `319`, `331-352`, `378`, `385`, `390`, `410`, `412-414`, `436-453`)
- **Evidence:**
  1. `RebuildStyleStrip()` assigns `Background`, `Foreground`, and `BorderBrush` via `TryFindBrush(...)` (`this.TryFindResource(...)` snapshot at construction time) rather than live dynamic resource bindings. Switching `ThemeVariantMode` (`System` ↔ `Dark` ↔ `Light`) in `SettingsView` updates `StageWindow.axaml` and `SettingsView.axaml`, while all 10 bottom Gallery cards remain frozen in the previous theme's surface/text/border colors.
  2. `pinBtn` (`lines 331-346`) uses a hardcoded `Color.FromArgb(0xB8, 0x14, 0x16, 0x1F)` background, a `28×28` DIP hit target (`< 40×40` minimum in `MASTER.md` §8.1), and a `<TextBlock Text="Pin" />` label instead of vector `PathIcon` (`LpIconPinRegular` / `LpIconPinFilled`).
  3. `cardVm.PropertyChanged += ...` (`line 436`) attaches a new anonymous delegate inside `RebuildStyleStrip()` every time `StyleFilter` changes (`All` / `Pinned` / `With Visuals` / `Motion`) without detaching prior handlers, leaking references to detached card controls.
- **Concrete Fix:**
  1. Bind strip card brushes dynamically using `control[!Property] = new DynamicResourceExtension("Lp...")` (or move the card template into `StageWindow.axaml` `<DataTemplate x:DataType="vm:StyleCardViewModel">` with `{DynamicResource}` bindings).
  2. Replace the `"Pin"` `TextBlock` with a `PathIcon` bound to `LpIconPinRegular`/`LpIconPinFilled` inside a `40×40` hit area (visual `32×32` pill) using `{DynamicResource LpGlassPillBrush}` / `{DynamicResource LpAccentSoftBgBrush}`.
  3. Track and unsubscribe `PropertyChanged` handlers (or use AXAML `DataTemplate` bindings) before rebuilding `StyleStripPanel.Children`.

##### `UI-14` — Hidden `StageWindow` (`OnClosing` → Tray) Never Pauses `_heroHost` + Unreachable `IsFullscreen` Branch & Over-Wide Strip Viewport Floor
- **Locations:**
  - `src/LyricPulse.UI/Views/StageWindow.axaml.cs:86-111, 144-163, 245-252, 476, 495-501`
  - `src/LyricPulse.Rendering/Host/LyricCanvasControl.cs:133, 255, 268`
- **Evidence:**
  1. When the user closes `StageWindow` to the System Tray (`OnClosing`, lines `146-159`), `Hide()` is called and `PauseAllPreviewCards(true)` pauses `_cardHosts.Values` (`lines 495-501`), **but `_heroHost` is never paused**. Because `Hide()` does not fire `OnUnloaded` and `LyricCanvasControl.cs:268` checks `IsVisible` (which stays `true`) rather than `IsEffectivelyVisible`, `_heroHost` keeps scheduling `RequestAnimationFrame` at 60+ FPS (`TargetMaxFps = 0`) and executing `AmbientPartyMesh.sksl` while the window is hidden.
  2. In `OnViewModelPropertyChanged` (`lines 245-252`), the first `if (e.PropertyName == nameof(StageViewModel.IsFullscreen))` short-circuits the `or nameof(StageViewModel.IsFullscreen)` clause in the `else if`, so toggling `IsFullscreen` relies incidentally on `SizeChanged` instead of calling `UpdateVisibleStripCards()` reliably.
  3. `UpdateVisibleStripCards()` (`line 476`) clamps `viewportW = Math.Max(StyleStripScroller.Viewport.Width, 1000.0)`, keeping off-screen cards unpaused when the available strip width is narrower than `1000` DIP (e.g., `760×600` compact mode or `380px` Settings drawer open).
- **Concrete Fix:**
  1. Pause `_heroHost.IsCardPaused = true` in `OnClosing` (`Hide()`) and resume `_heroHost.IsCardPaused = false; _heroHost.RequestWake();` in `SurfaceAndFocus` / `OnOpened`. Also check `IsEffectivelyVisible` (or window visibility) in `LyricCanvasControl` as defense-in-depth.
  2. Call `UpdateVisibleStripCards()` inside the `IsFullscreen` branch of `OnViewModelPropertyChanged`, and remove the `1000.0` floor on `StyleStripScroller.Viewport.Width` once laid out (`> 0`).

---

#### MEDIUM

##### `UI-15` — Media Status Dot Permanently Locked to `LpLiveGreenBrush`, Missing Active Button Selection States, `< 40px` Top Icon Hit Targets, & Minor Chrome Hexes
- **Locations:**
  - `src/LyricPulse.UI/Views/StageWindow.axaml:83-84, 157, 164, 171, 223-235, 263-268, 380-384`
  - `src/LyricPulse.UI/Views/SettingsView.axaml:45-88, 198-203, 242-252, 311-316, 342-349`
  - `src/LyricPulse.UI/Views/ObsBroadcastWindow.axaml:16, 18`
- **Evidence:**
  1. `StageWindow.axaml:83-84` sets `Fill="{DynamicResource LpLiveGreenBrush}"` unconditionally on the 8px media status dot even when `StageViewModel.MediaHealth` (`StageViewModel.cs:757`) is `Idle`, `Paused`, `Warning`, or `Error`—violating `MASTER.md` §1/§10 (Signal Emerald reserved exclusively for live playback).
  2. Segmented/selection button groups—Style Filters (`All`/`Pinned`/`With Visuals`/`Motion`), Font/Backdrop flyouts (`StageWindow.axaml:223-235, 264-267`), Bilingual Sub-line (`SettingsView.axaml:198-203`), 5-Zone Safe Anchor Grid (`SettingsView.axaml:242-251`, despite `IsAnchorTopCenter`..`IsAnchorBottomRight` existing in `StageViewModel.cs:698-702`), Theme Variant (`311-316`), and Scrim Intensity (`342-349`)—have no bound active-state styling (`LpAccentSoftBgBrush`/`LpAccentBorderBrush`). The 4 font `RadioButton`s (`SettingsView.axaml:45-88`) also retain default Fluent circular bullets.
  3. Top-right icon buttons (`StageWindow.axaml:157, 164, 171`) are `32×32` DIP (`< 40×40` DIP minimum hit area in `MASTER.md` §8.1).
  4. `ObsBroadcastWindow.axaml:16, 18` uses hardcoded `#CC14161F`, `#33FFFFFF`, and `#F4F6FA` on the floating chroma bar (while its child `Button`s inherit Light/Dark theme foreground/backgrounds), and `SettingsView.axaml:252` hardcodes `#475569` for the simulated taskbar strip.
- **Concrete Fix:**
  1. Bind the 8px status dot `Fill` to `MediaHealth` (`LpLiveGreenBrush` for `PlayingSynced`, `LpWarningBrush` for `Warning`, `LpDangerBrush` for `Error`, `LpTextTertiaryBrush` for `Idle`/`Paused`/`Loading`).
  2. Add `.active` / `:checked` pill styles using `LpAccentSoftBgBrush` + `LpAccentBorderBrush` (with `Padding` compensation so borders never shift layout) across all filter, anchor, theme, scrim, sub-line, and font-tile selectors.
  3. Expand top-bar icon button hit areas to `40×40` DIP (`36×36` minimum) and replace `#CC14161F`/`#33FFFFFF`/`#F4F6FA` in `ObsBroadcastWindow.axaml` with `{DynamicResource LpGlassPillBrush}` / `{DynamicResource LpGlassPillBorderBrush}` / `{DynamicResource LpTextPrimaryBrush}` (and `{DynamicResource LpBorderStrongBrush}` for `SettingsView.axaml:252`).

##### `UI-16` — `CutoutWord` & `Messages` Bypass HarfBuzz Multi-Script Fallback + Unsprung `FollowingDot` in 4 Styles + `NodeLine` RM Long-Line Overflow
- **Locations:**
  - `src/LyricPulse.Rendering/Styles/AestheticsOverlayRenderer.cs:234`
  - `src/LyricPulse.Rendering/Styles/MessagesStyleRenderer.cs:205, 302, 314, 425, 433`
  - `src/LyricPulse.Rendering/Styles/CodestyleStyleRenderer.cs:362`, `CodestyleVisualStyleRenderer.cs:376`, `SpidersWebStyleRenderer.cs:288`, `KineticWallStyleRenderer.cs:373`
  - `src/LyricPulse.Rendering/Styles/NodeLineStyleRenderer.cs:102-109, 348`
- **Evidence:**
  1. `AestheticsOverlayRenderer.DrawInvertedCutoutWord` (`line 234`) draws directly with `canvas.DrawText(text, ..., font, _cutoutInkPaint)` without checking `FontAssetRegistry.InspectScriptRequirements` / HarfBuzz shaping, and `MessagesStyleRenderer` (`lines 205, 302, 314, 425, 433`) calls `canvas.DrawText` directly with `Segoe UI Variable`, risking tofu boxes (`□`) on CJK/Arabic/Devanagari lyrics in `Messages` and non-Latin cutout words in `FisheyeVisual`.
  2. `CodestyleStyleRenderer`, `CodestyleVisualStyleRenderer`, `SpidersWebStyleRenderer`, and `KineticWallStyleRenderer` invoke `_aesthetics.DrawFollowingDot(...)` without calling `_aesthetics.AdvanceDot(...)` in `Advance()`, causing the pulse dot to teleport to `fallbackX/fallbackY` without `Spring.WordSnap` glide or hop arc.
  3. `NodeLineStyleRenderer` sets `effectiveSpan = spanW * (count / 8f)` (`line 348`) for `> 8`-word lines while locking `panX = 0` under `ReducedMotion` (`line 104`), pushing words 9+ past the right viewport edge instead of wrapping to 2 static rows (`MOTION_AND_STYLES.md` §6 Style 8).
- **Concrete Fix:**
  1. Route `DrawInvertedCutoutWord` and `MessagesStyleRenderer` text runs through `FontAssetRegistry.InspectScriptRequirements` + `GetOrCreateFallbackFont` / `ShapeToBlobSynchronized` (using `LegibilityPipeline` or a shared cached blob helper).
  2. Call `_aesthetics.AdvanceDot(...)` inside `Advance()` for `Codestyle`, `CodestyleVisual`, `SpidersWeb`, and `KineticWall` (or share the last-computed target anchor) so the dot hops smoothly on word changes.
  3. In `NodeLineStyleRenderer`, clamp `effectiveSpan` to `spanW` (or wrap to 2 static rows) when `packet.ReducedMotion == true` and `wordCount > 8`.

---

#### LOW

##### `UI-17` — Secondary Craft, Accessibility & Reduced-Motion Polish
- **Locations:**
  - `src/LyricPulse.UI/Views/StageWindow.axaml:36, 162, 169, 352`
  - `src/LyricPulse.UI/Themes/ColorPickerTheme.axaml:1-6`
  - `src/LyricPulse.UI/Controls/StageCardCanvasHost.cs:221-223`
  - `src/LyricPulse.Rendering/Styles/AestheticsOverlayRenderer.cs:91-172`
  - `src/LyricPulse.Rendering/Styles/MessagesStyleRenderer.cs:176, 384-385, 475, 503`
- **Evidence:**
  - `StageWindow.axaml:352` always renders `LpIconPinFilled` on the hero callout Pin button even when `IsActiveStylePinned == false` (and lines `162, 169` don't swap Regular/Filled states).
  - `StageWindow.axaml:36` keeps `TopGlassBar` `0.18s` opacity transition active under `ReducedMotion` (`MASTER.md` §9 specifies `100ms` under Reduced Motion).
  - `ColorPickerTheme.axaml` only sets `ColorViewContentBackgroundBrush` and `ColorViewTabBackgroundBrush` (`Transparent`), leaving inner `ColorPicker` flyout surfaces to `FluentTheme` defaults.
  - `StageCardCanvasHost.GetFallbackDemoDocument()` (`lines 221-223`) synchronously initializes `GloveSemanticVisualMatcher` on the UI thread during first packet creation (`StarterLexicon` mode is fast, but could be pre-warmed off-thread).
  - `AestheticsOverlayRenderer.DrawWordColorSweep` (`lines 91-172`) still draws the blurred moving glow sweep when `ReducedMotion == true`; `MessagesStyleRenderer` allocates small per-frame managed arrays/lists (`SKPoint[4]`, `List<LyricLine>`, `List<string>`).
- **Concrete Fix:** Swap Regular/Filled `PathIcon` geometries on active toggles; use static underline-only in `DrawWordColorSweep` under `ReducedMotion`; hoist `MessagesStyleRenderer` corner arrays and scratch lists to instance fields.


---

### Phase 6 Re-Verification Addendum (`e0dfe47`)

**Re-Verification Date:** 2026-11-25  
**Commit Audited:** `e0dfe47` (`fix(ui,ux): remediate all Phase 6 v1.0 UI and UX audit blockers`)  
**Updated Phase 6 Verdict:** **PASS WITH NOTES** — **0 Critical, 0 High blockers remain.** *(Build/test results `282/282`, 0 warnings, `--smoke-test` exit 0 reported by orchestrator; this re-verification audited source & git diff statically with `src/` read-only.)*

#### Blocker Closure Matrix (`UI-13` – `UI-17`)

| ID | Orig. Sev | Status on `e0dfe47` | Independent Verification Evidence |
|---|---|---|---|
| **`UI-13`** | HIGH | **RESOLVED** | `StageWindow.axaml.cs` removes `RebuildStyleStrip()` and `TryFindBrush(...)` entirely. `StageWindow.axaml:34-52, 450-513` binds the 10-card strip declaratively via `<ItemsControl ItemsSource="{Binding FilteredStyleCards}">`, `Button.card-btn`, and live `{DynamicResource}` brushes (`LpSurfaceBrush`, `LpBorderDefaultBrush`, `LpBorderStrongBrush`, `LpAccentBorderBrush`, `LpFocusRingBrush`, `LpStageBackgroundBrush`, `LpMediaOutlineBrush`, `LpAccentPrimaryBrush`, `LpOnAccentPrimaryBrush`, `LpTextPrimaryBrush`, `LpSurfaceElevatedBrush`, `LpTextSecondaryBrush`). Strip Pin control (`StageWindow.axaml:482-495`) is now a `40×40` `ToggleButton` with vector `PathIcon` (`LpIconPinFilled`, zero `"Pin"` ASCII text) and zero leaked `PropertyChanged` delegates. |
| **`UI-14`** | HIGH | **RESOLVED** | `StageWindow.axaml.cs:173-178` pauses `_heroHost.IsCardPaused = true` alongside `PauseAllPreviewCards(true)` in `OnClosing` (`Hide()` to tray), and `SurfaceAndFocus`/`OnOpened` (`lines 109-113, 151-155`) resume `_heroHost.IsCardPaused = false; _heroHost.RequestWake();`. `OnViewModelPropertyChanged` (`lines 372-376`) calls `UpdateVisibleStripCards()` inside the `IsFullscreen` branch, and `UpdateVisibleStripCards` (`line 420`) lowers the viewport floor from `1000.0` to `320.0`. |
| **`UI-15`** | MEDIUM | **MOSTLY RESOLVED** | `StageWindow.axaml:117-130` binds the 8px status dot to `StageViewModel.MediaHealthDotBrush` (`PlayingSynced` green, `Warning` amber, `Error` red, `Loading` indigo, `Idle`/`Paused` muted slate) + `ShowTroubleshootingPill` (`"Fix?"`). Active selection styling (`Button.chip.active`, `Button.seg.active`) is wired across Font/Backdrop flyouts, Gallery filters, Bilingual Sub-line, 5-Zone Safe Anchor Grid, Theme Variant, and Scrim Intensity (`StageWindow.axaml:22-33, 91-98, 265-276, 306-309, 418-421`; `SettingsView.axaml:11-23, 213-218, 257-266, 326-331, 357-364`). Top icon buttons expanded to `36×36` (`StageWindow.axaml:197-218`), and `SettingsView.axaml:267` uses `{DynamicResource LpBorderDefaultBrush}` for the taskbar strip. *(Remaining non-blocking notes below.)* |
| **`UI-16`** | MEDIUM | **DEFERRED (Non-blocking Note)** | `LyricPulse.Rendering` was unchanged in `e0dfe47`; HarfBuzz fallback in `DrawInvertedCutoutWord` & `MessagesStyleRenderer`, `AdvanceDot` spring glide in 4 styles, and `NodeLine` RM long-line wrapping remain deferred post-v1.0 polish. |
| **`UI-17`** | LOW | **DEFERRED (Non-blocking Note)** | Hero `Pin` Regular/Filled swap, `TopGlassBar` RM `100ms` duration, `ColorPickerTheme` inner flyout overrides, and `MessagesStyleRenderer` small managed list hoisting remain non-blocking polish. |

#### Remaining Non-Blocking Notes Observed in `e0dfe47`
1. **Local Property Precedence on Top Studio Pill Chips (`StageWindow.axaml:91-98`):** `Size`, `Font`, `Screen`, and `Aesthetics` buttons retain inline `Background="Transparent" BorderThickness="0"`, which overrides `Button.chip.active`'s `Background` and `BorderThickness="1.5"` in Avalonia (only `Foreground` from `.active` applies). Removing those two local attributes lets the active flyout pill highlight cleanly.
2. **Light-Mode Status Dot Brush Tokens (`StageViewModel.cs:909-916`):** `MediaHealthDotBrush` allocates `new SolidColorBrush(...)` with dark-theme hexes (`#22C55E`, `#F59E0B`, `#DC2626`) rather than cached/theme-aware `LpLiveGreenBrush`/`LpWarningBrush` tokens (`#15803D`/`#B45309` in Light mode); paired with text label so legible, easily improved post-v1.0.
3. **`ObsBroadcastWindow.axaml:16,18` & Font `RadioButton` Template (`SettingsView.axaml:60-103`):** Floating OBS chroma pill still uses `#CC14161F`/`#33FFFFFF`/`#F4F6FA` over `#00FF00` chroma key, and font tiles still display default Fluent `RadioButton` circles.
