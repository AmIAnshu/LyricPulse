# LyricPulse Design System — Master Specification

> **HIERARCHICAL RETRIEVAL RULE:** When implementing or auditing a specific view, read `MASTER.md` first, then check `design-system/lyricpulse/pages/[page].md` (`gallery.md`, `settings.md`, `tray.md`). Page-specific rules override global defaults where explicitly stated.

---

**Project:** LyricPulse (Clean-Room Open-Source Windows Desktop Kinetic Lyrics Companion)  
**Platform & Stack:** Windows 10 22H2+ / Windows 11 · .NET 9 · **Avalonia UI 11.x (`FluentTheme`)** · SkiaSharp 3.x + SkSL  
**Companion Motion Spec:** [`MOTION_AND_STYLES.md`](MOTION_AND_STYLES.md) (4-Layer Overlay Legibility, OKLCH Album Sync, SkSL Shaders & 10 Style Blueprints)  
**Design System Lineage:** Generated via `ui-ux-pro-max` (`music desktop lyrics creative studio dark minimal`, `--stack avalonia`, `--domain color/typography/style/ux`), refined with `frontend-design-direction` and `make-interfaces-feel-better` craft principles.  
**Last Updated:** 2026-11-25

---

## 1. Design Direction & Aesthetic Philosophy ("Acoustic Studio Lightbox")

| Dimension | Decision |
|---|---|
| **Purpose** | Configure, preview, and switch 10 live GPU-rendered kinetic lyric styles and sync settings with zero friction while music plays. |
| **Audience** | Desktop music listeners (Spotify, Apple Music, local players) who open the Gallery briefly to audition styles, tweak offset/monitors, then live in the System Tray + transparent click-through overlay. |
| **Tone** | **Quiet Creative Studio / Mastering Lightbox.** Dark-first OLED-friendly neutral obsidian surfaces with full Daylight Light Mode parity. Never a landing page, never a purple-gradient SaaS template. |
| **Memorable Detail** | A restrained neutral studio frame where the **10 live animated SkiaSharp previews** and a **3-dot live album-cover palette strip** carry the chromatic energy—never colliding with heavy tinted window chrome. |
| **Multi-Hue Discipline** | Neutral slate-graphite chrome + **Electric Indigo** (`#5B54EC` dark / `#4338CA` light) for interactive selection + **Signal Emerald** (`#22C55E` dark / `#15803D` light) reserved exclusively for live audio/connected playback + **Amber** for active timing offset / warnings + **Crimson** for errors / destructive actions. |

---

## 2. Avalonia 11 Architecture & Styling Contract

All UI code in `LyricPulse.UI` MUST follow these verified Avalonia 11 stack rules:

1. **Base Theme (`FluentTheme`)**: Inherit `<FluentTheme />` in `App.axaml` and layer `Themes/LyricPulseTokens.axaml` + `Themes/ColorPickerTheme.axaml` via `<ResourceDictionary.MergedDictionaries>`. Never rebuild standard Fluent control templates from scratch.
2. **Theme Switching via `ThemeDictionaries` + `{DynamicResource}`**:
   - Declare all semantic brushes inside `<ResourceDictionary.ThemeDictionaries>` under `x:Key="Dark"` and `x:Key="Light"`.
   - Always bind theme-aware colors with **`{DynamicResource LpSurfaceBrush}`** (NEVER `{StaticResource ...}`, which caches at load and breaks runtime Dark/Light switching, and NEVER hardcoded hex colors inside `.axaml` views).
   - Switch themes via `Application.Current.RequestedThemeVariant = ThemeVariant.Dark | Light | Default`.
3. **Compiled Bindings (`x:DataType`) & `#name` Shorthand**:
   - Every `Window`, `UserControl`, and `DataTemplate` MUST set `x:DataType` for compile-time validation and NativeAOT compatibility.
   - Use `#ControlName` shorthand (`{Binding #OffsetBadge.Text}`) instead of `ElementName=...` under compiled bindings.
4. **Pseudoclass State Selectors**:
   - Style states exclusively with Avalonia pseudoclasses (`:pointerover`, `:pressed`, `:focus-visible`, `:checked`, `:disabled`, `.active`). Never use WPF `VisualStateManager` or `Triggers`.
5. **Native Platform Controls**:
   - Use `TrayIcon` + `NativeMenu` for system tray interactions (see [`pages/tray.md`](pages/tray.md)).
   - Virtualize any unbounded or dynamic list (`ListBox` / `ItemsRepeater` with virtualizing panel).

---

## 3. Color System (Verified WCAG 2.2 AA/AAA — Dark & Light)

All text pairs below were programmatically verified for **>= 4.5:1** contrast (normal text) and **>= 3.0:1** for non-text UI boundaries, icons, and focus indicators across **both** Dark and Light themes.

### 3.1 Semantic Token Table

