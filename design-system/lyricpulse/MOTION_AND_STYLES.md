# LyricPulse — Motion Choreography & 10 Kinetic Lyric Styles Specification

> **Status:** Authoritative Engineering & Visual Spec for `LyricPulse.Rendering` & `LyricPulse.Core`  
> **Companion Document:** [MASTER.md](file:///C:/Users/Anshu/.gemini/antigravity/scratch/LyricPulse/design-system/lyricpulse/MASTER.md) (Application Chrome, Gallery & Settings UI)  
> **Rendering Target:** Avalonia UI 11.x (`ICustomDrawOperation` + `ISkiaSharpApiLeaseFeature`), SkiaSharp 3.x (`SKCanvas`, `SKRuntimeEffect`), HarfBuzzSharp, 60–144 FPS Windows 10/11 Click-Through Layered Overlays (`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`).

---

## Table of Contents

1. [Overlay Legibility System (Transparent Desktop Architecture)](#1-overlay-legibility-system-transparent-desktop-architecture)
2. [Album Color Sync — OKLCH Perceptual Derivation Rules](#2-album-color-sync--oklch-perceptual-derivation-rules)
3. [Typography & Bundled Font Roles](#3-typography--bundled-font-roles)
4. [Spring Physics & Easing Token System](#4-spring-physics--easing-token-system)
5. [SkSL Shader Contracts & Uniform Animation Curves](#5-sksl-shader-contracts--uniform-animation-curves)
6. [10 Kinetic Lyric Styles — Complete Blueprints](#6-10-kinetic-lyric-styles--complete-blueprints)
7. [Built-In Demo Song Script (Gallery Live Preview)](#7-built-in-demo-song-script-gallery-live-preview)
8. [Lifecycle States, Performance Budgets & QA Gates](#8-lifecycle-states-performance-budgets--qa-gates)

---

## 1. Overlay Legibility System (Transparent Desktop Architecture)

A click-through desktop overlay never controls its backdrop. In a single listening session, the same transparent window floats over pure white websites (`#FFFFFF`), OLED dark IDEs (`#0D0D14`), high-frequency 3D games, and saturated wallpapers. Relying on bare colored text or a single drop shadow fails catastrophically on white pages.

`LyricPulse.Rendering` MUST composite every non-card lyric scene through the **4-Layer Composed Legibility Pipeline** (strictly back-to-front in premultiplied `BGRA8888` color space):

```text
[Desktop Backdrop — Unknown: #FFFFFF to #000000]
  ├── Layer 0: Adaptive Radial Ambient Scrim (feathered elliptical cloud)
  ├── Layer 1: Dual-Pass Drop Shadow (tight Contact Shadow + wide Ambient Shadow)
  ├── Layer 2: Optical Stroke Halo / Outer Keyline (drawn strictly UNDER fill)
  └── Layer 3: Contrast-Guarded Glyph & Visual Icon Fill (AccentColor1 / 2 / 3)
```

### 1.1 Layer Specifications

| Layer | Purpose | SkiaSharp Implementation | Exact Parameters (`100% DPI`, scale by `dpiScale`) |
|---|---|---|---|
| **Layer 0: Ambient Scrim** | Lowers local background luminance & visual noise behind active lyrics without drawing a boxy rectangle. | `SKShader.CreateRadialGradient` transformed into an ellipse fitted to the active lyric cluster bounding box `B`. | Center: `B.MidX, B.MidY`<br>Radii: `rx = B.Width * 0.62 + 1.4 * fontSize`, `ry = B.Height * 0.68 + 1.1 * fontSize`<br>Color: `ScrimTint` (default `#0F0F23`, or Album `AmbientInk`)<br>Stops: `0.00 → α`, `0.52 → 0.58α`, `0.82 → 0.14α`, `1.00 → 0.00`<br>Default `α`: `0.42` (`Subtle=0.24`, `Balanced=0.42`, `Strong=0.62`, `Off=0.00`) |
| **Layer 1A: Contact Shadow** | Anchors glyph silhouettes against bright `#FFFFFF` web pages so bright accents never dissolve into white. | `SKImageFilter.CreateDropShadowOnly` (or offset blurred glyph pass). | `dx = 0`, `dy = 1.5px`, `sigmaX = 2.5px`, `sigmaY = 2.5px`<br>Color: `#050510` @ `72%` alpha (`0xB8050510`) |
| **Layer 1B: Ambient Shadow** | Adds atmospheric separation over busy game HUDs and textured wallpapers. | `SKImageFilter.CreateDropShadowOnly` (cached per size bucket). | `dx = 0`, `dy = 4.0px`, `sigmaX = 14.0px`, `sigmaY = 14.0px`<br>Color: `#050510` @ `48%` alpha (`0x7A050510`) |
| **Layer 2: Optical Stroke Halo** | Guarantees ≥ 7.0:1 boundary contrast right at every letter edge even when `Scrim = Off`. | `SKPaintStyle.Stroke`, `StrokeJoin = Round`, `StrokeCap = Round`, `StrokeMiter = 2.0f`, rendered *before* fill pass. | Width: `clamp(fontSize * haloRatio, 1.8px, 5.0px)`<br>`haloRatio`: `0.048` (sans/mono), `0.036` (`BodoniModa-Bold` to preserve hairlines)<br>Color: `#070712` @ `76%` alpha (flips to `#F8FAFC` @ `68%` if user manually picks a dark fill with OKLCH `L < 0.38`) |
| **Layer 3: Glyph & Icon Fill** | Crisp foreground text and semantic vector icons. | `SKPaintStyle.Fill`, `IsAntialias = true`, `SubpixelText = true`, `Edging = Antialias` (NEVER `SubpixelAntialias` / ClearType on transparent `WS_EX_LAYERED` surfaces — ClearType causes RGB color fringing!). | Active word: `AccentColor1` (`100%` alpha)<br>Active halo glow (optional subtle): `sigma = 8px`, `AccentColor2 @ 28%` alpha<br>Upcoming/past word: `AccentColor3` (`58–72%` alpha, hard floor `≥ 34%` while visible) |

### 1.2 Worst-Case Contrast Proof (Pure `#FFFFFF` Webpage)

- **Balanced Scrim (`α = 0.42`) + Contact Shadow + `76%` Optical Stroke Halo**: Effective boundary alpha directly bordering any active glyph over `#FFFFFF` is `1 - (1 - 0.42)(1 - 0.55)(1 - 0.76) = 0.937` → effective local edge luminance `Y_bg ≈ 0.010` (`#181822`).
- With `AccentColor1` clamped to OKLCH `L ∈ [0.78, 0.92]` (`Y_fg ∈ [0.49, 0.80]`):
  - Minimum effective contrast = `(0.49 + 0.05) / (0.010 + 0.05) = 9.0:1` (**exceeds WCAG AAA 7.0:1**).
- **Scrim = Off (`α = 0.00`)**: Contact Shadow (`~65%` core edge) + `76%` Optical Stroke Halo yields effective edge luminance `Y_bg ≈ 0.012` → minimum contrast `8.7:1`.
- **Past/Upcoming Readable Lines** (`AccentColor3`, `L ∈ [0.58, 0.74]` @ `62%` alpha): Effective contrast against local halo stays `≥ 4.8:1` (**exceeds WCAG AA 4.5:1 and Large Text 3.0:1**).

### 1.3 Premultiplied Alpha & Transparent Compositing Rules

1. Always clear the Skia overlay frame with `canvas.Clear(SKColors.Transparent)` (`0x00000000`).
2. Never use `SKFontEdging.SubpixelAntialias` on transparent layered windows; use `SKFontEdging.Antialias` with `Subpixel = true` + `LinearMetrics = true` for jitter-free animated glyph scaling.
3. Apply the user's **Global Overlay Opacity** (`0.25–1.00`, default `0.95`) once via a single outer `canvas.SaveLayer(new SKPaint { Color = SKColors.White.WithAlpha((byte)(opacity * 255)) })` rather than multiplying individual layer alphas (which exposes stroke outlines *through* semi-transparent fills!).
4. Style exception: `Messages` renders opaque/high-opacity chat bubbles (`92–96%` alpha) with a crisp `1px` edge rim + soft elevation shadow instead of Layer 0 radial scrim + Layer 2 text stroke.

---

## 2. Album Color Sync — OKLCH Perceptual Derivation Rules

Raw k-means RGB centroids from album covers frequently produce muddy browns, near-black shadows, neon clipping, or indistinguishable adjacent hues. `LyricPulse.Core.Colors.AlbumPaletteExtractor` MUST run extraction in **OKLab** and enforce strict **OKLCH** perceptual bounds.

### 2.1 Deterministic Extraction Pipeline (Off UI Thread, `< 2.0ms`)

1. **Downsample**: Resize GSMTC `MediaProperties.Thumbnail` into an `SKBitmap` of `64 × 64` (`4,096` pixels, `SKFilterQuality.Medium`).
2. **Filter & Convert**: Drop pixels with `alpha < 128`. Convert remaining sRGB pixels → Linear RGB → OKLab `(L, a, b)` and OKLCH `(L, C, H°)`.
3. **Achromatic / Monochrome Detection**: Compute the 85th percentile chroma `C_p85`.
   - If `C_p85 < 0.035` (black-and-white / grayscale album art): bypass artificial color invention and emit the **Monochrome Studio Palette**:
     - `AccentColor1 = oklch(0.95, 0.008, 250°)` (`#F1F5F9`)
     - `AccentColor2 = oklch(0.78, 0.015, 250°)` (`#CBD5E1`)
     - `AccentColor3 = oklch(0.64, 0.015, 250°)` (`#94A3B8`)
     - `ScrimTint    = oklch(0.16, 0.020, 265°)` (`#0F0F23`)
4. **K-Means++ in OKLab**: Run deterministic k-means++ (`k = 6`, fixed seed `42`, max `12` iterations, stop threshold `Δ < 1e-4`). Ignore clusters with `< 1.5%` pixel share unless all clusters are tiny.
5. **Salience Ranking**: Score each cluster `i` by:
   ```text
   salience(i) = (share_i ^ 0.62) * (0.30 + 2.20 * C_i) * (1.0 - 0.85 * abs(L_i - 0.60))
   ```
   Sort clusters descending by `salience`.

### 2.2 Perceptual Role Assignment & Clamping Matrix

| Token | Semantic Role | Hue Selection (`H°`) | OKLCH Lightness (`L`) Clamp | OKLCH Chroma (`C`) Clamp | Notes |
|---|---|---|---|---|---|
| **`AccentColor1`** | Primary Active Word, Hero Focal Glyph, Active Node Core | Top salient cluster `H_1` | **`[0.78, 0.92]`** (lift dark hues smoothly; never crush past `0.92` so hue stays recognizable) | **`[0.09, 0.22]`** (cap ultra-neon green/yellow to prevent eye fatigue) | Always luminous & crisp against dark keyline/scrim. |
| **`AccentColor2`** | Secondary Glow, Semantic Icons (`*Visual`), Code Keywords, Star Rays, Accent Strokes | Highest-salience cluster with `angularDist(H_k, H_1) ≥ 38°`. If none exists, synthesize `H_2 = (H_1 + 42°) mod 360°` (or `-48°` if `H_1 ∈ [40°, 110°]` to avoid sickly yellow-green). | **`[0.66, 0.82]`** | **`[0.13, 0.26]`** (richest saturation in palette for energetic icon & glow pop) | Must differ perceptually from `AccentColor1` by `ΔE_ok ≥ 0.14`. |
| **`AccentColor3`** | Past/Upcoming Lyric Lines, Constellation Threads, Web Silk, Subtle Metadata | Third distinct cluster with `≥ 28°` separation from `H_1`, or cool-shifted companion `(H_1 + 24°) mod 360°`. | **`[0.62, 0.76]`** | **`[0.04, 0.12]`** (deliberately muted so context lines never steal focus from `AccentColor1`) | Rendered at `55–72%` opacity in inactive states. |
| **`ScrimTint`** *(internal)* | Layer 0 Radial Scrim & 3D Depth Mist | `H_1` (or darkest dominant cluster hue `H_bg`) | **`[0.14, 0.22]`** | **`[0.015, 0.055]`** | Subtly marries overlay atmosphere to album mood while staying near-OLED black. |

### 2.3 Gamut Mapping, Fallback & Track-Change Morphing

- **sRGB Gamut Mapping**: Never clamp raw `R, G, B` channels independently (which shifts hue — e.g., blue turning purple). Perform binary search on chroma `C ∈ [0, C_target]` (10 iterations) keeping `L` and `H` invariant until all linearized sRGB channels lie inside `[0.0, 1.0]`.
- **Fallback / Demo / No-Art Palette** (harmonized with `MASTER.md` Dark OLED + Play Green):
  - `AccentColor1`: `#F8FAFC` (`oklch(0.98, 0.004, 248°)`) — or `#4ADE80` (`oklch(0.81, 0.18, 150°)`) for Play-Pulse Demo Mode
  - `AccentColor2`: `#818CF8` (`oklch(0.68, 0.17, 274°)`) — lifted electric indigo from `MASTER.md` `#4338CA`
  - `AccentColor3`: `#94A3B8` (`oklch(0.71, 0.03, 255°)`) — cool slate mist
  - `ScrimTint`: `#0F0F23` (`oklch(0.17, 0.04, 281°)`) — `MASTER.md` OLED Midnight
- **Track-Change Morph**: Interpolate `(AccentColor1..3, ScrimTint)` in **OKLab** over **`650ms`** (`Ease.ColorMorph`), or **`150ms`** when Reduced Motion is active.
- **Manual Color Override Guard**: When the user disables Album Color Sync and selects custom colors via Avalonia `ColorPicker`, render the exact chosen RGB values untouched, and automatically inspect `L_oklch(customAccent1)`: if `L < 0.38`, invert Layer 2 Optical Stroke Halo to `#F8FAFC @ 68%` so dark custom fonts remain readable on dark IDEs.

---

## 3. Typography & Bundled Font Roles

### 3.1 Font Matrix & Optical Calibration

All 4 primary lyric typefaces are bundled SIL OFL `.ttf` assets under `Assets/Fonts/` and loaded into a cached `SKTypeface` registry at startup (zero per-frame disk/GDI lookups).

| Role / Asset | Classification | Personality & Best Style Pairings | Tracking (`letterSpacing`) | Line Height | Stroke Halo Ratio | Min Overlay Size | Special Calibration |
|---|---|---|---|---|---|---|---|
| **`BodoniModa-Bold.ttf`** *(SIL OFL)* | High-Contrast Modern Serif (`opsz 6..96`) | Editorial, cinematic, dramatic luxury. Best for `Fisheye`, `Constellation`, `KineticWall`. | `-0.015em` (hero ≥ 56px)<br>`-0.005em` (body < 56px) | `1.14×` | **`0.036×`** *(thinner to protect delicate serif hairlines)* | `36px` | Never apply `BodoniModa` below `32px` on 100% DPI — hairlines subpixel-shimmer; auto-clamp floor to `36px`. |
| **`Jost-Bold.ttf`** *(SIL OFL)* | Geometric Sans (Futura lineage) | Architectural, bold poster punch, crisp circular geometry. **Default** for `Fisheye`, `SpidersWeb`, `KineticWall`. | `-0.020em` (title/mixed)<br>`+0.045em` (ALL CAPS) | `1.12×` | `0.048×` | `24px` | Optically balanced for spherical lens warping and 3D perspective skewing. |
| **`Mulish-Bold.ttf`** *(SIL OFL)* | Humanist Minimalist Sans | Ultra-clean, open apertures, zero visual clutter across busy wallpapers. **Default** for `VisualStack`, `Constellation`, `NodeLine`. | `-0.010em` | `1.20×` | `0.048×` | `22px` | Highest legibility at medium sizes and rapid rap tempos. |
| **`Nunito-SemiBold.ttf`** *(SIL OFL)* | Friendly Rounded Sans | Warm, organic, playful rounded terminals. **Default** for `FisheyeVisual`. | `0.000em` | `1.22×` | `0.050×` | `22px` | Pairs effortlessly with rounded Fluent Filled Icons and emoji visuals. |
| **Monospace (`Codestyle` / `CodestyleVisual`)** | Technical Monospace | IDE / terminal lyric syntax. Chain: **`Cascadia Mono`** (SemiBold/Regular, ligatures OFF) → **`Consolas`** → `Courier New` *(optional bundled SIL OFL `JetBrainsMono-Medium.ttf`)*. | `0.000em` (strict grid) | `1.38×` | `0.044×` | `18px` | Weight locked to `500–600` (never Heavy `800`). Enable `tabular-nums` (`tnum`) for gutter line indices (`01`, `02`). |
| **System UI (`Messages` Fixed Lock)** | Native OS Humanist Sans | Authentic phone messaging realism. Chain: **`Segoe UI Variable Text`** (Win 11) → **`Segoe UI`** (Win 10). | `-0.005em` | `1.34×` | `0.000×` *(bubble card provides contrast)* | `15px` (body)<br>`11px` (meta) | **Intentionally ignores user lyric font setting** (locked to OS UI typeface for chat fidelity). Use `tabular-nums` for timestamps. |
| **Semantic Icons (`FluentSystemIcons-Filled.ttf`)** | Monochrome Vector Icon Font (MIT) | Crisp resolution-independent word icons (`*Visual`, `VisualStack`). | `0` | `1.00×` | Matches paired text halo | `20px` | Rendered via Skia glyph run tinted with `AccentColor2` (active: shifts to `AccentColor1` bloom). |
| **Color Emoji Fallback (`Segoe UI Emoji`)** | Windows COLR/CPAL Vector Emoji | Expressive fallback when CLDR emoji wins semantic cosine score. | `0` | `1.00×` | Contact shadow only (no dark stroke around multicolor glyphs) | `20px` | Apply `0.92×` optical scale relative to Fluent icons because emoji bounding boxes fill higher ink density. |

### 3.2 HarfBuzz Multi-Script Shaping & Auto-Fit Rules

1. **Shaping**: Always shape words via `HarfBuzzSharp` + SkiaSharp (`SKShaper`) with system fallback chain (`Segoe UI`, `Yu Gothic UI`, `Malgun Gothic`, `Microsoft YaHei UI`, `Nirmala UI`, `Segoe UI Emoji`) so Japanese, Korean, Chinese, Cyrillic, Arabic, Devanagari, and accented Latin lyrics render seamlessly without tofu boxes (`□`).
2. **User Font Size Scaling**: User slider `FontSize` spans **`24px–96px`** (default **`48px`** at `100%` DPI). Multiply all pixel metrics by `dpiScale = monitorDpi / 96.0f`.
3. **Overflow Auto-Fit Guard**: Never let a single word or tight stack clip outside the monitor safe zone (`8%` horizontal inset, `8%` vertical inset):
   ```csharp
   float fitScale = Math.Clamp((viewportWidth * maxLineWidthRatio) / Math.Max(measuredWidth, 1f), 0.56f, 1.0f);
   ```
   If `fitScale` hits `0.56f`, wrap cleanly at word boundaries (never mid-word unless a single token exceeds `80%` width).

---

## 4. Spring Physics & Easing Token System

All spatial animations in `LyricPulse.Rendering` use a deterministic **Damped Harmonic Oscillator** (`SpringState`) so rapid track seeks, skips, and tempo changes retarget smoothly with conserved velocity (zero snap-glitches).

### 4.1 Spring Integrator Contract (`LyricPulse.Rendering.Physics`)

- **Fixed-Step / Clamped Integration**: Clamp frame `dt = Math.Min(rawDt, 1f / 30f)` (`≤ 33.3ms`) and sub-step at `1/240s` (`4.17ms`) or use closed-form analytical evaluation so 60Hz, 120Hz, and 144Hz displays trace the **exact same curve**.
- **Sleep / Rest Threshold**: Snap `value = target; velocity = 0` when `|value - target| < 0.001f && |velocity| < 0.002f` so idle frames drop CPU usage toward `< 1%`.
- **Damping Ratio Formula**: $\zeta = \frac{c}{2\sqrt{k \cdot m}}$, Natural Frequency: $\omega_0 = \sqrt{k / m}$.

### 4.2 Shared Spring Token Table

| Token Name | `stiffness (k)` | `damping (c)` | `mass (m)` | Damping Ratio (`ζ`) | Overshoot | Visual Settle | Primary Choreographic Usage |
|---|---:|---:|---:|---:|---:|---:|---|
| **`Spring.WordSnap`** | `420` | `28.0` | `1.0` | `0.683` | `~5.3%` | `~240ms` | Active word scale pop (`1.00 → 1.08`), focal lift, Fisheye active center lock |
| **`Spring.WordEnter`** | `280` | `24.0` | `1.0` | `0.717` | `~4.0%` | `~320ms` | Word entrance slide/rise, Kinetic Wall landing impact, Node label bloom |
| **`Spring.LineShift`** | `210` | `26.0` | `1.0` | `0.897` | `~0.2%` | `~360ms` | Vertical stack shift (`Fisheye`, `VisualStack`), line-to-line recentering (virtually bounce-free) |
| **`Spring.IconPop`** | `480` | `22.0` | `0.9` | `0.529` | `~14.0%` | `~330ms` | Semantic word-icon pop-in (`0.45 → 1.00`), Constellation star flare, Node milestone ring |
| **`Spring.CameraDrift`** | `95` | `19.5` | `1.0` | `1.000` | `0.0%` | `~600ms` | Critically damped silky camera pan/orbit (`KineticWall` 3D plane, `Constellation` view pan) |
| **`Spring.BubblePush`** | `310` | `27.0` | `1.0` | `0.767` | `~2.3%` | `~290ms` | `Messages` chat thread upward push and new message bubble entrance |
| **`Spring.LensBreath`** | `340` | `26.0` | `1.0` | `0.705` | `~4.4%` | `~300ms` | Elliptical Bulge shader `iStrength` impulse breathing on active word onset |

### 4.3 Easing Curve & Exit-Faster-Than-Enter Token Table

> **Core Motion Law (Exit Faster Than Enter):** Departing words/lines MUST exit **35–45% faster** and with lower visual energy (`Ease.Exit`) than arriving words (`Ease.Enter`), clearing cognitive space cleanly before the next lyrical phrase arrives.

| Token Name | Curve Definition | Duration Range | Usage |
|---|---|---:|---|
| **`Ease.Enter`** | `cubic-bezier(0.16, 1.00, 0.30, 1.00)` *(Expo Out)* | `260–340ms` | Opacity reveal, glow bloom, line arrival |
| **`Ease.Exit`** | `cubic-bezier(0.40, 0.00, 1.00, 1.00)` *(Quad/Cubic In)* | `140–180ms` | Departing lines, dismissed bubbles, faded stars (~40% faster than Enter) |
| **`Ease.Highlight`** | `cubic-bezier(0.22, 1.00, 0.36, 1.00)` *(Quint Out)* | `150–180ms` | Active word color shift (`AccentColor3 → AccentColor1`) & halo glow ramp |
| **`Ease.LinearProgress`** | `linear(t)` | `= wordDuration` | Continuous playback-synced fills: `NodeLine` path trace, `Constellation` connecting edge draw, `Codestyle` character typing cursor |
| **`Ease.ColorMorph`** | `cubic-bezier(0.25, 0.10, 0.25, 1.00)` | `650ms` | Album Color Sync OKLab cross-fade on track change |

### 4.4 Tempo-Adaptive Pacing Compression (Ballad vs. Fast Rap)

Real tracks range from slow ballads (`wordDuration = 1200ms`) to double-time rap (`wordDuration = 90ms`). Applying a fixed `320ms` spring with overshoot to `100ms` words creates unreadable jitter. Apply a per-word pacing multiplier:

```csharp
// Scales animation stiffness up and reduces overshoot on rapid words (< 250ms)
float pacing = Math.Clamp(wordDurationSec / 0.32f, 0.42f, 1.35f);
float effectiveStiffness = baseSpring.Stiffness / (pacing * pacing);
float effectiveDamping   = baseSpring.Damping / pacing * (pacing < 0.65f ? 1.25f : 1.0f);
```
- At `wordDuration = 120ms` (`pacing = 0.42`): transitions tighten automatically to `~110ms` with extra damping (zero chaotic bounce).
- At `wordDuration = 700ms` (`pacing = 1.35`): entrances relax gracefully to `~420ms` for lush ballad feel.

### 4.5 Global Reduced Motion Contract

Triggered whenever EITHER `AppSettings.ReducedMotion == true` OR Windows `SystemParametersInfo(SPI_GETCLIENTAREAANIMATION) == false`:
- **All SkSL Shaders Disabled**: `EllipticalBulge` bypassed (`iStrength = 0`), `DirectionalMotionBlur` bypassed (`iVelocity = (0,0)`).
- **Zero Spatial Drift / 3D Rotation / Bounce**: All springs clamp to `ζ ≥ 1.0` or replace positional travel (`translate`, `rotate`, `3D tilt`, `scale overshoot`) with crisp **`140ms` pure opacity + color cross-fades** (`Ease.Highlight`).
- **Blinking Cursor (`Codestyle`)**: Replaces hard blink with steady low-contrast breathing (`0.65 ↔ 0.95` alpha over `1.6s`) or static solid caret.
- **No Unbounded Continuous Motion**: Background star twinkle, web strand vibration, and 3D plane idle float stop completely.

---

## 5. SkSL Shader Contracts & Uniform Animation Curves

Both shaders are compiled once at startup via `SKRuntimeEffect.CreateShader(...)` with graceful fallback to unshaded rendering if GPU compilation fails on legacy drivers.

### 5.1 Shader 1 — Elliptical Bulge / Spherical Lens (`EllipticalBulge.sksl`)

Used by: **`Fisheye`**, **`FisheyeVisual`**.

```glsl
uniform shader content;
uniform float2 iCenter;   // Lens center in physical px
uniform float2 iSize;     // Lens active bounding dimensions in physical px
uniform float  iStrength; // Radial distortion intensity [0.00 .. 0.40]
uniform float  iPower;    // Radial falloff exponent [1.85 .. 2.35]

half4 main(float2 fragCoord) {
    float2 d = fragCoord - iCenter;
    float2 halfSize = max(iSize * 0.5, float2(1.0));
    float2 norm = d / halfSize;
    float r = length(norm);
    // Clamp r to prevent extreme peripheral fold-back artifacts
    float rClamped = min(r, 1.55);
    float factor = 1.0 + iStrength * pow(rClamped, iPower);
    float2 samplePos = iCenter + d * factor;
    // Soft edge mask outside source bounds to prevent edge smearing
    float2 rel = abs(samplePos - iCenter) / (halfSize * 1.15);
    float edgeFade = 1.0 - smoothstep(0.92, 1.05, max(rel.x, rel.y));
    return content.eval(samplePos) * half(edgeFade);
}
```

#### Exact Uniform Animation Curves (`EllipticalBulge`)

| Phase / Parameter | Formula / Curve | Comfort Guardrails |
|---|---|---|
| **`iCenter`** | `(viewportWidth * anchorX, viewportHeight * anchorY)` (default centered `0.50, 0.50`) | Locked steady — never jitters on word changes. |
| **`iSize`** | `float2(max(stackWidth * 1.45, fontSize * 7.5), fontSize * 4.6)` | Scales identically with `dpiScale` and Gallery thumbnail scale so curvature is resolution-independent! |
| **`iPower`** | Constant **`2.10`** (`2.05` for `BodoniModa` to widen flat center zone) | Keep inside **`[1.85, 2.35]`**. Guarantees central `r < 0.35` focal word stays optically flat and crystal clear while outer `prev`/`next` words bend smoothly around the sphere. |
| **`iStrength` Baseline** | **`0.24`** (resting convex barrel bulge) | **Hard Comfort Ceiling: `0.40`**. Values `> 0.44` warp glyphs past readable curvature and trigger vestibular discomfort. |
| **`iStrength` Active-Word Beat Impulse** | On every word onset, add velocity kick `+1.8` to `Spring.LensBreath` targeting `0.24`, producing a silky `+0.06..+0.09` swell peaking at `t = 55ms` (`peak ≈ 0.32`) and settling back to `0.24` by `t = 290ms`. | Scales down by `pacing` factor on fast songs so rapid beats don't strobe the lens. |
| **`iStrength` Line-Transition Swell** | Peak `0.36` over `110ms` (`Ease.Enter`), relax to `0.24` over `260ms` (`Spring.LensBreath`). | Creates a tactile optical "turn of the sphere" between lines. |
| **Reduced Motion** | **`iStrength = 0.0` (Bypass shader pass entirely)** | Zero GPU post-process overhead, flat crystal-clear stack. |

### 5.2 Shader 2 — 13-Tap Directional Motion Blur (`DirectionalMotionBlur.sksl`)

Used by: **`Fisheye`**, **`FisheyeVisual`**, **`VisualStack`**, **`KineticWall`**, and optionally light line-pan transitions in **`Constellation`**, **`SpidersWeb`**, **`NodeLine`**.  
**NEVER used by:** `Codestyle`, `CodestyleVisual`, `Messages` (crisp typographic/UI styles must remain razor sharp).

```glsl
uniform shader content;
uniform float2 iVelocity; // Blur vector in physical px (per-frame shutter travel)

half4 main(float2 fragCoord) {
    half4 accum = half4(0.0);
    float weightSum = 0.0;
    for (int i = 0; i < 13; i++) {
        float offset = (float(i) / 12.0) - 0.5;        // -0.5 .. +0.5
        float w = max(0.0, 1.0 - abs(offset) * 1.2);   // 1.0 center, 0.40 tails
        accum += content.eval(fragCoord + iVelocity * offset) * half(w);
        weightSum += w;
    }
    return accum / half(weightSum);
}
```

#### Exact Uniform Animation Curves (`DirectionalMotionBlur`)

1. **Instantaneous Velocity Derivation** (from active layout spring velocity `v_px_per_sec` in physical px/s):
   ```csharp
   SKPoint rawVel = new(
       (vPxPerSec.X / 60f) * shutterFactor,
       (vPxPerSec.Y / 60f) * shutterFactor
   );
   ```
   *(Divide by `60f` fixed reference, NOT current monitor refresh rate, so 144Hz monitors still receive consistent cinematic blur rather than disappearing!)*
2. **Dead-Zone Bypass (Zero Cost at Rest)**:
   - If `|rawVel| < 1.25f * dpiScale`, **skip shader pass entirely** and draw directly to the canvas! Keeps static text 100% razor sharp and saves GPU fill-rate.
3. **Soft Hyperbolic Saturation Clamp (Anti-Smear Guard)**:
   ```csharp
   float maxBlurPx = 22.0f * dpiScale; // Never exceeds 22px @ 100% DPI
   float mag = MathF.Sqrt(rawVel.X * rawVel.X + rawVel.Y * rawVel.Y);
   float clampedMag = maxBlurPx * MathF.Tanh(mag / maxBlurPx);
   SKPoint iVelocity = mag > 0.001f ? new(rawVel.X * (clampedMag / mag), rawVel.Y * (clampedMag / mag)) : SKPoint.Empty;
   ```
4. **Style Shutter Factor Matrix**:

| Style Group | `shutterFactor` | Motion Axis | Peak Blur Feel |
|---|---:|---|---|
| `Fisheye`, `FisheyeVisual` | `0.48` | Vertical (`Y` stack shift) | Silky cinema-170° vertical glide between 3-word slots |
| `VisualStack` | `0.42` | Vertical (`Y` stack push) | Clean energetic slot advance |
| `KineticWall` | `0.36` | 2D screen-space camera delta (`X, Y`) | Smooth pan across 3D typography wall |
| `Constellation`, `SpidersWeb`, `NodeLine` | `0.26` | Line-transition camera sweep only | Ultra-subtle atmospheric transition |
| `Codestyle`, `CodestyleVisual`, `Messages` | **`0.00`** | Disabled | Permanently razor-sharp monospace & chat UI |
| **Reduced Motion (All Styles)** | **`0.00`** | Disabled (bypassed) | Zero blur |

---

## 6. 10 Kinetic Lyric Styles — Complete Blueprints

### Master Comparison Table

| # | Enum ID | Display Name | Default Font | Semantic Icons | Elliptical Bulge | Motion Blur | Color Mode | Desktop Anchor |
|---:|---|---|---|---|---|---|---|---|
| 1 | `Fisheye` | **Fisheye** | `Jost-Bold` | None | **Active** | **Active (`0.48`)** | Album Sync | Center (`50%, 50%`) |
| 2 | `FisheyeVisual` | **Fisheye Visual** | `Nunito-SemiBold` | **Inline Left/Right** | **Active** | **Active (`0.48`)** | Album Sync | Center (`50%, 50%`) |
| 3 | `Codestyle` | **Codestyle** | `Cascadia Mono` | None | Off | Off | Album Sync + `#EF4444` Cursor | Lower-Center (`50%, 72%`) |
| 4 | `CodestyleVisual` | **Codestyle Visual** | `Cascadia Mono` | **Inline IDE Hint** | Off | Off | Album Sync + `#EF4444` Cursor | Lower-Center (`50%, 72%`) |
| 5 | `VisualStack` | **Visual Stack** | `Mulish-Bold` | **Every Match (Hero Pair)** | Off | **Active (`0.42`)** | Album Sync | Center (`50%, 52%`) |
| 6 | `Constellation` | **Constellation** | `Mulish-Bold` | Optional micro-star | Off | Line-pan (`0.26`) | Album Sync | Full Safe-Zone (`50%, 48%`) |
| 7 | `SpidersWeb` | **Spider's Web** | `Jost-Bold` | None | Off | Line-pan (`0.26`) | Album Sync | Center (`50%, 50%`) |
| 8 | `NodeLine` | **Node Line** | `Mulish-Bold` | None | Off | Line-pan (`0.26`) | Album Sync | Lower-Mid Ribbon (`50%, 66%`) |
| 9 | `KineticWall` | **Kinetic Wall** | `Jost-Bold` | None | Off | **Active (`0.36`)** | Album Sync | Center 3D Plane (`50%, 50%`) |
| 10 | `Messages` | **Messages** | `Segoe UI Variable` *(Locked)* | None *(emoji text native)* | Off | Off | **Fixed Chat Palette** | Lower-Right (`82%, 78%`) |

---

### Style 1 — `Fisheye`

- **Concept**: Tight kinetic vertical triad of 3 words (`Previous`, `Active`, `Next`) advancing through a breathing convex spherical lens. High drama, zero screen clutter.
- **Default Font**: `Jost-Bold.ttf` (also shines with `BodoniModa-Bold.ttf`).
- **Layout Geometry**:
  - Centered inside an offscreen layer (`SKSurface` / `SaveLayer`) sized `W = min(0.76 * viewportW, maxWordW * 1.65)`, `H = fontSize * 4.8`.
  - Vertical slot spacing: `slotPitch = fontSize * 1.18`.
  - Slot `-1` (Previous word): `y = centerY - slotPitch`, scale `0.78×`, alpha `0.54`.
  - Slot `0` (Active word): `y = centerY`, scale `1.08×`, alpha `1.00`.
  - Slot `+1` (Upcoming word): `y = centerY + slotPitch`, scale `0.78×`, alpha `0.48`.
  - Long single words auto-fit via `fitScale` (`maxLineWidthRatio = 0.62`).
- **Word-by-Word Choreography**:
  1. On word advance (`idx → idx + 1`), continuous vertical scroll offset `stackOffsetY` springs smoothly by `-slotPitch` via **`Spring.LineShift`** (`k=210, c=26`).
  2. Newly active word scales `0.78 → 1.08` via **`Spring.WordSnap`** (`k=420, c=28`) and blooms from `AccentColor3` → `AccentColor1` over `150ms` (`Ease.Highlight`).
  3. Departing Slot `-2` word fades out (`0.54 → 0.00`) over `140ms` (`Ease.Exit`).
  4. **Shader Pipeline**: Render 3-word layer → apply `DirectionalMotionBlur` (`shutterFactor = 0.48`, vertical `stackOffsetY` velocity) → apply `EllipticalBulge` (`iStrength = 0.24` + `Spring.LensBreath` beat impulse, `iPower = 2.10`).
- **Semantic Visual Icons**: None.
- **Color Mapping**:
  - Active word: `AccentColor1` + subtle `AccentColor2 @ 28%` outer bloom (`sigma = 10px`).
  - Prev/Next words: `AccentColor3`.
  - Scrim: Layer 0 radial scrim (`ScrimTint @ 42%`).
- **Gallery Preview Behavior**: Preview tile (`~300×180px`) scales `fontSize = tileH * 0.22` (`~40px`), centers all 3 slots, and runs full lens + blur live on the Demo Song Script starting at Line 1 (`"Chasing a silver star tonight"`).
- **Reduced Motion Fallback**: Bypass both shaders (`iStrength = 0`, `shutterFactor = 0`); keep 3-word vertical stack stationary and cross-fade highlight/opacity (`140ms`) as words advance.

---

### Style 2 — `FisheyeVisual`

- **Concept**: Pairs the iconic spherical lens triad of `Fisheye` with crisp semantic icons docked directly alongside matching words — bending together through the same lens space.
- **Default Font**: `Nunito-SemiBold.ttf` (rounded forms complement Fluent System Icons).
- **Layout Geometry**:
  - Same 3-slot vertical triad (`slotPitch = fontSize * 1.24`, slightly airier for icons).
  - **Icon Docking**: Whenever a word has a semantic match (`confidence ≥ threshold`), place the icon to the **left** of the word on odd word indices and to the **right** on even word indices (alternating optical balance so the stack center of mass stays centered!), separated by `gap = fontSize * 0.28`.
  - Re-center the combined `[Icon + Gap + Word]` unit horizontally around `centerX` so the whole unit sits cleanly inside the flattest sweet spot of the lens.
  - Icon optical size: `iconSize = fontSize * 0.92` (`FluentSystemIcons-Filled`) or `fontSize * 0.85` (`Segoe UI Emoji`), vertically aligned to word cap-height midpoint (`baseline - capHeight * 0.50`).
- **Word-by-Word Choreography**:
  - Inherits `Fisheye` vertical `Spring.LineShift` + `EllipticalBulge` + `DirectionalMotionBlur`.
  - When a matched word becomes **Active (Slot 0)**: its paired icon pops from `scale 0.45 → 1.12 → 1.00` via **`Spring.IconPop`** (`k=480, c=22, m=0.9`) with a tiny `±6°` playful tilt impulse settling to `0°`, plus an `AccentColor2 @ 38%` radial glow behind the glyph.
  - In Slot `+1` (Upcoming), matched icon previews subtly at `scale 0.65`, `alpha 0.35` so layout width never violently jumps when promoted to Slot `0`!
  - In Slot `-1` (Past), icon relaxes to `scale 0.78`, `alpha 0.48`.
- **Color Mapping**:
  - Active word: `AccentColor1`; Active icon (Fluent): `AccentColor2` (with subtle `AccentColor1` inner highlight); Active emoji: native COLR with contact shadow.
  - Inactive words & icons: `AccentColor3`.
- **Gallery Preview Behavior**: Starts at Line 1 (`star` ★, `fire` 🔥) so a vivid semantic icon is visible inside the curved lens within the first 0.6 seconds of opening the Gallery!
- **Reduced Motion Fallback**: Shaders off; icons remain at `scale 1.00` with zero tilt/bounce and cross-fade alpha (`0.42 → 1.00`, `140ms`) with their word.

---

### Style 3 — `Codestyle`

- **Concept**: Lyrics live-typed inside a sleek floating translucent code snippet with syntax highlighting, gutter line numbers, and a signature **blinking crimson cursor** (`#EF4444`).
- **Default Font**: Monospace (`Cascadia Mono` SemiBold/Medium → `Consolas`), **overrides lyric serif/sans preference** (or optional monospace-weight mapping).
- **Layout Geometry**:
  - Anchored lower-center (`centerX = 50%`, `bottomY = 76%` of viewport), max block width `min(68% viewportW, 920px)`.
  - Styled as a clean typographic code block over a subtle dark IDE-tinted ambient scrim (`#0B0C16 @ 68%`, rounded `14px` soft glass panel OR borderless floating dark-haloed code lines depending on user scrim setting; default: **floating subtle glass card** `#0B0C16 @ 64%` with `1px` top specular hairline `#FFFFFF @ 10%` and `18px` padding — zero heavy window titlebar chrome so it stays musical and lightweight!).
  - Displays up to **3 visible lines**:
    - Past line (`idx - 1`): dim comment syntax `// <previous line>` (`alpha 0.48`, `AccentColor3`).
    - Active line (`idx`): left gutter `tabular-nums` line number (`01`, `02`, ... in `AccentColor3 @ 45%`), followed by subtle syntax prefix (`lyric: "` or `> ` in `AccentColor2 @ 75%`), then words typed character-by-character.
    - Upcoming line (`idx + 1`, optional faint preview): ghost autocomplete hint (`alpha 0.24`, italic) appearing `300ms` before line start.
- **Word-by-Word & Character Choreography**:
  1. **Character Typing (`Ease.LinearProgress`)**: For the active word, reveal `floor(wordProgress * charCount)` characters smoothly across the first `78%` of `wordDuration` (so the complete word rests legibly for the final `22%` of its beat!).
  2. **Syntax Token Highlighting**:
     - Active currently-typing word: `AccentColor1` (crisp luminous string/literal highlight) + subtle underline bar (`2px`, `AccentColor2 @ 55%`).
     - Completed words on active line: `AccentColor1 @ 88%`; keywords/punctuation/articles (`the`, `in`, `of`, `and`, `through`) tinted `AccentColor2 @ 82%` for authentic syntax rhythm!
  3. **Blinking Red Cursor**:
     - Exact color: **`#EF4444`** (`MASTER.md` Destructive Red — signature focal signature!).
     - Geometry: crisp block-bar caret `width = max(2.5px, fontSize * 0.11)`, `height = fontSize * 1.08`, rounded `1.5px` corners, soft red glow (`sigma = 6px`, `#EF4444 @ 55%`).
     - Motion: advances smoothly horizontally with typed characters via crisp tracking; **solid `100%` alpha while actively typing**, then enters a classic **`530ms` on / `530ms` off** crisp blink cycle during pauses/sustains.
  4. **Line Commit Scroll**: On line advance, block scrolls up by `1 lineHeight` via **`Spring.LineShift`** (`k=210, c=26`), and previous active line softly desaturates into `// comment` styling over `200ms`.
- **Semantic Visual Icons**: None.
- **Shader Pipeline**: **Both shaders OFF** (pixel-sharp monospace legibility).
- **Gallery Preview Behavior**: Renders a compact 3-line code block (`fontSize = tileH * 0.115`) showing Line 6 (`06`: `"Writing clean code in the dark"`) so the syntax highlighting and blinking `#EF4444` cursor read instantaneously.
- **Reduced Motion Fallback**: Disable line-scroll travel (instant line swap with `140ms` opacity fade); keep words revealing whole-word-at-a-time instead of rapid character strobing; cursor stays solid `#EF4444` (no blinking).

---

### Style 4 — `CodestyleVisual`

- **Concept**: `Codestyle` enhanced with **IDE inline visual annotations** — whenever a typed lyric word semantically matches an icon, a compact glowing icon pill/glyph pops inline right after the word like a live VS Code/Rider inlay hint!
- **Default Font**: Monospace (`Cascadia Mono` → `Consolas`) + `FluentSystemIcons-Filled.ttf` / `Segoe UI Emoji`.
- **Layout Geometry**:
  - Identical code-block structure, syntax coloring, gutter numbers, and `#EF4444` blinking cursor as `Codestyle`.
  - **Inline Icon Slot**: Immediately after a matched word completes (or at `60%` typed progress), reserve inline horizontal width `chipW = fontSize * 1.25` (pre-allocated in line-wrap layout so subsequent words never jitter!) and render a micro-pill:
    - Pill background: `AccentColor2 @ 18%` fill, `1px` border `AccentColor2 @ 42%`, corner radius `fontSize * 0.28`, height `fontSize * 1.05`.
    - Centered semantic glyph inside pill at `iconSize = fontSize * 0.72` (tinted `AccentColor2`, shifting to `AccentColor1` while word is active).
- **Word-by-Word Choreography**:
  - Word types character-by-character under the `#EF4444` cursor.
  - The instant the word crosses `65%` completion, its inline icon chip pops from `scale 0.3 → 1.0` via **`Spring.IconPop`** (`k=480, c=22, m=0.9`), while the red cursor glides seamlessly past the chip to the next space!
  - When the line commits to past `// comment` status, inline icon chips dim gracefully to `alpha 0.42` without collapsing layout.
- **Color Mapping**: Identical to `Codestyle`, with inline icon chips accented in `AccentColor2`.
- **Gallery Preview Behavior**: Previews Lines 6–8 (`code` 💻, `rain` 🌧️, `city` 🏙️) so users immediately spot the difference between pure `Codestyle` and `CodestyleVisual`.
- **Reduced Motion Fallback**: Same as `Codestyle`, plus inline icon chips fade in (`0 → 1`, `140ms`) with zero pop scale overshoot.

---

### Style 5 — `VisualStack`

- **Concept**: An expressive, editorial centered stack where **every semantically matched word becomes a visual duo** — pairing bold humanist typography with a prominent glowing icon beside it, stacking vertically with smooth rhythmic cadence.
- **Default Font**: `Mulish-Bold.ttf` (also great with `Nunito-SemiBold.ttf`).
- **Layout Geometry**:
  - Anchored center (`50%, 52%`), vertical stack holding up to **4 visible rows** (2 past words fading upward, 1 hero active word at center `y = 0`, 1 subtle upcoming word preview below, or stacked phrase chunks of `1–2` words so every visual word gets room to breathe!).
  - Row height pitch: `rowPitch = fontSize * 1.48`.
  - For each matched row:
    - **Icon Badge**: Circular/squircle luminous halo badge (`diameter = fontSize * 1.18`, fill `AccentColor2 @ 18%`, subtle `1px` ring `AccentColor2 @ 45%`, soft shadow) placed `fontSize * 0.36` to the **left** of the word; centered glyph inside at `fontSize * 0.74`.
    - **Unmatched connector words** (e.g. "in the", "through a"): grouped tightly onto a lightweight secondary micro-row (`scale 0.76×`, `alpha 0.78`) OR paired inline with the following content word so the stack feels rhythmically rich rather than cluttered with empty filler rows!
    - *Grouping Rule*: Merge leading stopword(s) (max 2 short words, ≤ 10 chars total) onto the same visual row as the next semantically matched noun/verb (e.g., `[★] Chasing the star`, `[🔥] sparks of fire`). This makes `VisualStack` look remarkably intentional and curated!
- **Word-by-Word Choreography**:
  1. Stack shifts upward by `rowPitch` via **`Spring.LineShift`** (`k=210, c=26`) with vertical **`DirectionalMotionBlur`** (`shutterFactor = 0.42`).
  2. Active row enlarges (`0.84 → 1.06`) via **`Spring.WordSnap`**; active word fills `AccentColor1` while grouped leading stopwords stay at `AccentColor1 @ 78%`.
  3. Matched icon pops in (`0.35 → 1.00`) via **`Spring.IconPop`**, preceded by a subtle expanding ripple ring (`radius 0.8× → 1.5×`, `alpha 0.45 → 0.00`, `280ms`, `Ease.Enter`) in `AccentColor2`.
  4. Past rows step back gracefully: Row `-1` (`scale 0.82, alpha 0.56`), Row `-2` (`scale 0.68, alpha 0.28`), Row `-3` exits (`alpha → 0.00`, `150ms`, `Ease.Exit`).
- **Color Mapping**:
  - Active hero word: `AccentColor1`.
  - Icon glyph & ripple halo: `AccentColor2` (Fluent icon filled `AccentColor1` at active peak, resting `AccentColor2`).
  - Past/upcoming rows: `AccentColor3`.
  - Layer 0 ambient radial scrim fitted softly behind the 4-row column.
- **Gallery Preview Behavior**: Displays a crisp 3-row stack (`fontSize = tileH * 0.16`) showcasing `[★] star`, `[🔥] fire`, `[🌊] ocean` with glowing circular icon badges.
- **Reduced Motion Fallback**: Motion blur off, ripple ring off; stack rows cross-fade opacity (`140ms`) with no vertical sliding or pop overshoot.

---

### Style 6 — `Constellation`

- **Concept**: Each lyric line transforms into a bespoke **celestial constellation** floating across the desktop — glowing stars ignite word by word as crisp vector light-threads trace from star to star, illuminating the sky with poetry.
- **Default Font**: `Mulish-Bold.ttf` (also luminous with `BodoniModa-Bold.ttf`).
- **Layout Geometry**:
  - Safe-zone canvas region: `width = 68% viewportW`, `height = 46% viewportH`, centered around `(50%, 46%)`.
  - **Deterministic Star Placement Algorithm** (seeded by `Hash(lineIndex, lineText)` so the same song line always forms the same iconic constellation!):
    - Divide horizontal span into `N = wordCount` gently staggered columns (`x_i` progressing broadly left-to-right for natural reading flow, with deterministic organic vertical waves `y_i` using low-discrepancy jitter).
    - Enforce **Minimum Star Separation** `minDist ≥ max(fontSize * 2.8, measuredWordW_i * 0.65 + measuredWordW_{i-1} * 0.65)` via 8-iteration Lloyd/force-relaxation pass so **word labels NEVER overlap**!
    - Alternate word label vertical offset (`+fontSize * 0.82` below star vs `-fontSize * 0.72` above star) based on local crest/valley geometry so labels always clear connecting lines.
  - **Star Geometry**:
    - Unlit upcoming star: pinpoint `2.5px` core (`AccentColor3 @ 42%`) + subtle `6px` halo.
    - Active lit star: `5.0px` bright white-hot core (`#FFFFFF`), `12px` inner glow (`AccentColor1 @ 85%`), 4-point diffraction spikes (`length = 16px`, `1.2px` tapered stroke in `AccentColor2 @ 75%`).
    - Lit past star: settled `3.8px` core (`AccentColor1 @ 80%`) + soft `8px` aura (`AccentColor2 @ 35%`).
- **Word-by-Word Choreography**:
  1. **Line Arrival**: Upcoming constellation's dim unlit starfield fades in over `260ms` (`Ease.Enter`) before Word 0 ignites.
  2. **Star-to-Star Trace (`Ease.LinearProgress`)**: As Word `i` (`i > 0`) becomes active, a luminous vector thread (`1.6px` crisp stroke + `6px` blurred glow copy at `32%` alpha in `AccentColor2`) draws smoothly from `Star[i-1]` to `Star[i]` across the first `55%` of `wordDuration`, led by a bright shooting spark dot at the head!
  3. **Star Ignition (`Spring.IconPop`)**: When the thread reaches `Star[i]`, its diffraction spikes flare (`scale 0.2 → 1.25 → 1.00`), and Word `i`'s label blooms from `alpha 0.0 → 1.0`, `translateY +6px → 0` (`Spring.WordEnter`) in `AccentColor1`.
  4. **Previous Word Settle**: Word `i-1` dims gently from `AccentColor1` to `AccentColor3 @ 78%` so the whole completed lyric phrase remains readable as a glowing star-map.
  5. **Line Exit**: After the line ends, the entire constellation drifts gently upward `-14px` and dissolves over `180ms` (`Ease.Exit`) while the next constellation fades in.
- **Semantic Visual Icons**: None (star geometry is the hero; optional tiny `10px` twinkle accent dots).
- **Shader Pipeline**: `EllipticalBulge` OFF; light `DirectionalMotionBlur` (`0.26`) only during fast line transitions.
- **Gallery Preview Behavior**: Previews Line 1's 5-star constellation (`Chasing → a → silver → star → tonight`) and Line 4 (`Quiet → night → where → shadows → hide`) with active glowing diffraction flares and indigo connecting threads.
- **Reduced Motion Fallback**: All stars and threads for the current line appear immediately at line start (no shooting spark, no drift, no twinkle); active word highlights via pure `140ms` color/brightness transition on its star & label.

---

### Style 7 — `SpidersWeb`

- **Concept**: Each lyric line spins itself into an intricate geometric **radial spider's web** — shooting a taut luminous silk strand per word outward from a focal hub, with words anchored along the glowing threads like dew-caught typography.
- **Default Font**: `Jost-Bold.ttf` (sharp geometric elegance complements radial silk geometry).
- **Layout Geometry**:
  - Centered at `(50%, 50%)`, outer web radius `R = min(viewportW * 0.34, viewportH * 0.36)`.
  - **Spoke Count**: `M = max(wordCount, 6)` radial anchor spokes distributed around `360°` starting near `-115°` (top-left clockwise for intuitive reading order!) with subtle deterministic organic angular offset (`±5°`).
  - **Concentric Silk Rings**: `3` softly sagging catenary rings (quadratic Bezier curves bowing `4%` inward toward center between adjacent spokes) at radii `0.36R`, `0.66R`, `0.94R` (`1.0px` stroke in `AccentColor3 @ 26%`).
  - **Word Anchoring**: Word `i` anchors along Spoke `i` at alternating radial tiers (`0.54R` on even indices, `0.80R` on odd indices) so adjacent labels never collide! Words remain **strictly upright (`0°` horizontal rotation)** anchored cleanly to a glowing silk dew-node on the spoke — *never upside-down radial text* (critical readability rule!).
- **Word-by-Word Choreography**:
  1. **Hub & Scaffold Arrival**: Faint translucent concentric web rings materialize outward from center (`scale 0.88 → 1.00`, `240ms`, `Ease.Enter`).
  2. **Strand Spin (`Ease.LinearProgress`)**: On Word `i` onset, Spoke `i` shoots tautly from center `(0,0)` to outer perimeter `R` over the first `45%` of `wordDuration` (`1.8px` bright silk stroke in `AccentColor1` + soft `AccentColor2 @ 35%` glow), illuminating the connecting catenary arc back to Spoke `i-1`!
  3. **Plucked-Strand Vibration**: For `320ms` after Spoke `i` snaps taut, apply a damped transverse midpoint deflection `amp(t) = 4.5px * exp(-9t) * sin(42t)` so the strand visually *twangs* like tensioned silk!
  4. **Word Bloom**: Dew-node pops (`Spring.IconPop`) and Word `i` scales `0.85 → 1.05` (`Spring.WordSnap`) in `AccentColor1` with full Layer 1–2 legibility halo so silk lines crossing behind text never reduce legibility!
  5. **Line Exit**: Entire web gently contracts `1.00 → 0.92` and fades (`170ms`, `Ease.Exit`).
- **Semantic Visual Icons**: None.
- **Shader Pipeline**: `EllipticalBulge` OFF; light `DirectionalMotionBlur` (`0.26`) on line exit/reset only.
- **Gallery Preview Behavior**: Renders a crisp 6-spoke hexagonal web (`R = tileH * 0.42`) with active glowing strand and upright crisp word labels anchored at the radial nodes.
- **Reduced Motion Fallback**: Full web geometry appears statically at line start (no shooting strand, no transverse twang vibration, no contraction); active word & its spoke simply cross-fade brightness (`140ms`).

---

### Style 8 — `NodeLine`

- **Concept**: A sleek horizontal telemetry/melody ribbon draws itself continuously across the screen; every word illuminates a glowing circuit/timeline **node** along the path, balancing clean scannability with kinetic grace.
- **Default Font**: `Mulish-Bold.ttf` (crisp, scannable along horizontal rhythm).
- **Layout Geometry**:
  - Anchored across lower-middle third (`y = 66% viewportH`), horizontal span `width = min(74% viewportW, 1180px)`, centered horizontally.
  - **Path Curve**: Smooth subtle sinusoidal / Catmull-Rom spline passing through `N = wordCount` horizontally distributed nodes (`x_i` weighted lightly by word character count so longer words get wider horizontal clearance!), with gentle alternating vertical wave amplitude `y_i = baseY + ((i % 2 == 0) ? -14px : +14px)`.
  - **Word Label Placement**: Placed strictly on the exterior side of each wave crest/trough:
    - Even nodes (upper crest): word floats `fontSize * 0.78` **above** the node.
    - Odd nodes (lower trough): word floats `fontSize * 0.82` **below** the node.
    - *Long-Line Scroll Window*: If a line exceeds `8` words, smoothly pan the horizontal camera along the path via **`Spring.CameraDrift`** keeping the active node inside `[30%, 65%]` of the horizontal span!
  - **Node Geometry**:
    - Inactive node: `4px` ring (`1.5px` stroke, `AccentColor3 @ 45%`).
    - Active node: `7px` solid glowing core (`AccentColor1`) + expanding outer pulse ring (`7px → 20px`, `AccentColor2 @ 65% → 0%`).
    - Passed node: `5px` filled dot (`AccentColor2 @ 85%`).
- **Word-by-Word Choreography**:
  1. **Track Guide**: Subtle dashed/thin base spline (`1.5px`, `AccentColor3 @ 25%`) fades in at line start (`220ms`, `Ease.Enter`).
  2. **Continuous Path Draw (`Ease.LinearProgress`)**: Illuminated progress curve (`3.0px` stroke in `AccentColor2` with luminous `AccentColor1` comet head + `10px` blur aura) sweeps smoothly from `Node[i]` to `Node[i+1]` synchronized frame-accurately to playback time!
  3. **Node Ignition**: As progress reaches `Node[i]`, node pops (`Spring.IconPop`), ripple pulse expands over `320ms` (`Ease.Enter`), and Word `i` lifts `+8px` (or `-8px` below) into `scale 1.06` (`Spring.WordSnap`) in `AccentColor1`.
  4. **Passed Word**: Relaxes to `scale 0.94`, `AccentColor3 @ 75%` so the whole line stays effortless to read at a glance.
  5. **Line Exit**: Ribbon glides slightly left (`-24px`) and fades out over `160ms` (`Ease.Exit`).
- **Semantic Visual Icons**: None.
- **Shader Pipeline**: `EllipticalBulge` OFF; light `DirectionalMotionBlur` (`0.26`) only when long-line camera pans.
- **Gallery Preview Behavior**: Displays a 5-node glowing wave across the center of the preview tile with the luminous comet head sweeping smoothly node-to-node.
- **Reduced Motion Fallback**: Full spline draws immediately; active node & word highlight via crisp `140ms` color/ring change without wave ripples or horizontal camera panning (wrap long lines to 2 clean static rows instead).

---

### Style 9 — `KineticWall` (3D Drifting Plane)

- **Concept**: Cinematic architectural typography wall floating in true 3D perspective — words stamp one-by-one onto an angled editorial plane while a smooth virtual camera tracks the active word across rows with silky depth-of-field fade.
- **Default Font**: `Jost-Bold.ttf` (also striking with `BodoniModa-Bold.ttf` for poster drama).
- **Layout Geometry & 3D Projection Math**:
  - Construct a 2D logical typographic poster canvas (max logical line width `fontSize * 12.5`, tight poster leading `1.04×`, alternating left/center editorial alignment or crisp left-flush wall rows; keep active line + `2` past rows + `1` faint upcoming row on the wall).
  - **3D Perspective Transform (`SKMatrix44` → `SKMatrix`)**:
    - Virtual camera focal length: `f = 1100px * dpiScale` (telephoto-cinematic lens — avoids harsh fishbowl skew!).
    - Gentle resting plane orientation:
      - Pitch (`rotateX`): **`+6.5°`** (subtle upward tilt)
      - Yaw (`rotateY`): smoothly oscillates/orients between **`-11°` and `+11°`** angled toward the active word's horizontal column so the active word always faces the viewer!
      - Roll (`rotateZ`): **`±1.5°` max** (strictly bounded — excessive roll induces motion sickness!).
    - **Camera Target Tracking**: Smoothly translate wall origin so the currently active word's 3D centroid tracks toward viewport center `(50%, 50%)` via **`Spring.CameraDrift`** (`k=95, c=19.5, ζ=1.00` — critically damped, zero bounce!).
    - **Depth Attenuation**: Words further back in Z-depth (past/far rows) gently step down opacity (`0.42–0.65`) and blur subtly (`0 → 2.5px` max for distant past rows) to reinforce genuine spatial depth without making text muddy.
- **Word-by-Word Choreography**:
  1. **Word Landing**: Each word lands onto its exact grid slot on the 3D wall from slightly forward in Z (`zOffset = +65px → 0px`, perceived scale `1.22 → 1.00`, `alpha 0.0 → 1.0`) via **`Spring.WordEnter`** (`k=280, c=24`), snapping into the wall with tactile typographic authority!
  2. **Active Emphasis**: Landed active word glows in `AccentColor1` with subtle `AccentColor2 @ 30%` surface bloom and a hairline `2px` underline bar sweeping beneath it (`Ease.LinearProgress`).
  3. **Camera Glide**: Virtual camera follows each newly landed word smoothly via **`Spring.CameraDrift`** (`ζ=1.0`), producing subtle screen-space **`DirectionalMotionBlur`** (`shutterFactor = 0.36`) during row-to-row sweeps.
  4. **Row Retirement**: Rows older than `currentLine - 2` fade out cleanly over `180ms` (`Ease.Exit`).
- **Semantic Visual Icons**: None.
- **Color Mapping**:
  - Active landed word: `AccentColor1`.
  - Active accent rule / highlight shimmer: `AccentColor2`.
  - Past wall rows: `AccentColor3` (graduated alpha `0.62`, `0.38`).
  - Layer 0 wide radial ambient scrim (`rx = 0.42 * viewportW, ry = 0.34 * viewportH`) softly darkens behind the floating plane.
- **Gallery Preview Behavior**: Previews a dramatic angled 3-row 3D poster wall (`yaw = -9°, pitch = +6°`) where words stamp into place live and the camera glides smoothly down the wall.
- **Reduced Motion Fallback**: **Disable 3D perspective rotation (`pitch=0°, yaw=0°, roll=0°`), disable camera pan, disable Z-slam, disable motion blur.** Render as a crisp flat 2D editorial multi-row stack where words reveal cleanly via `140ms` opacity/color cross-fade.

---

### Style 10 — `Messages` (Phone Screen Thread)

- **Concept**: Turns the currently playing song into an intimate, hyper-clean **floating phone-screen message thread** — every lyric line arrives as a tactile chat bubble complete with a subtle typing indicator right before the next phrase drops.
- **Font Lock (Fixed)**: **`Segoe UI Variable Text`** (Win 11) / **`Segoe UI`** (Win 10) — **intentionally ignores** the user's lyric typeface selector so it always looks authentically like native messaging UI.
- **Color Palette Lock (Fixed Chat Palette)**: Intentionally decoupled from Album Color Sync text fills for unmistakable chat legibility and realism!

| `Messages` Token | Fixed Hex / RGBA | WCAG Contrast Check | Role |
|---|---|---|---|
| `Chat.ThreadBackdrop` | `#10111A` @ `76%` (`0xC210111A`) + `1px` rim `#FFFFFF @ 12%` | — | Optional slim floating phone-glass backdrop (`cornerRadius = 22px`, soft shadow `dy=12px, sigma=28px, #000 @ 45%`) |
| `Chat.HeaderText` | `#F8FAFC` (title), `#94A3B8` (artist / "Now Playing") | `16.4:1` / `6.8:1` | Compact 36px top thread header (tiny circular album avatar `24px` + Track Title + live green dot `#22C55E`) |
| `Chat.OutgoingBg` | **`#0066D6`** (`100%` alpha, subtle vertical gradient `#0B74E8 → #005EC7`) | — | Lead vocal / primary right-aligned bubble |
| `Chat.OutgoingText` | **`#FFFFFF`** | **`5.44:1`** (passes WCAG AA!) | Text inside outgoing bubble (`15.5px` Medium/Regular) |
| `Chat.IncomingBg` | **`#222433`** (`96%` alpha) + `1px` rim `#FFFFFF @ 9%` | — | Alternating / chorus / response left-aligned bubble |
| `Chat.IncomingText` | **`#F8FAFC`** | **`14.9:1`** (passes WCAG AAA!) | Text inside incoming bubble |
| `Chat.ActiveWordTint` | `#FFFFFF` (`100%` alpha) vs unsung words in current bubble `#FFFFFF @ 58%` (outgoing) / `#CBD5E1 @ 55%` (incoming) | Active `≥ 5.44:1` | Subtle karaoke progression inside the latest arrived bubble! |
| `Chat.Timestamp` | `#94A3B8` @ `80%` (`11px`, `tabular-nums`) | `5.3:1` | Timestamp / "Delivered" micro-caption under newest bubble (`0:14`) |
| `Chat.TypingDot` | `#CBD5E1` (3 dots `5.5px` inside `#222433` mini-pill `46×28px`) | `10.1:1` | Pre-arrival typing cue `450ms` before next line starts! |

- **Layout Geometry**:
  - Anchored lower-right companion zone (`rightInset = 4% viewportW`, `bottomInset = 8% viewportH`, user-repositionable).
  - Slim phone-width viewport: `threadWidth = clamp(320px, viewportW * 0.22, 390px)`, `threadHeight = clamp(380px, viewportH * 0.48, 520px)` (scaled by `dpiScale`).
  - Top fades softly with a `48px` alpha-mask gradient (`SaveLayer` + `DstIn` linear gradient) so scrolling history bubbles dissolve seamlessly below the slim header!
  - Bubble geometry: `maxWidth = threadWidth * 0.78`, `padding = (14px H, 9px V)`, `cornerRadius = 18px` with `5px` tailored inner corner on the sender tail side, vertical gap `8px` between consecutive bubbles (same side) or `14px` when alternating sides.
  - **Side-Alternation Cadence**: Alternate sides thoughtfully so it reads like a musical dialogue — stay on the same side for consecutive short phrases (`gap < 0.7s`) and switch between Outgoing (right) and Incoming (left) at musical phrase breaks or every 2 lines (with a crisp 4-line max visible history).
- **Word-by-Word & Line Choreography**:
  1. **Typing Indicator Pre-Cue**: When `nextLineStart - now <= 450ms` (and > 0ms), pop a tiny 3-dot typing pill at the bottom (`Spring.BubblePush`), with staggered smooth vertical dot wave (`amplitude 2.5px`, period `900ms`).
  2. **Bubble Arrival**: On line start, typing pill morphs smoothly into the full message bubble (`scale 0.86 → 1.00` anchored from its bottom corner + upward thread push via **`Spring.BubblePush`**, `k=310, c=27`).
  3. **Live Word Illumination Inside Bubble**: All words of the newly arrived bubble are legible immediately, with upcoming words at `62%` alpha lighting cleanly to `100%` crisp white word-by-word (`150ms`, `Ease.Highlight`) in exact sync with vocals!
  4. **History Scroll**: Older bubbles push smoothly upward and fade out across the top mask (`Ease.Exit`).
- **Semantic Visual Icons**: None (native emoji inside lyrics render naturally via `Segoe UI Emoji`).
- **Shader Pipeline**: **Both shaders OFF** (razor-sharp system typography & rounded bubble UI).
- **Gallery Preview Behavior**: Renders a crisp scaled phone-card frame centered inside the Gallery tile showing 2 settled bubbles + 1 active glowing blue bubble + live typing dots at the bottom!
- **Reduced Motion Fallback**: Typing indicator dots remain static (no bounce); new bubbles appear with `140ms` opacity fade and instant clean vertical positioning (no spring scale pop).

---

## 7. Built-In Demo Song Script (Gallery Live Preview)

To make the **Style Gallery** and **Settings Preview** come alive immediately — even when Spotify/Apple Music are closed — `LyricPulse.Core` embeds this **100% original, copyright-free 12-line synced track**:

- **Title**: `Neon Horizon`
- **Artist**: `LyricPulse Studio`
- **Duration**: `36.0s` (seamless loop: `34.8s` fade-out → `0.0s` restart)
- **Tempo**: `100 BPM` (quarter note = `600ms`, natural conversational singing cadence)
- **Demo Album Palette**:
  - `AccentColor1`: `#5EEAD4` (Luminous Electric Mint-Cyan, `oklch(0.86, 0.13, 180°)`)
  - `AccentColor2`: `#818CF8` (Vibrant Studio Indigo, `oklch(0.68, 0.17, 274°)`)
  - `AccentColor3`: `#94A3B8` (Cool Slate Mist, `oklch(0.71, 0.03, 255°)`)
  - `ScrimTint`: `#0F0F23` (`MASTER.md` OLED Midnight)

### 7.1 Enhanced LRC Source (`Assets/Demo/neon-horizon.lrc`)

```lrc
[ti:Neon Horizon]
[ar:LyricPulse Studio]
[al:Demo Preview]
[length:00:36.00]
[00:00.20]<00:00.20>Chasing <00:00.65>a <00:00.85>silver <00:01.40>star <00:02.05>tonight
[00:03.00]<00:03.00>Sparks <00:03.50>of <00:03.75>fire <00:04.35>burn <00:04.85>so <00:05.15>bright
[00:05.90]<00:05.90>Across <00:06.45>the <00:06.70>midnight <00:07.35>ocean <00:08.05>tide
[00:08.80]<00:08.80>Quiet <00:09.35>night <00:09.95>where <00:10.30>shadows <00:10.95>hide
[00:11.70]<00:11.70>Feel <00:12.15>the <00:12.40>rhythm <00:13.00>in <00:13.25>my <00:13.55>heart
[00:14.40]<00:14.40>Writing <00:14.95>clean <00:15.40>code <00:16.00>in <00:16.25>the <00:16.50>dark
[00:17.30]<00:17.30>Silver <00:17.85>rain <00:18.45>on <00:18.70>neon <00:19.25>glass
[00:20.10]<00:20.10>Through <00:20.60>the <00:20.85>waking <00:21.45>city <00:22.10>lights
[00:22.90]<00:22.90>Take <00:23.35>a <00:23.55>sudden <00:24.15>midnight <00:24.80>flight
[00:25.70]<00:25.70>Hold <00:26.15>onto <00:26.60>every <00:27.15>inch <00:27.55>of <00:27.80>time
[00:28.60]<00:28.60>Follow <00:29.15>every <00:29.65>beam <00:30.15>of <00:30.40>light
[00:31.30]<00:31.30>Living <00:31.85>inside <00:32.45>this <00:32.80>electric <00:33.55>dream
```

### 7.2 Line & Semantic Keyword Verification Matrix

All 12 high-salience visual keywords are evenly spaced (one guaranteed primary visual anchor per line) so every `*Visual` and `VisualStack` preview hits a rich semantic icon immediately:

| Line # | Start–End (s) | Lyric Text | Required Visual Keyword | Expected Fluent Icon (Primary) | CLDR Emoji Fallback | `Messages` Bubble Side |
|---:|---|---|---|---|---|---|
| 1 | `0.20 – 2.85` | Chasing a silver **star** tonight | **`star`** | `ic_fluent_star_24_filled` | ⭐ | Outgoing (Right) |
| 2 | `3.00 – 5.75` | Sparks of **fire** burn so bright | **`fire`** | `ic_fluent_fire_24_filled` | 🔥 | Outgoing (Right) |
| 3 | `5.90 – 8.65` | Across the midnight **ocean** tide | **`ocean`** | `ic_fluent_water_24_filled` | 🌊 | Incoming (Left) |
| 4 | `8.80 – 11.55` | Quiet **night** where shadows hide | **`night`** | `ic_fluent_weather_moon_24_filled` | 🌙 | Incoming (Left) |
| 5 | `11.70 – 14.25` | Feel the rhythm in my **heart** | **`heart`** | `ic_fluent_heart_24_filled` | ❤️ | Outgoing (Right) |
| 6 | `14.40 – 17.15` | Writing clean **code** in the dark | **`code`** | `ic_fluent_code_24_filled` | 💻 | Outgoing (Right) |
| 7 | `17.30 – 19.95` | Silver **rain** on neon glass | **`rain`** | `ic_fluent_weather_rain_24_filled` | 🌧️ | Incoming (Left) |
| 8 | `20.10 – 22.75` | Through the waking **city** lights | **`city`** | `ic_fluent_city_24_filled` | 🏙️ | Incoming (Left) |
| 9 | `22.90 – 25.55` | Take a sudden midnight **flight** | **`flight`** | `ic_fluent_airplane_24_filled` | ✈️ | Outgoing (Right) |
| 10 | `25.70 – 28.45` | Hold onto every inch of **time** | **`time`** | `ic_fluent_clock_24_filled` | ⏱️ | Outgoing (Right) |
| 11 | `28.60 – 31.15` | Follow every beam of **light** | **`light`** | `ic_fluent_lightbulb_24_filled` | 💡 | Incoming (Left) |
| 12 | `31.30 – 34.80` | Living inside this electric **dream** | **`dream`** | `ic_fluent_sparkle_24_filled` | ✨ | Outgoing (Right) |

### 7.3 Gallery Preview Stagger & Wallpaper Toggle

- **Gallery Card Phase Offsets**: To prevent all 10 live cards in the Gallery from flashing in lockstep robotic unison (which causes visual fatigue!), stagger each card's initial demo clock offset so every style showcases its most photogenic signature line on first open:
  - `Fisheye`: `t0 = 0.2s` (Line 1), `FisheyeVisual`: `t0 = 3.0s` (Line 2 `fire`), `Codestyle`: `t0 = 14.4s` (Line 6 `code`), `CodestyleVisual`: `t0 = 17.3s` (Line 7 `rain`), `VisualStack`: `t0 = 5.9s` (Line 3 `ocean`), `Constellation`: `t0 = 8.8s` (Line 4 `night`), `SpidersWeb`: `t0 = 11.7s` (Line 5 `heart`), `NodeLine`: `t0 = 20.1s` (Line 8 `city`), `KineticWall`: `t0 = 28.6s` (Line 11 `light`), `Messages`: `t0 = 22.9s` (Line 9 `flight` with Line 7–8 already in chat history).
- **Gallery Performance Rule**: Pause off-screen/unfocused Gallery preview rendering (`IsVisible && Window.IsActive`) and cap Gallery thumbnail canvases to **`30 FPS`** (or `60 FPS` on hovered/selected tile) while keeping the live desktop overlay locked at full display refresh (`60–144 FPS`).
- **Backdrop Preview Switcher (Settings / Gallery)**: Allow testing legibility inside the Gallery against 3 mini backdrops: `OLED Dark (#0F0F23)`, `Bright Webpage (#F8FAFC)`, and `Vibrant Wallpaper` so users can tune Scrim/Opacity with live proof!

---

## 8. Lifecycle States, Performance Budgets & QA Gates

### 8.1 Playback Lifecycle Choreography

| State Transition | Overlay Visual Behavior | Duration / Token |
|---|---|---|
| **Playing → Paused** | Freeze active word progression; gently dim overlay to `0.62×` current opacity over `300ms` (`Ease.Exit`); after `8.0s` paused, fade cleanly to `0.00` (`450ms`) so static lyrics never linger over work. | `300ms` → `8s` idle → `450ms` fade |
| **Instrumental Break (`gap > 4.0s`)** | After current line finishes + `0.9s` linger, fade line out (`180ms`, `Ease.Exit`). Display an ultra-minimal breathing 3-dot musical ellipsis (`AccentColor2 @ 45%`, slow `2.2s` sine pulse — static under Reduced Motion) or remain completely transparent if user enables *Hide During Instrumentals*. | `180ms` exit; pre-wake `450ms` before vocals resume |
| **Track Change** | Cross-fade departing lyrics out (`160ms`, `Ease.Exit`), reset timing offset to `0ms`, morph OKLab album palette (`650ms`, `Ease.ColorMorph`), and enter new track's first line cleanly. | `160ms` exit + `650ms` color morph |
| **No Synced Lyrics Found** | Fade overlay cleanly to transparent (`0%` alpha) — never show ugly error banners or boxes on the click-through desktop! Report lyrics status exclusively inside the Settings/Gallery status pill. | `180ms` fade |
| **Timing Offset Nudge (`±0.5s / ±1.0s`)** | Immediately retarget active word/line springs (conserving velocity) and display a subtle transient floating pill (`Offset: +0.5s`) for `1.2s` in `Codestyle` tabular-nums mono before fading out (`180ms`). | Instant retarget + `1.2s` toast |

### 8.2 GPU & Memory Performance Budgets (`LyricPulse.Rendering`)

- **Frame Budget**: `≤ 4.5ms` combined CPU+GPU per frame at `1080p @ 60Hz`; `≤ 3.0ms` at `1440p @ 120Hz`.
- **Zero Per-Frame Allocations**:
  - Pre-allocate and pool `SKPaint`, `SKFont`, `SKPath`, `SKRuntimeEffectUniforms`, and `SKRuntimeEffectChildren` buffers inside each `ILyricStyleRenderer`.
  - Cache `SKImageFilter` (Contact & Ambient shadows) keyed by `(int fontSizeBucket, byte opacityBucket)` — never call `SKImageFilter.CreateDropShadowOnly` on every `Render()` tick!
  - Cache HarfBuzz glyph shaping (`SKTextBlob`) per `(wordText, fontSize, typefaceId)` across the lifetime of the current line.
- **Dirty-Skip Optimization**: When paused (`opacity == 0`), instrumental hidden, or Reduced Motion static between word boundaries (`allSpringsAtRest == true`), skip Skia redraws until the next word timestamp or state event fires (`< 0.5%` idle CPU!).

### 8.3 Automated Visual & Motion QA Verification Checklist

- [ ] **White-Page Legibility Gate**: Render all 10 styles over `#FFFFFF` and verify active glyph boundary effective contrast `≥ 7.0:1` (all non-Messages styles via Layer 1+2 halo) and `≥ 5.4:1` (`Messages` outgoing `#0066D6` bubble).
- [ ] **Dark-IDE Legibility Gate**: Render all 10 styles over `#0D0D14` and verify zero white-edge premultiplied alpha fringing and zero ClearType RGB subpixel artifacts.
- [ ] **Album Color Sync OKLCH Gate**: Unit-test `AlbumPaletteExtractor` on 5 synthetic stress covers (pure black, pure white, neon `#00FF00`, monochrome photo, muddy brown) and assert `AccentColor1.L ∈ [0.78, 0.92]`, `AccentColor2` hue separation `≥ 38°`, zero NaN/out-of-gamut sRGB channels.
- [ ] **Spring Determinism & Timestep Invariance Gate**: Simulate all 7 spring tokens at `60Hz`, `120Hz`, and variable jittered `dt` (`5ms–45ms`) and assert final settled state within `±0.002` with zero energy explosion.
- [ ] **Shader Comfort Ceiling Gate**: Assert `EllipticalBulge.iStrength ≤ 0.40` and `DirectionalMotionBlur.|iVelocity| ≤ 22 * dpiScale` under extreme seek/rap-tempo stress tests.
- [ ] **Reduced Motion Gate**: With `ReducedMotion = true`, assert zero SkSL shader dispatch calls, zero 3D rotation matrices (`KineticWall`), zero cursor hard-blink (`Codestyle`), and all transitions `≤ 150ms` pure opacity/color fades.
- [ ] **Demo Song 12-Keyword Coverage Gate**: Verify all 12 demo lines resolve their expected semantic icon/emoji offline with zero network calls and loop seamlessly at `36.0s`.
