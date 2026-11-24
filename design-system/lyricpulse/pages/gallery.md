# Page Spec: Style Gallery Window (`GalleryWindow.axaml`)

> **Inherits:** [`../MASTER.md`](../MASTER.md)  
> **Primary View:** `LyricPulse.UI/Views/GalleryWindow.axaml`  
> **Job to Be Done:** Let the user immediately verify what music is playing, nudge lyric sync timing if needed, audition all 10 live-animated kinetic styles at a glance, activate any style in one click, and pin favorites to the System Tray.

---

## 1. Window Architecture & Layout Geometry

- **Window Title**: `LyricPulse`
- **Window Dimensions**:
  - Default: `1000 × 740` DIP (centered on primary monitor on first launch; remembers last position/size safely within active virtual screen bounds).
  - Minimum: `760 × 600` DIP.
- **Window Chrome**: Fluent title bar (`ExtendClientAreaToDecorationsHint="True"`, `ExtendClientAreaTitleBarHeightHint="36"`), custom left brand lockup (`LyricPulse` 16×16 vector logo mark + `13px SemiBold` wordmark) + subtle inline theme indicator, native Windows minimize/maximize/close caption buttons on the right.
- **Structural Layout**:
  ```text
  ┌─────────────────────────────────────────────────────────────────────────┐
  │ [Logo] LyricPulse                                             ─   □   X │  36px Title Bar
  ├─────────────────────────────────────────────────────────────────────────┤
  │ [Art] Track Title         │ [-1s][-0.5s][ 0.0s ][+0.5s][+1s] │[Eye][Set]│  64px Sticky Media & Sync Bar
  │       ● Playing · Spotify │        Resets next track         │          │
  ├───────────────────────────────────────────────────────┬─────────────────┤
  │ Style Gallery (10)  [All][Pinned][Visual]  [Pause]    │                 │  44px Utility Strip
  │ ┌───────────────────────┐ ┌───────────────────────┐   │                 │
  │ │ [Active]        [Pin] │ │                 [Pin] │   │  Settings       │
  │ │   Live SkiaSharp      │ │   Live SkiaSharp      │   │  Drawer         │  Main Split Container
  │ │   16:9 Preview        │ │   16:9 Preview        │   │  (380px when    │  (Gallery ScrollViewer
  │ ├───────────────────────┤ ├───────────────────────┤   │   expanded)     │   + optional inline
  │ │ Fisheye       Shader  │ │ Fisheye Visual  Icons │   │                 │   SettingsView)
  │ │ Tight 3-word stack... │ │ Fisheye paired with.. │   │                 │
  │ └───────────────────────┘ └───────────────────────┘   │                 │
  └───────────────────────────────────────────────────────┴─────────────────┘
  ```

---

## 2. Sticky Top Media-Status & Sync Bar (`Height="64"`)

Pinned below the title bar with `Background="{DynamicResource LpSurfaceBrush}"`, `BorderBrush="{DynamicResource LpBorderDefaultBrush}"`, `BorderThickness="0,0,0,1"`, `Padding="24,10"`.

> **Focus Clearance Rule:** The Gallery `ScrollViewer` begins *below* this grid row (`Grid.Row="2"`), so the sticky bar physically never overlaps focused scrolled cards (`Focus Not Obscured` AA/AAA compliant by layout geometry).

### 2.1 Left Cluster — Live Media Capsule (`Grid.Column="0"`, flexible width `MinWidth="220"`)

- **Album Art Frame**: `40 × 40px`, `CornerRadius="6"`, `Stretch="UniformToFill"`, wrapped in `1px` `LpMediaOutlineBrush` inset border.
  - *Idle Fallback*: `LpSurfaceElevatedBrush` square with centered `20px` `MusicNote2Regular` Fluent vector icon (`LpTextTertiaryBrush`).
  - *Album Color Micro-Strip*: Directly beneath or overlaid along the bottom-right edge (`3` circular `6px` dots separated by `2px`, `1px` neutral border) displaying the live extracted `AccentColor1..3` when `Auto-Sync Album Colors` is active.
