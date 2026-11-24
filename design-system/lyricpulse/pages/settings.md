# Page Spec: Settings Drawer (`SettingsView.axaml`)

> **Inherits:** [`../MASTER.md`](../MASTER.md)  
> **Primary View:** `LyricPulse.UI/Views/SettingsView.axaml` (+ `Themes/ColorPickerTheme.axaml`)  
> **Job to Be Done:** Provide instant, live-applied controls for lyric typography, album-art vs. custom colors, multi-monitor targeting, overlay opacity & safe screen placement, accessibility accommodations, and global Windows hotkeys—without covering or disconnecting the live Style Gallery preview.

---

## 1. Drawer Architecture & Responsive Behavior

- **Presentation Mode**:
  - **Standard & Wide (`Window Width >= 880px`)**: Inline right-docked panel (`Width="380"` at `< 1200px`, `Width="400"` at `>= 1200px`), separated from the Gallery pane by a `1px` `LpBorderDefaultBrush` left border. Because it is inline, every font, scale, color, and reduced-motion change updates the **live SkiaSharp preview cards immediately beside it**.
  - **Compact (`Window Width < 880px`)**: Right-anchored slide-over overlay (`Width="360"`, `CornerRadius="16,0,0,16"`, `BoxShadow="{DynamicResource LpShadowDrawer}"`) backed by `LpDrawerScrimBrush`. Clicking the scrim or pressing `Esc` dismisses the drawer.
- **Motion**:
  - Enter: `LpMotionDrawerEnter` (`220ms`, `CubicEaseOut`, `TranslateX(24px -> 0)` + `Opacity(0 -> 1)`).
  - Exit: `LpMotionDrawerExit` (`140ms`, `CubicEaseIn`, faster & quieter).
  - Reduced Motion: `100ms` opacity fade only (zero horizontal slide).
- **Focus Management**:
  - Opening Settings moves initial keyboard focus to the Settings header region or Close button (`Esc` closes drawer and returns focus cleanly to the Settings toggle button in the Gallery top bar).
- **Internal Structure** (No nested cards-inside-cards—clean stacked sections with `24px` section spacing and `1px` `LpBorderDefaultBrush` hairline dividers):
  - **Sticky Drawer Header** (`Height="52"`, `Padding="20,0,12,0"`, `BorderThickness="0,0,0,1"`): `"Settings"` (`LpTypeTitle` `16px SemiBold`) + Close `Button` (`40 × 40px` hit target, `DismissRegular` `16px`, `AutomationProperties.Name="Close Settings"`, `ToolTip.Tip="Close (Esc)"`).
  - **Scrollable Body** (`ScrollViewer`, `Padding="20,16,20,24"`): 6 grouped sections below.
  - **Sticky Utility Footer** (`Height="52"`, `Padding="20,8"`, `BorderThickness="0,1,0,0"`): `"Reset Defaults"` (two-step inline confirmation) + `"LyricPulse · Open Source"` caption.

---

## 2. Section 1 — Typography

**Header**: `TextFontRegular` (`16px`) + `"Typography"` (`LpTypeSection`).

### 2.1 Bundled Font Picker (2 × 2 Selectable Tile Grid, `Spacing="8"`)

Each radio tile (`MinHeight="68"`, `CornerRadius="8"`, `Padding="12,10"`, `Cursor="Hand"`) renders its specimen `"Aa"` in the **actual bundled SIL OFL typeface** so users choose by eye, not guesswork:

| Tile | Specimen | Font Name | Descriptor (`12px` `LpTextTertiaryBrush`) |
|---|---|---|---|
| 1 *(Default)* | `Aa` *(Jost-Bold, 22px)* | **Jost** | Geometric sans |
| 2 | `Aa` *(BodoniModa-Bold, 22px)* | **Bodoni Moda** | Editorial serif |
| 3 | `Aa` *(Mulish-Bold, 22px)* | **Mulish** | Clean humanist |
| 4 | `Aa` *(Nunito-SemiBold, 22px)* | **Nunito** | Rounded soft |

- **States**:
  - Unselected: `LpSurfaceElevatedBrush`, `1px` `LpBorderDefaultBrush` (+ `1px` padding compensation).
  - Selected (`:checked`): `LpAccentSoftBgBrush`, `2px` `LpAccentBorderBrush` (`0px` compensation—zero layout shift), small `CheckmarkCircleFilled` (`14px`, `LpAccentTextBrush`) in top-right corner.
- **Contextual Note** (`LpTypeCaption`, `LpTextTertiaryBrush`, `Margin="0,8,0,0"`): *"Codestyle uses monospace and Messages uses system chat typography."*

### 2.2 Lyric Scale / Size Slider

- **Row Header**: Label `"Lyric Scale"` (`LpTypeBody`) + right-aligned live tabular badge (`LpTypeMono`, `tnum`, e.g., `100%`) + subtle `"Reset"` link button (visible when `!= 100%`).
- **Control**: Avalonia Fluent `Slider` (`Minimum="60"`, `Maximum="160"`, `TickFrequency="5"`, `IsSnapToTickEnabled="True"`, `SmallChange="5"`, `LargeChange="15"`).
- **Scale Endpoints**: Below slider, left `"60% Compact"` and right `"160% Stage"` (`12px`, `LpTextTertiaryBrush`).
- **Accessibility**: `AutomationProperties.Name="Lyric text scale percentage"`, keyboard operable with `Left`/`Right` arrow keys.

