# Requirements: LyricPulse

**Defined:** 2026-11-25
**Core Value:** Zero-friction, butter-smooth (60+ FPS) real-time synced kinetic lyrics overlaid cleanly over your desktop without stealing focus or mouse clicks.
**Target Platform:** Windows 10 22H2 (19045)+ & Windows 11 (22H2–24H2+), `.NET 10` (`net10.0-windows10.0.19041.0`), Avalonia UI `11.3.x`, SkiaSharp `3.119.4`, HarfBuzzSharp `8.3.1.5`, CsWinRT `2.3.1`.

---

## v1 Requirements

Every v1 requirement has a stable ID and maps to **exactly one phase** in [ROADMAP.md](ROADMAP.md).

### Core Engine, Media Sessions & State (`CORE`)

- [ ] **CORE-01**: Multi-project `.NET 10` (`net10.0-windows10.0.19041.0`) solution scaffold (`LyricPulse.Core`, `LyricPulse.Rendering`, `LyricPulse.UI`, `LyricPulse.App`, `LyricPulse.Tests`) using `Directory.Build.props`, `Directory.Packages.props` (Central Package Management with transitive pinning), NativeAOT/trim analyzers, `InvariantGlobalization=false`, and compile-time `System.Text.Json` `JsonSerializerContext` source generation (zero reflection serialization).
- [ ] **CORE-02**: Strictly immutable domain models (`sealed record`, `readonly record struct`, `ImmutableArray<T>`) and platform-agnostic service boundaries (`IMediaSessionProvider`, `ITitleNormalizer`, `ILyricsProvider`, `ILyricsOrchestrator`, `ILyricsCache`, `ILyricsParser`, `IWordVisualMatcher`, `IAlbumColorExtractor`, `ISettingsStore`, `IOverlayWindowPlatform`, `IGlobalHotkeyService`, `IFullscreenDetector`).
- [ ] **CORE-03**: Windows GSMTC media session tracker (`WindowsGsmtcSessionProvider` + `MediaSessionSelector`) supporting Spotify (priority 100), Apple Music (90), Dedicated Audio Players (60), and Browser/Generic PWAs (30), with `SupportedPlayersOnly` vs. `UniversalFallback` toggle, smart browser video filtering, editable `BlockedAppIds` (`Discord.exe`, `Teams.exe`, `Zoom.exe`), 50 ms event coalescing, 1,500 ms track-transition hysteresis, explicit COM event detachment, and COM fault isolation.
- [ ] **CORE-04**: Lock-free, zero-allocation monotonic `Stopwatch` playback clock (`GetInterpolatedPosition()`) with drift clamping (≤40 ms jitter ignored, 40–250 ms smooth exponential slew, >250 ms hard-snap seek with `SeekDetected`), exact-position pause freeze, stall guard, and Spotify ad / empty-title / >20-minute podcast / live-stream guards.
- [ ] **CORE-05**: Safe off-UI-thread GSMTC album thumbnail reader enforcing a hard 10 MB read cap and SHA-256 content hash deduplication to skip redundant palette extraction on repeated tracks.
- [ ] **CORE-06**: Atomic AOT-safe JSON settings persistence (`%LOCALAPPDATA%\LyricPulse\settings.json`) with schema versioning, debounced writes via temp-file swap, and safe `Reset Defaults` support.
- [ ] **CORE-07**: Embedded original, copyright-free 12-line Enhanced LRC demo song (`Neon Horizon` by `LyricPulse Studio`, 36.0s loop, 100 BPM) bundled offline for instant Style Gallery auditioning and Desktop Demo Preview.

### Synced Lyrics Pipeline (`LYR`)