- **Track & Source Stack** (`Margin="12,0,0,0"`, `VerticalAlignment="Center"`):
  - **Line 1 — Track Title**: `LpTypeCardTitle` (`14px SemiBold`), `LpTextPrimaryBrush`, `TextTrimming="CharacterEllipsis"`, full title in `ToolTip` & `AutomationProperties.Name`.
    - *Idle copy*: `"No music playing"`
  - **Line 2 — Source & Live Status Row** (`Orientation="Horizontal"`, `Spacing="6"`):
    - Status Indicator Dot (`8 × 8px` ellipse):
      - **Playing & Synced**: `LpLiveGreenBrush` + explicit label `"Playing · Spotify"` / `"Playing · Apple Music"` / `"Playing · [Player]"` (`LpLiveGreenTextBrush`, `12px Medium`).
      - **Paused**: `LpTextTertiaryBrush` hollow ring + `"Paused · Spotify"`.
      - **Unsynced / Lyrics Not Found**: `LpWarningBrush` + `"No synced lyrics · [Artist]"`.
      - **Idle**: `LpTextTertiaryBrush` + `"Waiting for Spotify or Apple Music"`.
    - **Artist Name**: Separated by middle dot `·`, `LpTextSecondaryBrush`, `TextTrimming="CharacterEllipsis"`.
- **One-Click Troubleshooting Action**:
  - **Contextual Pill** (auto-visible when `Idle`, `Lyrics Not Found`, or `Provider Error`): Compact pill button (`Height="28"`, `CornerRadius="999"`, `LpWarningSoftBgBrush` or `LpSurfaceElevatedBrush`, `12px Medium`, icon `QuestionCircleRegular` `14px` + `"Not detecting?"` / `"Fix sync"`).
  - **Always-Available Help Icon** (when actively playing & synced): Quiet `36 × 36px` (expanded `40 × 40px` hit area) ghost button with `InfoRegular` icon (`AutomationProperties.Name="Connection and lyrics diagnostics"`).
  - **Diagnostic Flyout Popover** (`Width="340"`, `CornerRadius="12"`, `LpSurfaceBrush`, `LpShadowPopover`):
    - Displays 3 live evidence rows (using Fluent `CheckmarkCircleFilled` / `WarningFilled` / `DismissCircleFilled` icons—zero emojis):
      1. **Windows Media Session (GSMTC)**: `Connected (Spotify.exe)` or `No active session detected`.
      2. **Lyrics Pipeline**: `Synced LRC loaded (LRCLIB)` / `Fallback (NetEase)` / `Not found for normalized title`.
      3. **Overlay Windows**: `Active on 2 of 2 displays`.
    - Quick Tips (2 concise bullets, e.g., *"Use Spotify Desktop or Apple Music app (browser tabs require Windows media controls enabled)"*).
    - Action Footer: Primary `"Retry Lyrics Fetch"` button + Secondary `"Run Demo Preview on Desktop"` toggle.
- **Loading Progress Hairline**: When fetching lyrics, a `2px` indeterminate `ProgressBar` (`LpAccentPrimaryBrush`) renders cleanly along the bottom border of the 64px top bar—zero vertical shift.

### 2.2 Center-Right Cluster — Per-Song Timing Offset Control (`Grid.Column="1"`)

Clean segmented studio bar inside a single pill well (`Background="{DynamicResource LpSurfaceSubtleBrush}"`, `BorderBrush="{DynamicResource LpBorderDefaultBrush}"`, `BorderThickness="1"`, `CornerRadius="8"`, `Padding="2"`):