---

## 3. Section 2 — Color Mode & Palette

**Header**: `ColorRegular` (`16px`) + `"Color Mode"` (`LpTypeSection`).

### 3.1 Mode Segmented Switch (`Height="36"`, `CornerRadius="8"`, `LpSurfaceSubtleBrush` well)

- Option A: **`Auto-Sync Album`** (default) — Extracts `AccentColor1..3` in real time from active GSMTC album thumbnail.
- Option B: **`Custom Palette`** — Manual 3-accent picker via themed Avalonia `ColorPicker`.

### 3.2 Auto-Sync Album State
- Displays a compact live preview strip (`Padding="12"`, `CornerRadius="8"`, `LpSurfaceElevatedBrush`) with 3 circular `24 × 24px` swatches (`Accent 1`, `Accent 2`, `Accent 3`, each with `1px` `LpMediaOutlineBrush` rim) + `"Synced to current album art"` (or `"Using default studio palette (no album art)"` when idle) + subtle badge `"Contrast-safe"`.

### 3.3 Custom Palette State (3-Accent `ColorPicker` Trio)
- **3 Interactive Swatch Rows** (`Spacing="8"`), each `Height="44"`, `CornerRadius="8"`, `LpSurfaceElevatedBrush`:
  1. **Primary Lyric** (`AccentColor1`) — Main spoken word color.
  2. **Highlight / Active** (`AccentColor2`) — Currently singing word / node / cursor accent.
  3. **Glow / Secondary** (`AccentColor3`) — Secondary lines, web strands, and subtle glow tint.
