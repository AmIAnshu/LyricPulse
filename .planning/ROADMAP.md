# Roadmap: LyricPulse

## Overview

LyricPulse will be delivered across **6 sequential phases** transforming a clean-room `.NET 10` + Avalonia UI + SkiaSharp foundation into a zero-friction, 60+ FPS Windows 10/11 kinetic desktop lyrics companion. Phases 1–3 converge rapidly onto **Milestone 1A (Phase 3 Check-in)** — a live, playable 3-style MVP (`Fisheye`, `Codestyle`, `Visual Stack`) controlled via the Windows System Tray so the CEO can test real desktop overlays halfway through development. Phases 4–6 complete the offline GloVe/Fluent/Emoji semantic engine, all 10 kinetic styles, the "Acoustic Studio Lightbox" Style Gallery & Settings Drawer, and final QA/UX/UI soak audits and NativeAOT/self-contained Windows release packaging.

## Canonical Architecture & Spec Alignment

Before Phase 1 begins, all cross-document technical discrepancies identified across `PROJECT.md`, `CORE_ENGINE.md`, `RENDERING_AND_WIN32.md`, `DEVELOPER_ADDITIONS.md`, `MASTER.md`, `MOTION_AND_STYLES.md`, and `pages/{gallery,settings,tray}.md` are locked to a **single canonical engineering contract**:

| # | Spec Area | Discrepancy Resolved | Locked Canonical Decision |
|---:|---|---|---|
| 1 | **Runtime / TFM** | `PROJECT.md` / `MASTER.md` cited `.NET 9` vs. `CORE_ENGINE.md` / `RENDERING_AND_WIN32.md` targeting `.NET 10`. | Lock **`net10.0-windows10.0.19041.0`** (`SupportedOSPlatformVersion=10.0.19041.0`, `LangVersion=latest`, `Microsoft.Windows.CsWinRT 2.3.1`, `CsWinRTAotOptimizerEnabled=true`, `InvariantGlobalization=false`). |
| 2 | **Avalonia & SkiaSharp ABI** | `CORE_ENGINE.md` noted Avalonia `12.2.0` + `SkiaSharp 3.119.0`/`3.119.4` vs. `RENDERING_AND_WIN32.md` & `MASTER.md` on Avalonia `11.3.x` + `SkiaSharp 3.119.4`. | Lock **Avalonia `11.3.x`** (latest stable 11.3 patch verified on NuGet restore) + unified **`SkiaSharp 3.119.4`**, **`SkiaSharp.HarfBuzz 3.119.4`**, **`SkiaSharp.NativeAssets.Win32 3.119.4`**, and **`HarfBuzzSharp 8.3.1.5`** in `Directory.Packages.props` with `CentralPackageTransitivePinningEnabled=true`. Enforce SkiaSharp 3.x `SKFont` & `SKSamplingOptions` APIs exclusively (zero obsolete `SKPaint.TextSize`/`SKFilterQuality`). Keep `LyricPulse.Core` free of SkiaSharp by placing pure OKLab math in `Core` and `SkiaAlbumColorExtractor` in `Rendering`. One-file contingency pin to `SkiaSharp 2.88.9` + `HarfBuzzSharp 7.3.0.3` ONLY if an ABI smoke check fails. |
| 3 | **OKLab Album Color Sync** | `CORE_ENGINE.md` (`K=8`, SHA-256 seed, `L∈[0.64,0.88]`) vs. `MOTION_AND_STYLES.md` (`K=6`, seed `42`, per-role `L` bounds & monochrome guard). | Lock **hybrid canonical pipeline**: `SKCodec` bomb guard (`≤10 MB`, `≤64 MP`), `64×64` `SKSamplingOptions.Linear` resize, achromatic check (`C_p85 < 0.035` → Studio Monochrome `#F1F5F9`/`#CBD5E1`/`#94A3B8`), **OKLab K-Means++ (`K = 8`, SHA-256 deterministic seed, max 12 iters, radial center-weighting)**, and **per-role OKLCH contrast clamps** from `MOTION_AND_STYLES.md` (`Accent1 L∈[0.78,0.92]`, `Accent2 L∈[0.66,0.82]` `ΔH≥38°`, `Accent3 L∈[0.62,0.76]`, `ScrimTint L∈[0.14,0.22]`, `ΔE_OK≥0.16`, 12-step binary-search sRGB gamut mapping, Studio Fallback `#F8FAFC`/`#818CF8`/`#94A3B8`/`#0F0F23`, 650 ms OKLab morph). |
| 4 | **AccentColor1..3 Roles** | `settings.md` labeled `Accent2` as "Active" and `Accent3` as "Glow" vs. `MOTION_AND_STYLES.md` using `Accent1` for Active, `Accent2` for Icons/Glow/Highlights, `Accent3` for Context/Threads. | Lock **`MOTION_AND_STYLES.md` §2.2 roles** across both rendering and Settings labels: `AccentColor1` = Active Word & Primary Lyric; `AccentColor2` = Icons, Glow & Highlights; `AccentColor3` = Upcoming/Past Context Lines & Threads. |
| 5 | **Elliptical Bulge SkSL Sign & Uniforms** | `MOTION_AND_STYLES.md` used `1.0 + iStrength * pow(...)` with positive `iStrength=+0.24` (inverse lookup sign bug — pinches instead of bulges!) vs. `RENDERING_AND_WIN32.md` noting negative sign for convex bulge. | Lock **positive choreography strength `iStrength ∈ [0.0, 0.40]`** (baseline `0.24`, beat peak `~0.32`, line peak `0.36`, hard cap `0.40`, `iPower = 2.10`) with **corrected inverse-lookup SkSL**: `factor = max(1.0 - iStrength * radial, 0.15)` (guaranteeing true convex outward magnification). Standardize uniforms: `EllipticalBulge.sksl` (`content`, `iSize`, `iStrength`, `iPower`) and `DirectionalMotionBlur13.sksl` (`content`, `iVelocity`, 60 Hz reference scaling, `<1.25 * dpiScale` dead-zone bypass, `22 * dpiScale` `tanh` cap). |
| 6 | **Timing Offset Sign & Persistence** | `CORE_ENGINE.md` had `+0.5s = Earlier` (`Position + Offset`) vs. `gallery.md`/`tray.md`/`settings.md` having `−0.5s = Earlier` and `+0.5s = Later`; baseline reset vs. per-track persistence. | Lock **user-intuitive musical sign convention** across Core & UI: **`−0.5s / −1.0s` = Earlier**, **`+0.5s / +1.0s` = Later**, computed as `EffectiveLookupTime = Clamp(InterpolatedPosition - TimingOffset, 0, Duration)`. Support opt-in per-track persistence (`track-offsets.json`, LRU 1,000 tracks) via `EnablePerTrackSavedOffsets` (default **`true`**; caption dynamically shows `"Saved per song"` vs. `"Resets next track"`). |
| 7 | **Default Global Hotkeys** | `DEVELOPER_ADDITIONS.md` proposed `Ctrl+Alt+*` vs. `settings.md`/`tray.md` specifying `Ctrl+Shift+*`. | Lock `settings.md` / `tray.md` defaults: **`Ctrl+Shift+L`** (Show/Hide), **`Ctrl+Shift+S`** (Cycle Pinned Styles), **`Ctrl+Shift+[`** (Earlier `−0.5s`), **`Ctrl+Shift+]`** (Later `+0.5s`), **`Ctrl+Shift+G`** (Open Gallery) — all user-remappable via `RegisterHotKey` (zero `WH_KEYBOARD_LL`). |
| 8 | **Bundled Fonts & Lyric Scale** | `MASTER.md` cited `avares://LyricPulse.UI/Assets/Fonts/` vs. `RENDERING_AND_WIN32.md` centralizing in `LyricPulse.Rendering/Assets/Fonts/`; Settings uses `60–160%` scale vs. `24–96px` px range. | Single bundled font home in **`LyricPulse.Rendering/Assets/Fonts/`** (static TTFs: `BodoniModa-Bold`, `Jost-Bold`, `Mulish-Bold`, `Nunito-SemiBold`, `FluentSystemIcons-Filled`) shared via `LyricPulseFontCollection` & `FontAssetRegistry`. Default user font = **`Jost`** (Styles 3–4 locked to Monospace; Style 10 locked to `Segoe UI Variable`). Store `FontScale` (`0.60–1.60`, default `1.00`, step `0.05`) multiplying style base DIP size (`48 DIP` hero default, obeying `BodoniModa ≥ 36 DIP` floor). |
| 9 | **Overlay Bounds & Safe Zones** | `RENDERING_AND_WIN32.md` showed `screen.Bounds` in snippet vs. `WorkingArea` recommendation; `settings.md` specifies 5-Zone Safe Anchor Grid vs. 9-Point/Drag. | Size per-monitor `OverlayWindow` to **`screen.WorkingArea`** (`WindowState="Normal"`, `CanResize="False"`, physical `PixelPoint` + `GetDpiForMonitor` DIP size) with `SafeMarginPx` (`16–120px`, default `48px`). Ship the visual **5-Zone Safe Anchor Grid** in v1.0 backed by normalized `(AnchorX, AnchorY, SafeMarginPx)` coordinates (reserving Unlock-to-Drag for v1.1). |
| 10 | **Default Pinned Styles & OSS Tests** | Phase 3 has 3 styles vs. v1.0 10 styles (4 default pins); FluentAssertions v8+ is commercial. | Phase 3 MVP defaults to pinning all 3 live styles (`Fisheye`, `Codestyle`, `Visual Stack`); Phase 4–6 v1.0 defaults to 4 pins (`Fisheye`, `Codestyle`, `Constellation`, `Kinetic Wall`). Use **`xunit.v3` + `AwesomeAssertions 9.6.0` (Apache-2.0) + `NSubstitute` + `coverlet.collector`** for 100% open-source licensing hygiene. |