| Segment | Visual Label | Action | Width / Style |
|---|---|---|---|
| 1 | `−1s` | Nudge offset `-1.0s` (earlier) | `MinWidth="44"`, `Height="34"`, `LpTypeMono` (`tnum`) |
| 2 | `−0.5s` | Nudge offset `-0.5s` (earlier) | `MinWidth="48"`, `Height="34"`, `LpTypeMono` (`tnum`) |
| 3 *(Center Readout)* | `0.0s` *(or `+0.5s`, `-1.5s`)* | Click resets offset to `0.0s` (disabled when `0.0s`) | `MinWidth="58"`, `Height="34"`, `LpTypeMono` (`SemiBold`, `tnum`). When `!= 0.0s`: `Background="{DynamicResource LpWarningSoftBgBrush}"`, `Foreground="{DynamicResource LpWarningTextBrush}"`, `BorderBrush="{DynamicResource LpWarningBrush}"` `1px`. |
| 4 | `+0.5s` | Nudge offset `+0.5s` (later) | `MinWidth="48"`, `Height="34"`, `LpTypeMono` (`tnum`) |
| 5 | `+1s` | Nudge offset `+1.0s` (later) | `MinWidth="44"`, `Height="34"`, `LpTypeMono` (`tnum`) |

- **Accessibility & Hint**:
  - Group wrapper `AutomationProperties.Name="Lyric timing offset, resets on next track"`.
  - Each step has explicit `AutomationProperties.Name` (e.g., `"Shift lyrics 0.5 seconds earlier"`).
  - Center readout tooltip: `"Current per-song offset. Click to reset to 0.0s. Automatically resets when track changes."`
  - Disabled gracefully (`IsEnabled="False"`, `Opacity="0.45"`) when no lyrics are loaded, with explanatory tooltip.
  - Keyboard shortcuts while Gallery focused: `Ctrl+Left` (`-0.5s`), `Ctrl+Right` (`+0.5s`), `Ctrl+0` (reset).

### 2.3 Far-Right Cluster — Global Quick Actions (`Grid.Column="2"`, `Spacing="8"`)

1. **Overlay Visibility ToggleButton** (`Height="36"`, `Padding="12,0"`, `CornerRadius="8"`, `HotKey="Ctrl+Shift+L"`):
   - Checked (`Overlay On`): `LpSurfaceElevatedBrush`, `EyeRegular` `16px` (`LpLiveGreenTextBrush`), label `"Live"` (compact) or `"Overlay On"` (standard).
   - Unchecked (`Overlay Hidden`): `LpWarningSoftBgBrush`, `EyeOffFilled` `16px` (`LpWarningTextBrush`), label `"Hidden"`.
2. **Settings Drawer ToggleButton** (`Width="40"`, `Height="40"`, `CornerRadius="8"`, `HotKey="Ctrl+,"`):
   - `SettingsRegular` (`20px`) when closed → `SettingsFilled` + `LpAccentSoftBgBrush` + `LpAccentTextBrush` when Settings drawer is open.
   - `AutomationProperties.Name="Toggle Settings panel"`, `ToolTip.Tip="Settings (Ctrl+,)"`.

---

## 3. Gallery Utility Strip (`Height="44"`, `Padding="24,12,24,4"`)

- **Left**: `"Style Gallery"` (`LpTypeSection`) + muted count chip `"10 styles · 3 pinned"` (`LpTypeCaption`, `LpTextTertiaryBrush`).
- **Center/Right**:
  - Compact filter chips (`Height="28"`, `CornerRadius="999"`, `NoWrap`): `All`, `Pinned`, `With Visuals`, `Typography & Motion`.
  - **Stage Backdrop Switcher** (compact 3-segment toggle aligned with [`../MOTION_AND_STYLES.md`](../MOTION_AND_STYLES.md) §7.3): `Dark` (`#08090D`, default) | `Light Page` (`#F8FAFC`, verifies white-webpage legibility!) | `Gradient` (vibrant desktop test backdrop).
  - **Preview Playback Toggle** (`Height="28"`, `Padding="10,0"`, `CornerRadius="999"`): `PauseRegular` + `"Pause previews"` / `PlayRegular` + `"Play previews"`. Satisfies WCAG 2.2.2 (Pause, Stop, Hide) and automatically defaults to paused/stepped when `ReducedMotion` is enabled.

---

## 4. Responsive 2-Column Live Preview Card Grid

- **Container**: `ScrollViewer` (`VerticalScrollBarVisibility="Auto"`, `HorizontalScrollBarVisibility="Disabled"`) wrapping a centered max-width container (`MaxWidth="1120"`, `Padding="24,12,24,24"`).
- **Columns**:
  - `Viewport Width >= 680px` (available gallery pane width, accounting for open Settings drawer): **2 equal columns**, `16px` (`LpSpaceLg`) horizontal and vertical gutters.
  - `Viewport Width < 680px`: **1 column** stacked cleanly with zero horizontal overflow.