- Each row shows: `24 × 24px` rounded color swatch (`CornerRadius="6"`, `1px` `LpMediaOutlineBrush` border) + Role label + `LpTypeMono` hex readout (`#F8FAFC`) + Avalonia `ColorPicker` flyout button styled via `Themes/ColorPickerTheme.axaml` (strictly using `LpSurfaceBrush`, `LpBorderDefaultBrush`, `LpTextPrimaryBrush`, `LpFocusRingBrush`).
- **Quick Studio Presets Row** (4 compact clickable 3-dot preset chips below the custom pickers so users don't have to build palettes from scratch):
  - *Pure Studio* (`#F8FAFC` / `#818CF8` / `#38BDF8`)
  - *Vinyl Warm* (`#FEF3C7` / `#F59E0B` / `#FB7185`)
  - *Mint Pulse* (`#ECFDF5` / `#34D399` / `#2DD4BF`)
  - *Monochrome* (`#FFFFFF` / `#CBD5E1` / `#64748B`)

---

## 4. Section 3 — Displays (Multi-Monitor Targeting)

**Header**: `DesktopRegular` (`16px`) + `"Displays"` (`LpTypeSection`) + right-aligned `"Refresh"` ghost icon button (`ArrowClockwiseRegular`, `36 × 36px` hit area, re-enumerates `Screens`).

- **Display Selector Cards** (1 card per connected Windows monitor, vertical stack `Spacing="8"`):
  - Container: `MinHeight="56"`, `Padding="12,10"`, `CornerRadius="8"`, `LpSurfaceElevatedBrush`, `1px` border (`LpAccentBorderBrush` subtle tint when enabled, `LpBorderDefaultBrush` when disabled).
  - **Left**: `DesktopFilled` (when active, `LpAccentTextBrush`) or `DesktopRegular` (when disabled, `LpTextTertiaryBrush`), `20px` + display index badge (`1`, `2`, `3`).
  - **Center**:
    - Line 1: `"Display 1 · Primary"` (or monitor friendly name e.g. `"DELL U2723QE"`, `13px SemiBold`, `TextTrimming="CharacterEllipsis"`).
    - Line 2: Resolution & Windows DPI in `LpTypeMono` (`tnum`): `"2560 × 1440 · 125% DPI"` (`12px`, `LpTextSecondaryBrush`).
  - **Right**: Fluent `ToggleSwitch` (`OnContent=""`, `OffContent=""`, bound inversely to `DisabledDisplays`, `AutomationProperties.Name="Show lyrics on Display 1"`).
- **All-Disabled Guardrail Banner**: If every monitor is toggled off, display an inline `LpWarningSoftBgBrush` callout (`WarningFilled` `16px` + `"Lyrics overlay is disabled on all displays."` + `"Enable Primary"` action button).

---

## 5. Section 4 — Overlay Appearance & Safe Position

**Header**: `LayerRegular` (`16px`) + `"Overlay & Appearance"` (`LpTypeSection`).

1. **Overlay Opacity Slider**:
   - Label `"Overlay Opacity"` + `LpTypeMono` (`tnum`) readout (`25%` – `100%`, step `5%`, default `95%`).
2. **Safe Screen Position Anchor**:
   - Label `"Vertical Zone & Alignment"` + interactive **5-Zone Safe Anchor Grid** (visual mini-monitor rectangle `Height="92"`, `CornerRadius="8"`, `LpStageBackgroundBrush`, `1px` `LpBorderStrongBrush`, showing a thin Windows taskbar strip at the bottom edge so "safe positioning" is visually unmistakable!):
     - Selectable radio zones (`36 × 28px` hit buttons with keyboard arrow navigation): `Top Center`, `Middle Center`, `Bottom-Third (Default)`, `Bottom-Left`, `Bottom-Right`.
   - **Taskbar & Edge Safe Margin Slider**: `16px` to `120px` (default `48px`, `tnum` readout `"48 px"`), guaranteeing lyrics never collide with the Windows Taskbar or corner notifications.
3. **App Window Theme Variant** (`RequestedThemeVariant`):
   - Segmented 3-way control (`Height="34"`): `System` | `Dark` | `Light`. Immediately updates `{DynamicResource}` tokens across Gallery & Settings windows with zero restart.

---

## 6. Section 5 — Accessibility

**Header**: `AccessibilityRegular` (`16px`) + `"Accessibility"` (`LpTypeSection`).

1. **Reduced Motion (`ToggleSwitch`)**:
   - Title: `"Reduced Motion"` (`13px Medium`).
   - Description (`12px`, `LpTextSecondaryBrush`): `"Replaces lens shaders, motion blur, and 3D drift with calm crossfades, and pauses gallery animations."`
   - Subtle chip when auto-matched from Windows: `"Synced with Windows setting"`.
2. **High-Contrast Text Scrim (`ToggleSwitch` + Intensity Preset)**:
   - Title: `"High-Contrast Text Scrim"` (`13px Medium`).
   - Description (`12px`, `LpTextSecondaryBrush`): `"Reinforces the 4-layer overlay legibility pipeline with a strong backdrop scrim behind lyrics for guaranteed legibility over bright apps and wallpapers (11.4:1 contrast)."`
   - Companion Ambient Scrim Intensity selector (`Off` | `Subtle` | `Balanced [Default]` | `Strong`, mapped directly to Layer 0 radial scrim alpha `0.00 / 0.24 / 0.42 / 0.62` in [`../MOTION_AND_STYLES.md`](../MOTION_AND_STYLES.md) §1.1).

---

## 7. Section 6 — Global Hotkeys

**Header**: `KeyboardRegular` (`16px`) + `"Global Hotkeys"` (`LpTypeSection`) + subtle `"Reset"` ghost button.

- **Keycap List** (`Spacing="8"`):

| Action | Default Shortcut | Behavior |
|---|---|---|
| Toggle Show / Hide Lyrics | `Ctrl + Shift + L` | Immediately shows/hides all overlay windows |
| Cycle Pinned Styles | `Ctrl + Shift + S` | Advances to next pinned style (wraps around) |
| Timing Offset Earlier (−0.5s) | `Ctrl + Shift + [` | Shifts current track lyrics 0.5s earlier |
| Timing Offset Later (+0.5s) | `Ctrl + Shift + ]` | Shifts current track lyrics 0.5s later |
| Open Style Gallery | `Ctrl + Shift + G` | Brings `GalleryWindow` to foreground |

- **Row Anatomy** (`MinHeight="40"`):
  - Left: Action label (`13px`, `LpTextPrimaryBrush`).
  - Right: Clickable Keycap Recorder `Button` (`MinHeight="32"`, `Padding="10,4"`, `CornerRadius="6"`, `LpSurfaceElevatedBrush`, `BorderThickness="1,1,1,2"`, `LpBorderStrongBrush`, `LpTypeMono` `12px`).
- **Recording State**:
  - Clicking or pressing `Enter` on a keycap enters active capture (`LpAccentSoftBgBrush`, `2px` `LpAccentBorderBrush`, text `"Press keys… (Esc cancels)"`).
  - **Validation Rules**:
    - Requires at least one modifier (`Ctrl`, `Alt`, or `Shift+Ctrl/Alt`) to avoid hijacking normal typing.
    - `Backspace` / `Delete` clears the shortcut (`"Unassigned"`).
    - If Windows `RegisterHotKey` fails (occupied by another application) or collides with another LyricPulse action, show an inline `LpDangerSoftBgBrush` error message directly below the row (`"Ctrl+Shift+L is already in use by another app. Try another combination."`) and keep the previous valid binding intact.

---

## 8. Drawer Footer & Reset Safety

- **Reset All Settings**: Uses an inline **two-step confirmation** (`"Reset Settings"` → click swaps cleanly into `"Confirm Reset"` [`LpDangerBrush`] + `"Cancel"` [`LpSurfaceElevatedBrush`] for 5 seconds or until acted upon). No jarring modal popups, zero accidental resets.
- **Persistence**: All changes debounce-save cleanly to local JSON (`%LocalAppData%\LyricPulse\settings.json`) via atomic temp-file swap with zero UI-thread blocking.