| Avalonia Resource Key (`SolidColorBrush`) | Role | Dark Hex | Light Hex | Verified Contrast (Dark / Light) |
|---|---|---|---|---|
| `LpCanvasBrush` | Window backdrop / scroll viewport | `#0C0D12` | `#F4F5F8` | Base canvas |
| `LpSurfaceBrush` | Cards, sticky header, drawer surface | `#14161F` | `#FFFFFF` | Primary surface |
| `LpSurfaceElevatedBrush` | Hover surface, inputs, segmented well, keycaps | `#1C1F2B` | `#EBEEF4` | Secondary control surface |
| `LpSurfaceSubtleBrush` | Recessed wells, inset sections | `#101219` | `#F8F9FB` | Subtle container |
| `LpStageBackgroundBrush` | Dark preview canvas behind kinetic SkiaSharp art | `#08090D` | `#0E1017` | Always dark studio stage for lyric fidelity |
| `LpTextPrimaryBrush` | Headings, active labels, card titles | `#F4F6FA` | `#111420` | **16.67:1** / **18.34:1** (on Surface) |
| `LpTextSecondaryBrush` | Descriptions, metadata, subtitles | `#A6B0C3` | `#475166` | **8.26:1** / **7.96:1** (on Surface) |
| `LpTextTertiaryBrush` | Captions, hints, helper copy, units | `#8490A7` | `#5B657B` | **5.61:1** / **5.85:1** (on Surface); **5.10:1** / **5.03:1** (on Elevated) |
| `LpBorderDefaultBrush` | Subtle card & divider hairline (1px) | `#2A2F42` | `#D5DBE7` | Structural hairline separation |
| `LpBorderStrongBrush` | Interactive input/button/toggle borders | `#677294` | `#737E99` | **3.45:1** / **3.49:1** (on Elevated, >= 3.0:1 PASS) |
| `LpAccentPrimaryBrush` | Primary CTA fill, Active badge fill, selected switch | `#5B54EC` | `#4338CA` | **3.37:1** / **7.90:1** vs Surface; **5.35:1** / **7.90:1** with white text |
| `LpAccentPrimaryHoverBrush` | Primary CTA hover fill | `#645DF0` | `#3730A3` | **4.81:1** / **9.93:1** with white text |
| `LpAccentPrimaryPressedBrush` | Primary CTA pressed fill (paired with `LpAccentBorderBrush` rim) | `#5B54EC` | `#312E81` | **5.35:1** / **11.42:1** with white text |
| `LpOnAccentPrimaryBrush` | Text & icons inside `LpAccentPrimaryBrush` | `#FFFFFF` | `#FFFFFF` | **5.35:1** / **7.90:1** |
| `LpAccentTextBrush` | Accent links, pinned icons, active pill text | `#A5B4FC` | `#3730A3` | **9.05:1** / **9.93:1** (on Surface) |
| `LpAccentBorderBrush` | Active card border, selected tile ring (2px) | `#818CF8` | `#4338CA` | **5.50:1** / **6.80:1** (on Elevated) |
| `LpAccentSoftBgBrush` | Selected tile tint, accent badge background | `#1E2140` | `#EEF2FF` | **7.82:1** / **8.88:1** with `LpAccentTextBrush` |
| `LpLiveGreenBrush` | Live playback dot, connected indicator, solid live pill | `#22C55E` | `#15803D` | **7.92:1** / **5.02:1** (non-text on Surface) |
| `LpLiveGreenTextBrush` | Live playback text (`"Playing · Spotify"`) | `#4ADE80` | `#166534` | **10.35:1** / **7.13:1** (on Surface); **8.98:1** / **6.76:1** (on SoftBg) |
| `LpLiveGreenSoftBgBrush` | Live status capsule tinted background | `#10281B` | `#ECFDF3` | Status pill backdrop |
| `LpOnLiveGreenBrush` | Text on solid `LpLiveGreenBrush` | `#052E16` | `#FFFFFF` | **6.54:1** / **5.02:1** |
| `LpWarningBrush` | Non-zero timing offset border/icon, degraded state | `#F59E0B` | `#B45309` | **7.64:1** / **4.32:1** (non-text on Elevated) |
| `LpWarningTextBrush` | Warning & active offset text | `#FBBF24` | `#92400E` | **10.80:1** / **7.09:1** (on Surface); **9.47:1** / **6.37:1** (on SoftBg) |
| `LpWarningSoftBgBrush` | Warning banner & shifted offset pill fill | `#2B2110` | `#FEF3C7` | Warning container backdrop |
| `LpDangerBrush` | Destructive button fill, error icon | `#DC2626` | `#DC2626` | **4.83:1** with `LpOnDangerBrush` |
| `LpDangerTextBrush` | Error messages, destructive link text | `#FCA5A5` | `#B91C1C` | **9.50:1** / **6.47:1** (on Surface); **8.98:1** / **5.30:1** (on SoftBg) |
| `LpDangerSoftBgBrush` | Error callout backdrop | `#2D1518` | `#FEE2E2` | Error container backdrop |
| `LpOnDangerBrush` | Text on solid `LpDangerBrush` | `#FFFFFF` | `#FFFFFF` | **4.83:1** / **4.83:1** |
| `LpFocusRingBrush` | Keyboard `:focus-visible` 2px perimeter ring | `#A5B4FC` | `#3730A3` | **9.05:1** / **9.93:1** (on Surface) |
| `LpMediaOutlineBrush` | Neutral 1px inset rim on album art & preview stages | `#1AFFFFFF` | `#1A000000` | Neutral alpha (10%)—never brand-tinted |
| `LpDrawerScrimBrush` | Modal / narrow-mode drawer backdrop scrim | `#99050609` | `#66111420` | 60% dark / 40% slate translucent backdrop |