### 4.1 Style Preview Card Anatomy

```text
┌──────────────────────────────────────────────────────────┐
│ ┌──────────────────────────────────────────────────────┐ │  4px inset (12px outer / 8px inner radius)
│ │ [Active]                                     [Pin]   │ │  Overlay chips inside 16:9 dark Skia stage
│ │                                                      │ │
│ │               LIVE SKIASHARP PREVIEW                 │ │  Live 30 FPS (60 FPS hover/active, stepped RM)
│ │         (Neon Horizon demo or live song)             │ │  Staggered per-card phase offsets (MOTION §7.3)
│ │                                                      │ │
│ └──────────────────────────────────────────────────────┘ │
│  Fisheye                                  Lens Shader    │  15px SemiBold + subtle nowrap tag chip
│  Tight 3-word stack bulging through a spherical lens.    │  13px LpTextSecondaryBrush (max 2 lines)
└──────────────────────────────────────────────────────────┘
```

- **Outer Card Container**:
  - `CornerRadius="12"`, `Background="{DynamicResource LpSurfaceBrush}"`, `Cursor="Hand"`.
  - **Border & Anti-Jitter Active Ring**:
    - Default: `BorderThickness="1"`, `BorderBrush="{DynamicResource LpBorderDefaultBrush}"`, inner `Padding="1"` compensation.
    - Hover (`:pointerover`): `BorderBrush="{DynamicResource LpBorderStrongBrush}"`, `BoxShadow="{DynamicResource LpShadowCardHover}"` (`150ms` `BrushTransition`). Zero vertical translate shift.
    - Active (`.active`): `BorderThickness="2"`, `BorderBrush="{DynamicResource LpAccentBorderBrush}"`, inner `Padding="0"` compensation (exact same outer box height/width—zero jitter!).
    - Focus (`:focus-visible`): Outer `2px` `LpFocusRingBrush` adorner offset `2px`.
- **Live SkiaSharp Preview Stage**:
  - Aspect Ratio: **`16:9`** (`MinHeight="168"`, `MaxHeight="230"`), `Margin="4,4,4,0"`, `CornerRadius="8"`, `ClipToBounds="True"`, `Background="{DynamicResource LpStageBackgroundBrush}"`.
  - Wrapped in `1px` `LpMediaOutlineBrush` inset border so the dark canvas has crisp edges on both dark and light surfaces.
  - **Performance & Stagger Discipline**: Cards scrolled outside the visible `ScrollViewer` viewport (or when `GalleryWindow` is inactive/minimized) automatically pause their SkiaSharp render ticker (`IsVisibleInViewport`). Visible preview cards cap at **`30 FPS`** (upgrading to **`60 FPS`** on hovered or active cards) and use staggered per-card phase offsets on the built-in `Neon Horizon` demo song (see [`../MOTION_AND_STYLES.md`](../MOTION_AND_STYLES.md) §7.3) so cards never flash in lockstep unison.
- **Top-Left Active Badge** (visible only on the currently selected style):
  - `Margin="10"`, `Padding="8,4"`, `CornerRadius="999"`, `Background="{DynamicResource LpAccentPrimaryBrush}"`.
  - Contents: `CheckmarkCircleFilled` (`12px`, `#FFFFFF`) + `"Active"` (`12px SemiBold`, `#FFFFFF`, `NoWrap`). Contrast: `5.35:1` (Dark) / `7.90:1` (Light).
- **Top-Right Pin-to-Tray ToggleButton**:
  - Placed with `Margin="6"`, hit area **`40 × 40px`** (`Width="40"`, `Height="40"`), visual badge **`32 × 32px`** centered inside (`CornerRadius="8"`, dark translucent backdrop `#B314161F` with `1px` `#26FFFFFF` rim so it reads cleanly over ANY bright SkiaSharp lyric color!).
  - Unpinned: `PinRegular` (`16px`, `#E2E8F0`).
  - Pinned (`:checked`): `PinFilled` (`16px`, `#A5B4FC`) + subtle `#CC1E2140` fill + `1px` `#818CF8` border.
  - Clicking Pin calls `e.Handled = true` so pinning a style does NOT unintentionally switch the active style.
  - Accessible name: `"Pin [Style Name] to System Tray"` / `"Unpin [Style Name] from System Tray"`.