---

## Phases

**Phase Numbering:**
- Integer phases (`1`–`6`): Planned Milestone 1 work
- Decimal phases (`2.1`, `3.1`, etc.): Urgent insertions only (marked with `INSERTED`)

- [ ] **Phase 1: Foundation, Media Engine & Synced Lyrics Core** - Build the `.NET 10` solution scaffold, immutable domain contracts, GSMTC multi-player tracker with monotonic clock, LRCLIB + NetEase (YRC/bilingual) pipeline, atomic disk cache, and ≥80% Core unit tests.
- [ ] **Phase 2: Click-Through Overlay Host, SkiaSharp Pipeline & Shaders** - Build the Win32 click-through multi-monitor Per-Monitor V2 DPI overlay host, global hotkeys, fullscreen auto-hide, GPU SkiaSharp `ICustomDrawOperation` loop, 4-layer legibility, OKLab album color sync, spring physics, and compiled SkSL shaders.
- [ ] **Phase 3: Working 3-Style MVP + Tray Control (Milestone 1A Check-in)** - Ship a live, CEO-testable desktop lyrics app featuring `Fisheye`, `Codestyle`, and `Visual Stack` (powered by embedded `StarterLexicon.tsv`), System Tray `NativeMenu`, and the Milestone 1A QA/UX/UI verification gate.
- [ ] **Phase 4: Offline GloVe/Fluent/Emoji Semantic Engine + Remaining 7 Kinetic Styles** - Add the offline quantized GloVe 100d + Fluent Icons + Unicode CLDR 16.0 SIMD semantic matcher (with bigram idioms & mood fallback) and implement the remaining 7 kinetic lyric styles with full Reduced Motion support.
- [ ] **Phase 5: Acoustic Studio Lightbox Gallery, Settings Drawer & ColorPicker** - Deliver the complete Avalonia 11 Dark/Light UI adhering to `MASTER.md` and `pages/*.md` — 10-card live-animated `Neon Horizon` Style Gallery, Pin-to-Tray, Settings drawer with 2×2 font specimens, 3-accent ColorPicker, per-monitor/safe-zone/hotkey controls, and bilingual/OBS mode polish.
- [ ] **Phase 6: Full QA/UX/UI Audit, Performance Tuning & Release Packaging** - Execute full `qa-test-engineer`, `ux-evaluator`, and `ui-auditor` audits, 10-minute 60+ FPS / <3% idle CPU / <150 MB soak tests, Self-Contained & NativeAOT `win-x64` release builds, and CEO final release sign-off.

---

## Phase Details

### Phase 1: Foundation, Media Engine & Synced Lyrics Core
**Goal**: Establish a rock-solid, AOT-safe `.NET 10` headless engine that accurately tracks playing media from Spotify, Apple Music, or any Windows player, maintains sub-millisecond monotonic playback timing, fetches/caches/parses word-synced and bilingual lyrics offline-first, and proves correctness with ≥80% automated test coverage.
**Depends on**: Nothing (first phase; requires CEO roadmap approval and `.NET 10 SDK` availability)
**Assigned Subagents**:
- **Primary Builder**: `developer-core`
- **Audit Gate**: `qa-test-engineer` (≥80% Core line/branch coverage, immutability, zero live-network test determinism)
**Requirements**: CORE-01, CORE-02, CORE-03, CORE-04, CORE-05, CORE-06, CORE-07, LYR-01, LYR-02, LYR-03, LYR-04, LYR-05, LYR-06, LYR-07, QA-01
**Success Criteria** (what must be TRUE):
  1. Running `dotnet build` and `dotnet test` compiles all 5 projects cleanly under `net10.0-windows10.0.19041.0` with Central Package Management (`Directory.Packages.props`), zero trim/AOT warnings, and `≥80%` unit test coverage on `LyricPulse.Core`.
  2. When Spotify, Apple Music, or another Windows media player is playing, `WindowsGsmtcSessionProvider` deterministically selects the active track (Spotify over Apple Music on simultaneous play, browser video filtered out, ads/empty titles suppressed) and reports smooth, monotonic `GetInterpolatedPosition()` timestamps that freeze cleanly on pause and snap accurately on seeks.
  3. Given raw noisy track titles (e.g., `"Levitating (feat. DaBaby) - Radio Edit"`), the lyrics orchestrator normalizes metadata, checks local `.lrc` sidecars and the sharded atomic disk cache first, falls through LRCLIB (`/api/get` → normalized retry → scored `/api/search`) to NetEase (`yrc` > `lrc` + `tlyric`/`romalrc`), and produces monotonic word-timed `LyricsDocument` instances (native word-sync or proportional syllable fallback).
  4. Nudging timing offset (`−0.5s`/`−1.0s` earlier, `+0.5s`/`+1.0s` later, or `0.0s` reset) shifts `EffectiveLookupTime = Position - Offset` predictably and persists per-track offsets atomically when `EnablePerTrackSavedOffsets` is enabled.
  5. Settings (`settings.json`) and the embedded 12-line `Neon Horizon` demo song load and round-trip cleanly using AOT-safe `System.Text.Json` source generation without reflection.