### 3.2 Overlay Scrim & Album-Color Sync Guardrails

Dynamic album-cover extraction (`AccentColor1`, `AccentColor2`, `AccentColor3`) drives the **SkiaSharp lyric overlay and card previews**, NOT the Avalonia app chrome.
- **Luminance Floor on Dark Preview Stage**: Extracted overlay colors rendered against transparent desktop/dark stage MUST be clamped to a minimum relative luminance `>= 0.24` (or boosted in OKLCH lightness) so dark navy/charcoal album art never renders invisible lyrics.
- **High-Contrast Text Scrim Token** (`Settings.HighContrastScrim = true`):
  - Scrim fill: `#CC08090D` (`rgba(8, 9, 13, 0.80)`) rounded pill (`CornerRadius=14`, padding `12px 20px`) with subtle `1px` `#26FFFFFF` inner hairline.
  - Verified worst-case contrast: White `#FFFFFF` lyric text over `#CC08090D` composited atop a pure `#FFFFFF` white desktop window yields **11.37:1** contrast (exceeds WCAG AAA 7:1).
- **Default Unscrimmed Readability**: When scrim is off, overlay text includes a calibrated multi-pass dark drop shadow (`0 2px 12px rgba(0,0,0,0.65)` + `1px` dark outline halo at `45%` alpha) so white/accent kinetic words remain legible across mixed wallpapers.