- **Card Footer** (`Padding="14,12,14,14"`, `Spacing="4"`):
  - **Header Row** (`DockPanel` / 2-column `Grid`):
    - Style Name (`LpTypeCardTitle`, `LpTextPrimaryBrush`, `NoWrap`).
    - Right-aligned Capability Chip (`Padding="6,2"`, `CornerRadius="6"`, `LpSurfaceElevatedBrush`, `LpTextSecondaryBrush`, `12px Medium`, `NoWrap`).
  - **Description**: `LpTypeBody` (`13px`), `LpTextSecondaryBrush`, `TextWrapping="Wrap"`, `MaxLines="2"`, `TextTrimming="CharacterEllipsis"`.

---

## 5. Complete 10-Style Card Catalog (Authoritative Copy & Tags)

All 10 styles MUST render in this canonical order (matching `PROJECT.md` and `REVERSE_ENGINEERING_BASELINE.md`):

| # | Style ID | Display Name | Capability Chip | Concise Card Description (1–2 lines) | Default Pinned |
|---|---|---|---|---|---|
| 1 | `Fisheye` | **Fisheye** | `Lens Shader` | Tight 3-word stack bulging outward through a spherical SkSL lens distortion. | Yes |
| 2 | `FisheyeVisual` | **Fisheye Visual** | `Visual Icons` | Spherical Fisheye typography paired with offline semantic icons beside matching words. | No |
| 3 | `Codestyle` | **Codestyle** | `Monospace` | Lyrics typed out live as syntax-highlighted code beneath a blinking red cursor. | Yes |
| 4 | `CodestyleVisual` | **Codestyle Visual** | `Visual Icons` | Live syntax-highlighted code lyrics enhanced with inline semantic word icons. | No |
| 5 | `VisualStack` | **Visual Stack** | `Visual Icons` | Centered kinetic word column pairing every matching lyric word with an expressive icon. | No |
| 6 | `Constellation` | **Constellation** | `Vector Path` | Each lyric line traces across space as a connected glowing constellation, star by star. | Yes |
| 7 | `SpidersWeb` | **Spider's Web** | `Geometric` | Words spin outward into an intricate geometric web with a luminous strand per word. | No |
| 8 | `NodeLine` | **Node Line** | `Vector Path` | A flowing line draws itself across the screen as every word illuminates a glowing node. | No |
| 9 | `KineticWall` | **Kinetic Wall** | `3D Motion` | Words land sequentially onto a typographic wall drifting smoothly in 3D perspective. | Yes |
| 10 | `Messages` | **Messages** | `Chat Thread` | Song lines arrive as a sleek phone message thread with timed chat bubbles per line. | No |

---

## 6. Gallery Page Content States

| State | Visual Presentation |
|---|---|
| **Live Music Playing + Synced** | Top bar pulses green with real album art, 3-dot palette strip, and live song lyrics animating across all visible style cards. |
| **Idle (No Media Session)** | Top bar shows neutral music icon + `"No music playing"` + `"Not detecting?"` pill. All 10 style cards animate rich built-in **demo lyrics** so the gallery looks alive and immediately testable on first launch. |
| **Filter Empty (`Pinned` filter with 0 pinned)** | Centered empty state (`PinRegular` `24px`, `"No pinned styles yet"`, `"Pin your favorite styles to switch them from the System Tray."`, button `"Show All Styles"`). |
| **Lyrics Fetch Failed / Offline** | Top bar shows amber `"Lyrics unavailable"` + `"Fix sync"` pill; cards smoothly fall back to demo lyrics while preserving album colors. |
| **Reduced Motion Enabled** | Preview cards display clean stepped lyric frames instead of continuous 60 FPS distortion/3D drift; utility strip shows `"Reduced motion active"` quiet indicator. |