**Key Deliverables**:
  - `LyricPulse.sln`, `Directory.Build.props`, `Directory.Packages.props`, and project skeletons (`Core`, `Rendering`, `UI`, `App`, `Tests`)
  - Immutable domain models (`LyricPulse.Core.Models`), interfaces (`LyricPulse.Core.Abstractions`), `LyricsJsonContext` & `SettingsJsonContext`
  - `WindowsGsmtcSessionProvider`, `MediaSessionSelector`, `MonotonicPlaybackClock`, bounded album thumbnail fetcher
  - `TitleNormalizer`, `LrclibLyricsProvider`, `NetEaseLyricsProvider`, `LocalSidecarLyricsProvider`, `FileLyricsCache`, `LyricsOrchestrator`, `LyricsParser` (LRC/Enhanced LRC/YRC/proportional + bilingual alignment), `TimingOffsetController`, `TrackOffsetStore`
  - Embedded `Assets/Demo/neon-horizon.lrc` and offline deterministic `LyricPulse.Tests` suite (≥80% coverage)
**Plans**: TBD

### Phase 2: Click-Through Overlay Host, SkiaSharp Pipeline & Shaders
**Goal**: Deliver a flicker-free, zero-focus-stealing, multi-monitor Per-Monitor V2 DPI Windows overlay host backed by a zero-allocation GPU SkiaSharp rendering pipeline, bundled SIL OFL typography, 4-layer desktop legibility, OKLab album-art color syncing, deterministic spring physics, and compiled SkSL shaders with graceful software/Reduced Motion fallbacks.
**Depends on**: Phase 1
**Assigned Subagents**:
- **Primary Builder**: `developer-ui-render` (with `developer-core` contributing pure `OklabColorMath` & `IAlbumColorExtractor` contracts)
- **Audit Gate**: `qa-test-engineer` (SkSL CI pixel tests, OKLab 5-cover stress tests, spring timestep invariance, Win32 click-through smoke check) + `ui-auditor` (4-layer white-page/dark-IDE legibility spot check)
**Requirements**: WIN-01, WIN-02, WIN-03, WIN-04, WIN-05, RND-01, RND-02, RND-03, RND-04, RND-05, RND-06, RND-07, QA-02
**Success Criteria** (what must be TRUE):
  1. Launching the overlay host spawns borderless, transparent, always-on-top click-through windows on every enabled monitor (`screen.WorkingArea`, respecting `DisabledDisplays` and mixed DPI like 150%+100%) where mouse clicks pass straight through to underlying apps, Alt-Tab hides the overlays, no DWM shadow or Win11 corner rounding clips the frame, and launching a second instance activates the running instance in `<20 ms`.
  2. Global hotkeys (`Ctrl+Shift+L`, `Ctrl+Shift+S`, `Ctrl+Shift+[`, `Ctrl+Shift+]`, `Ctrl+Shift+G`) register safely via `RegisterHotKey` (reporting conflicts cleanly without low-level hooks), and entering a fullscreen D3D game or presentation automatically hides overlays and drops GPU rendering to `0%`.
  3. The `LyricCanvasControl` + `ICustomDrawOperation` (`ISkiaSharpApiLeaseFeature`) render loop sustains 60+ FPS with zero per-frame managed GC spikes while active, sleeps at `<1%` CPU when idle, recovers automatically from monitor hotplug/DPI changes/sleep-wake, and falls back cleanly to CPU software rendering when ANGLE GPU is unavailable.
  4. Both SkSL shaders (`EllipticalBulge.sksl` with true convex outward magnification `factor = max(1.0 - iStrength * radial, 0.15)` capped at `0.40`, and `DirectionalMotionBlur13.sksl` capped at `22 * dpiScale`) compile cleanly and render verified pixels onto pooled physical-pixel `SKSurface`s, while bypassing completely under software mode or `ReducedMotion = true`.
  5. All 4 bundled static SIL OFL fonts + MIT `FluentSystemIcons-Filled.ttf` render crisp Latin/CJK/emoji text via HarfBuzz through the 4-layer legibility pipeline (`≥7.0:1` effective edge contrast on pure `#FFFFFF`) using deterministic OKLab `K=8` album-synced accents.