### 3.3 Avalonia `ResourceDictionary` Skeleton (`Themes/LyricPulseTokens.axaml`)

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <ResourceDictionary.ThemeDictionaries>
    <ResourceDictionary x:Key="Dark">
      <SolidColorBrush x:Key="LpCanvasBrush" Color="#0C0D12"/>
      <SolidColorBrush x:Key="LpSurfaceBrush" Color="#14161F"/>
      <SolidColorBrush x:Key="LpSurfaceElevatedBrush" Color="#1C1F2B"/>
      <SolidColorBrush x:Key="LpSurfaceSubtleBrush" Color="#101219"/>
      <SolidColorBrush x:Key="LpStageBackgroundBrush" Color="#08090D"/>
      <SolidColorBrush x:Key="LpTextPrimaryBrush" Color="#F4F6FA"/>
      <SolidColorBrush x:Key="LpTextSecondaryBrush" Color="#A6B0C3"/>
      <SolidColorBrush x:Key="LpTextTertiaryBrush" Color="#8490A7"/>
      <SolidColorBrush x:Key="LpBorderDefaultBrush" Color="#2A2F42"/>
      <SolidColorBrush x:Key="LpBorderStrongBrush" Color="#677294"/>
      <SolidColorBrush x:Key="LpAccentPrimaryBrush" Color="#5B54EC"/>
      <SolidColorBrush x:Key="LpAccentPrimaryHoverBrush" Color="#645DF0"/>
      <SolidColorBrush x:Key="LpAccentPrimaryPressedBrush" Color="#5B54EC"/>
      <SolidColorBrush x:Key="LpOnAccentPrimaryBrush" Color="#FFFFFF"/>
      <SolidColorBrush x:Key="LpAccentTextBrush" Color="#A5B4FC"/>
      <SolidColorBrush x:Key="LpAccentBorderBrush" Color="#818CF8"/>
      <SolidColorBrush x:Key="LpAccentSoftBgBrush" Color="#1E2140"/>
      <SolidColorBrush x:Key="LpLiveGreenBrush" Color="#22C55E"/>
      <SolidColorBrush x:Key="LpLiveGreenTextBrush" Color="#4ADE80"/>
      <SolidColorBrush x:Key="LpLiveGreenSoftBgBrush" Color="#10281B"/>
      <SolidColorBrush x:Key="LpOnLiveGreenBrush" Color="#052E16"/>
      <SolidColorBrush x:Key="LpWarningBrush" Color="#F59E0B"/>
      <SolidColorBrush x:Key="LpWarningTextBrush" Color="#FBBF24"/>
      <SolidColorBrush x:Key="LpWarningSoftBgBrush" Color="#2B2110"/>
      <SolidColorBrush x:Key="LpDangerBrush" Color="#DC2626"/>
      <SolidColorBrush x:Key="LpDangerTextBrush" Color="#FCA5A5"/>
      <SolidColorBrush x:Key="LpDangerSoftBgBrush" Color="#2D1518"/>
      <SolidColorBrush x:Key="LpOnDangerBrush" Color="#FFFFFF"/>
      <SolidColorBrush x:Key="LpFocusRingBrush" Color="#A5B4FC"/>
      <SolidColorBrush x:Key="LpMediaOutlineBrush" Color="#1AFFFFFF"/>
      <SolidColorBrush x:Key="LpDrawerScrimBrush" Color="#99050609"/>
    </ResourceDictionary>

    <ResourceDictionary x:Key="Light">
      <SolidColorBrush x:Key="LpCanvasBrush" Color="#F4F5F8"/>
      <SolidColorBrush x:Key="LpSurfaceBrush" Color="#FFFFFF"/>
      <SolidColorBrush x:Key="LpSurfaceElevatedBrush" Color="#EBEEF4"/>
      <SolidColorBrush x:Key="LpSurfaceSubtleBrush" Color="#F8F9FB"/>
      <SolidColorBrush x:Key="LpStageBackgroundBrush" Color="#0E1017"/>
      <SolidColorBrush x:Key="LpTextPrimaryBrush" Color="#111420"/>
      <SolidColorBrush x:Key="LpTextSecondaryBrush" Color="#475166"/>
      <SolidColorBrush x:Key="LpTextTertiaryBrush" Color="#5B657B"/>
      <SolidColorBrush x:Key="LpBorderDefaultBrush" Color="#D5DBE7"/>
      <SolidColorBrush x:Key="LpBorderStrongBrush" Color="#737E99"/>
      <SolidColorBrush x:Key="LpAccentPrimaryBrush" Color="#4338CA"/>
      <SolidColorBrush x:Key="LpAccentPrimaryHoverBrush" Color="#3730A3"/>
      <SolidColorBrush x:Key="LpAccentPrimaryPressedBrush" Color="#312E81"/>
      <SolidColorBrush x:Key="LpOnAccentPrimaryBrush" Color="#FFFFFF"/>
      <SolidColorBrush x:Key="LpAccentTextBrush" Color="#3730A3"/>
      <SolidColorBrush x:Key="LpAccentBorderBrush" Color="#4338CA"/>
      <SolidColorBrush x:Key="LpAccentSoftBgBrush" Color="#EEF2FF"/>
      <SolidColorBrush x:Key="LpLiveGreenBrush" Color="#15803D"/>
      <SolidColorBrush x:Key="LpLiveGreenTextBrush" Color="#166534"/>
      <SolidColorBrush x:Key="LpLiveGreenSoftBgBrush" Color="#ECFDF3"/>
      <SolidColorBrush x:Key="LpOnLiveGreenBrush" Color="#FFFFFF"/>
      <SolidColorBrush x:Key="LpWarningBrush" Color="#B45309"/>
      <SolidColorBrush x:Key="LpWarningTextBrush" Color="#92400E"/>
      <SolidColorBrush x:Key="LpWarningSoftBgBrush" Color="#FEF3C7"/>
      <SolidColorBrush x:Key="LpDangerBrush" Color="#DC2626"/>
      <SolidColorBrush x:Key="LpDangerTextBrush" Color="#B91C1C"/>
      <SolidColorBrush x:Key="LpDangerSoftBgBrush" Color="#FEE2E2"/>
      <SolidColorBrush x:Key="LpOnDangerBrush" Color="#FFFFFF"/>
      <SolidColorBrush x:Key="LpFocusRingBrush" Color="#3730A3"/>
      <SolidColorBrush x:Key="LpMediaOutlineBrush" Color="#1A000000"/>
      <SolidColorBrush x:Key="LpDrawerScrimBrush" Color="#66111420"/>
    </ResourceDictionary>
  </ResourceDictionary.ThemeDictionaries>

  <!-- Spacing, Radii, Icon Sizes (Theme-Invariant) -->
  <x:Double x:Key="LpSpaceXs">4</x:Double>
  <x:Double x:Key="LpSpaceSm">8</x:Double>
  <x:Double x:Key="LpSpaceMd">12</x:Double>
  <x:Double x:Key="LpSpaceLg">16</x:Double>
  <x:Double x:Key="LpSpaceXl">24</x:Double>
  <x:Double x:Key="LpSpace2Xl">32</x:Double>

  <CornerRadius x:Key="LpRadiusBadge">6</CornerRadius>
  <CornerRadius x:Key="LpRadiusControl">8</CornerRadius>
  <CornerRadius x:Key="LpRadiusCard">12</CornerRadius>
  <CornerRadius x:Key="LpRadiusDrawer">16</CornerRadius>
  <CornerRadius x:Key="LpRadiusPill">999</CornerRadius>

  <x:Double x:Key="LpIconXs">12</x:Double>
  <x:Double x:Key="LpIconSm">16</x:Double>
  <x:Double x:Key="LpIconMd">20</x:Double>
  <x:Double x:Key="LpIconLg">24</x:Double>
