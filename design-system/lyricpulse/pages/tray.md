# Page Spec: System Tray Icon & `NativeMenu` (`TrayController`)

> **Inherits:** [`../MASTER.md`](../MASTER.md)  
> **Primary Surface:** `LyricPulse.UI/Tray/TrayController.cs` & `App.axaml` (`TrayIcon.Icons`)  
> **Job to Be Done:** Act as LyricPulse's zero-footprint primary daily remote control—letting users check what is playing, toggle the desktop overlay on/off, switch pinned kinetic styles, nudge per-song timing offset, or reopen the Style Gallery without ever leaving their active window.

---

## 1. Avalonia `TrayIcon` + `NativeMenu` Architecture

- Follow the Avalonia 11 stack guideline: **always use native `TrayIcon` + `NativeMenu` / `NativeMenuItem`** rather than custom fake popup windows, ensuring native Windows 10/11 DPI scaling, high-contrast theme inheritance, screen-reader (NVDA/Narrator) operability, and zero focus-stealing glitches.
- **Zero Emojis in Menu Labels**: Win32/Shell native menus rely on system fonts where Unicode emojis render inconsistently or break menu alignment. Use **clean typography + native `ToggleType="CheckBox"` and `ToggleType="Radio"` indicators exclusively**.

---

## 2. Tray Icon Visual States & Adaptive Taskbar Theming

Provide crisp vector-derived multi-size `.ico` bundles (`16×16`, `20×20`, `24×24`, `32×32`, `48×48` for `100%`–`300%` Windows DPI scaling) that adapt to the Windows Taskbar theme (`HKCU\...\Personalize\SystemUsesLightTheme`):

| State | Visual Glyphs | Windows Taskbar Theme Adaptation |
|---|---|---|
| **Playing & Synced** | LyricPulse 3-bar kinetic waveform mark + `4px` bottom-right `LpLiveGreenBrush` (`#22C55E`) live dot | White glyph `#F8FAFC` on Dark Taskbar; Deep Slate `#111420` on Light Taskbar |
| **Idle / Waiting** | Clean monochrome LyricPulse waveform mark (no badge dot) | `#E2E8F0` on Dark Taskbar; `#1E293B` on Light Taskbar |
| **Overlay Hidden** | Dimmed waveform mark with crisp diagonal slash (clear visual cue that lyrics are toggled off) | `65%` opacity monochrome glyph |
| **Sync Issue / No Lyrics** | Waveform mark + `4px` bottom-right Amber (`#F59E0B`) dot | Matches taskbar polarity |

### Dynamic Hover Tooltip (`TrayIcon.ToolTipText`)
Windows truncates tray tooltips beyond 127 characters; keep concise (`<= 96 chars`):
- **Playing (offset 0s)**: `LyricPulse — Blinding Lights · The Weeknd (Fisheye)`
- **Playing (offset shifted)**: `LyricPulse — Blinding Lights · The Weeknd (+0.5s)`
- **Overlay Hidden**: `LyricPulse — Lyrics hidden (Click to open Gallery)`
- **Idle**: `LyricPulse — Ready (Waiting for Spotify or Apple Music)`

---

## 3. Mouse & Single-Instance Interactions

| Input Gesture | Action |
|---|---|
| **Left-Click (Single Click)** | Open `GalleryWindow` or restore & bring it to foreground (`WindowState = Normal`, `Activate()`). |
| **Right-Click** | Display Windows native `NativeMenu` anchored to the tray icon. |
| **Middle-Click** *(optional Win32 hook / fallback)* | Instant toggle Show / Hide Lyrics overlay without opening the menu. |
| **Second App Launch (`Local\LyricPulse.ShowWindow`)** | Existing single-instance process activates `GalleryWindow` smoothly—never spawns a duplicate tray icon. |
| **Closing `GalleryWindow` (`X` button)** | Minimizes/hides `GalleryWindow` cleanly to the System Tray while keeping the desktop lyrics overlay running. On **first-ever close**, optionally display a one-time subtle notice inside the tray tooltip (`"Still running in tray — Quit from tray menu"`). |

---

## 4. Authoritative `NativeMenu` Hierarchy

```text
┌────────────────────────────────────────────────┐
│  Playing: Blinding Lights — The Weeknd         │  Disabled header (truncated @ 38 chars)
│  Spotify · Synced lyrics                       │  Disabled secondary status line
├────────────────────────────────────────────────┤
│  ✓  Show Lyrics                  Ctrl+Shift+L  │  ToggleType="CheckBox"
├────────────────────────────────────────────────┤
│  ●  Fisheye                                    │  Pinned Styles (ToggleType="Radio")
│     Codestyle                                  │
│     Constellation                              │
│     Kinetic Wall                               │
│     All Styles                             ▸   │  Submenu of all 10 styles (Radio)
├────────────────────────────────────────────────┤
│     Timing Offset (+0.5s)                  ▸   │  Submenu (shows live offset when != 0)
├────────────────────────────────────────────────┤
│     Open Style Gallery...        Ctrl+Shift+G  │  Activates GalleryWindow
│     Settings...                        Ctrl+,  │  Opens GalleryWindow + Settings drawer
├────────────────────────────────────────────────┤
│     Quit LyricPulse                            │  Graceful shutdown & resource cleanup
└────────────────────────────────────────────────┘
```

