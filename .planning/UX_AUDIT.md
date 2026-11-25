# LyricPulse — Milestone 1A (Phases 1–3) UX Audit

**Audit Date:** 2026-11-25  
**Auditor:** LyricPulse UX Evaluator (ECC UX Review & Journey Verification)  
**Scope:** Milestone 1A — Phases 1–3 (`LyricPulse.App`, `LyricPulse.Core`, `LyricPulse.Rendering`; read-only audit of `src/`). Phase 4 styles 4–10 and Phase 5 `GalleryWindow` / `SettingsView` are out of Milestone 1A scope and are evaluated only where Milestone 1A stopgap behavior affects real user journeys.  
**Authoritative Specs Audited Against:**
- `design-system/lyricpulse/pages/tray.md`
- `design-system/lyricpulse/MOTION_AND_STYLES.md` (§§4.5, 6, 7, 8.1)
- `design-system/lyricpulse/MASTER.md` (§§1, 3.1, 4.1)
- `.planning/ROADMAP.md` (Canonical Decisions #6, #7, #9, #10 & Phase 3 Milestone 1A Success Criteria)

---

## Executive Verdict: **PASS WITH NOTES** *(Re-Verified on `70876e0` + `c6420bb` — 0 Critical, 0 High Blockers Remaining; Cleared for Milestone 1A CEO Check-In)*

> **Audit Progression:** Baseline `371bfa5` Verdict = **FAIL** (1 Critical, 5 High, 4 Medium, 3 Low) → Post-Remediation `c6420bb` Verdict = **PASS WITH NOTES** (**0 Critical, 0 High, 1 Medium, 3 Low** non-blocking polish notes for Phases 4–6; see [Re-Verification Addendum (`c6420bb`)](#re-verification-addendum-c6420bb)).

### Baseline Findings Summary (`371bfa5` — Historical Record)

| Severity | Baseline (`371bfa5`) | Re-Verified (`c6420bb`) | Summary |
|---|---:|---:|---|
| **CRITICAL** | **1** | **0** | `UX-01` **RESOLVED:** 180/180 `StarterLexicon.tsv` Fluent Icon codepoints verified bijectively against bundled TTF (12/12 `Neon Horizon` exact) + `#D90B0C16` obsidian badges routed through `LegibilityPipeline` (`>= 9.28:1` over `#FFFFFF`) |
| **HIGH** | **5** | **0** | `UX-02`–`UX-06` **ALL RESOLVED:** synchronous initial tray menu + Demo auto-reveal + `--demo` second-instance IPC; `LyricsStatus.Loading`, strict `Synced`-only gate, `"Retry Lyrics Fetch"`, neutral `Instrumental`, `IsPostSongOutro`; hotkey conflict surfacing, unsynced offset guard, zero cross-track offset/toast leaks & soft `OffsetGeneration` spring retargeting; predictable tray left-click, `OverlayHidden` vs `Paused` icons, status-aware tooltips, `"Follow All Media Players"`; live OS Reduced Motion (`WM_SETTINGCHANGE` + `150ms` clamp), `>= 0.90` Reduced Motion active opacity, and UI-first bounded `1.5s` Quit teardown |
| **MEDIUM** | **4** | **1** | `UX-07` (mostly fixed — only in-place `NativeMenu` mutation remains as Phase 5 polish), `UX-08` **RESOLVED**, `UX-09` **RESOLVED**; `UX-10` (transient style-switch/show HUD cue when idle/paused) deferred to Phase 5 |
| **LOW** | **3** | **3** | Light-taskbar dots, `WM_SETTINGCHANGE` refresh, `· Limit` toast tag, ripple reset, trailing `+`, `iTunes.exe`, and live hotkey re-registration **RESOLVED**; minor Phase 5/6 polish notes retained (`CanAdjustTimingOffset` hidden/fullscreen guard, `<ApplicationIcon>` + multi-size `.ico`, all-displays-disabled tray notice) |

---

## 1. Milestone 1A User Journey Scorecard

| # | User Journey | Status | Key Evidence & Primary Friction |
|---|---|---|---|
| 1 | **First Launch & Zero-Player Onboarding** | **FAIL** | Silent launch disappears into Windows `^` tray overflow; `_trayIcon.Menu` is `null` until GSMTC finishes (up to 14s backoff or forever on error); `--demo` & Tray Demo toggle fail silently if `LyricsVisible == false`; `--demo` arg dropped on second launch; duplicate idle header rows |
| 2 | **Spotify / Apple Music Detection & Recovery** | **FAIL** | `PlainOnly` lyrics freeze forever on Line 1 in `Codestyle`/`Visual Stack` (blank in `Fisheye`); every track change flashes false amber error dot + `"No synced lyrics found"` during fetch; no Retry action when offline recovery completes; `Instrumental` wrongly shows amber Error dot; long outros freeze final line; no Milestone 1A toggle for Spotify Web / YouTube Music / VLC (`UniversalPlayerMode`) |
| 3 | **Timing Offset Ergonomics (`−1.0s..+1.0s`)** | **PARTIAL / FAIL** | **Sign convention is 100% consistent** (`−` = Earlier, `+` = Later, `Position - Offset`, `U+2212`) across Core, Tray, Hotkeys, and `1.2s` HUD toast — **blocked by** unguarded `Ctrl+Shift+[`/`]` hotkeys firing over empty/unsynced desktops, stale previous-track offset & toast leaking across track switches, and hard spring resets (`ResetState()`) breaking velocity-conserving retargeting |
| 4 | **System Tray Menu Ergonomics** | **FAIL** | Clean zero-emoji `NativeMenu` & clean `IsVisible = false` Quit disposal — **blocked by** unpredictable 3-way overloaded left-click (silently cycles styles), wrong slash/amber icon mappings, status-blind hover tooltip, full menu rebuild + cross-thread `_lastState` race, non-native shortcut string, and blocking UI-thread `.GetAwaiter().GetResult()` during Quit |
| 5 | **Second-Instance & Hotkey Conflict UX** | **FAIL** | `<20ms` mutex + `EventWaitHandle` + abandoned-mutex recovery **PASS**; **blocked by** `_ = manager.RegisterAll(...)` throwing away all `ERROR_HOTKEY_ALREADY_REGISTERED` conflicts while tray menu keeps advertising dead shortcuts, and second-instance/`Ctrl+Shift+G` doing nothing whenever a track is paused/no-lyrics/error or Demo Mode is active |
| 6 | **Zero-Interruption Desktop Behavior** | **PASS WITH NOTES** | `OverlayWindow` (`WindowState.Normal`, `WorkingArea`, `WS_EX_LAYERED \| TRANSPARENT \| TOOLWINDOW \| NOACTIVATE`, `WS_EX_NOREDIRECTIONBITMAP` guard, DWM strips, `QUNS_BUSY`/`D3DFullScreen` auto-hide to 0% GPU) is strong — **needs fixes** for startup `_hiddenAnchorWindow` `Show()/Hide()` flash, defensive `~WS_EX_APPWINDOW` strip, frozen `BoundsConfig` closure, and mixed-DPI `Show()` ordering |
| 7 | **Reduced Motion & Accessibility** | **FAIL** | Native Win32 `NativeMenu` (Narrator/NVDA/High-Contrast safe) & immediate shader/blink/ripple shutdown in all 3 styles **PASS** — **blocked by** OS `SPI_GETCLIENTAREAANIMATION` being ignored by tray checkbox & palette morphs, un-clamped mid-flight `650ms` morph on tray toggle, `0.45` onset opacity dip below previous word (`0.54`/`0.56`), and non-native accelerator string in `Show Lyrics` |

---

## 2. Detailed UX Findings & Concrete Fixes

### CRITICAL

#### `UX-01` — `Visual Stack` & Demo Mode Semantic Comprehension Failure (12/12 `Neon Horizon` Icons Wrong + Unreadable White-Page Badges)
- **Journeys Impacted:** Journey 1 (First Launch `--demo`), Journey 2 (Live Playback), Journey 7 (Legibility Accessibility)
- **Locations:**
  - `src/LyricPulse.Core/Resources/StarterLexicon.tsv:3-189`
  - `src/LyricPulse.Rendering/Styles/StyleLayoutHelper.cs:116-158`
  - `src/LyricPulse.Rendering/Styles/VisualStackStyleRenderer.cs:442-497`
- **User Impact:**
  - During the CEO Milestone 1A demo (`LyricPulse.App.exe --demo` → switch to `Visual Stack`), every single signature keyword displays a completely unrelated icon (verified against the bundled `FluentSystemIcons-Filled.ttf` SFNT `cmap`/`post` tables in `UI_AUDIT.md`): `"star"` shows Speaker Settings, `"fire"` shows a Flag, `"heart"` shows a VR Headset, `"flight"` shows a Plus Circle, `"time"` shows Clipboard Search, `"light"` shows a Lasso, and `"dream"` shows Slide Layout (179/180 lexicon rows mismatched).
  - Furthermore, `StyleLayoutHelper.DrawVisualGlyph` bypasses `LegibilityPipeline` (zero shadow, zero optical stroke halo), making the icons virtually invisible (`1.04:1–2.72:1` contrast) over bright white browser/document windows.
  - A user auditioning `Visual Stack` immediately perceives LyricPulse's flagship "Word Pictures" differentiator as broken rather than magical.
- **Concrete Fix:**
  1. Regenerate column 3 (`codepoint_hex`) of `StarterLexicon.tsv` directly from the bundled `FluentSystemIcons-Filled.ttf` `cmap` + `post` tables (replacing the 6 icon names absent from the TTF) and enforce 100% glyph-name alignment via a unit test.
  2. Route Fluent icon rendering in `StyleLayoutHelper.DrawVisualGlyph` through `LegibilityPipeline.DrawLegibleText(..., includeStrokeHalo: true)` and give the `VisualStack` badge a dark translucent backing (`#0B0C16 @ 64%`) so icon-word pairs stay crisp over pure `#FFFFFF` apps.

---

### HIGH

#### `UX-02` — Silent First Launch, Unusable Initial Tray Menu (`Menu == null`), and Broken `--demo` / Second-Instance Feedback
- **Journeys Impacted:** Journey 1 (First Launch & Onboarding), Journey 5 (Second-Instance Launch)
- **Locations:**
  - `src/LyricPulse.App/Tray/TrayController.cs:35-42, 148-154`
  - `src/LyricPulse.App/App.axaml.cs:74, 91-130, 233-235, 244-270`
  - `src/LyricPulse.App/Program.cs:36-40`
  - `src/LyricPulse.Core/Media/WindowsGsmtcSessionProvider.cs:21-26, 82, 196-223`
  - `src/LyricPulse.Core/Runtime/OverlayRuntimeController.cs:208-236`
- **User Impact:**
  1. **Dead Tray Menu During Startup (or Permanently on Error):** `TrayController` (`TrayController.cs:35-41`) leaves `_trayIcon.Menu` as `null` until `StartRuntimeAsync()` finishes awaiting `WindowsGsmtcSessionProvider.StartAsync()`. If GSMTC initialization hits transient `COMException`/`UnauthorizedAccessException`, `InitializeManagerWithBackoffAsync` waits `1s + 3s + 10s = 14 seconds` before returning — leaving right-click completely dead (cannot even Quit or start Demo Mode) and global hotkeys unregistered! Worse, `StartRuntimeAsync()` (`App.axaml.cs:126-129`) only catches `OperationCanceledException`; any unexpected startup error leaves `_trayIcon.Menu == null` permanently (zombie tray icon requiring Task Manager).
  2. **Invisible First Launch with No Music Playing:** Double-clicking `LyricPulse.App.exe` opens no window and emits no desktop confirmation. Because Windows 10/11 hides new tray icons inside the `^` overflow chevron by default, a first-time user / CEO sees nothing happen at all and naturally wonders whether the app crashed or failed to start.
  3. **`--demo` & Tray Demo Mode Fail Silently When Lyrics Were Previously Hidden:** Neither `App.StartRuntimeAsync` (`App.axaml.cs:109-112`) nor `OverlayRuntimeController.SetDemoModeAsync(true)` (`OverlayRuntimeController.cs:208-236`) ensures `Settings.LyricsVisible = true`. If `LyricsVisible` was left `false` in `settings.json` (or unchecked in the tray), launching `LyricPulse.App.exe --demo` or checking `"Demo Mode (Neon Horizon)"` displays **nothing on screen** while the tray claims Demo Mode is active.
  4. **Silent No-Op on Second Launch / `Ctrl+Shift+G` & Dropped `--demo` Arg:** `HandleSecondInstanceActivationAsync` (`App.axaml.cs:253-264`) only reacts if `!LyricsVisible` or `(!IsDemoMode && Track is null)`. If Spotify is paused (lyrics auto-hid after 8s), or on a track with no lyrics/error, or Demo Mode is already active, double-clicking `LyricPulse.App.exe` or pressing `Ctrl+Shift+G` produces **zero visible feedback**. Likewise, running `LyricPulse.App.exe --demo` while an instance is already running drops `--demo` at `Program.cs:36-40`.
- **Concrete Fix:**
  1. Populate `_trayIcon.Menu` synchronously inside `TrayController`'s constructor from `_runtime.SampleFrame()` (so right-click and `Quit LyricPulse` work within `< 10ms` regardless of GSMTC timing), register global hotkeys before awaiting GSMTC backoff, and log + recover cleanly from any non-cancellation exception in `StartRuntimeAsync()`.
  2. In `OverlayRuntimeController.SetDemoModeAsync(true)`, automatically restore `LyricsVisible = true` (or update settings) so explicitly activating Demo Mode always produces visible lyrics.
  3. Add a lightweight transient HUD status banner (e.g., `2.0s` tabular-mono pill at the overlay anchor, non-activating) shown on: (a) Milestone 1A first launch when idle (`"LyricPulse ready in System Tray · Press Ctrl+Shift+G for Demo"`), and (b) every second-instance activation / `Ctrl+Shift+G` when no `GalleryWindow` exists yet.
  4. Add a second named auto-reset event (`Local\LyricPulse.StartDemo`) signaled when a second launch passes `--demo`, so `LyricPulse.App.exe --demo` reliably activates Demo Mode on an already-running instance.

#### `UX-03` — Broken Lyrics State Handling: `PlainOnly` Freezes Forever on Line 1, Every Track Change Flashes False Error, Zero Offline Retry, Wrong `Instrumental` Warning, & Frozen Outros
- **Journeys Impacted:** Journey 2 (Spotify / Apple Music Detection & Recovery)
- **Locations:**
  - `src/LyricPulse.Rendering/Styles/PlaybackLifecycleChoreographer.cs:119-123`
  - `src/LyricPulse.Core/Runtime/ActiveLyricSlice.cs:42-54, 158-172`
  - `src/LyricPulse.Core/Lyrics/LyricsParser.cs:94-123`
  - `src/LyricPulse.Core/Models/LyricsModels.cs:24-31`
  - `src/LyricPulse.Core/Runtime/OverlayRuntimeController.cs:367-391, 420-478`
  - `src/LyricPulse.App/Tray/TrayMenuState.cs:222-231, 259`
  - `src/LyricPulse.App/Tray/TrayController.cs:87-172`
- **User Impact:**
  1. **`PlainOnly` (Unsynced) Lyrics Permanently Freeze Line 1 Across the Desktop:** `PlaybackLifecycleChoreographer.cs:121` includes `LyricsStatus.PlainOnly` in `hasRenderableLyrics`, while `ActiveLyricSlice.Resolve` (`ActiveLyricSlice.cs:42-54`) pins `PlainUnsynced` documents permanently to `Lines[0]` (`Words = Empty`). As a result, when a song only has plain unsynced lyrics:
     - `Codestyle` (`CodestyleStyleRenderer.cs:266-272`) and `Visual Stack` (`VisualStackStyleRenderer.cs:240-245`) **freeze Line 1 on the user's desktop for the entire 3–5 minute song**, looking completely crashed!
     - `Fisheye` (`FisheyeStyleRenderer.cs:49-53`) renders blank because `Words` is empty — creating bizarre cross-style inconsistency.
     - Directly violates `MOTION_AND_STYLES.md:644` (*"No Synced Lyrics Found → Fade overlay cleanly to transparent (0% alpha)"*).
  2. **False Amber Warning Dot + `"No synced lyrics found"` on Every Track Change:** `LyricsStatus` has no `Loading` / `Fetching` member. `OverlayRuntimeController.OnMediaTrackChanged` (`line 382`) sets `LiveLyricsStatus = LyricsStatus.NotFound` immediately while LRCLIB/NetEase are fetching asynchronously, causing `TrayMenuState` (`lines 228, 259`) to flash the amber `Error` icon and `"Spotify · No synced lyrics found"` for 1–10 seconds on **every song transition**.
  3. **Zero Offline / Transient Error Recovery Action:** When offline or when LRCLIB/NetEase time out (`LyricsStatus.Error`), `TrayMenuState` shows `"Spotify · Lyrics lookup error"`, but `TrayController` offers **no `"Retry Lyrics Fetch"` action**. Once Wi-Fi reconnects, lyrics never reload for the current track because `MediaSessionCoordinator` only fires `TrackChanged` when `CacheKey` changes — forcing the user to skip songs or restart the app.
  4. **Legitimate `Instrumental` Tracks Flagged as Errors:** `TrayMenuState.ResolveIconState` (`line 259`) maps every non-synced playing track — including `LyricsStatus.Instrumental` — to `TrayIconVisualState.Error` (amber warning dot), causing constant false-alarm warnings on lo-fi, classical, and soundtrack albums.
  5. **Final Lyric Line Frozen Forever During Instrumental Outros:** `ActiveLyricSlice.DetermineInstrumentalGap` (`lines 165-168`) unconditionally returns `false` when `next is null`. During a 30–60s guitar/synth outro after the final vocal line finishes, the last word remains stuck on the desktop at 100% opacity until the song ends instead of lingering `0.9s` and fading out cleanly (`MOTION_AND_STYLES.md:642`).
- **Concrete Fix:**
  1. Restrict `PlaybackLifecycleChoreographer.cs:121` to `snapshot.LyricsStatus == LyricsStatus.Synced` so `PlainOnly` fades silently to `0%` transparent across all styles (while the tray reports `"Spotify · Unsynced lyrics only"`).
  2. Add `LyricsStatus.Loading` (or `IsFetchingLyrics` on `OverlayPlaybackSnapshot`), set it in `OnMediaTrackChanged` when `track is not null`, display `"Searching for lyrics…"` in `TrayMenuState`, and show the neutral `Idle` monochrome icon (not Amber) while fetching.
  3. Add `OverlayRuntimeController.RetryCurrentTrackLyricsAsync()` (clearing in-flight/cache and re-running `BeginTrackLoad`) and surface a conditional `"Retry Lyrics Fetch"` `NativeMenuItem` in `TrayController` whenever `LyricsStatus is LyricsStatus.Error or LyricsStatus.NotFound`.
  4. Map `LyricsStatus.Instrumental` to `TrayIconVisualState.Idle` (clean monochrome icon) rather than `Error`.
  5. In `ActiveLyricSlice.DetermineInstrumentalGap`, when `next is null && current.EndTime > current.StartTime && pos >= current.EndTime + PostLineLingerDuration`, return `true` (and suppress 3-dot breathing indicator after final line via an `IsPostSongOutro` flag) so the final lyric fades out gracefully into instrumental outros.

#### `UX-04` — Global Hotkey Conflicts Silently Discarded + Unguarded Offset Hotkeys, Track-Switch Offset Leak, and Non-Smooth Spring Resets
- **Journeys Impacted:** Journey 3 (Timing Offset Ergonomics), Journey 5 (Hotkey Conflict UX)
- **Locations:**
  - `src/LyricPulse.App/App.axaml.cs:227-232, 272-286`
  - `src/LyricPulse.App/Platform/GlobalHotkeyManager.cs:77-92, 221-224`
  - `src/LyricPulse.Core/Runtime/OverlayRuntimeController.cs:169-203, 370-391, 426-463`
  - `src/LyricPulse.Rendering/Styles/PlaybackLifecycleChoreographer.cs:124-139`
  - `src/LyricPulse.Rendering/Styles/LyricStyleRouter.cs:69-77`
- **User Impact:**
  1. **Hotkey Conflicts Silently Thrown Away:** `GlobalHotkeyManager.RegisterAll` carefully captures `ERROR_HOTKEY_ALREADY_REGISTERED (1409)` and returns friendly per-action error messages, yet `App.RegisterGlobalHotkeys` (`App.axaml.cs:276`) explicitly discards the result (`_ = manager.RegisterAll(...)`). When common defaults (`Ctrl+Shift+L` in 1Password/ShareX, `Ctrl+Shift+S` in Edge/OBS, `Ctrl+Shift+G` in GeForce/Radeon) are already claimed by another app, LyricPulse gives **zero warning**, logs nothing, and continues advertising `Show Lyrics   (Ctrl+Shift+L)` in the tray menu as if it worked.
  2. **Offset Hotkeys Fire Over Empty / Unsynced Desktops:** Unlike `TrayMenuState` (`canNudge = hasSyncedLyrics`), `App.HandleHotkeyAsync` (`lines 227-232`) and `OverlayRuntimeController.NudgeTimingOffsetAsync` (`lines 169-185`) do not verify that synced lyrics are loaded or visible. Pressing `Ctrl+Shift+[` or `Ctrl+Shift+]` while idle or on an instrumental track mutates the offset, pops up an `"Offset: −0.5s (Earlier)"` HUD pill on an empty desktop (`PlaybackLifecycleChoreographer.cs:137-138`), and leaves `Timing Offset (−0.5s)` displayed in the tray with all reset buttons disabled!
  3. **Previous Track's Offset & Toast Leak Into Next Track:** `OnMediaTrackChanged` (`OverlayRuntimeController.cs:370-387`) never resets `TimingOffset` or clears `LastOffsetNudgeUtc`; the old song's offset remains visible until all three tasks in `Task.WhenAll(offsetTask, lyricsTask, paletteTask)` finish (`line 430`, up to 10s!), and any rapid skip within `1.2s` of a nudge flashes the wrong track's toast. Furthermore, `line 460` can overwrite a user nudge made during track loading.
  4. **Hard Spring Reset Breaks Smooth Retargeting:** Because `NudgeTimingOffsetAsync` bumps `SeekGeneration` (`line 180`), `LyricStyleRouter.cs:71-77` calls `renderer.ResetState()`, snapping all springs to rest instead of smoothly retargeting while conserving velocity (`MOTION_AND_STYLES.md:645`).
- **Concrete Fix:**
  1. Capture and store `IReadOnlyDictionary<HotkeyAction, string>` from `manager.RegisterAll(...)` on `App` / `TrayController`, log every conflict, suppress the conflicting gesture hint on `Show Lyrics` (replacing with a disabled status row or submenu `"Shortcut conflict: Ctrl+Shift+L in use"` in Milestone 1A so users immediately know why a hotkey isn't firing).
  2. Guard `NudgeTimingOffsetAsync` / `ResetTimingOffsetAsync` (or `HandleHotkeyAsync`) so nudges are ignored when `!IsLyricsVisible` or no synced lyrics are loaded (in `PlaybackLifecycleChoreographer.cs:137-138`, suppress the offset toast when `!snapshot.IsLyricsVisible || !hasSyncedLyrics`).
  3. Immediately reset `TimingOffset` (or load `offsetTask` before `lyricsTask` without blocking on HTTP) and clear `LastOffsetNudgeUtc = null` in `OnMediaTrackChanged` and `SetDemoModeAsync`; only apply the loaded saved offset if the user hasn't nudged since `generation` started.
  4. Separate `OffsetGeneration` from `SeekGeneration` (or skip `renderer.ResetState()` on small offset nudges) so active word/line springs retarget smoothly with conserved velocity.

#### `UX-05` — Unpredictable 3-Way Tray Left-Click, Misleading Paused/Fullscreen Icon & Tooltip States, and No Milestone 1A Universal Player Fallback Toggle
- **Journeys Impacted:** Journey 2 (Player Detection & Recovery), Journey 4 (System Tray Ergonomics), Journey 6 (Fullscreen Auto-Hide Clarity)
- **Locations:**
  - `src/LyricPulse.App/Tray/TrayController.cs:174-191`
  - `src/LyricPulse.App/Tray/TrayMenuState.cs:209-288`
  - `src/LyricPulse.Core/Models/AppSettings.cs:103, 127-129`
  - `src/LyricPulse.Core/Media/MediaSessionSelector.cs:165-169`
- **User Impact:**
  1. **Surprising Overloaded Tray Left-Click:** `OnTrayIconLeftClicked` (`TrayController.cs:174-191`) silently performs 3 different actions depending on state: (a) if hidden → shows lyrics, (b) if idle → starts Demo Mode, (c) if playing/demo → **cycles active lyric style** (`Fisheye → Codestyle → Visual Stack`). Clicking a tray icon expecting to open the app (standard Windows habit & `tray.md:40`) unexpectedly scrambles the user's chosen style with zero tooltip warning — and clicking again after unhiding doesn't re-hide (asymmetric toggle).
  2. **Paused & Fullscreen Icon Ambiguity:** `TrayMenuState.cs:254-257` maps `MediaPlaybackState.Paused` to `TrayIconVisualState.PausedOrHidden` (diagonal slash), violating `tray.md:24` which reserves the slash exclusively for **Overlay Hidden** (lyrics toggled off). Meanwhile, when `IsFullscreenHidden == true` (`line 239`), the icon turns slashed while `✓ Show Lyrics` remains checked and **neither the header nor the tooltip mentions fullscreen auto-hide** — leaving multi-monitor users baffled why lyrics vanished when a video/game is fullscreen on a second screen. Similarly, `MediaPlaybackState.Changing` (buffering) shows the green `Playing` dot while lyrics are fading out (`line 259`).
  3. **Status-Blind Hover Tooltip:** When the tray shows an amber dot (No lyrics / PlainOnly / Offline Error) or slashed icon (Fullscreen Hidden), hovering over the icon (`TrayMenuState.cs:262-288`) still says `"LyricPulse — Title · Artist (Fisheye)"` — giving zero explanation for the warning dot!
  4. **Milestone 1A Brick Wall for Web / Other Audio Players:** `UniversalPlayerMode` defaults to `false` (`SupportedPlayersOnly`, blocking Spotify Web Player, YouTube Music, Tidal, MusicBee, Foobar2000, VLC). Because Phase 5 `SettingsView` is not built yet, a user testing Milestone 1A with anything other than native Spotify Desktop or Apple Music Desktop has **no tray toggle** to enable Universal Player Mode and is stuck forever at `"Waiting for Spotify / Apple Music..."`.
- **Concrete Fix:**
  1. Map `MediaPlaybackState.Paused` and `MediaPlaybackState.Changing` to `TrayIconVisualState.Idle` (clean unslashed waveform) and reserve the diagonal slash strictly for `!LyricsVisible || IsFullscreenHidden`.
  2. Enrich `TrayMenuState.BuildTooltip` and `BuildHeaderSecondary` to explicitly reflect every state (`"Paused"`, `"Buffering…"`, `"Searching for lyrics…"`, `"Instrumental"`, `"No synced lyrics"`, `"Offline / lookup error"`, `"Paused for fullscreen app"`, and idle hint `"Ready · Left-click for Demo, right-click for menu"`).
  3. Make Milestone 1A left-click predictable and explicitly documented in the tooltip (e.g., idle → toggle Demo Mode; hidden → show lyrics; playing → toggle Show/Hide Lyrics cleanly rather than silently mutating the user's selected style, leaving style switching to `Ctrl+Shift+S` and the tray radio menu until Phase 5 opens `GalleryWindow`).
  4. Expose a temporary Milestone 1A `"Detect All Players (Universal)"` CheckBox toggle in the tray menu (or sub-item) so testers using Spotify Web Player, YouTube Music, Tidal, or VLC can verify live lyrics without hand-editing `settings.json`.

#### `UX-06` — Reduced Motion OS Desync, Un-Calmed Mid-Flight Palette Morphs, Inverse Onset Hierarchy Dip, and Blocking Quit Shutdown Race
- **Journeys Impacted:** Journey 4 (Clean Quit), Journey 7 (Reduced Motion & Accessibility)
- **Locations:**
  - `src/LyricPulse.App/App.axaml.cs:59, 317-369`
  - `src/LyricPulse.App/Tray/TrayMenuState.cs:91`
  - `src/LyricPulse.Core/Runtime/OverlayRuntimeController.cs:283-299, 602-607`
  - `src/LyricPulse.Rendering/Styles/FisheyeStyleRenderer.cs:311, 327, 332`
  - `src/LyricPulse.Rendering/Styles/VisualStackStyleRenderer.cs:337-338`
- **User Impact:**
  1. **OS Reduced Motion Desync & Palette Morph Leak:** `_systemReducedMotion` (`App.axaml.cs:59`) is read once at launch and never refreshed on `WM_SETTINGCHANGE`. `TrayMenuState.cs:91` binds `IsReducedMotion` only to `settings.ReducedMotion`, so a user with Windows "Animation effects" OFF sees `Reduced Motion` unchecked and cannot tell why animations are disabled (nor does unchecking it change anything). Worse, `OverlayRuntimeController.CreatePaletteMorph` (`line 605`) never checks `_systemReducedMotion` (running full `650ms` morphs under OS Reduced Motion), and toggling `Reduced Motion` ON from the tray does not shorten an already-running `650ms` palette morph down to `150ms`.
  2. **Brief Inverted Visual Hierarchy Under Reduced Motion:** In `Fisheye` (`line 332`) and `Visual Stack` (`line 337`), a newly active word fades in from `0.45` opacity — **dimmer** than the adjacent previous word (`0.54` in Fisheye, `0.56` in Visual Stack) — causing a momentary focus-drop flicker on every single word transition under Reduced Motion.
  3. **Quit UI-Thread Hang Risk & Teardown Race:** `App.CleanupDesktopHost()` (`App.axaml.cs:317-353`) runs synchronously on the UI thread, calling `_runtime.StopAsync().GetAwaiter().GetResult()` with **no timeout** before closing `_overlayCoordinator`. If Windows GSMTC COM or a thumbnail read holds `_refreshLock`, clicking `Quit LyricPulse` freezes the app (and delays `_trayController.Dispose()` at line 348, risking a ghost tray icon if force-closed), while live overlay canvases continue calling `_runtime.SampleFrame()` during runtime teardown.
- **Concrete Fix:**
  1. Pass a live `Func<bool> isSystemReducedMotion` (refreshed on `WM_SETTINGCHANGE` on `_hiddenAnchorWindow`) into `OverlayRuntimeController` so `CreatePaletteMorph` honors `settings.ReducedMotion || isSystemReducedMotion()`, clamp any in-flight `PaletteMorph` remaining duration to `<= 150ms` inside `UpdateSettingsAsync` when Reduced Motion turns ON, and reflect OS-enforced Reduced Motion clearly in the tray (`"Reduced Motion (Synced with Windows)"` disabled+checked or clearly captioned).
  2. Raise Reduced Motion active-word onset floor from `0.45f` to `0.68f → 1.00f` (with smooth color shift `AccentColor3 → AccentColor1`) in `FisheyeStyleRenderer` and `VisualStackStyleRenderer` so the active word always remains the strongest focal point.
  3. Reorder `CleanupDesktopHost()`: (a) hide & dispose `_trayController` and `_overlayCoordinator` FIRST (instant visual exit + zero ghost tray icon + stops `SampleFrame()` calls), (b) unregister hotkeys and stop fullscreen monitor, (c) bound `_runtime.StopAsync()` / `DisposeAsync()` with a `1.5s` `CancellationTokenSource` timeout off-thread so Quit never hangs.

---

### MEDIUM

#### `UX-07` — System Tray Menu Ergonomics, `tray.md` Copy Fidelity, Menu Rebuild Flicker, and Screen-Reader Accelerator Hygiene
- **Journeys Impacted:** Journey 4 (System Tray Menu Ergonomics), Journey 7 (Screen-Reader / Keyboard Accessibility)
- **Locations:**
  - `src/LyricPulse.App/Tray/TrayMenuState.cs:169-184, 195, 202-206, 218`
  - `src/LyricPulse.App/Tray/TrayController.cs:44-60, 84, 97, 123-139`
- **User Impact:**
  - **Duplicate Idle Copy & Spec Drifts:** Idle headers read `"Waiting for Spotify / Apple Music..."` (Row 1) and `"Waiting for Spotify or Apple Music"` (Row 2) instead of `tray.md` §4.1's `"No media playing"` / `"Waiting for Spotify or Apple Music"`. Playing Row 1 omits `"Playing: "` (`tray.md:76`), and Paused Row 1 creates a double em-dash (`"Paused — Title — Artist"`) instead of `"Paused: Title — Artist"`.
  - **Full `NativeMenu` Rebuild & Cross-Thread Race:** `TrayController.cs:84` replaces `_trayIcon.Menu = BuildNativeMenu(state)` on every state change instead of updating existing `NativeMenuItem` properties in place (`tray.md:119`), risking menu flicker/drop if a song changes while the menu is open. Additionally, `App.OnRuntimeStateChanged` (`App.axaml.cs:167`) and `ApplySnapshotToHost` (`App.axaml.cs:178`) invoke `ApplySnapshot` from two threads against unsynchronized `_lastState` (`TrayController.cs:53-59`).
  - **Shortcut Accessibility & Discoverability:** `TrayController.cs:97` bakes `({state.ShowLyricsGestureHint})` into the label string (causing NVDA/Narrator to read `"left paren ... right paren"` and misaligning the Windows accelerator column) rather than setting `NativeMenuItem.Gesture`. Meanwhile, `Ctrl+Shift+S`, `Ctrl+Shift+[`, and `Ctrl+Shift+]` have zero discoverability anywhere in the Milestone 1A tray menu.
  - **Surrogate-Pair Truncation Bug:** `TruncateMiddle` (`TrayMenuState.cs:169-184`) slices UTF-16 `char` indices directly, risking lone surrogates (`□`) on track titles containing emojis or supplementary Unicode characters.
- **Concrete Fix:**
  1. Match `tray.md` §4.1 copy (`"No media playing"`, `"Playing: ..."`, `"Paused: ..."`).
  2. Mutate persistent `NativeMenuItem` instances strictly on `Dispatcher.UIThread` (removing the duplicate background call at `App.axaml.cs:167`).
  3. Set `NativeMenuItem.Gesture` via `KeyGesture.TryParse` on `Show Lyrics` (and on `−0.5s` / `+0.5s` offset items when registered without conflict).
  4. Make `TruncateMiddle` and `TruncateEnd` surrogate-pair/grapheme-safe (`StringInfo` or surrogate boundary adjustment).

#### `UX-08` — Live Reading Ergonomics: `Codestyle` Mid-Line Vertical Jump, Offset Toast Contrast/Collision, Pause-Dim Drift, and Unwired Non-Latin Fallback
- **Journeys Impacted:** Journey 2 (Live Playback), Journey 3 (Timing Offset HUD), Journey 7 (Legibility)
- **Locations:**
  - `src/LyricPulse.Rendering/Styles/CodestyleStyleRenderer.cs:125-136, 310-311`
  - `src/LyricPulse.Rendering/Styles/PlaybackLifecycleChoreographer.cs:42, 274-297, 317-348`
  - `src/LyricPulse.Rendering/Legibility/LegibilityPipeline.cs:144-203`
- **User Impact:**
  - **Jarring Mid-Line Jump (`Codestyle`):** At `68%` line progress (`CodestyleStyleRenderer.cs:126`), the upcoming ghost line switches `visibleRows` from `2` to `3` and re-centers `cardRect.Top`, snapping the currently typing lyric line upward by `~21 DIP` right as the user is reading it. Long previous lines are also clipped mid-word by `ClipRoundRect`, and `word.Text[..revealedChars]` can split UTF-16 surrogate pairs.
  - **Offset Toast Contrast & Positioning:** `DrawOffsetToast` (`lines 329-348`) draws `packet.Palette.AccentColor1` on `#0B0C16` with `includeStrokeHalo: false` (unreadable if user sets a dark custom `ManualAccent1`, and inconsistent with `MASTER.md` amber offset tokens `#FBBF24` / `#F8FAFC`). At `FontScale >= 1.3` or in `Visual Stack`, `preferredY = anchorY + BaseFontSizeDip * 2.15f` can overlap the bottom lyric row.
  - **Pause Dim Spec Drift & Instrumental Dots on White Pages:** `PauseDimOpacity = 0.45f` (`line 42`) dims darker than the locked `0.62×` (`MOTION_AND_STYLES.md:641`, `STY-04`). Bare instrumental dots (`lines 274-297`) have no contact shadow/outline (`1.57:1` over `#FFFFFF`).
  - **International Music Tofu Boxes:** Because `FontAssetRegistry.ResolveTypefaceForText` and `TextShaperService.ShapeToBlob` are unwired from `LegibilityPipeline`, any K-Pop, J-Pop, C-Pop, Hindi, or Arabic song from Spotify/Apple Music renders unreadable tofu boxes (`□`).
- **Concrete Fix:**
  1. Anchor `Codestyle`'s active row stably (or reserve a steady 3-row height), measure all visible lines for card width with clean ellipsis trimming, and slice typing reveal by grapheme clusters (`StringInfo`).
  2. Render `OffsetToast` using high-contrast fixed HUD tokens (`#FBBF24` for non-zero offset, `#F8FAFC` for reset) and compute vertical clearance from the active style's safe bounds; restore `PauseDimOpacity = 0.62f` and add subtle dark shadows behind instrumental dots.
  3. Wire cached HarfBuzz `SKTextBlob` + OS multi-script fallback typefaces into `LegibilityPipeline.DrawLegibleText`.

#### `UX-09` — Zero-Interruption Desktop & Multi-Monitor Edge Cases (`_hiddenAnchorWindow` Flash, `WS_EX_APPWINDOW`, Frozen `BoundsConfig`, Mixed-DPI Ordering)
- **Journeys Impacted:** Journey 6 (Zero-Interruption Desktop & Multi-Monitor Behavior)
- **Locations:**
  - `src/LyricPulse.App/App.axaml.cs:69-72, 132-158, 288-294, 296-306`
  - `src/LyricPulse.App/Platform/Win32OverlayInterop.cs:21-26`
  - `src/LyricPulse.App/Platform/MultiMonitorOverlayCoordinator.cs:126-146, 189-197`
- **User Impact:**
  - `_hiddenAnchorWindow` (`App.axaml.cs:69-72, 296-306`) briefly calls `Show()` then `Hide()` without `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, `Focusable = false`, `IsHitTestVisible = false`, or off-screen manual coordinates — risking a momentary Alt+Tab/task-switcher flash on launch.
  - `Win32OverlayInterop.ComputeDesiredExStyle` (`line 25`) ORs `WS_EX_TOOLWINDOW` without explicitly clearing `WS_EX_APPWINDOW` (`& ~0x00040000`).
  - `App.CreateOverlayCanvas` (`line 155`) permanently captures startup `placement.BoundsConfig`, so live changes to `AnchorZone`, `SafeMarginPx`, or `PerDisplayBounds` never move existing overlays (and `ShouldReconcileOverlayLayout` only compares dictionary `.Count`).
  - `MultiMonitorOverlayCoordinator.cs:138-144` applies placement *before* `Show()` on newly attached mixed-DPI secondary monitors (before the HWND switches to the target monitor's DPI context), and `ApplyVisibilityState` (`lines 191-196`) calls `SetOverlayPaused(false)` before `overlay.Show()` when resuming from fullscreen.
- **Concrete Fix:**
  1. Configure `_hiddenAnchorWindow` with `WindowStartupLocation = Manual`, `Position = new PixelPoint(-32000, -32000)`, `Focusable = false`, `IsHitTestVisible = false`, and apply `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` before `Show()`/`Hide()`.
  2. Mask `& ~Win32Native.WS_EX_APPWINDOW` in `Win32OverlayInterop.ComputeDesiredExStyle`.
  3. Store the active `OverlayBoundsConfig` on `OverlayWindow` (updated in `ApplyPlacement` and read via `Volatile.Read` in `packetFactory`), compare `PerDisplayBounds` values in `ShouldReconcileOverlayLayout`, re-apply placement after `Show()` on new monitors, and call `overlay.Show()` + `ApplyWin32ClickThrough()` before `SetOverlayPaused(false)`.

#### `UX-10` — Zero Visual Confirmation When Cycling Styles (`Ctrl+Shift+S`) or Unhiding Lyrics (`Ctrl+Shift+L`) Outside Active Vocals
- **Journeys Impacted:** Journey 4 (Tray & Hotkey Feedback), Journey 5 (Hotkey UX)
- **Locations:**
  - `src/LyricPulse.App/App.axaml.cs:221-226`
  - `src/LyricPulse.Rendering/Styles/PlaybackLifecycleChoreographer.cs:299-348`
- **User Impact:**
  - Pressing `Ctrl+Shift+S` during an instrumental gap, pause, no-lyrics song, or idle state silently changes `ActiveStyle` with zero visual confirmation — users tap it repeatedly wondering if the hotkey is broken.
  - Pressing `Ctrl+Shift+L` to show/hide lyrics when idle or paused gives no subtle transient cue (`"Lyrics: Shown"` / `"Lyrics: Hidden"` or `"Style: Codestyle"`).
- **Concrete Fix:**
  - Reuse `PlaybackLifecycleChoreographer`'s lightweight `1.2s` tabular-mono HUD pill (already designed to stay non-intrusive and click-through) to briefly confirm `"Style: Codestyle"` on `Ctrl+Shift+S` (and `"Lyrics shown"` when toggling visibility ON via hotkey).

---

### LOW

#### `UX-11` — Missing Windows `<ApplicationIcon>` & Stale/Single-Size Tray Icon Polarity
- **Journeys Impacted:** Journey 1 (First Launch Shell Presence), Journey 4 (System Tray Polish)
- **Locations:**
  - `src/LyricPulse.App/LyricPulse.App.csproj:1-10`
  - `src/LyricPulse.App/Tray/TrayIconFactory.cs:28-29, 55-59`
- **User Impact:** `LyricPulse.App.exe` displays the generic blank Windows executable icon in File Explorer, Task Manager, and Windows System Tray Settings (`Other system tray icons`). Additionally, `TrayIconFactory` emits only a single `32×32` PNG (slightly soft at 100%/125% DPI), uses low-contrast Light-taskbar dot colors (`#22C55E`/`#F59E0B`), and does not refresh when switching Windows Light/Dark taskbar themes at runtime.
- **Concrete Fix:** Bundle a multi-size `LyricPulse.ico` (`16/20/24/32/48px`) as `<ApplicationIcon>`, use `MASTER.md` Light-theme status dots (`#15803D` / `#B45309`) on light taskbars, and refresh `_trayIcon.Icon` on `WM_SETTINGCHANGE` (`ImmersiveColorSet`).

#### `UX-12` — Minor Ergonomic & Edge-Case Polish
- **Locations:**
  - `src/LyricPulse.Rendering/Styles/PlaybackLifecycleChoreographer.cs:260-272` (`±15.0s` clamp limit gives no `"(Limit)"` hint in toast)
  - `src/LyricPulse.Rendering/Styles/VisualStackStyleRenderer.cs:107-114, 457` (toggling `ReducedMotion` OFF mid-row can resume an interrupted ripple — set `_rippleElapsedSec = RippleDurationSeconds` inside the Reduced Motion branch)
  - `src/LyricPulse.App/Platform/HotkeyGestureParser.cs:83, 153-154` (cannot parse trailing `+` key like `"Ctrl+Shift++"`; US-keyboard `VK_OEM_4`/`VK_OEM_6` `[`/`]` defaults will need clean Phase 5 remapping for ISO keyboards)
  - `src/LyricPulse.Core/Media/PlayerClassifier.cs:12-25, 141-143` (classic desktop `iTunes.exe` is not recognized under `AppleMusic` or `DedicatedAudioTokens`)
  - `src/LyricPulse.App/Tray/TrayController.cs:156-163` (`High-Contrast Scrim` is implemented in `LegibilityPipeline` but not yet toggleable from the Milestone 1A tray for low-vision testers before Phase 5 Settings)
- **Concrete Fix:** Address opportunistically during Milestone 1A remediation or Phase 5 polish.

#### `UX-13` — Phase 5 Readiness Bridges (Runtime Hotkey Re-Registration & All-Displays-Disabled Guardrail)
- **Locations:**
  - `src/LyricPulse.App/App.axaml.cs:122, 171-192`
  - `src/LyricPulse.App/Tray/TrayMenuState.cs:209-232`
- **User Impact:** `RegisterGlobalHotkeys` only runs once at startup (runtime hotkey edits won't rebind until restart), and if all connected monitors are listed in `DisabledDisplays` (`ActiveOverlayCount == 0`), the tray menu gives no warning that overlays are suppressed on every display.
- **Concrete Fix:** Re-register hotkeys in `ApplySnapshotToHost` whenever `settings.GlobalHotkeys` changes, and surface `"All displays disabled"` in the tray status/tooltip when `ActiveOverlayCount == 0`.

---

## 3. Verified UX Strengths (Preserve During Remediation)

1. **Timing Offset Sign Consistency:** `−1.0s / −0.5s` = Earlier and `+0.5s / +1.0s` = Later (`EffectiveLookupTime = Clamp(Position - Offset, 0, Duration)`) with proper Unicode minus `−` (`U+2212`) is **100% consistent** across `TimingOffsetService`, `TrayMenuState`, `GlobalHotkeyManager` (`App.HandleHotkeyAsync`), and `PlaybackLifecycleChoreographer.FormatOffsetToastLabel`.
2. **Zero-Emoji Native Tray Architecture:** Genuine Avalonia `TrayIcon` + `NativeMenu` with `ToggleType.CheckBox` / `ToggleType.Radio`, zero emojis in chrome, `"Show Lyrics"` stable CheckBox verb (never flipping Show/Hide), and clean `_trayIcon.IsVisible = false; _trayIcon.Dispose()` on Quit.
3. **Click-Through Overlay Foundations:** `OverlayWindow` enforces `WindowState.Normal` within `WorkingArea`, `ShowActivated = false`, `Focusable = false`, `IsHitTestVisible = false`, `WS_EX_LAYERED | TRANSPARENT | TOOLWINDOW | NOACTIVATE`, guarded `SetLayeredWindowAttributes` (`WS_EX_NOREDIRECTIONBITMAP`), DWM shadow/corner stripping, and `QUNS_BUSY`/`D3DFullScreen`/`PresentationMode` fullscreen auto-hide releasing GPU resources cleanly.
4. **Fast Single-Instance Guard & Clean `MOD_NOREPEAT` Hotkeys:** Pre-Avalonia mutex with `AbandonedMutexException` recovery + `<20ms` `EventWaitHandle` signaling, and anti-cheat-safe `RegisterHotKey` with `MOD_NOREPEAT` preventing runaway offset nudges.
5. **Immediate Reduced Motion Motion-Stop in Renderers:** All 3 MVP styles (`Fisheye`, `Codestyle`, `Visual Stack`) immediately bypass both SkSL shaders, stop spring travel/overshoot, stop cursor blinking, and stop instrumental dot pulsing when `packet.ReducedMotion == true`.

---

## 4. Recommended Remediation Priority Order (For Milestone 1A Re-Verification)

1. **Blocker 1 (`UX-01`):** Fix `StarterLexicon.tsv` codepoints against bundled `FluentSystemIcons-Filled.ttf` and route icon badges through `LegibilityPipeline`.
2. **Blocker 2 (`UX-02`):** Initialize `_trayIcon.Menu` synchronously on startup, ensure Demo Mode restores `LyricsVisible = true`, add a non-intrusive transient startup/second-instance HUD confirmation pill, and propagate `--demo` across single-instance handoff.
3. **Blocker 3 (`UX-03`):** Exclude `PlainOnly` from overlay rendering, add `LyricsStatus.Loading`, add `"Retry Lyrics Fetch"` tray recovery, stop flagging `Instrumental` as amber Error, and fade out final lines into long outros.
4. **Blocker 4 (`UX-04`):** Surface `RegisterAll` hotkey conflicts in tray/logs, guard offset hotkeys against idle/unsynced/hidden states, reset/isolate per-track offset & toast on track switches, and preserve spring velocity on offset nudges.
5. **Blocker 5 (`UX-05` & `UX-06`):** Simplify tray left-click, fix Paused/Fullscreen icon & status-aware tooltips, add temporary Milestone 1A Universal Player toggle, sync OS Reduced Motion live across tray + palette morphs + active-word onset opacity, and fix Quit teardown ordering with timeout.
6. **Polish (`UX-07`–`UX-10`):** Align `tray.md` headers & in-place `NativeMenu` updates with `NativeMenuItem.Gesture`, stabilize `Codestyle` card height & offset toast colors, wire HarfBuzz/OS script fallback, harden `_hiddenAnchorWindow` + `~WS_EX_APPWINDOW` + live `BoundsConfig`, and add hotkey style-switch HUD cue.

---

## Re-Verification Addendum (`c6420bb`)

**Re-Verified Commits:** `70876e0` (Core) + `c6420bb` (Rendering/App) on `master` (read-only audit of `src/`).  
**Final Milestone 1A Verdict:** **`PASS WITH NOTES`** — **0 CRITICAL**, **0 HIGH** blockers remaining. Approved for Milestone 1A CEO hands-on review.

### 1. CRITICAL & HIGH Remediation Verification Matrix

| ID | Severity | Status | Exact Code Verification (`70876e0` + `c6420bb`) |
|---|---|---|---|
| **`UX-01`** | CRITICAL | **VERIFIED FIXED** | Independent binary SFNT `cmap` (format 12) + `post` (v2.0) verification against bundled `FluentSystemIcons-Filled.ttf` confirms **180 / 180 exact glyph matches (0 mismatches)**, including all 12 `Neon Horizon` demo keywords (`StarterLexicon.tsv:3-14`) and Supplementary PUA-A (`> U+FFFF`) decoded cleanly via `Rune.DecodeFromUtf16` (`StyleLayoutHelper.cs:186-188, 235-251`). `VisualStackStyleRenderer.cs:25-26, 548-571` composites dark obsidian `#D90B0C16` (active) / `#B80B0C16` (inactive) backing and routes glyphs through `StyleLayoutHelper.DrawVisualGlyph` → `LegibilityPipeline.DrawLegibleText(..., includeStrokeHalo: true)` (`StyleLayoutHelper.cs:196-208`), yielding `>= 9.28:1` contrast over `#FFFFFF` even before halo/shadow; Emoji fallbacks use `0.92×` optical scale + contact shadow (`StyleLayoutHelper.cs:213-232`). |
| **`UX-02`** | HIGH | **VERIFIED FIXED** | `TrayController` (`TrayController.cs:45-56`) synchronously builds `initialState` and populates `_trayIcon.Menu` in the constructor before `StartRuntimeAsync()` awaits GSMTC; `App.axaml.cs:128-135, 183-186` registers hotkeys upfront and catches/logs non-cancellation startup exceptions. `OverlayRuntimeController.SetDemoModeAsync(true)` (`OverlayRuntimeController.cs:258-275`) automatically restores `LyricsVisible = true` whenever hidden. `Program.cs:36-37`, `SingleInstanceGuard.cs:46-48, 67-81, 101-130`, and `App.axaml.cs:146-148, 323-357` propagate `--demo` across single-instance wakeup and surface Demo Mode on second-instance activation whenever idle, paused, or unsynced. |
| **`UX-03`** | HIGH | **VERIFIED FIXED** | Added `LyricsStatus.Loading = 5` (`LyricsModels.cs:31`), emitted immediately on track switches (`OverlayRuntimeController.cs:506`) with neutral `Idle` tray icon and `"Fetching lyrics…"` copy (`TrayMenuState.cs:290, 324-327, 365`). `PlaybackLifecycleChoreographer.cs:123-128` strictly gates rendering to `LyricsStatus.Synced`, eliminating frozen Line 1 on `PlainOnly`. Added `LyricsOrchestrator.GetLyricsAsync(..., forceRefresh: true)` bypassing/purging negative disk cache (`LyricsOrchestrator.cs:42-56, 82-85`), `OverlayRuntimeController.RetryLyricsFetchAsync()` (`lines 228-253`), and `"Retry Lyrics Fetch"` in `TrayController.cs:220-222` (enabled for `Error`/`NotFound`). `Instrumental` maps to clean `Idle` icon (`TrayMenuState.cs:324-327`), and `ActiveLyricSlice.IsPostSongOutro` (`ActiveLyricSlice.cs:89-102, 200-217`) + `PlaybackLifecycleChoreographer.cs:182-185` fade out final lyrics cleanly into long outros without breaking the `36.0s` `Neon Horizon` loop. |
| **`UX-04`** | HIGH | **VERIFIED FIXED** | `App.RegisterGlobalHotkeys` (`App.axaml.cs:240-243, 359-393`) captures `RegisterAll` conflicts, logs each conflict, pushes them to `_trayController.SetHotkeyConflicts(conflicts)`, and re-registers dynamically when bindings change. `TrayMenuState.cs:62, 85-90, 374-394` & `TrayController.cs:158-168` strip dead shortcut hints from `Show Lyrics`, add a disabled conflict notice row, and append `[Shortcut conflict]` to tooltips. `OverlaySnapshotBuilder.CanAdjustTimingOffset` (`OverlaySnapshotBuilder.cs:31-35`) guards `NudgeTimingOffsetAsync`/`ResetTimingOffsetAsync` against idle/unsynced states; `PlaybackLifecycleChoreographer.cs:394` suppresses toast when hidden/unsynced. `OnMediaTrackChanged` (`OverlayRuntimeController.cs:509-511`) immediately clears `TimingOffset`, `HasUserNudgedInCurrentTrack`, and `LastOffsetNudgeUtc`, while `HydrateTrackOffsetAsync` (`lines 611-645`) hydrates saved per-track offsets without blocking on HTTP or clobbering user nudges. `LyricStyleRouter.cs:84-99` skips `renderer.ResetState()` when `OffsetGeneration` increments, conserving spring velocity across offset nudges. |
| **`UX-05`** | HIGH | **VERIFIED FIXED** | `TrayController.OnTrayIconLeftClicked` (`TrayController.cs:262-275`) is now predictable and non-destructive: starts Demo Mode when idle with no live player, otherwise cleanly toggles Show/Hide Lyrics (never surprise-cycling styles). `TrayIconVisualState.OverlayHidden` (`TrayIconFactory.cs:11, 114-126`) reserves the diagonal slash strictly for `!LyricsVisible || IsFullscreenHidden`, while `Paused` and `Changing` use the unslashed `Idle` mark (`TrayMenuState.cs:306-329`). Hover tooltips (`TrayMenuState.cs:332-382`) explicitly report `Paused for fullscreen app`, `Fetching lyrics`, `Instrumental`, `Offline / lyrics unavailable`, `No synced lyrics`, `Paused`, and idle click guidance (`<= 96` graphemes). Added `"Follow All Media Players"` CheckBox in `TrayController.cs:235-241` wired to `OverlayRuntimeController.ToggleUniversalPlayerModeAsync()` (`lines 152-153`). |
| **`UX-06`** | HIGH | **VERIFIED FIXED** | `App.axaml.cs:106, 115, 258-267` & `MultiMonitorOverlayCoordinator.cs:273-276` sync Windows `SPI_GETCLIENTAREAANIMATION` both at startup and live via `WM_SETTINGCHANGE`, pushing `SetOsReducedMotion` into `OverlayRuntimeController` (`lines 324-353`). Both `SetOsReducedMotion` and `ApplySettingsToState` (`lines 411-414`) clamp active `PaletteMorph` remaining duration to `<= 150ms` via `PaletteMorphState.ClampRemainingDuration` (`OverlayPlaybackSnapshot.cs:34-50`); `TrayMenuState.cs:96-98` & `TrayController.cs:243-250` show `"Reduced Motion (Windows setting)"` (checked, disabled) when OS-enforced. Active opacity under Reduced Motion is `0.90f..1.00f` across `FisheyeStyleRenderer.cs:384`, `VisualStackStyleRenderer.cs:409`, and `CodestyleStyleRenderer.cs:159` (never dimmer than context words). `App.CleanupDesktopHost()` (`App.axaml.cs:436-519`) closes `_trayController` and `_overlayCoordinator` FIRST on the UI thread, then runs runtime/DI teardown off-thread under a strict `1,500ms` timeout. |

### 2. Bonus MEDIUM / LOW Fixes Confirmed in `c6420bb`

- **`UX-07` (Mostly Fixed):** Exact `tray.md` §4.1 idle/playing/paused header copy (`TrayMenuState.cs:256, 263-265, 284`), Win32 tab-aligned accelerator (`Show Lyrics\t{hint}`, `TrayController.cs:168`), UI-thread-only serialized state updates, and grapheme-safe `StringInfo` truncation (`TrayMenuState.cs:205-245`).
- **`UX-08` (Fixed):** `Codestyle` locked to stable 3-row height with full 3-line width measurement, ellipsis trimming, and grapheme-safe typing (`CodestyleStyleRenderer.cs:126-203, 325-329`); `PauseDimOpacity = 0.62f`, high-contrast `#F8FAFC` / `#D90B0C16` offset toast with `· Limit` indicator, dark pill halo behind instrumental dots (`PlaybackLifecycleChoreographer.cs:43, 274-371`); cached HarfBuzz `SKTextBlob` + OS multi-script fallback wired into `LegibilityPipeline`.
- **`UX-09` (Fixed):** Off-screen click-through `_hiddenAnchorWindow` (`App.axaml.cs:117-124, 420-434`), defensive `& ~WS_EX_APPWINDOW` (`Win32OverlayInterop.cs:25`), dynamic `Volatile` `OverlayWindow.CurrentBoundsConfig` + value-based `PerDisplayBounds` reconciliation (`OverlayWindow.cs:63-75`, `App.axaml.cs:61-98, 215`), post-`Show()` mixed-DPI `ApplyPlacement`, and `Show()` prior to `SetOverlayPaused(false)` (`MultiMonitorOverlayCoordinator.cs:149-151, 199-205`).
- **`UX-11` / `UX-12` / `UX-13` (Mostly Fixed):** WCAG Light-taskbar status dots (`#15803D`/`#B45309`) + live `WM_SETTINGCHANGE` icon refresh, stale `VisualStack` ripple reset, trailing-`+` hotkey parsing, classic `iTunes.exe` classification, and live hotkey re-registration on settings update.

### 3. Remaining Non-Blocking Notes (Tracked for Phases 4–6)

1. **[LOW] Hidden / Fullscreen Guard on Offset Hotkeys (`OverlaySnapshotBuilder.cs:31-35`):** `CanAdjustTimingOffset` blocks idle/unsynced nudges cleanly, and `PlaybackLifecycleChoreographer.cs:394` blocks the toast when hidden, but pressing `Ctrl+Shift+[`/`]` while a synced track is playing with `LyricsVisible == false` or `IsFullscreenHidden == true` still updates `_timingOffsetService` silently. Adding `state.Settings.LyricsVisible && !state.IsFullscreenHidden` to `CanAdjustTimingOffset` closes that edge case.
2. **[LOW] Zero-Music First Launch Discoverability (`App.axaml.cs:153-187`):** Launching `LyricPulse.App.exe` without `--demo` and with no player active stays silent in the Windows tray (resolved naturally when Phase 5 opens `GalleryWindow` on first launch; in Milestone 1A, double-clicking the `.exe` a second time or left-clicking the tray immediately launches `Neon Horizon` Demo Mode).
3. **[MEDIUM — Deferred to Phase 5] Tray `NativeMenu` In-Place Mutation & Hotkey Style-Switch Cue (`UX-07` / `UX-10`):** `TrayController.cs:147` replaces `_trayIcon.Menu` on state transitions (rather than mutating persistent `NativeMenuItem` instances in place), and `Ctrl+Shift+S` / `Ctrl+Shift+L` have no transient HUD confirmation pill when pressed during idle/paused states.
4. **[LOW — Deferred to Phase 6 Packaging] Shell Icon & All-Displays-Disabled Notice (`UX-11` / `UX-13`):** Add multi-size `LyricPulse.ico` `<ApplicationIcon>` to `LyricPulse.App.csproj` and surface an `"All displays disabled"` tray status once Phase 5 exposes per-monitor `DisabledDisplays` switches.


---

## Phase 6 v1.0 Release UX Audit

**Audit Date:** 2026-11-25  
**Commit Audited:** `b88131e` (`master`, read-only inspection of `src/`)  
**Auditor:** LyricPulse UX Evaluator (ECC Review & UX Recipes)  
**Authoritative Specs Audited Against:**
- `design-system/lyricpulse/MASTER.md`
- `design-system/lyricpulse/pages/gallery.md`
- `design-system/lyricpulse/pages/settings.md`
- `design-system/lyricpulse/pages/tray.md`

### Executive Verdict: **FAIL** *(0 CRITICAL, 5 HIGH, 5 MEDIUM, 4 LOW — 5 HIGH UX/A11y/Contrast Blockers Before v1.0 Release Sign-Off)*

> **Summary:** Core v1.0 architecture is strong—`StageWindow` launches directly into the grainy `AmbientPartyMesh` stage with automatic idle `Neon Horizon` fallback, `FullscreenStageScale` (`1.85×`) scales all 10 styles cleanly, `Following Beat Dot` and `Color-Changing Sweep Layer` work across renderers, `F11` enters borderless fullscreen with 2s idle bar auto-hide and `Esc` cascade, minimize-to-tray (`X`) and clean `1.5s`-bounded Quit teardown work reliably, and 2-step Reset safety + OBS Chroma-Key capture are in place. However, **5 HIGH** issues in responsive Settings drawer docking (`< 880px`), hotkey capture/conflict rollback, keyboard accessibility of the 10-card strip & drawer focus, missing active-selection/media-health UI bindings, and Light-mode contrast block immediate v1.0 sign-off.

---

### 1. v1.0 User Journey & CEO Requirements Scorecard

| # | User Journey / CEO Requirement | Status | Key Evidence & Verdict Notes |
|---|---|---|---|
| 1 | **Primary `StageWindow` Launch, Floating Glass Pill Bar, `<`/`>` Carousel & 10-Card Strip** | **PARTIAL / FAIL** | Launches directly into `StageWindow` (`App.axaml.cs:165, 190-192`) with `AmbientPartyMesh.sksl` grain/vignette + idle `Neon Horizon` fallback (`StageCardCanvasHost.cs:159-179`) and `<`/`>` carousel (`StageWindow.axaml:314-334`). Blocked by hardcoded green media dot (`UX6-04`), unreadable Light-mode hero counter/category contrast (`UX6-05`), unfocusable `Border` strip cards (`UX6-03`), and missing active filter/font states (`UX6-04`). |
| 2 | **`F11` Pure Fullscreen Party Stage, 2s Idle Auto-Hide, Mouse Wake & `Esc` Cascade** | **PASS WITH NOTES** | `F11` hides carousel/strip (`StageViewModel.cs:427-428`), enters `WindowState.FullScreen` with `ExtendClientAreaTitleBarHeightHint = 0` (`StageWindow.axaml.cs:255-270`), fades `TopGlassBar` after `2.0s` idle pointer (`lines 63-71`), wakes on `OnPointerMoved` (`lines 165-174`), and cascades `Esc` (`Flyout -> Settings -> Fullscreen`, `StageViewModel.cs:110-128`). Minor `Done`-button timer gap (`UX6-12`). |
| 3 | **Bigger Concert Typography (`1.85×`), Following Beat Dot & Color-Changing Sweep Layer** | **PASS** | `DefaultFullscreenStageScale = 1.85f` (`AppSettings.cs:91`) flows via `OverlayFrameAdapter.ComputeRecommendedHeroScale` (`OverlayFrameAdapter.cs:90-93`) into `EffectiveHeroFontSizeDip` and `StyleLayoutHelper.ResolveStyleFontSize` across all 10 styles (`Codestyle*` capped cleanly at `68f`, `Messages` scaled `1.0–1.65×`). `ShowFollowingDot` & `ShowColorSweepLayer` toggles in Aesthetics flyout & Settings drive `AestheticsOverlayRenderer` cleanly. |
| 4 | **Diagnostics Flyout, Retry Fetch, Universal Player Toggle, `−1s..+1s` Offset & Desktop Overlay Toggle** | **PASS WITH NOTES** | Diagnostics flyout surfaces GSMTC, Lyrics pipeline, Display count, `Follow All Media Players`, `Retry Lyrics`, and `Toggle Demo` (`StageWindow.axaml:288-309`); 5-step tabular `−1s..+1s` offset + `0.0s` reset + `Ctrl+Left/Right/0` work cleanly; `DesktopOverlayEnabled` toggle switches multi-monitor click-through overlays (`App.axaml.cs:398-409`). Blocked by unbound `MediaHealth` dot & `ShowTroubleshootingPill` (`UX6-04`). |
| 5 | **Minimize-to-Tray (`X`), Second-Instance Wakeup, Responsive Settings Drawer, Hotkey Capture, 2-Step Reset & Quit** | **FAIL** | `OnClosing` minimizes cleanly to tray (`StageWindow.axaml.cs:144-163`), 2-step Reset works (`SettingsView.axaml:393-403`), and `CleanupDesktopHost` (`App.axaml.cs:590-684`) disposes tray/windows before `1.5s`-bounded runtime teardown. Blocked by `< 880px` inline drawer squeeze (`UX6-01`), hotkey capture unregistering valid keys on conflict + failing `D0–D9`/`Backspace` (`UX6-02`), missing Settings focus management (`UX6-03`), missing Settings active states (`UX6-04`), and second-instance demo hijack (`UX6-06`). |

---

### 2. Severity-Classified Findings

#### HIGH (Release Blockers)

##### `UX6-01` — Settings Drawer Always Docks Inline (`380px`), Squeezing `StageWindow` & Clipping Top Bar Below `880px`
- **Locations:**
  - `src/LyricPulse.UI/Views/StageWindow.axaml:10, 21, 40, 417-420`
  - `src/LyricPulse.UI/Themes/LyricPulseTokens.axaml:37, 40, 76, 79, 101` (`LpDrawerScrimBrush`, `LpShadowDrawer`, `LpRadiusDrawer` unused)
- **User Impact:** `StageWindow` defines `MinWidth="760"` (`line 10`), yet `SettingsView` is permanently placed in `Grid.Column="1"` (`Width="380"`, `lines 418-420`). When a user opens Settings (`Ctrl+,` or gear icon) at `760–880px` (or even `1000px`), the main stage/gallery pane is crushed to `380–620px` wide, clipping the `Auto,*,Auto` `TopGlassBar` pill bar off the right edge. Violates `settings.md:11-13` (compact `< 880px` slide-over drawer with `LpDrawerScrimBrush` backdrop).
- **Concrete Fix:** Switch `SettingsView` dynamically at `Bounds.Width < 880` to a right-anchored overlay `Grid.Column="0"` (`Width="360"`, `CornerRadius="{StaticResource LpRadiusDrawer}"`, `BoxShadow="{DynamicResource LpShadowDrawer}"`) backed by a clickable `LpDrawerScrimBrush` dismiss layer; keep `380px` inline docking at `>= 880px` (and `400px` at `>= 1200px`), and allow `TopGlassBar` pills to wrap/compact gracefully at narrow widths.

##### `UX6-02` — Hotkey Recorder Destroys Valid Previous Shortcut on Conflict & Rejects `D0–D9` / `Backspace` / `Delete`
- **Locations:**
  - `src/LyricPulse.App/Platform/GlobalHotkeyManager.cs:209-228`
  - `src/LyricPulse.UI/ViewModels/StageViewModel.cs:274-283, 1028-1042`
  - `src/LyricPulse.UI/Views/StageWindow.axaml.cs:178-195, 506-523`
  - `src/LyricPulse.App/Platform/HotkeyGestureParser.cs:139-206`
- **User Impact:**
  1. **Silent Loss of Working Hotkey on Conflict:** When rebinding an active hotkey (e.g., `Ctrl+Shift+L`) to a shortcut occupied by another app (`ERROR_HOTKEY_ALREADY_REGISTERED`), `GlobalHotkeyManager.DrainCommandsOnMessageThread` (`lines 210-214`) unregisters the existing Win32 hotkey ID *before* attempting `RegisterHotKey` for the candidate. When registration fails, `CommitCapturedHotkey` keeps `"Ctrl+Shift+L"` in `AppSettings.GlobalHotkeys` (`lines 1031-1041`) without re-registering it—so the UI displays `"Ctrl+Shift+L"` while the shortcut is dead until restart (violates `settings.md:149`).
  2. **Number Keys Fail (`D0–D9`):** `StageWindow.FormatKeyGesture` (`line 519`) formats `Key.D1..Key.D9` as `"D1".."D9"`, which `HotkeyGestureParser.TryParseVirtualKey` (`lines 141-205`) rejects with `"Unrecognized key 'D1'"`.
  3. **Cannot Clear with `Backspace` / `Delete`:** `ClearHotkeyCommand` (`StageViewModel.cs:274-283`) is unwired from both `SettingsView.axaml` and `StageWindow.OnKeyDown`, so pressing `Backspace` or `Delete` (`settings.md:148`) shows `"Include Ctrl, Alt, or Shift+Ctrl/Alt."` instead of clearing to `"Unassigned"`.
- **Concrete Fix:** (1) On `TryRegister` failure in `ValidateAndRegisterSingleHotkey` / `GlobalHotkeyManager`, immediately restore the previous valid binding via Win32 `RegisterHotKey`; (2) normalize `Key.D0..Key.D9` (and `OemMinus`, `OemPlus`, `OemQuestion`, `Oem1`, `OemPipe`) in `FormatKeyGesture` / `TryParseVirtualKey`; (3) handle `Key.Back` / `Key.Delete` during `IsRecordingHotkey` to execute `ClearHotkeyCommand` and exit capture.

##### `UX6-03` — Bottom 10-Card Strip Unreachable via Keyboard + Missing Settings/Flyout Focus Management
- **Locations:**
  - `src/LyricPulse.UI/Views/StageWindow.axaml.cs:238-253, 331-353, 404-424`
  - `src/LyricPulse.UI/ViewModels/StageViewModel.cs:110-157`
  - `src/LyricPulse.UI/Views/SettingsView.axaml:22-33`
- **User Impact:**
  1. All 10 style cards in `RebuildStyleStrip()` are constructed as raw `Border` controls with `PointerPressed` (`lines 404-424`)—they are not `Focusable`, have no `TabStop`, no `Enter`/`Space` key activation, no `AutomationProperties.Name`, and no `:focus-visible` ring (`MASTER.md:302`). Meanwhile, each card's `pinBtn` (`lines 331-353`) lacks `AutomationProperties.Name` (screen readers announce only `"Pin"`/`"Pinned"` without the style name).
  2. Opening Settings (`Ctrl+,` or gear button) or any Top Pill flyout does not move keyboard focus inside the panel, and pressing `Esc` (`ExitFullscreenCommand`) does not restore focus to the trigger control (`settings.md:19`, `MASTER.md:310`).
- **Concrete Fix:** Build strip cards as focusable `Button`s (or `Focusable = true` with `KeyDown` `Enter`/`Space`, `AutomationProperties.Name="{DisplayName}, {CapabilityChip}"`, and `:focus-visible` `LpFocusRingBrush` adorner), add `AutomationProperties.Name` from `cardVm.PinToolTip` on `pinBtn`, and manage focus entry/restoration on `IsSettingsDrawerOpen` and `ActiveFlyout` transitions.

##### `UX6-04` — Unbound Media Health Dot / Troubleshooting Pill & Missing Active-State Indicators on 7 Control Groups
- **Locations:**
  - `src/LyricPulse.UI/Views/StageWindow.axaml:83-84, 223-234, 264-267, 380-383`
  - `src/LyricPulse.UI/Views/SettingsView.axaml:197-204, 242-251, 310-317, 341-350`
  - `src/LyricPulse.UI/ViewModels/StageViewModel.cs:698-702, 757-783`
- **User Impact:**
  1. **Always-Green Media Dot & Hidden Troubleshooting Affordance:** `StageWindow.axaml:83-84` hardcodes `Fill="{DynamicResource LpLiveGreenBrush}"` even when `"No music playing"`, `"Paused"`, `"Lyrics offline"`, or `"No synced lyrics"`—violating `MASTER.md:23, 356` and `gallery.md:54-60`. `StageViewModel.MediaHealth` (`lines 757-780`) and `ShowTroubleshootingPill` (`lines 782-783`) exist in the ViewModel but are completely unbound in XAML.
  2. **Invisible Selection State on 7 Button Groups:** Plain `<Button>` controls with zero checked/selected styling are used for: (a) Settings **5-Zone Safe Anchor Grid** (`Top`/`Center`/`B-Left`/`B-Third`/`B-Right`, even though `IsAnchorTopCenter..IsAnchorBottomRight` exist at `StageViewModel.cs:698-702`), (b) **Theme Variant** (`System`/`Dark`/`Light`), (c) **Ambient Scrim Intensity** (`Off`/`Subtle`/`Balanced`/`Strong`), (d) **Bilingual Sub-line** (`Off`/`Translate`/`Romanize`), (e) Aesthetics flyout **Backdrop Mode** (`Ambient Party Mesh`/`Pure Black`), (f) Font flyout **Typeface tiles**, and (g) Bottom strip **Filter chips** (`All`/`Pinned`/`With Visuals`/`Motion`). Users cannot tell which option is currently active.
- **Concrete Fix:** Bind the media status dot fill & troubleshooting pill (`"Not detecting?"` / `"Fix sync"`) to `MediaHealth` / `ShowTroubleshootingPill`, and convert all 7 selection groups to `RadioButton` / `ToggleButton` (or `.active` pseudoclass bound to VM properties) with `LpAccentSoftBgBrush` + `LpAccentBorderBrush` active styling.

##### `UX6-05` — Unreadable Light-Mode Contrast on Hero Stage Counter Pill (`~1.4:1`) & Category Label (`~1.8:1`)
- **Locations:**
  - `src/LyricPulse.UI/Views/StageWindow.axaml:339, 355-358`
  - `src/LyricPulse.UI/Themes/LyricPulseTokens.axaml:48-49, 60`
- **User Impact:** When switching to **Light** theme (`Settings -> Light`), `StyleCounterText` (`Foreground="#CBD5E1"`, `StageWindow.axaml:357`) renders inside `LpGlassPillBrush` (`#EBFFFFFF` frosted white in Light mode, `LyricPulseTokens.axaml:49`), collapsing to **~1.4:1** contrast (completely illegible). Simultaneously, `SelectedStyleInfo.CategoryLabel` (`StageWindow.axaml:339`) binds `Foreground="{DynamicResource LpAccentTextBrush}"`, which flips to `#3730A3` dark indigo (`LyricPulseTokens.axaml:60`) directly on the `#0E1017` dark hero stage (**~1.8:1** contrast).
- **Concrete Fix:** Bind `StyleCounterText` to `{DynamicResource LpTextSecondaryBrush}` (high contrast inside `LpGlassPillBrush` in both themes) and style `CategoryLabel` as an always-dark-stage luminous token (`#A5B4FC` or a `LpStageAccentTextBrush` / glass pill badge) since the hero stage canvas remains dark in both themes.

---

#### MEDIUM

##### `UX6-06` — Second-Instance Wakeup & `Ctrl+Shift+G` Force Global Demo Mode When Paused / Instrumental / No-Lyrics
- **Locations:** `src/LyricPulse.App/App.axaml.cs:462-464, 491-494`
- **User Impact:** Pressing `Ctrl+Shift+G` or launching a second instance without `--demo` executes `if (!snap.IsDemoMode && (!snap.IsPlaying || snap.LyricsStatus != LyricsStatus.Synced)) await _runtime.SetDemoModeAsync(true)`. Because `StageWindow`'s `StageCardCanvasHost` (`StageCardCanvasHost.cs:159-179`) already renders `Neon Horizon` automatically on the Stage when idle, mutating global `IsDemoMode = true` hijacks paused Spotify/Apple Music or instrumental tracks, overwrites the Top Pill Bar with `"Neon Horizon"`, and broadcasts demo lyrics onto the Desktop Overlay if enabled.
- **Concrete Fix:** Reserve `SetDemoModeAsync(true)` in `HandleSecondInstanceActivationAsync` strictly for `forceDemo == true` (`--demo` CLI flag), letting normal `OpenGallery` / second-instance wakeup simply call `SurfaceAndFocus` without overriding live paused/instrumental state.

##### `UX6-07` — `"Play previews"` Button Is a Dead No-Op Under Reduced Motion
- **Locations:** `src/LyricPulse.UI/ViewModels/StageViewModel.cs:163, 443-444`, `src/LyricPulse.UI/Views/StageWindow.axaml:386-392`
- **User Impact:** `EffectivePreviewsPaused => PreviewsPaused || _snapshot.IsEffectiveReducedMotion`. Under Reduced Motion, clicking `"Play previews"` flips `_previewsPaused` (`false <-> true`), yet `EffectivePreviewsPaused` remains `true` forever—the label never changes and previews never play, making the button appear broken.
- **Concrete Fix:** Either allow explicit user override (`PreviewsPausedOverride` until Reduced Motion toggles) or disable the button when `IsEffectiveReducedMotion` is active with `"Paused (Reduced Motion)"` clarity (`gallery.md:189`).

##### `UX6-08` — `RebuildStyleStrip()` Accumulates `PropertyChanged` Subscriptions on Filter Changes
- **Locations:** `src/LyricPulse.UI/Views/StageWindow.axaml.cs:276, 436-453`
- **User Impact:** Every filter switch (`All` / `Pinned` / `With Visuals` / `Motion`) clears `StyleStripPanel.Children` and attaches a fresh `cardVm.PropertyChanged += ...` closure capturing the old `Border`, `ToggleButton`, and `StageCardCanvasHost` references without detaching previous handlers—leaking old visual subtrees on repeated filter toggles.
- **Concrete Fix:** Track and detach `PropertyChanged` delegates before rebuilding, or bind `StyleStripPanel` via an `ItemsControl` / `DataTemplate` instead of imperative code-behind event subscriptions.

##### `UX6-09` — Startup Hotkey Conflicts Are Not Synced to `StageViewModel`
- **Locations:** `src/LyricPulse.App/App.axaml.cs:148, 165, 393-396, 546`
- **User Impact:** `RegisterGlobalHotkeys` runs at `line 148` before `_stageViewModel` is constructed (`line 182`). Because `_lastRegisteredHotkeys` is cached at `line 539`, `ApplySnapshotToHost` (`line 393`) skips re-registering—so startup hotkey conflicts appear in the System Tray but never display inline errors in `Settings -> Global Hotkeys`.
- **Concrete Fix:** Store `_lastHotkeyConflicts` on `App` and forward it to `_stageViewModel.SetHotkeyConflicts(_lastHotkeyConflicts)` inside `CreateAndShowPrimaryStageWindow`.

##### `UX6-10` — Unmodified `Left` / `Right` Arrow Keys Hijack Keyboard Navigation Inside Settings & Toolbar
- **Locations:** `src/LyricPulse.UI/Views/StageWindow.axaml.cs:220-232`
- **User Impact:** `OnKeyDown` intercepts `Key.Left` / `Key.Right` whenever the focused control is `not Slider`, causing arrow navigation inside `SettingsView` (`RadioButton` groups, `ToggleSwitch`es, `ColorPicker`) or `TopGlassBar` to unexpectedly switch the active lyric style instead of navigating controls.
- **Concrete Fix:** Restrict unmodified `Left` / `Right` style cycling to `!vm.IsSettingsDrawerOpen && !vm.IsAnyFlyoutOpen` (or when focus is on the stage/strip container rather than form controls).

---

#### LOW

##### `UX6-11` — Hit-Area & Icon Consistency Polish (`MASTER.md:297`, `gallery.md:93-94, 149-151`)
- **Locations:** `StageWindow.axaml:157-178`, `StageWindow.axaml.cs:331-346`, `ObsBroadcastWindow.axaml:11-23`
- **Notes:** `TopGlassBar` icon buttons are `32×32px` and strip `pinBtn` is `28×28px` (below `40×40px` icon-button target); `pinBtn` renders `"Pin"`/`"Pinned"` text (`9px`) instead of `LpIconPinRegular`/`LpIconPinFilled`; `DesktopOverlay` toggle does not swap to `LpIconEyeOffFilled` when off; `ObsBroadcastWindow` lacks active highlight on the selected `Green`/`Blue`/`Black` chroma button.

##### `UX6-12` — Fullscreen Idle Timer After Closing Flyout & `2px` Titlebar Overlap
- **Locations:** `StageWindow.axaml.cs:64-71`, `StageViewModel.cs:153-157`, `StageWindow.axaml:13, 31`
- **Notes:** Clicking `Done` (`ClosePopupsCommand`) during Fullscreen stops flyout blocking, but `_fullscreenIdleTimer` is not restarted until the next `OnPointerMoved`, leaving `TopGlassBar` visible if the mouse stays still; `TopGlassBar` `Margin="16,38,16,0"` overlaps `ExtendClientAreaTitleBarHeightHint="40"` by `2px`.

##### `UX6-13` — `SurfaceAndFocus` Monitor Bounds Check & 5s Reset Auto-Disarm
- **Locations:** `StageWindow.axaml.cs:86-111`, `StageViewModel.cs:257-263`
- **Notes:** `SurfaceAndFocus` does not verify `Position` intersects `Screens.All` after undocking an external monitor (`tray.md:118`); `IsResetConfirmArmed` remains armed until clicked (no 5s timeout or disarm on Settings close).

##### `UX6-14` — Tray All-Displays-Disabled Notice & Diagnostics `Retry Lyrics` State
- **Locations:** `src/LyricPulse.App/Tray/TrayMenuState.cs:280-382`, `src/LyricPulse.UI/Views/StageWindow.axaml:304`
- **Notes:** `TrayMenuState` does not surface `"All displays disabled"` if `DesktopOverlayEnabled` is ON with every monitor disabled; Diagnostics `"Retry Lyrics"` button stays enabled in Demo/Synced states (`CanRetryLyricsFetch` could disable it when not applicable).


---

### Phase 6 Re-Verification Addendum (`e0dfe47`)

**Re-Verified Commit:** `e0dfe47` (`master`, read-only inspection of `src/`)  
**Verification Runs Executed:**
- `dotnet build LyricPulse.sln -c Debug --no-restore`: **0 Warning(s), 0 Error(s)**
- `dotnet build LyricPulse.sln -c Release --no-restore`: **0 Warning(s), 0 Error(s)**
- `dotnet test LyricPulse.sln -c Release --no-build`: **282 / 282 Passed (0 Failed, 0 Skipped)** — includes `Phase5StageAndSettingsTests` Phase 6 tests (`HotkeyGestureParser_AcceptsDigitKeys_D0ThroughD9`, `StageViewModel_Phase6DrawerModes_HotkeyClear_HealthDot_AndActiveBooleans`)
- `LyricPulse.App.dll --smoke-test`: **Exit 0** (`[PASS] LyricPulse Phase 5 smoke test succeeded | Fonts=5 | Shaders=3/3 OK | DemoLines=12 | Styles=10/10 | StageCards=10 | TrayHeader='Demo: Neon Horizon'`)

**Updated Phase 6 Verdict:** **`PASS WITH NOTES`** — **0 CRITICAL**, **0 HIGH** blockers remaining. Approved for **v1.0 Release Sign-Off**.

#### 1. Remediation Verification Matrix (`e0dfe47`)

| ID | Original Severity | Status | Verified Fix Evidence (`e0dfe47`) |
|---|---|---|---|
| **`UX6-01`** | HIGH | **VERIFIED FIXED** | `StageViewModel.cs:393-407` adds `IsCompactDrawerMode`, `IsDockedDrawerVisible`, and `IsOverlayDrawerVisible`; `StageWindow.axaml.cs:76, 99, 147` updates `Bounds.Width < 880.0` on open, `SurfaceAndFocus`, and resize; `StageWindow.axaml:519-534` switches `< 880px` to a `360px` right-anchored overlay drawer (`LpRadiusDrawer`, `LpShadowDrawer`) backed by clickable `LpDrawerScrimBrush` (`OnDrawerScrimPressed`, `StageWindow.axaml.cs:333-340`). |
| **`UX6-02`** | HIGH | **VERIFIED FIXED** | `GlobalHotkeyManager.cs:20, 211-241, 259` stores `_registeredSpecs` and rolls back the prior working `RegisterHotKey` binding on Win32 error `1409`; `HotkeyGestureParser.cs:166-171` + `StageWindow.axaml.cs:456` support `D0`–`D9` (`Ctrl+0..9` / `Ctrl+Shift+0..9`); `StageWindow.axaml.cs:208-213` + `StageViewModel.cs:1059-1078` wire `Backspace`/`Delete` to `ClearCurrentRecordingHotkey()` -> `ClearHotkeyCommand`. |
| **`UX6-03`** | HIGH | **VERIFIED FIXED** | `StageWindow.axaml:34-52, 450-513` converts the 10-card strip to declarative `ItemsControl` + focusable `Button.card-btn` with `.active` and `:focus-visible` (`LpFocusRingBrush`), `AutomationProperties.Name="{Binding DisplayName}"`, and `40×40` `Pin` `ToggleButton` with `AutomationProperties.Name="{Binding PinToolTip}"`. `StageWindow.axaml.cs:338, 381-384` restores focus to `SettingsToggleBtn` when the drawer closes; `lines 248-263` guard `Left`/`Right` arrows against `Slider`, `TextBox`, `Button`, and `ToggleButton` (which includes `RadioButton`). |
| **`UX6-04`** | HIGH | **VERIFIED FIXED** | `StageWindow.axaml:117-130` binds `MediaHealthDotBrush` (`StageViewModel.cs:750-757`) and `ShowTroubleshootingPill` (`"Fix?"` pill); `SettingsView.axaml:11-23, 213-218, 257-266, 326-331, 357-364` and `StageWindow.axaml:22-33, 91-98, 265-276, 306-309, 418-421` apply `.chip`/`.seg` + `.active` states across all selection groups; `SettingsView.axaml:348` binds `IsEnabled="{Binding IsReducedMotionToggleEnabled}"`. |
| **`UX6-05`** | HIGH | **VERIFIED FIXED** | `StageWindow.axaml:356-401` renders hero chevrons, pin button (`40×40`), `StyleCounterText` pill (`#E2E8F0` on `#CC14161F`), `CategoryLabel` (`#A5B4FC`), `DisplayName` (`#F8FAFC`), and `Description` (`#CBD5E1`) with high contrast on the dark hero stage in both Dark and Light themes. |
| **`UX6-06`** | MEDIUM | **VERIFIED FIXED** | `App.axaml.cs:493-496` restricts second-instance/`Ctrl+Shift+G` auto-Demo activation to `snap.Track is null` (preserving live paused, instrumental, and no-lyrics sessions). |
| **`UX6-08`** | MEDIUM | **VERIFIED FIXED** | Declarative `ItemsControl` strip (`StageWindow.axaml:450-513` + `OnThumbHostLoaded`/`OnThumbHostUnloaded` at `StageWindow.axaml.cs:269-298`) removes `RebuildStyleStrip()` and its `PropertyChanged` closure leak; `StageViewModel.RebuildFilteredStyleCards()` (`StageViewModel.cs:1179-1208`) also guards against redundant collection churn. |
| **`UX6-09`** | MEDIUM | **VERIFIED FIXED** | `App.axaml.cs:42, 190, 547` retains `_lastHotkeyConflicts` and passes it to `_stageViewModel.SetHotkeyConflicts(_lastHotkeyConflicts)` during `CreateAndShowPrimaryStageWindow`. |

#### 2. Remaining Non-Blocking Notes (Post-v1.0 Polish)

1. **[MEDIUM — Non-Blocking Edge Case] Empty Unassigned Hotkey Treated as Registration Conflict (`GlobalHotkeyManager.cs:85-92`, `App.axaml.cs:528-537`):** Clearing a hotkey via `Backspace`/`Delete` (`ClearCurrentRecordingHotkey`) writes `string.Empty` into `GlobalHotkeys`, causing `RegisterAll` -> `TryRegister(action, "")` to return `"Shortcut cannot be empty."` as a conflict error in Settings and `[Shortcut conflict]` in the Tray until reassigned. Skipping `string.IsNullOrWhiteSpace(gesture)` in `RegisterAll` / `App.RegisterGlobalHotkeys` resolves this cleanly.
2. **[MEDIUM — Retained] `"Play previews"` Under App-Toggled Reduced Motion (`UX6-07`, `StageViewModel.cs:467-468`, `StageWindow.axaml:425`):** `IsReducedMotionToggleEnabled` disables the preview toggle under OS Reduced Motion (`!IsOsReducedMotion`), whereas manually toggling app `ReducedMotion = true` leaves the button enabled while `EffectivePreviewsPaused` remains locked `true`.
3. **[LOW — Retained Polish]** Docked `TopGlassBar` crowding between `880px–1080px` when Settings (`380px`) is open (`StageWindow.axaml:74`); initial focus entry into `SettingsView` Close button on open; `UX6-11` (`TopGlassBar` `36×36` icons, `LpIconEyeOffFilled` toggle swap, `ObsBroadcastWindow` active chroma indicator); `UX6-12` (restarting `_fullscreenIdleTimer` on `Done`); `UX6-13` (`SurfaceAndFocus` off-screen monitor check + 5s Reset auto-disarm); `UX6-14` (`TrayMenuState` all-displays-disabled notice + Diagnostics `Retry Lyrics` disable state).