</ResourceDictionary>
```

---

## 4. Typography System

LyricPulse separates **App Chrome Typography** (Windows Fluent clarity for settings & gallery) from **Lyric Stage Typography** (expressive bundled SIL OFL typefaces rendered via SkiaSharp/HarfBuzz).

### 4.1 App Chrome Typeface & Hierarchy

- **Primary UI Font Stack (`LpFontUi`)**: `Segoe UI Variable Text, Segoe UI, Inter, sans-serif`
- **Display Header Font (`LpFontDisplay`)**: `Segoe UI Variable Display, avares://LyricPulse.UI/Assets/Fonts#Jost, Segoe UI`
- **Monospace / Telemetry Font (`LpFontMono`)**: `Cascadia Mono, Consolas, monospace`

| Role | Resource Key | Size | Weight | Line Height | Tracking | Usage & Wrapping Rules |
|---|---|---|---|---|---|---|
| **Window Title / Brand** | `LpTypeTitle` | `18px` | `SemiBold (600)` | `24px` | `-0.01em` | Top bar wordmark, Settings drawer heading. Single-line `NoWrap`. |
| **Section Header** | `LpTypeSection` | `14px` | `SemiBold (600)` | `20px` | `0` | Gallery section header, Settings group headers. Single-line. |
| **Card Title / Track Name** | `LpTypeCardTitle` | `15px` | `SemiBold (600)` | `20px` | `-0.005em` | Style card title, active song title. `TextTrimming="CharacterEllipsis"` + full-text `ToolTip` & `AutomationProperties.Name`. |
| **Body / Control Label** | `LpTypeBody` | `13px` | `Regular (400)` | `19px` | `0` | Style descriptions (max 2 lines), setting descriptions, troubleshooting steps. `TextWrapping="Wrap"`. |
| **Control Action / Button** | `LpTypeControl` | `13px` | `Medium (500)` | `18px` | `0` | Buttons, segmented tabs, radio labels. `NoWrap` with flexible min-width so compact pills never wrap to 2 lines. |
| **Caption / Meta** | `LpTypeCaption` | `12px` | `Regular (400)` | `16px` | `+0.01em` | Artist name, monitor resolution, reset hint. Never smaller than `12px`. |
| **Mono Telemetry / Keycap** | `LpTypeMono` | `12px` | `Medium (500)` | `16px` | `0` | Timing offsets (`-0.5s`, `+1.0s`), opacity `%`, hex colors, hotkey badges (`Ctrl+Shift+L`). **MUST use `FontFeatures="tnum"`** (tabular numerals) so changing digits never jitter adjacent controls. |

### 4.2 Bundled Lyric Stage Typefaces (SIL OFL)

Selectable in Settings and previewed live inside the 10 SkiaSharp cards:

| Font Family | Bundled Asset | Character | Default Applicability |
|---|---|---|---|
| **Jost** *(Default)* | `avares://LyricPulse.UI/Assets/Fonts/Jost-Bold.ttf#Jost` | Geometric Futura-inspired sans; clean optical rhythm | Styles 1, 2, 5, 6, 7, 8, 9 |
| **Bodoni Moda** | `avares://LyricPulse.UI/Assets/Fonts/BodoniModa-Bold.ttf#Bodoni Moda` | High-contrast modern Didone serif; editorial drama | Styles 1, 2, 5, 6, 7, 8, 9 |
| **Mulish** | `avares://LyricPulse.UI/Assets/Fonts/Mulish-Bold.ttf#Mulish` | Crisp minimalist humanist sans; effortless fast reading | Styles 1, 2, 5, 6, 7, 8, 9 |
| **Nunito** | `avares://LyricPulse.UI/Assets/Fonts/Nunito-SemiBold.ttf#Nunito` | Rounded balanced terminals; warm pop/indie feel | Styles 1, 2, 5, 6, 7, 8, 9 |
| **Cascadia Mono** *(Fixed)* | System / bundled monospace | Code-editor syntax feel | Styles 3 & 4 (`Codestyle`, `Codestyle Visual`) |
| **Segoe UI Variable** *(Fixed)* | System UI sans | Authentic phone chat bubbles | Style 10 (`Messages`) |

---

## 5. Spacing, Concentric Radius & Responsive Desktop Grid

### 5.1 4/8px Spacing Scale