### 4.1 Item-by-Item Behavioral Specification

1. **Now Playing Header** (2 disabled informational `NativeMenuItem` rows, `IsEnabled="False"`):
   - **Row 1**:
     - Playing: `Playing: [Track] — [Artist]` (middle-truncated cleanly at 38 characters with `…` so the menu never stretches wide).
     - Paused: `Paused: [Track] — [Artist]`
     - Idle: `No media playing`
   - **Row 2**:
     - Playing + Synced: `Spotify · Synced lyrics` (or `Apple Music · Synced lyrics` / `[App] · Synced lyrics`).
     - Playing + Unsynced/None: `Spotify · No synced lyrics found`.
     - Idle: `Waiting for Spotify or Apple Music`.
2. **Show Lyrics Toggle** (`ToggleType="CheckBox"`, `IsChecked="{Binding IsOverlayVisible}"`):
   - Always labeled `"Show Lyrics"` (never flip the verb between "Show" and "Hide" when using a CheckBox—checked = visible, unchecked = hidden, eliminating state ambiguity!).
   - Displays native shortcut hint `Ctrl+Shift+L` (synced dynamically with user's configured hotkey).
3. **Pinned Styles Radio List** (`ToggleType="Radio"`):
   - Renders each style pinned by the user in `GalleryWindow`, in canonical order.
   - Default first-run pins: **`Fisheye`**, **`Codestyle`**, **`Constellation`**, **`Kinetic Wall`**.
   - **Active Unpinned Fallback**: If the user currently has an *unpinned* style active (e.g., `Messages`), append a clean top checked entry `Messages (Current)` so the active state is always visible inside the tray menu.
   - **Zero Pinned Fallback**: If the user unpins all styles, render one disabled helper item `No pinned styles — pin in Gallery` above the `All Styles ▸` submenu.
4. **All Styles ▸ Submenu**:
   - Contains all 10 canonical styles (`Fisheye`, `Fisheye Visual`, `Codestyle`, `Codestyle Visual`, `Visual Stack`, `Constellation`, `Spider's Web`, `Node Line`, `Kinetic Wall`, `Messages`) as `ToggleType="Radio"` items so any style is reachable in 2 clicks without launching the Gallery window.
5. **Timing Offset ▸ Submenu**:
   - Parent menu header updates dynamically:
     - When `0.0s`: `Timing Offset`
     - When shifted: `Timing Offset (+0.5s)` or `Timing Offset (−1.0s)` (uses true Unicode minus `−` U+2212 for visual balance).
   - **Submenu Items**:
     - `Earlier  −1.0s`
     - `Earlier  −0.5s`
     - `Reset to  0.0s` (`IsEnabled="{Binding HasNonZeroOffset}"`)
     - `Later  +0.5s`
     - `Later  +1.0s`
     - `--- Separator ---`
     - `Resets automatically on next track` (`IsEnabled="False"`)
   - All nudge items remain disabled (`IsEnabled="False"`) with parent tooltip/status clarity when no synced lyrics are currently loaded.
6. **Open Style Gallery...**:
   - Opens/focuses `GalleryWindow` (`Gallery` view, Settings drawer collapsed unless already open). Gesture hint `Ctrl+Shift+G`.
7. **Settings...**:
   - Opens/focuses `GalleryWindow` and immediately expands the inline `SettingsView` drawer. Gesture hint `Ctrl+,`.
8. **Quit LyricPulse**:
   - Unregisters Windows global hotkeys, disposes all per-monitor click-through `OverlayWindow` handles cleanly, removes `TrayIcon` from the notification area (preventing ghost icons on hover), flushes settings JSON, releases `Local\LyricPulse.SingleInstance` mutex, and exits with code `0`.

---

## 5. Edge Cases & Windows Tray Resilience

- **Explorer.exe Restart**: Re-initialize/verify `TrayIcon` visibility if Windows Explorer restarts (handled by Avalonia Win32 tray shell message handling).
- **Multi-Monitor DPI Change**: Clicking `Open Style Gallery...` checks that the saved window coordinates still intersect a currently connected screen; otherwise centers on the primary monitor so the Gallery never opens off-screen after undocking a laptop.
- **Settings Sync**: Pinning/unpinning a style in `GalleryWindow`, changing a hotkey in `SettingsView`, or switching tracks via GSMTC updates `NativeMenu` items on the UI dispatcher thread cleanly without rebuilding/flickering an already-open menu.