**Key Deliverables**:
  - `LyricPulse.App/app.manifest` (`PerMonitorV2`), `Win32Native.cs` (`[LibraryImport]`), `WindowsOverlayPlatform`, `MultiMonitorOverlayCoordinator`, `SingleInstanceGuard`, `WindowsGlobalHotkeyService`, `FullscreenGameDetector`
  - `AvaloniaBootstrapper`, `LyricCanvasControl`, `LyricDrawOperation`, `RenderResourceCache`, `RenderTelemetrySink`
  - `LyricPulse.Rendering/Assets/Fonts/` (4 static SIL OFL `.ttf` + `FluentSystemIcons-Filled.ttf`), `LyricPulseFontCollection`, `FontAssetRegistry`, HarfBuzz `TextShaperCache`
  - `OverlayLegibilityPipeline` (4-layer renderer), `OklabColorMath` + `SkiaAlbumColorExtractor` (`K=8` + per-role OKLCH bounds), `SpringPhysics` (7 spring tokens + tempo pacing + easing curves)
  - `EllipticalBulge.sksl`, `DirectionalMotionBlur13.sksl`, and Phase 2 automated Skia/OKLab/spring/Win32 test suite
**Plans**: TBD

### Phase 3: Working 3-Style MVP + Tray Control (Milestone 1A Check-in)
**Goal**: Wire Core, Overlay Host, and Rendering end-to-end into a real, daily-usable Windows desktop lyrics app featuring the 3 signature kinetic styles (**Fisheye**, **Codestyle**, and **Visual Stack** backed by the embedded `StarterLexicon.tsv`), full playback lifecycle choreography, and a native System Tray remote control — passing a full QA/UX/UI verification gate for the CEO's mid-project hands-on review.
**Depends on**: Phase 2
**Assigned Subagents**:
- **Core Builder**: `developer-core` (`StarterLexicon.tsv` embedded matcher + `StarterLexiconVisualMatcher`, live pipeline wiring)
- **UI/Render Builder**: `developer-ui-render` (`Fisheye`, `Codestyle`, `Visual Stack` renderers, lifecycle choreography, `TrayController` + adaptive `.ico` assets)
- **Audit Gate (Milestone 1A)**: `qa-test-engineer`, `ux-evaluator`, and `ui-auditor` (full 3-auditor mid-project check-in gate before CEO sign-off)
**Requirements**: VIS-01, STY-01, STY-02, STY-03, STY-04, UI-01, QA-03
**Success Criteria** (what must be TRUE):
  1. Playing any song in Spotify, Apple Music, or another Windows player displays real-time word-synced lyrics directly on the desktop in **Fisheye** (convex spherical lens + vertical motion blur), **Codestyle** (syntax-highlighted monospace typing under a blinking `#EF4444` cursor), or **Visual Stack** (centered word stack with glowing semantic icon badges from embedded `StarterLexicon.tsv`), adapting colors live to the playing album cover.
  2. Right-clicking the Windows System Tray icon opens a zero-emoji native `NativeMenu` displaying Now Playing track/source status, `Show Lyrics` CheckBox toggle, pinned style Radio switches (`Fisheye`, `Codestyle`, `Visual Stack`), `Timing Offset ▸` nudges (`−1.0s`, `−0.5s`, `Reset`, `+0.5s`, `+1.0s`), and `Quit LyricPulse` (leaving zero ghost tray icons on exit).
  3. Pausing music dims and cleanly fades lyrics out after 8 seconds, instrumental gaps show a subtle breathing indicator (or stay clean), track changes crossfade & morph album colors over 650 ms, missing lyrics fade silently to transparent, and offset nudges display a transient 1.2s tabular-mono toast.
  4. All 3 MVP styles pass the Milestone 1A audit by `qa-test-engineer`, `ux-evaluator`, and `ui-auditor` over both `#FFFFFF` web pages and `#0D0D14` dark apps (with Reduced Motion fallbacks verified), giving the CEO a runnable live build to experience firsthand.
**Key Deliverables**:
  - `LyricPulse.Core/Resources/StarterLexicon.tsv` (~300 concepts / ~900 synonyms covering all 12 `Neon Horizon` keywords) & `StarterLexiconVisualMatcher`
  - `FisheyeStyleRenderer`, `CodestyleStyleRenderer`, `VisualStackStyleRenderer`, and `PlaybackLifecycleChoreographer` (pause fade, instrumental break, offset toast, track morph)
  - `TrayController` (`TrayIcon` + dynamic `NativeMenu`), Dark/Light adaptive multi-resolution `.ico` states (Playing, Idle, Hidden, Sync Issue), and end-to-end `LyricPulse.App` host wiring
  - Milestone 1A QA/UX/UI Verification Report & runnable `win-x64` MVP build for CEO check-in