| Token | Value | Application |
|---|---|---|
| `LpSpaceXs` | `4px` | Icon-to-label micro gaps inside compact chips, concentric inner frame padding |
| `LpSpaceSm` | `8px` | Inline control gaps, segmented button internal gap, badge padding |
| `LpSpaceMd` | `12px` | Card footer vertical padding, Settings row gaps, button horizontal padding |
| `LpSpaceLg` | `16px` | Gallery card grid gutter, Settings section padding, standard button horizontal padding |
| `LpSpaceXl` | `24px` | Gallery outer canvas margin, Settings inter-section vertical rhythm |
| `LpSpace2Xl` | `32px` | Empty-state vertical breathing room |

### 5.2 Concentric Corner Radii (`make-interfaces-feel-better`)

Nested rounded containers follow **`outer radius = inner radius + inset padding`**:
- **Style Card (`12px` outer)** with **`4px` inset stage bezel** → Live Preview Stage uses **`8px` inner radius** (`8 + 4 = 12`).
- **Floating Action Pill (`8px` radius)** sitting inside the preview stage (`8px` margin from stage edge) remains visually distinct without pinching corners.
- **Settings Drawer (`16px` left corners)** housing **Font Picker Tiles (`8px` radius)** at `16px` horizontal padding → optically harmonious separation.
- **Album Art Thumbnail (`6px` radius)** inside the `64px` top bar (`12px` vertical padding) with a `1px` neutral `LpMediaOutlineBrush` inset outline.

### 5.3 Desktop Target Viewports & DPI Scaling

| Viewport Profile | Window Size (DIP) | Gallery Columns | Settings Panel Mode |
|---|---|---|---|
| **Compact Desktop** | `760 × 600` (MinSize) | 2 columns (tight) or 1 column `< 680px` | Overlay slide-over drawer (`360px`) with `LpDrawerScrimBrush` |
| **Standard Default** | `1000 × 740` | 2 columns (balanced 16:9 cards) | Inline right-docked drawer (`380px`)—Gallery stays live beside Settings |
| **Wide / Ultrawide** | `1360 × 860` | 2 columns centered (`MaxWidth="1120"` gallery container so preview cards never stretch absurdly wide) | Inline right-docked drawer (`400px`) |
| **High-DPI Check** | Windows `100%`, `125%`, `150%`, `200%` | All vectors (`PathIcon` + SkiaSharp DPI scaling via `GetDpiForMonitor`) stay razor-sharp; zero bitmap blur | Zero clipped labels or scroll overflow |

---

## 6. Elevation, Borders & Optical Polish

- **Borders First, Shadows Second**:
  - Cards and controls rely on crisp `1px` `LpBorderDefaultBrush` borders so surfaces separate cleanly in both OLED Dark and Daylight Light modes without muddy drop shadows.
  - Active/selected cards upgrade to a **`2px` `LpAccentBorderBrush`** border (use `Padding` compensation of `1px` or outer `Border` overlay so toggling active state **never shifts layout by 1px**!).
- **Layered Subtle `BoxShadows` Tokens**:
  - `LpShadowCardHover`: Dark `0 8 24 0 #4D000000` · Light `0 6 18 0 #1A111420`
  - `LpShadowPopover`: Dark `0 12 32 0 #73000000` · Light `0 12 28 0 #24111420`
  - `LpShadowDrawer`: Dark `-8 0 28 0 #66000000` · Light `-6 0 24 0 #1F111420`
- **Media Inset Outline**: Every album cover thumbnail and SkiaSharp dark stage carries a `1px` inner border using `LpMediaOutlineBrush` (`#1AFFFFFF` dark / `#1A000000` light) so dark album artwork never melts invisibly into `#14161F` surfaces.
- **Optical Alignment**:
  - Play triangle icon shifted `+1px` right (`Margin="1,0,0,0"`) inside circular/square icon buttons.
  - Inline icons paired with `13px` text aligned vertically via `VerticalAlignment="Center"` with `8px` (`LpSpaceSm`) horizontal spacing.

---

## 7. Iconography System (Zero Emojis in Structural Chrome)

> **CRITICAL RULE:** **ZERO emojis** (no palette, gear, pushpin, musical note, monitor, speaker, or warning Unicode emojis) are permitted anywhere in Avalonia structural chrome (buttons, headers, badges, tabs, settings rows, troubleshooting panels, or system tray menus).

- **Icon Family**: **Microsoft Fluent System Icons** (MIT License) implemented as vector `StreamGeometry` resources (`Themes/Icons.axaml`) rendered inside Avalonia `<PathIcon Data="{StaticResource LpIconPinRegular}" Width="16" Height="16"/>`.
- **Filled vs. Outline Discipline**:
  - **Idle / Inactive Chrome**: `Regular` (1.5px optical stroke) icons (e.g., `PinRegular`, `SettingsRegular`, `EyeRegular`, `DesktopRegular`).
  - **Active / Pinned / Selected / Status**: `Filled` icons (e.g., `PinFilled`, `CheckmarkCircleFilled`, `EyeOffFilled`, `WarningFilled`).