- [ ] **LYR-01**: Compile-time `[GeneratedRegex]` (100 ms timeout, ReDoS-safe) track-title & artist normalization pipeline performing NFKC Unicode cleanup, zero-width stripping, 7 ordered metadata-stripping passes (up to 2 iterations for nested parentheticals), short-title safety guard, and canonical band-name `&` whitelist.
- [ ] **LYR-02**: Primary LRCLIB provider (`GET /api/get` with exact integer seconds duration + normalized title/artist retry, `GET /api/search` with deterministic 0..115 candidate scorer rejecting `<65`), compliant `User-Agent`, `TokenBucketRateLimiter`, 5s per-request / 10s pipeline timeouts, and strict `429 Retry-After` + exponential jitter backoff.
- [ ] **LYR-03**: Fallback NetEase Cloud Music provider (`POST /api/search/get` + `GET /api/song/lyric/v1` with `yv=1&lv=1&tv=-1&kv=1`) preferring word-synced `yrc` over line-synced `lrc`, detecting `nolyric`/`uncollected`, rate-limited with per-provider cooldown circuit breaker.
- [ ] **LYR-04**: Sharded atomic local disk cache (`%LOCALAPPDATA%\LyricPulse\cache\lyrics\{shard2}\{sha256}.json`) with tiered TTLs (90d Synced, 30d Instrumental, 7d Plain, 24h NotFound negative cache), 50 MB / 5,000-file LRU eviction, in-flight request deduplication, and priority-0 local `.lrc` sidecar override folder (`%LOCALAPPDATA%\LyricPulse\custom-lyrics\`).
- [ ] **LYR-05**: Multi-format lyrics parser supporting Standard LRC (multi-tag chorus expansion, `[offset:]` header), Enhanced LRC (`<mm:ss.xx>`/`<mm:ss.xxx>`), NetEase YRC (JSON credit line skipping + Latin syllable re-aggregation while preserving CJK units), and proportional syllable/character word-timing fallback with instrumental gap capping and strict monotonicity invariants.
- [ ] **LYR-06**: Per-song timing offset state machine supporting `−1.0s`, `−0.5s`, `0.0s` reset, `+0.5s`, `+1.0s` (clamped `[-15s, +15s]`) with canonical user-intuitive sign convention (`− = Earlier`, `+ = Later`; `EffectiveLookupTime = Clamp(Position - Offset)`), plus opt-in per-track persistence (`%LOCALAPPDATA%\LyricPulse\track-offsets.json`, LRU 1,000 tracks, default enabled).
- [ ] **LYR-07**: Timestamp-aligned bilingual translation (`tlyric`) and romanization (`romalrc`) merging (`|Δt| ≤ 300 ms`) onto primary `LyricLine` records (`TranslationText`, `RomanizedText`) for international music playback.

### Win32 Overlay Host & System Integration (`WIN`)

- [ ] **WIN-01**: NativeAOT-safe `[LibraryImport]` Win32 click-through transparent always-on-top overlay host applying `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, guarding `SetLayeredWindowAttributes` against `WS_EX_NOREDIRECTIONBITMAP` (DirectComposition/WinUI Composition safety), disabling DWM NC shadows/transitions/Peek and Win11 rounded corners, and guaranteeing zero focus stealing (`SWP_NOACTIVATE`, `ShowActivated=False`, `Focusable=False`).
- [ ] **WIN-02**: Per-Monitor V2 DPI awareness (`app.manifest`, `GetDpiForMonitor`) and multi-monitor overlay orchestration using `WindowState="Normal"` on each enabled display's `WorkingArea` (preventing `+8px` multi-monitor overshoot), stable Win32 device-name `DisabledDisplays` persistence, and 250 ms debounced `WM_DISPLAYCHANGE` / `WM_DPICHANGED` / `WM_POWERBROADCAST` hotplug & sleep/wake GPU recovery.
- [ ] **WIN-03**: Pre-Avalonia single-instance guard (`Local\LyricPulse.SingleInstance` named mutex + `Local\LyricPulse.ShowWindow` auto-reset event) activating the running `GalleryWindow` in `<20 ms` on second launch and recovering cleanly from abandoned mutexes.
- [ ] **WIN-04**: Optional user-remappable Windows global hotkeys via `RegisterHotKey` / `UnregisterHotKey` (`WM_HOTKEY`, zero low-level keyboard hooks `WH_KEYBOARD_LL` for anti-cheat/AV safety) with defaults `Ctrl+Shift+L` (Show/Hide), `Ctrl+Shift+S` (Cycle Pinned Styles), `Ctrl+Shift+[` (Earlier `−0.5s`), `Ctrl+Shift+]` (Later `+0.5s`), `Ctrl+Shift+G` (Open Gallery), and registration conflict reporting.
- [ ] **WIN-05**: Fullscreen game & presentation auto-hide via `SHQueryUserNotificationState` (`shell32.dll`, 1,500 ms poll, anti-cheat safe) pausing GPU rendering (`0%` overhead) and hiding overlays during `QUNS_RUNNING_D3D_FULL_SCREEN` / `QUNS_PRESENTATION_MODE`.
- [ ] **WIN-06**: Dedicated Streamer / OBS Broadcast Capture Window (`LyricPulse Broadcast`, selectable `1920×1080` or `1280×720`, omitting `WS_EX_TOOLWINDOW` for OBS Window Capture / Windows Graphics Capture) reusing the live SkiaSharp scene renderer with Transparent Alpha, Chroma Green (`#00FF00`), Chroma Blue (`#0000FF`), or Solid Black (`#000000`) backdrop modes.

### SkiaSharp GPU Rendering, Typography, Color & Shaders (`RND`)

- [ ] **RND-01**: Avalonia `AppBuilder` configured with explicit `.UseSkia()`, `AngleEgl → Software` rendering chain, `WinUIComposition → DirectComposition → RedirectionSurface` composition chain, `MaxGpuResourceSizeBytes = 32 MB`, and `LyricCanvasControl` paced by `TopLevel.RequestAnimationFrame` with an idle zero-burn sleep gate, immutable `FrameRenderPacket`, and `ICustomDrawOperation` (`ISkiaSharpApiLeaseFeature`).
- [ ] **RND-02**: Clean-room bundled static font pipeline in `LyricPulse.Rendering/Assets/Fonts/` (`BodoniModa-Bold.ttf`, `Jost-Bold.ttf`, `Mulish-Bold.ttf`, `Nunito-SemiBold.ttf` [SIL OFL] + `FluentSystemIcons-Filled.ttf` [MIT]), dual-registered via `LyricPulseFontCollection` (Avalonia UI) and `FontAssetRegistry` (`SKData.Create` → `SKTypeface.FromData`), strict SkiaSharp 3.x `SKFont` API compliance, and HarfBuzzSharp (`SKShaper`) multi-script CJK/Arabic/Devanagari/Emoji fallback without tofu glyphs.
- [ ] **RND-03**: 4-Layer Composed Overlay Legibility Pipeline in premultiplied `BGRA8888` (Layer 0 adaptive radial ambient scrim with `Off/Subtle/Balanced/Strong` + High-Contrast pill mode, Layer 1A/1B contact & ambient drop shadows, Layer 2 optical stroke halo with dark-fill inversion, Layer 3 contrast-guarded fill using `SKFontEdging.Antialias` and single outer `SaveLayer` opacity) guaranteeing `≥7.0:1` effective edge contrast over `#FFFFFF` and `≥11.37:1` with High-Contrast Scrim.
- [ ] **RND-04**: Deterministic OKLab K-Means++ (`K = 8`, SHA-256 seeded, radial center-weighted, `≤64 MP` `SKCodec` bomb guard, `64×64` `SKSamplingOptions.Linear`) album-cover palette extractor enforcing per-role OKLCH contrast bounds (`AccentColor1` `L∈[0.78,0.92]`, `AccentColor2` `L∈[0.66,0.82]` `ΔH≥38°`, `AccentColor3` `L∈[0.62,0.76]`, `ScrimTint` `L∈[0.14,0.22]`), achromatic monochrome studio fallback (`C_p85 < 0.035`), 12-step binary-search sRGB gamut mapping, and 650 ms OKLab track-change morphing.
- [ ] **RND-05**: Compiled SkSL runtime shaders (`EllipticalBulge.sksl` with convex outward lookup `factor = max(1.0 - iStrength * radial, 0.15)`, `iStrength ∈ [0.0, 0.40]`, `iPower = 2.10`; `DirectionalMotionBlur13.sksl` 13-tap tent blur with `60Hz` reference scaling, `<1.25 * dpiScale` dead-zone bypass, `22 * dpiScale` `tanh` cap) executing on pooled physical-pixel `SKSurface`s (`SKShaderTileMode.Decal`, `SKAlphaType.Premul`) with automatic fallback on Software/RDP, GPU context loss, compile failure, or Reduced Motion.
- [ ] **RND-06**: Deterministic damped harmonic oscillator spring physics library (`Spring.WordSnap`, `WordEnter`, `LineShift`, `IconPop`, `CameraDrift`, `BubblePush`, `LensBreath`) with clamped `dt` + `1/240s` sub-stepping, tempo-adaptive pacing compression for ballad-to-rap tempos, Exit-Faster-Than-Enter easing curves (`Ease.Enter`, `Ease.Exit`, `Ease.Highlight`, `Ease.LinearProgress`, `Ease.ColorMorph`), and rest-state sleep snapping.
- [ ] **RND-07**: Zero-per-frame-allocation `RenderResourceCache` (64 px-bucketed pooled `SKSurface`s, reused `SKPaint`/`SKFont`/`SKPath`/`SKImageFilter`/`SKTextBlob`/`SKRuntimeEffectUniforms`) and lock-free 120-sample `RenderTelemetrySink` tracking P95/max frame ms, dropped frames, backend state, and automatic `0.75×` offscreen shader scale degradation if P95 exceeds `14.0 ms` for 3 seconds.

### Offline Semantic Word-to-Icon Engine (`VIS`)

- [ ] **VIS-01**: Embedded zero-download `StarterLexicon.tsv` resource (`~300` poetic/lyric concepts + `~900` synonym mappings, `<25 KB`) bundled inside `LyricPulse.Core` delivering instant offline word-to-icon/emoji matching (covering 100% of the 12 `Neon Horizon` demo keywords) for Phase 3 MVP and resilient fallback when full GloVe weights are absent.
- [ ] **VIS-02**: Reproducible offline Python asset pipeline (`tools/nlp/build_semantic_assets.py`) compiling PDDL 1.0 GloVe 6B 100d (`V ≈ 50,000`, stopword filtered, mean-centered, top-2 PCA "All-but-the-Top" removed, L2 unit-normalized, per-vector `int8` quantized `glove-100d-q8.bin` `~5.2 MB`), MIT Fluent System Icons (`fluent-icons.tsv` with UI-chrome denylist), and Unicode CLDR 16.0 Emoji annotations (`emoji-index.tsv`).
- [ ] **VIS-03**: Zero-allocation SIMD runtime matcher (`FrozenDictionary.GetAlternateLookup<ReadOnlySpan<char>>`, stopword filter, ~250-entry irregular lyric lemmatizer + vocab-validated suffix stripping, `TensorPrimitives.Dot` AVX2/AVX-512 scanning ~2,400 concept vectors in `<50 µs`, `>= 0.56f` cosine threshold, `Balanced`/`FluentIconsOnly`/`EmojiOnly` preference, line deduplication & density cap) with binary header/checksum validation and automatic `StarterLexicon` degradation.
- [ ] **VIS-04**: Phrase-level bigram idiom matching (~150 curated lyric bigrams such as `"broken heart"`, `"shooting star"`, `"red light"`) checked prior to unigram lookup, plus line/song-mood averaged GloVe vector fallback (`>= 0.48` threshold) when an entire line has zero individual matches in visual-heavy styles.

### 10 Kinetic Lyric Styles & Lifecycle Choreography (`STY`)

- [ ] **STY-01**: Style 1 — **Fisheye**: tight 3-word vertical triad (`Prev`/`Active`/`Next`), `Spring.LineShift` + `Spring.WordSnap`, `DirectionalMotionBlur` (`0.48`) + `EllipticalBulge` (`0.24` baseline + `Spring.LensBreath` beat impulse, `0.36` line swell), and Reduced Motion static crossfade fallback.
- [ ] **STY-02**: Style 3 — **Codestyle**: floating 3-line translucent syntax-highlighted monospace code snippet (`Cascadia Mono → Consolas`, locked font), gutter `tnum` line numbers, character-by-character typing across first 78% of word duration, blinking `#EF4444` block-bar cursor, shaders permanently OFF, and Reduced Motion whole-word / solid-cursor fallback.
- [ ] **STY-03**: Style 5 — **Visual Stack**: centered 4-row vertical stack with leading-stopword grouping (`[★] Chasing the star`), glowing circular/squircle semantic icon badges with `Spring.IconPop` + expanding ripple ring, vertical `DirectionalMotionBlur` (`0.42`), and Reduced Motion crossfade fallback.
- [ ] **STY-04**: Unified playback lifecycle choreography across all styles: pause dim (`0.62×` over 300 ms → 8s fade to `0.00`), instrumental break (`gap > 4.0s`) 3-dot breathing indicator with 450 ms vocal pre-wake, track-change 160 ms crossfade + 650 ms OKLab palette morph, silent transparent fade when no synced lyrics exist, and 1.2s transient tabular-mono timing offset toast.
- [ ] **STY-05**: Style 2 — **Fisheye Visual**: Fisheye 3-word lens triad paired with alternating left/right docked semantic icons (re-centered `[Icon + Gap + Word]` unit inside lens sweet spot, `Spring.IconPop` + `±6°` tilt) bending smoothly through the same `EllipticalBulge` + `DirectionalMotionBlur` pipeline, with Reduced Motion fallback.
- [ ] **STY-06**: Style 4 — **Codestyle Visual**: Codestyle monospace typing enhanced with pre-allocated inline IDE hint icon pills popping via `Spring.IconPop` at 65% word completion without horizontal layout jitter, dimming cleanly on committed `// comment` lines, with Reduced Motion fallback.
- [ ] **STY-07**: Style 6 — **Constellation**: deterministic hash-seeded starfield per line with 8-iteration minimum-distance overlap relaxation, progressive star-to-star glowing vector thread with shooting spark, 4-point diffraction star ignition flares, and Reduced Motion static starfield fallback.
- [ ] **STY-08**: Style 7 — **Spider's Web**: radial geometric hub + 3 sagging catenary silk rings, word-triggered shooting radial spokes with damped transverse plucked-strand twang vibration, strictly upright `0°` word labels anchored on alternating radial tiers (`0.54R`/`0.80R`), and Reduced Motion static web fallback.
- [ ] **STY-09**: Style 8 — **Node Line**: lower-mid sinusoidal/Catmull-Rom timeline ribbon with playback-synced luminous comet stroke, alternating exterior above/below word labels, node ripple ignition, `Spring.CameraDrift` horizontal pan for `>8`-word lines, and Reduced Motion wrapped static fallback.
- [ ] **STY-10**: Style 9 — **Kinetic Wall**: true 3D perspective editorial typography wall via `SKMatrix44` (`f = 1100px`, pitch `+6.5°`, active-word-tracking yaw `±11°`, bounded roll `±1.5°`), Z-depth word landings (`+65px → 0px`), critically damped `Spring.CameraDrift` tracking with subtle `DirectionalMotionBlur` (`0.36`) and depth attenuation, and Reduced Motion flat 2D editorial stack fallback.
- [ ] **STY-11**: Style 10 — **Messages**: lower-right phone-screen message thread locked to `Segoe UI Variable` and WCAG-verified fixed chat palette (`#0066D6`/`#FFFFFF` outgoing `5.44:1`, `#222433`/`#F8FAFC` incoming `14.9:1`), 450 ms pre-arrival 3-dot typing indicator, alternating Outgoing/Incoming bubbles via `Spring.BubblePush`, intra-bubble karaoke word lighting, `DstIn` top history fade mask, and Reduced Motion fallback.
- [ ] **STY-12**: Bilingual dual-line secondary sub-line rendering (65% scale, 70% opacity translation or romanization below active line in `Codestyle`, `CodestyleVisual`, `NodeLine`, `KineticWall`, and `Messages`) controlled by `AppSettings.SecondaryLineMode` (`Off` / `Translation` / `Romanization`).

### Desktop UI — System Tray, Gallery & Settings (`UI`)

- [ ] **UI-01**: Avalonia `TrayIcon` + dynamic `NativeMenu` (`ShutdownMode.OnExplicitShutdown`, zero emojis in menu labels), Windows Taskbar Dark/Light adaptive multi-size `.ico` states (Playing, Idle, Hidden, Sync Issue), `<=96`-char dynamic hover tooltip, left-click Gallery activation, right-click menu with 2-row Now Playing header, `Show Lyrics` CheckBox, Pinned Styles Radio list (+ active unpinned fallback), `All Styles ▸` Radio submenu, `Timing Offset ▸` submenu (`−1.0s`, `−0.5s`, `Reset 0.0s`, `+0.5s`, `+1.0s`), Gallery/Settings triggers, and clean `Quit LyricPulse` disposal with zero ghost tray icon.
- [ ] **UI-02**: "Acoustic Studio Lightbox" Avalonia 11 design system foundation (`Themes/LyricPulseTokens.axaml`, `Themes/ColorPickerTheme.axaml`, `Themes/Icons.axaml`) built on `<FluentTheme />`, Dark & Light `ThemeDictionaries` bound exclusively via `{DynamicResource}`, compiled bindings (`x:DataType`), concentric corner radii, Fluent System Icon `StreamGeometry` `<PathIcon>` resources (ZERO emojis in structural chrome), and pseudoclass state styling.
- [ ] **UI-03**: `GalleryWindow` sticky `64px` top Media-Status & Sync Bar featuring 40×40 album art with `1px` `LpMediaOutlineBrush` + 3-dot live palette strip, track/artist/source live status capsule, one-click `"Not detecting?"`/`"Fix sync"` 3-row live diagnostic flyout (GSMTC, Lyrics Pipeline, Overlay Windows + Retry Fetch & Demo-on-Desktop actions), `2px` loading hairline, 5-segment `tnum` timing offset control, Overlay Visibility toggle, and Settings drawer toggle.
- [ ] **UI-04**: `GalleryWindow` utility strip (`All`/`Pinned`/`With Visuals`/`Typography & Motion` filter chips, `Dark`/`Light Page`/`Gradient` stage backdrop switcher, WCAG 2.2.2 `Pause/Play previews` toggle) and responsive 2-column (1-column `<680px`, `MaxWidth=1120`) 10-card live-animated Style Gallery using staggered `Neon Horizon` phase offsets, viewport-culled 30 FPS (60 FPS hovered/active) rendering, anti-jitter `1px→2px` active border compensation, and `40×40` Pin-to-Tray toggles syncing live with the System Tray menu.
- [ ] **UI-05**: Responsive `SettingsView` drawer (inline right-docked `380–400px` at `>=880px`, slide-over overlay with scrim `<880px`) with Section 1 Typography (2×2 live SIL OFL `"Aa"` specimen tiles for Jost, Bodoni Moda, Mulish, Nunito + `60%–160%` Lyric Scale slider) and Section 2 Color Mode (`Auto-Sync Album` live 3-swatch strip vs. `Custom Palette` 3-accent themed Avalonia `ColorPicker` + 4 Quick Studio Presets).
- [ ] **UI-06**: `SettingsView` Section 3 Displays (per-monitor cards with friendly name, `tnum` resolution/DPI, Refresh action, `DisabledDisplays` switches, all-disabled warning guardrail) and Section 4 Overlay & Appearance (Opacity `25–100%`, visual 5-Zone Safe Anchor Grid + `16–120px` Taskbar Safe Margin slider, `System`/`Dark`/`Light` ThemeVariant switch, Media Source Policy & Blocked Apps, Secondary Bilingual Line mode, and OBS Broadcast Window launcher).
- [ ] **UI-07**: `SettingsView` Section 5 Accessibility (`Reduced Motion` auto-synced with Windows `SPI_GETCLIENTAREAANIMATION` + `High-Contrast Text Scrim` toggle & `Off`/`Subtle`/`Balanced`/`Strong` preset) and Section 6 Global Hotkeys (interactive Keycap Recorder requiring modifier, Backspace clear, `Esc` cancel, and inline `RegisterHotKey` conflict error feedback).
- [ ] **UI-08**: `SettingsView` sticky footer with two-step inline `"Reset Defaults"` confirmation (5s auto-revert, zero jarring modal popups) plus off-screen bounds validation when restoring `GalleryWindow` across changed monitor topologies.

### Accessibility & User Experience (`UX`)

- [ ] **UX-01**: Complete keyboard-only operability across `GalleryWindow` and `SettingsView` (`Tab`/`Shift+Tab`, Arrow keys across 2-column cards, font tiles & safe zones, `Enter`/`Space`, `Esc` with focus restoration, `Ctrl+,`, `Ctrl+Left/Right/0`) with visible `2px` `LpFocusRingBrush` (`>=9.0:1`), zero sticky-bar focus obscuration, `AutomationProperties.Name` on all interactive controls, and polite live status announcements.
- [ ] **UX-02**: All 6 Universal Content States (Loading, Empty/Idle, Error, Partial/Unsynced, Success, Permission/Boundary) handled cleanly across Gallery, Settings, and Tray with human-readable copy and non-destructive recovery actions.
- [ ] **UX-03**: Audited WCAG 2.2 AA/AAA visual compliance across Dark & Light themes (`>=4.5:1` normal text, `>=3.0:1` UI boundaries/icons/focus rings, `>=7.0:1` overlay edge legibility on `#FFFFFF`, `>=11.37:1` High-Contrast Scrim, `>=5.44:1` Messages bubbles, zero emojis in structural chrome, `FontFeatures="tnum"` on all numeric telemetry, zero layout shift on hover/active).
- [ ] **UX-04**: End-to-end UX journey verification across `100%`, `125%`, `150%`, `200%` Windows DPI and `760×600`, `1000×740`, `1360×860` viewports covering first-run idle onboarding, live Spotify/Apple Music/Universal playback, style auditioning & tray pinning, timing nudge, multi-monitor toggling, Reduced Motion, and silent Tray-only operation.

### Quality Assurance, Performance & Packaging (`QA`)

- [ ] **QA-01**: Deterministic offline `LyricPulse.Tests` suite (`xunit.v3`, `AwesomeAssertions`, `NSubstitute`, `coverlet.collector`) achieving `>=80%` line and branch coverage on `LyricPulse.Core` (GSMTC selection/hysteresis, monotonic clock drift/seek, 7-pass normalization, LRCLIB/NetEase waterfall/retry/cache, LRC/YRC/proportional parser, offset sign & persistence).
- [ ] **QA-02**: Automated SkiaSharp rendering & Win32 verification suite covering SkSL shader compilation + offscreen pixel output, 5-cover synthetic OKLab K-Means stress tests, spring timestep invariance (60/120/jittered Hz), shader comfort ceilings (`iStrength <= 0.40`, `|iVelocity| <= 22 * dpiScale`), Reduced Motion zero-shader assertions, and Windows click-through/topmost/no-focus smoke checks.
- [ ] **QA-03**: **Milestone 1A (Phase 3) Verification Gate**: Full runnable 3-style MVP (`Fisheye`, `Codestyle`, `Visual Stack`) + System Tray audited by `qa-test-engineer`, `ux-evaluator`, and `ui-auditor` (live playback sync, white-page & dark-IDE legibility, zero click capture, Tray toggle/offset/style switching) prior to CEO mid-project hands-on review.
- [ ] **QA-04**: Offline NLP & 10-style test verification suite achieving `>=80%` combined coverage across `LyricPulse.Core` & `LyricPulse.Rendering`, asserting SIMD `TensorPrimitives.Dot` parity against scalar reference (`±1e-5`), bigram/fallback matching, 12/12 `Neon Horizon` keyword hits, and deterministic layout/Reduced Motion checks for all 10 kinetic styles.
- [ ] **QA-05**: Comprehensive Phase 6 QA/UX/UI audit and 10-minute animated soak test across all 10 styles verifying `60+ FPS` (`<= 4.5 ms` 1080p frame budget, GPU P95 `<= 6 ms`), `<3%` idle CPU when paused, `<150 MB` working set with flat native memory trend, and all 15 Windows 10/11 pitfalls mitigated.
- [ ] **QA-06**: Clean Windows `win-x64` release publish verification for both **Self-Contained Single-File** and **NativeAOT** (`-p:UseNativeAot=true`) profiles verifying bundled SIL OFL fonts, MIT Fluent icons, NLP weights, native DLLs (`av_libglesv2.dll`, `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`), single-instance IPC, and zero trim/AOT exceptions for CEO final release sign-off.

---

## v2 / Deferred & Permanently Out of Scope

### Deferred to v1.1 / v2 (Tracked Cleanly — Not Blocking v1.0)

- **DEF-01 — Interactive Unlock-to-Drag Overlay Placement**: Toggle `WS_EX_TRANSPARENT` off temporarily (`Ctrl+Shift+P`) with dashed bounding box & resize handles to drag/resize overlays freely (normalized per-monitor coordinates are already reserved in `AppSettings` in v1.0).
- **DEF-02 — Optional WASAPI Loopback Peak-Meter Audio Reactivity**: Opt-in `IAudioMeterInformation.GetPeakValue()` envelope feeding `RenderFrameContext.AudioEnergy` for subtle lens/star/node beat breathing (respecting Reduced Motion).
- **DEF-03 — Hotkey Quick-Scrub Mini HUD**: Translucent scrollable lyric sheet + transport controls calling GSMTC `TryChangePlaybackPositionAsync` to jump playback to any clicked line.
- **DEF-04 — One-Click Shareable Lyric Poster Export**: Offscreen `1080×1080` SkiaSharp PNG card renderer copying current kinetic lyric + album gradient to Windows Clipboard.
- **DEF-05 — Launch-at-Login Windows Startup Integration**: Opt-in startup task/registry toggle (deferred per `PROJECT.md` unless requested by CEO).
- **DEF-06 — LRCLIB v2 Beta Search Optimization**: Enable `GET /api/v2/search` + `GET /api/get/{id}` once LRCLIB promotes v2 out of beta (`UseLrclibV2Search` flag).
- **DEF-07 — macOS / Linux Platform Ports**: Implement `IMediaSessionProvider` (MPRIS / NowPlaying) and `IOverlayWindowPlatform` (Wayland/X11/AppKit) behind v1.0's clean abstractions.

### Permanently Out of Scope

- **OOS-01 — Paid Licensing / DRM / Telemetry**: LyricPulse is 100% free, open-source, and offline-respecting; zero license gates, telemetry beacons, or Polar.sh/payment hooks.
- **OOS-02 — Proprietary Asset Copying or Binary Extraction**: Strict clean-room implementation using exclusively open-source assets (SIL OFL fonts, MIT Fluent System Icons, PDDL 1.0 GloVe, Unicode CLDR, public lyrics APIs).

---

## Requirement-to-Phase Traceability

**Coverage Summary:** **61 / 61** v1 requirements mapped (100% coverage, 0 orphans, 0 duplicates).

| Requirement | Category | Phase | Status |
|---|---|---|---|
| `CORE-01` | Core Engine | Phase 1 | Pending |
| `CORE-02` | Core Engine | Phase 1 | Pending |
| `CORE-03` | Core Engine | Phase 1 | Pending |
| `CORE-04` | Core Engine | Phase 1 | Pending |
| `CORE-05` | Core Engine | Phase 1 | Pending |
| `CORE-06` | Core Engine | Phase 1 | Pending |
| `CORE-07` | Core Engine | Phase 1 | Pending |
| `LYR-01` | Synced Lyrics | Phase 1 | Pending |
| `LYR-02` | Synced Lyrics | Phase 1 | Pending |
| `LYR-03` | Synced Lyrics | Phase 1 | Pending |
| `LYR-04` | Synced Lyrics | Phase 1 | Pending |
| `LYR-05` | Synced Lyrics | Phase 1 | Pending |
| `LYR-06` | Synced Lyrics | Phase 1 | Pending |
| `LYR-07` | Synced Lyrics | Phase 1 | Pending |
| `QA-01` | Quality & Testing | Phase 1 | Pending |
| `WIN-01` | Win32 & System | Phase 2 | Pending |
| `WIN-02` | Win32 & System | Phase 2 | Pending |
| `WIN-03` | Win32 & System | Phase 2 | Pending |
| `WIN-04` | Win32 & System | Phase 2 | Pending |
| `WIN-05` | Win32 & System | Phase 2 | Pending |
| `RND-01` | Rendering & Shaders | Phase 2 | Pending |
| `RND-02` | Rendering & Shaders | Phase 2 | Pending |
| `RND-03` | Rendering & Shaders | Phase 2 | Pending |
| `RND-04` | Rendering & Shaders | Phase 2 | Pending |
| `RND-05` | Rendering & Shaders | Phase 2 | Pending |
| `RND-06` | Rendering & Shaders | Phase 2 | Pending |
| `RND-07` | Rendering & Shaders | Phase 2 | Pending |
| `QA-02` | Quality & Testing | Phase 2 | Pending |
| `VIS-01` | Offline Semantic NLP | Phase 3 | Pending |
| `STY-01` | Kinetic Styles | Phase 3 | Pending |
| `STY-02` | Kinetic Styles | Phase 3 | Pending |
| `STY-03` | Kinetic Styles | Phase 3 | Pending |
| `STY-04` | Kinetic Styles | Phase 3 | Pending |
| `UI-01` | Desktop UI & Tray | Phase 3 | Pending |
| `QA-03` | Quality & Testing | Phase 3 | Pending |
| `VIS-02` | Offline Semantic NLP | Phase 4 | Pending |
| `VIS-03` | Offline Semantic NLP | Phase 4 | Pending |
| `VIS-04` | Offline Semantic NLP | Phase 4 | Pending |
| `STY-05` | Kinetic Styles | Phase 4 | Pending |
| `STY-06` | Kinetic Styles | Phase 4 | Pending |
| `STY-07` | Kinetic Styles | Phase 4 | Pending |
| `STY-08` | Kinetic Styles | Phase 4 | Pending |
| `STY-09` | Kinetic Styles | Phase 4 | Pending |
| `STY-10` | Kinetic Styles | Phase 4 | Pending |
| `STY-11` | Kinetic Styles | Phase 4 | Pending |
| `QA-04` | Quality & Testing | Phase 4 | Pending |
| `WIN-06` | Win32 & System | Phase 5 | Pending |
| `STY-12` | Kinetic Styles | Phase 5 | Pending |
| `UI-02` | Desktop UI & Tray | Phase 5 | Pending |
| `UI-03` | Desktop UI & Tray | Phase 5 | Pending |
| `UI-04` | Desktop UI & Tray | Phase 5 | Pending |
| `UI-05` | Desktop UI & Tray | Phase 5 | Pending |
| `UI-06` | Desktop UI & Tray | Phase 5 | Pending |
| `UI-07` | Desktop UI & Tray | Phase 5 | Pending |
| `UI-08` | Desktop UI & Tray | Phase 5 | Pending |
| `UX-01` | Accessibility & UX | Phase 5 | Pending |
| `UX-02` | Accessibility & UX | Phase 5 | Pending |
| `UX-03` | Accessibility & UX | Phase 6 | Pending |
| `UX-04` | Accessibility & UX | Phase 6 | Pending |
| `QA-05` | Quality & Testing | Phase 6 | Pending |
| `QA-06` | Quality & Testing | Phase 6 | Pending |

---
*Last updated: 2026-11-25 — Authoritative v1.0 requirements baseline*