**Plans**: TBD
**UI hint**: yes

### Phase 4: Offline GloVe/Fluent/Emoji Semantic Engine + Remaining 7 Kinetic Styles
**Goal**: Upgrade the offline "Word Pictures" engine to full quantized GloVe 100d + Microsoft Fluent System Icons + Unicode CLDR 16.0 SIMD semantic matching (with phrase bigrams and song-mood fallback) and complete all remaining 7 kinetic lyric styles so LyricPulse achieves full 10-style feature completeness with Reduced Motion parity.
**Depends on**: Phase 3 (Milestone 1A CEO check-in feedback incorporated)
**Assigned Subagents**:
- **Core / NLP Builder**: `developer-core` (`tools/nlp/build_semantic_assets.py`, `glove-100d-q8.bin` loader, SIMD `TensorPrimitives.Dot` matcher, bigram idioms & song-mood fallback)
- **Render Builder**: `developer-ui-render` (remaining 7 SkiaSharp style renderers: `FisheyeVisual`, `CodestyleVisual`, `Constellation`, `SpidersWeb`, `NodeLine`, `KineticWall`, `Messages`)
- **Audit Gate**: `qa-test-engineer` (≥80% combined Core + Rendering coverage, SIMD accuracy parity, 12/12 demo keywords) + `ui-auditor` (visual legibility, motion ceilings, and Reduced Motion checks across all 10 styles)
**Requirements**: VIS-02, VIS-03, VIS-04, STY-05, STY-06, STY-07, STY-08, STY-09, STY-10, STY-11, QA-04
**Success Criteria** (what must be TRUE):
  1. Running `tools/nlp/build_semantic_assets.py` reproducibly generates clean-room `glove-100d-q8.bin`, `glove-vocab.txt`, `fluent-icons.tsv`, and `emoji-index.tsv`; at runtime `GloVeSemanticVisualMatcher` loads off the UI thread, matches unseen poetic words (plus ~150 bigram idioms like `"broken heart"` and line-mood fallbacks) via `TensorPrimitives.Dot` in `<50 µs` per word, and falls back gracefully to `StarterLexicon` if binary weights are absent or corrupted.
  2. All 7 remaining kinetic styles render at 60+ FPS with their signature choreography: **Fisheye Visual** (alternating lens-docked icons), **Codestyle Visual** (pre-allocated inline IDE icon pills), **Constellation** (non-overlapping hash-seeded star-to-star traces), **Spider's Web** (shooting spokes with plucked-strand twang and strictly upright `0°` labels), **Node Line** (sinusoidal comet ribbon with long-line camera pan), **Kinetic Wall** (`SKMatrix44` 3D drifting editorial wall), and **Messages** (phone-screen chat thread locked to `Segoe UI Variable` and WCAG-verified fixed chat bubbles with 450 ms typing indicator).
  3. Switching `ReducedMotion = true` disables all SkSL shaders, 3D perspective matrices, strand twang, camera panning, and cursor hard-blinking across all 10 styles, replacing them with calm `≤150 ms` crossfades.
  4. All 10 styles are selectable from the Tray `All Styles ▸` submenu (with default 4 pinned styles `Fisheye`, `Codestyle`, `Constellation`, `Kinetic Wall`), and automated unit/visual tests achieve `≥80%` coverage across Core & Rendering including 12/12 `Neon Horizon` keyword verification.
**Key Deliverables**:
  - `tools/nlp/build_semantic_assets.py` + prebuilt clean-room binary/TSV assets (`glove-100d-q8.bin`, `glove-vocab.txt`, `fluent-icons.tsv`, `emoji-index.tsv`)
  - `GloVeSemanticVisualMatcher` (`FrozenDictionary` alternate lookup, irregular lyric lemmatizer, `TensorPrimitives.Dot`, bigram dictionary, line-mood fallback, checksum validator)
  - 7 SkiaSharp style renderers: `FisheyeVisualStyleRenderer`, `CodestyleVisualStyleRenderer`, `ConstellationStyleRenderer`, `SpidersWebStyleRenderer`, `NodeLineStyleRenderer`, `KineticWallStyleRenderer`, `MessagesStyleRenderer`
  - Phase 4 NLP SIMD parity tests, 10-style deterministic layout/Reduced Motion tests, and `ui-auditor` 10-style visual review
**Plans**: TBD