- **Size Tokens**: `12px` (`LpIconXs` inline chips), `16px` (`LpIconSm` buttons & menu rows), `20px` (`LpIconMd` top-bar icon buttons & display cards), `24px` (`LpIconLg` empty states).
- **Accessibility Semantics**:
  - Decorative icon next to visible text label → `AutomationProperties.AccessibilityView="Raw"`.
  - Standalone icon button (e.g., Pin, Settings, Close) → MUST define `AutomationProperties.Name` + `ToolTip.Tip` + toggle state (`ToggleButton.IsChecked`).
- **Explicit Art-Engine Boundary**: Styles 2 (`Fisheye Visual`), 4 (`Codestyle Visual`), and 5 (`Visual Stack`) intentionally render semantic Fluent Icons and Unicode CLDR emoji glyphs *inside the SkiaSharp kinetic lyric canvas* as part of the offline GloVe word-pictures art engine. That canvas output is user media art—never structural UI chrome.

---

## 8. Interaction States, Focus & Keyboard Navigation

### 8.1 Hit Area Minimums & Anti-Jitter Rule
- **Standard buttons & segmented controls**: Minimum height **`36px`** (`MinHeight="36"`, `Padding="12,0"`).
- **Icon-only buttons (Pin, Settings, Close, Help, Reset)**: Minimum hit area **`40 × 40px`** (or **`44 × 44px`** where layout allows). Visual glyph stays `16–20px` centered inside transparent clickable padding; expanded hit areas MUST NOT overlap sibling controls.
- **Cursor**: Set `Cursor="Hand"` on all clickable cards, icon buttons, toggles, chips, and swatches.
- **Anti-Jitter Stability**: Hover and press states MUST NOT change layout bounds (`Margin`, `Width`, `Height`, `BorderThickness` without compensation). Use brush transitions, `BoxShadow`, and subtle `RenderTransform="scale(0.97)"` on `:pressed` (disabled under Reduced Motion).

### 8.2 Focus Rings & Keyboard Operability (WCAG 2.2 AA)
- **Visible `:focus-visible` Ring**: Every focusable control (cards, buttons, sliders, toggles, swatches, font tiles, offset steps, drawer close) uses a `2px` solid `LpFocusRingBrush` perimeter ring offset `2px` outward (`>= 9.0:1` contrast in both themes). Never suppress `FocusAdorner` without replacing it with this token.
- **Focus Not Obscured**: The `ScrollViewer` below the sticky `64px` top media-status bar uses top content padding (`Padding="24,16,24,24"`) and `BringIntoView` margin clearance so tabbing backward through Style Cards never tucks a focused card behind the top bar.
- **Keyboard Shortcuts (`Window.KeyBindings` / `HotKey`)**:
  - `Tab` / `Shift+Tab`: Predictable visual reading order (Top Bar → Gallery Filter → Style Cards → Active Drawer).
  - `Arrow Keys`: Navigate between 2-column style cards, font tiles, and segmented controls.
  - `Enter` / `Space`: Activate focused Style Card or toggle focused control.
  - `Ctrl+,`: Toggle Settings drawer.
  - `Ctrl+Left` / `Ctrl+Right` / `Ctrl+0`: Nudge timing offset `−0.5s` / `+0.5s` / reset `0.0s` while Gallery is focused.
  - `Esc`: Close open popover, cancel hotkey capture, or close Settings drawer (restoring focus to trigger button).
- **Live Announcements**: Media track changes, offset changes, and style activation expose a single polite status region (`AutomationProperties.LiveSetting="Polite"`), announcing contextual phrases (e.g., `"Activated Constellation style"`, `"Timing offset plus 0.5 seconds"`) without stealing keyboard focus.

---

## 9. Motion & Reduced-Motion Token System

Never animate `all` properties. Specify explicit Avalonia `Transitions` (`BrushTransition`, `DoubleTransition`, `TransformOperationsTransition`) on compositor-friendly properties only.

| Token | Duration | Easing | Usage |
|---|---|---|---|
| `LpMotionInstant` | `90ms` | `CubicEaseOut` | Button `:pressed` scale (`0.97`), icon cross-fade swap |
| `LpMotionFast` | `150ms` | `CubicEaseOut` | Hover border/background transitions, badge state shifts, exit animations |
| `LpMotionStandard` | `200ms` | `CubicEaseOut` | Active card border transition, segmented indicator slide |
| `LpMotionDrawerEnter` | `220ms` | `CubicEaseOut` | Settings drawer slide-in (`TranslateX 24px -> 0` + `Opacity 0 -> 1`) |
| `LpMotionDrawerExit` | `140ms` | `CubicEaseIn` | Settings drawer exit (always faster and quieter than enter) |

### Reduced-Motion Contract (`Settings.ReducedMotion` or Windows OS `SPI_GETCLIENTAREAANIMATION == false`)
1. **App Chrome**: All `TransformOperationsTransition` and slide transforms are disabled (`0ms`); drawer and popovers use subtle `100ms` opacity fade or instant swap. Button press uses border/fill shift only (no `scale(0.97)`).
2. **Gallery Live Previews**: Auto-playing 60 FPS kinetic previews default to a **calm stepped lyric preview** (clean crossfade every 2.4s) or paused state with a clear `"Play preview"` toggle.
3. **Desktop Overlay**: SkSL Elliptical Bulge (`Fisheye`), 13-tap Directional Motion Blur, and 3D perspective drift (`Kinetic Wall`) degrade gracefully to clean typographic opacity/word-highlight transitions with zero camera swing or distortion.

---

## 10. Universal Content States Contract

Every view in LyricPulse MUST handle these 6 states cleanly:

| State | Trigger | Visible Feedback | Safe Next Action |
|---|---|---|---|
| **Loading** | Fetching lyrics from LRCLIB/NetEase or initializing GloVe index | Subtle indeterminate Fluent progress bar (`2px` height along bottom edge of top media bar) + `"Fetching synced lyrics…"` caption; layout never shifts. | Cancel/switch track naturally; Gallery remains 100% interactive with demo lyrics. |
| **Empty / Idle** | No active Spotify / Apple Music / GSMTC session detected | Top bar displays muted neutral icon + `"Waiting for music"` + `"Not detecting?"` troubleshooting pill. Preview cards run rich demo lyrics so Gallery is immediately useful. | One-click `"Not detecting?"` diagnostic checklist or `"Preview Demo on Desktop"`. |
| **Error** | Network timeout, LRCLIB + NetEase both fail, or hotkey registration conflict | Inline `LpDangerSoftBgBrush` banner with `WarningFilled` vector icon and specific human-readable reason (never raw stack traces). | `"Retry Fetch"` button, `"Choose Different Hotkey"`, or continue using offline styles. |
| **Partial** | Unsynced plain lyrics found (no timestamps) OR album thumbnail unavailable | Badge shows `"Unsynced lyrics"` / `"Default palette"` clearly; proportional timing fallback or default studio accents engage seamlessly. | Timing offset controls disable with explanatory tooltip when lyrics are unavailable; manual color picker stays available. |
| **Success** | Style activated, pinned to tray, offset adjusted, or settings saved | Immediate visual state change (Active badge, Filled Pin, amber offset pill, subtle `2s` non-blocking status text update). | Undo via single click (unpin, reset `0.0s`, re-select previous style). |
| **Permission / Boundary** | Windows global hotkey blocked by elevated app or Win32 monitor enumeration change | Inline `LpWarningSoftBgBrush` note explaining the OS boundary (e.g., `"Hotkey in use by another app"`). | Prompt user to record a non-conflicting shortcut or refresh display list. |

---

## 11. Anti-Patterns & Pre-Delivery Audit Checklist

### Forbidden Anti-Patterns
- **NO** emojis used as structural UI icons — vector Fluent System Icons (`PathIcon`) only.
- **NO** hardcoded hex colors or `{StaticResource}` for theme-aware brushes in `.axaml` views.
- **NO** nested cards inside cards (use clean dividers and spacing inside Settings).
- **NO** layout-shifting hover/active states (never shift grid cards when border switches `1px -> 2px`).
- **NO** color-only status communication (always pair Live Green / Amber / Red with an icon or text label).
- **NO** unannounced or focus-obscuring sticky bars, modals, or drawers.
- **NO** blocking HTTP/GloVe work on the Avalonia UI thread.

### Audit Gate Checklist (Used by UI & UX Auditors)
- [ ] Both **Dark** and **Light** `ThemeVariant` modes verified; all normal text `>= 4.5:1`, UI boundaries/focus rings `>= 3.0:1`.
- [ ] Zero emojis in structural chrome; 100% consistent Fluent System Icons (`PathIcon`).
- [ ] All dynamic readouts (timing offset, opacity %, scale %, monitor resolution) use `FontFeatures="tnum"`.
- [ ] Keyboard-only traversal (`Tab`, `Shift+Tab`, `Arrows`, `Enter`, `Space`, `Esc`, `Ctrl+,`) works across Gallery and Settings with visible `2px` `LpFocusRingBrush` unobscured by sticky bar.
- [ ] Reduced Motion toggle disables chrome slides, pauses/calms preview loops, and simplifies overlay shaders.
- [ ] Responsive verification across `760×600`, `1000×740`, and `1360×860` at `100%` and `150%` Windows DPI scaling with zero clipped text or horizontal scrollbars.
- [ ] All 6 content states (Loading, Empty, Error, Partial, Success, Permission) visually verified.