### Phase 5: Acoustic Studio Lightbox Gallery, Settings Drawer & ColorPicker
**Goal**: Build the complete, WCAG-verified "Acoustic Studio Lightbox" Avalonia 11 desktop experience strictly adhering to `MASTER.md`, `gallery.md`, `settings.md`, and `tray.md` — letting users audition all 10 live-animated kinetic styles side-by-side, pin favorites to the System Tray, configure fonts/colors/monitors/safe-zones/hotkeys/accessibility in real time, and use bilingual sub-lines and OBS Broadcast Capture Mode.
**Depends on**: Phase 4
**Assigned Subagents**:
- **Primary Builder**: `developer-ui-render` (Avalonia XAML tokens, Fluent `PathIcon` library, `GalleryWindow`, `SettingsView`, `ColorPickerTheme`, `BroadcastWindow`, bilingual sub-line rendering)
- **Supporting Builder**: `developer-core` (ViewModel state orchestration, diagnostic status provider, live hotkey validation & bilingual settings plumbing)
- **Audit Gate**: `ui-auditor` (`MASTER.md` & `pages/*.md` visual/token/contrast/zero-emoji/DPI audit) + `ux-evaluator` (keyboard navigation, focus rings, 6 content states, responsive drawer & onboarding flow) + `qa-test-engineer` (ViewModel/settings round-trip & binding tests)
**Requirements**: WIN-06, STY-12, UI-02, UI-03, UI-04, UI-05, UI-06, UI-07, UI-08, UX-01, UX-02
**Success Criteria** (what must be TRUE):
  1. Opening `GalleryWindow` displays the sticky `64px` Media-Status & Sync Bar (live album art + 3-dot palette strip, source status, `"Not detecting?"`/`"Fix sync"` 3-row live diagnostic flyout, 5-segment `tnum` offset controls, Overlay & Settings toggles) above a responsive 2-column (1-column `<680px`) 10-card live-animated Style Gallery running staggered `Neon Horizon` (or live song) previews at viewport-culled 30/60 FPS with `Dark`/`Light Page`/`Gradient` backdrop switcher, `Pause previews`, and 40×40 Pin-to-Tray buttons that update the Tray menu live.
  2. Opening the Settings drawer (`Ctrl+,`) docks inline beside the live Gallery at `≥880px` (or slides over with scrim `<880px`) and lets users live-preview the 2×2 SIL OFL `"Aa"` font specimen picker, `60–160%` Lyric Scale slider, `Auto-Sync Album` vs. 3-accent themed Avalonia `ColorPicker` (+ 4 Studio Presets), per-monitor enable cards (`DisabledDisplays`), Opacity & 5-Zone Safe Anchor Grid + Taskbar Margin, `System`/`Dark`/`Light` theme switching with zero restart, Reduced Motion & High-Contrast Scrim, interactive Keycap Hotkey Recorder with conflict errors, and two-step inline `Reset Defaults`.
  3. Users can enable optional bilingual Translation/Romanization secondary sub-lines in supported styles (`Codestyle`, `CodestyleVisual`, `NodeLine`, `KineticWall`, `Messages`) and launch the dedicated `LyricPulse Broadcast` OBS capture window (1080p/720p, Transparent Alpha / Chroma Green / Chroma Blue / Solid Black).
  4. Entire `GalleryWindow` and `SettingsView` operate 100% via keyboard (`Tab`, Arrows, `Enter`/`Space`, `Esc`, `Ctrl+,`, `Ctrl+Left/Right/0`) with visible `2px` `LpFocusRingBrush`, zero emojis in structural chrome, `{DynamicResource}` theme compliance, and all 6 Universal Content States verified.
**Key Deliverables**:
  - `Themes/LyricPulseTokens.axaml` (Dark & Light `ThemeDictionaries`), `Themes/ColorPickerTheme.axaml`, `Themes/Icons.axaml` (Fluent System Icons `StreamGeometry`)
  - `GalleryWindow.axaml` + `GalleryViewModel` (sticky media bar, diagnostic popover, utility strip, 10-card staggered live SkiaSharp preview grid, off-screen bounds recovery)
  - `SettingsView.axaml` + `SettingsViewModel` (all 6 sections, 2×2 font specimens, 3-accent ColorPicker, monitor cards, 5-zone anchor grid, hotkey keycap recorder, two-step reset footer)
  - `BroadcastWindow.axaml` (OBS / Streamer Capture Window) and bilingual secondary sub-line rendering in layout-compatible styles
  - Phase 5 ViewModel/Settings unit tests + Phase 5 `ui-auditor` & `ux-evaluator` compliance pass
**Plans**: TBD
**UI hint**: yes

### Phase 6: Full QA/UX/UI Audit, Performance Tuning & Release Packaging
**Goal**: Harden LyricPulse to production-grade Windows desktop quality via dedicated runs of all 3 ECC evaluators (`qa-test-engineer`, `ux-evaluator`, `ui-auditor`), prove 60+ FPS / <3% idle CPU / <150 MB working set under a 10-minute animated soak test, verify clean Self-Contained Single-File and NativeAOT `win-x64` release builds, and obtain final CEO release sign-off.
**Depends on**: Phase 5
**Assigned Subagents**:
- **Primary Evaluators (All 3 ECC Auditors)**:
  - `qa-test-engineer`: Final `≥80%` coverage gate across `Core` + `Rendering`, edge-case & 10-minute memory/FPS soak test, 15 Windows 10/11 pitfall matrix verification, Self-Contained & NativeAOT publish smoke test
  - `ux-evaluator`: End-to-end user journey audit across onboarding, live playback, sync nudging, multi-monitor, Reduced Motion, keyboard-only & screen-reader (Narrator/NVDA) accessibility
  - `ui-auditor`: Final pixel-level Dark/Light WCAG contrast audit (`>=4.5:1` / `>=3.0:1`), multi-DPI (`100%`/`125%`/`150%`/`200%`) & multi-viewport (`760×600`, `1000×740`, `1360×860`) verification, zero-emoji & `tnum` compliance
- **Remediation & Packaging Builders**: `developer-core` & `developer-ui-render` (fix all Critical/High audit findings, optimize frame/memory budgets, finalize release scripts)
**Requirements**: UX-03, UX-04, QA-05, QA-06
**Success Criteria** (what must be TRUE):
  1. `qa-test-engineer` signs off with `≥80%` automated test coverage on `Core` & `Rendering`, zero failing tests, all 15 Windows 10/11 pitfalls verified mitigated, and a 10-minute animated soak test across all 10 styles demonstrating `60+ FPS` (GPU P95 `≤ 6.0 ms`, 1080p `≤ 4.5 ms`), `<3%` idle CPU when paused, and `<150 MB` working set with flat native heap trend.
  2. `ui-auditor` and `ux-evaluator` sign off with zero Critical/High defects across Dark & Light themes (`≥4.5:1` normal text, `≥7.0:1` overlay edge legibility on `#FFFFFF`, `≥11.37:1` High-Contrast Scrim), `100%–200%` Windows DPI scaling, `760×600`/`1000×740`/`1360×860` viewports, keyboard traversal, Reduced Motion, and Tray-only workflows.
  3. Both `dotnet publish -c Release -r win-x64` (Self-Contained Single-File) and `dotnet publish -c Release -r win-x64 -p:UseNativeAot=true` (NativeAOT) build and launch cleanly on Windows, shipping all open-source bundled fonts, Fluent icons, NLP weights, native Skia/ANGLE DLLs, and open-source third-party license notices (`THIRD-PARTY-NOTICES.md`) with zero trim/AOT crashes.
  4. Complete Release Verification Packet is presented to the CEO for final Milestone 1 v1.0 release approval.
**Key Deliverables**:
  - Dedicated `QA_AUDIT_REPORT.md`, `UX_AUDIT_REPORT.md`, and `UI_AUDIT_REPORT.md` (with zero unresolved Critical/High issues)
  - 10-minute animated soak benchmark & memory profile report (`RenderTelemetrySink` metrics)
  - Release build artifacts (`win-x64` Self-Contained Single-File + NativeAOT package), `THIRD-PARTY-NOTICES.md` (SIL OFL, MIT, PDDL 1.0, Unicode CLDR, Apache-2.0 licenses), and `README.md` quickstart
  - CEO Milestone 1 Final Release Sign-Off Gate
**Plans**: TBD
**UI hint**: yes

---

## Progress

**Execution Order:**
Phases execute strictly in numeric order: `1 → 2 → 3 (Milestone 1A CEO Check-in) → 4 → 5 → 6 (Final Release Sign-Off)`.

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Foundation, Media Engine & Synced Lyrics Core | 0/TBD | Not started | - |
| 2. Click-Through Overlay Host, SkiaSharp Pipeline & Shaders | 0/TBD | Not started | - |
| 3. Working 3-Style MVP + Tray Control (Milestone 1A Check-in) | 0/TBD | Not started | - |
| 4. Offline GloVe/Fluent/Emoji Semantic Engine + Remaining 7 Kinetic Styles | 0/TBD | Not started | - |
| 5. Acoustic Studio Lightbox Gallery, Settings Drawer & ColorPicker | 0/TBD | Not started | - |
| 6. Full QA/UX/UI Audit, Performance Tuning & Release Packaging | 0/TBD | Not started | - |

---
*Last updated: 2026-11-25 — Authoritative GSD Milestone 1 Roadmap (61/61 v1 requirements mapped)*
