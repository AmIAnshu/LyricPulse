# Graph Report - LyricPulse  (2026-11-25)

## Corpus Check
- 166 files · ~170,341 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 23 file(s) not represented in the graph (top: .axaml 7, .ttf 5, .sksl 3)

## Summary
- 2581 nodes · 8513 edges · 109 communities (96 shown, 13 thin omitted)
- Extraction: 78% EXTRACTED · 22% INFERRED · 0% AMBIGUOUS · INFERRED: 1847 edges (avg confidence: 0.88)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `e0dfe472`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Kinetic Styles & Semantic Visuals
- Win32 Overlay & NativeAOT Packaging
- GSMTC Clock & Album Color Sync
- Studio UI, Gallery & System Tray
- Lyrics Pipeline & Core Test Suite
- GPU SkSL Shaders & Font Registry
- Roadmap, RAF Host & Broadcast Scope
- StageViewModel
- RgbColor
- FontAssetRegistry
- FrameRenderPacket
- NetEaseLyricsProvider
- AppSettings
- List
- CancellationToken
- LyricPulse.Core.Abstractions
- StyleRenderContext
- OverlayRuntimeController
- StageWindow
- skiasharp
- TrackIdentity
- .RenderingUtilities_HandleAllBoundaryGuards_NaN_Empty_Fallback_AndDisposal
- Win32Native
- LyricPulse.Core.Models
- Fact
- HotkeyAction
- MultiMonitorOverlayCoordinator
- App
- WindowsGsmtcSessionProvider
- TimeSpan
- .Render
- GloveSemanticVisualMatcher
- App.axaml.cs
- ILogger
- OverlayStyleId
- DisplayOptionViewModel
- TimingOffsetService
- StageCardCanvasHost
- StarterLexiconCatalog
- ReadOnlySpan
- SKCanvas
- StarterLexiconVisualMatcher
- awesomeassertions
- ShaderLibrary
- .ParseLrc
- FrameBudgetTelemetry
- build_glove_index.py
- FullscreenAutoHideMonitor
- TitleNormalizer
- Task
- MediaPlayerKind
- HotkeyBindingViewModel
- MediaSessionSelector
- LrcLibLyricsProvider
- LyricsModels.cs
- MonotonicPlaybackClock
- IDisposable
- ImmutableArray
- AlbumAccentPalette
- .ComputePlacements
- TrayMenuState
- OffscreenSurfacePool
- MediaSessionCoordinator
- TrayIconFactory
- SettingsView
- ObsBroadcastWindow
- ILyricSceneRenderer
- Phase 2: Click-Through Overlay Host, SkiaSharp Pipeline & Shaders
- TrayController
- LegibilityPipeline
- StyleCardViewModel
- ITitleNormalizer
- TrackOffsetStore
- LyricPulse.App.csproj
- .OnKeyDown
- LyricPulse — Milestone 1A (Phases 1–3) UX Audit
- Project State — LyricPulse
- SecondaryLineMode
- OversizedReadStream
- WordTimingInterpolator
- .BuildServiceProvider
- .Enter
- .ResolveIconState
- SpringParams
- LyricPulse.Tests.csproj
- .ScaleAlpha
- SingleInstanceGuard
- LyricPulse.Rendering.csproj
- ClockUpdateKind
- OverlayPlaybackSnapshot
- LyricPulse.Core.csproj
- LyricPulse — Milestone 1A (Phases 1–3) UI & Visual Design Audit
- .TryParse
- LyricsStatus
- MediaPlaybackState
- Phase4ASemanticAndSettingsTests
- MediaHealthKind
- StageFlyoutKind
- StyleCatalog
- Milestone1ARemediationTests
- LyricPulse.UI.csproj
- ConceptCentroidIndex
- ObsChromaKeyMode
- UiModule.cs
- 2. Critical Invariant & Edge-Case Audit Matrix
- OffscreenSurfaceSlot

## God Nodes (most connected - your core abstractions)
1. `StageViewModel` - 209 edges
2. `LyricPulse.Core.Models` - 98 edges
3. `AppSettings` - 94 edges
4. `OverlayRuntimeController` - 89 edges
5. `FontAssetRegistry` - 86 edges
6. `App` - 68 edges
7. `LegibilityPipeline` - 64 edges
8. `FrameRenderPacket` - 62 edges
9. `StyleRenderContext` - 59 edges
10. `LyricsParser` - 57 edges

## Surprising Connections (you probably didn't know these)
- `SkSL Shader Uniform Animation Curves (Motion Spec)` --semantically_similar_to--> `EllipticalBulge SkSL Shader (Rendering Contract)`  [AMBIGUOUS] [semantically similar]
  design-system/lyricpulse/MOTION_AND_STYLES.md → .planning/research/RENDERING_AND_WIN32.md
- `SkSL Shader Uniform Animation Curves (Motion Spec)` --semantically_similar_to--> `13-Tap DirectionalMotionBlur SkSL Shader (Rendering Contract)`  [AMBIGUOUS] [semantically similar]
  design-system/lyricpulse/MOTION_AND_STYLES.md → .planning/research/RENDERING_AND_WIN32.md
- `App Chrome & Stage Font URI Specification (MASTER.md)` --semantically_similar_to--> `FontAssetRegistry & LyricPulseFontCollection (SIL OFL)`  [AMBIGUOUS] [semantically similar]
  design-system/lyricpulse/MASTER.md → .planning/research/RENDERING_AND_WIN32.md
- `1. Milestone 1A User Journey Scorecard` --references--> `OverlayWindow`  [INFERRED]
  .planning/UX_AUDIT.md → src/LyricPulse.App/Platform/OverlayWindow.cs
- ``UX-11` — Missing Windows `<ApplicationIcon>` & Stale/Single-Size Tray Icon Polarity` --references--> `TrayIconFactory`  [INFERRED]
  .planning/UX_AUDIT.md → src/LyricPulse.App/Tray/TrayIconFactory.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Zero-Allocation GPU SkSL Render Pipeline** — planning_research_rendering_and_win32_lyric_canvas_control, planning_research_rendering_and_win32_lyric_draw_operation, planning_research_rendering_and_win32_render_resource_cache, planning_research_rendering_and_win32_sksl_elliptical_bulge_render, planning_research_rendering_and_win32_sksl_motion_blur_render, design_system_lyricpulse_motion_and_styles_four_layer_legibility [EXTRACTED 1.00]
- **Offline Semantic Word-Pictures Engine** — planning_research_core_engine_offline_nlp_pipeline, planning_research_core_engine_simd_word_visual_matcher, planning_research_core_engine_starter_lexicon_fallback, planning_research_developer_additions_phrase_bigram_mood_fallback [EXTRACTED 1.00]
- **Phase 1 Must-Ship Core Differentiators** — planning_research_developer_additions_universal_gsmtc_filter, planning_research_developer_additions_local_lrc_sidecar, planning_research_developer_additions_per_track_saved_offsets, planning_research_core_engine_netease_provider [EXTRACTED 1.00]
- **Real-Time Media-to-Synced-Lyrics Dataflow** — planning_research_core_engine_windows_gsmtc_provider, planning_research_core_engine_media_session_selector, planning_research_core_engine_monotonic_playback_clock, planning_research_core_engine_lyrics_orchestrator, planning_research_core_engine_lyrics_parser_word_timing, planning_research_core_engine_timing_offset_state [EXTRACTED 1.00]
- **Acoustic Studio UI Surfaces (Gallery, Settings, Tray)** — design_system_lyricpulse_master_acoustic_studio_design_system, design_system_lyricpulse_pages_gallery_gallery_window, design_system_lyricpulse_pages_settings_settings_drawer_view, design_system_lyricpulse_pages_tray_tray_controller [EXTRACTED 1.00]
- **10 Kinetic Lyric Style Renderers** — design_system_lyricpulse_motion_and_styles_style_fisheye_pair, design_system_lyricpulse_motion_and_styles_style_codestyle_pair, design_system_lyricpulse_motion_and_styles_style_visual_stack, design_system_lyricpulse_motion_and_styles_style_constellation, design_system_lyricpulse_motion_and_styles_style_spiders_web, design_system_lyricpulse_motion_and_styles_style_node_line, design_system_lyricpulse_motion_and_styles_style_kinetic_wall, design_system_lyricpulse_motion_and_styles_style_messages [EXTRACTED 1.00]
- **Win32 Transparent Click-Through Multi-Monitor Host** — planning_research_rendering_and_win32_win32_clickthrough_interop, planning_research_rendering_and_win32_directcomp_layered_guard, planning_research_rendering_and_win32_multimonitor_dpi_coordinator, planning_research_rendering_and_win32_no_maximized_overshoot_rule, planning_research_rendering_and_win32_single_instance_guard [EXTRACTED 1.00]
- **Cross-Document Contract Alignment Hotspots** — planning_research_core_engine_timing_offset_state, design_system_lyricpulse_motion_and_styles_shader_curves_motion, design_system_lyricpulse_motion_and_styles_oklch_palette_rules_motion, design_system_lyricpulse_master_master_font_asset_uris, design_system_lyricpulse_pages_settings_settings_a11y_hotkeys [INFERRED 0.95]

## Communities (109 total, 13 thin omitted)

### Community 0 - "Kinetic Styles & Semantic Visuals"
Cohesion: 0.17
Nodes (19): Neon Horizon Built-In Demo Song (12-Line LRC), 4-Layer Composed Overlay Legibility Pipeline, SkSL Shader Uniform Animation Curves (Motion Spec), Damped Harmonic Spring & Easing Token System, Styles 3-4: Codestyle & CodestyleVisual, Style 6: Constellation, Styles 1-2: Fisheye & FisheyeVisual, Style 9: KineticWall (3D Drift) (+11 more)

### Community 1 - "Win32 Overlay & NativeAOT Packaging"
Cohesion: 0.18
Nodes (12): Avalonia 11 XAML & Compiled Bindings Standards, Multi-Monitor Selector Cards & 5-Zone Safe Anchor Grid, CsWinRT 2.3.1 AOT GSMTC Projection, Central Package Management & SkiaSharp ABI Lockstep, Fullscreen Game/Presentation Auto-Hide (SHQueryUserNotificationState), 9-Point Safe-Zone Anchors & Unlock-to-Drag Mode, AppBuilder & Win32PlatformOptions Pipeline, Avalonia 11.3.x + SkiaSharp 3.119.x + HarfBuzzSharp Stack (+4 more)

### Community 2 - "GSMTC Clock & Album Color Sync"
Cohesion: 0.19
Nodes (13): Keyboard Navigation, Focus Rings & Live Regions, OKLCH Perceptual Album Color Derivation (Motion Spec), Sticky Media Capsule, Diagnostics Flyout & Timing Bar, AccentPalette Domain Contract, IMediaSessionProvider, MediaSessionSelector (Priority + Hysteresis), MonotonicPlaybackClock & Drift Clamping, SkiaAlbumColorExtractor (OKLab K-Means++ K=8) (+5 more)

### Community 3 - "Studio UI, Gallery & System Tray"
Cohesion: 0.29
Nodes (11): Acoustic Studio Lightbox Design System (MASTER.md), App Chrome & Stage Font URI Specification (MASTER.md), WCAG 2.2 AA/AAA ThemeDictionaries Token System, Live SkiaSharp Style Preview Cards (30/60 FPS Stagger), GalleryWindow (2-Column Live Style Gallery), SettingsView Inline/Overlay Drawer, Typography Picker (2x2 SIL OFL) & ColorPicker Trio, TrayController (Avalonia TrayIcon + NativeMenu) (+3 more)

### Community 4 - "Lyrics Pipeline & Core Test Suite"
Cohesion: 0.24
Nodes (12): Five-Project Solution Topology, LyricPulse.Tests (xUnit v3 + AwesomeAssertions >=80%), Sharded Atomic Disk Lyrics Cache, LRCLIB Lyrics Provider, System.Text.Json AOT Source Generation (LyricsJsonContext), Multi-Provider Lyrics Pipeline (ILyricsOrchestrator), ILyricsParser (Enhanced LRC, YRC & Proportional Timing), NetEase Cloud Music Provider (YRC/LRC) (+4 more)

### Community 5 - "GPU SkSL Shaders & Font Registry"
Cohesion: 0.38
Nodes (7): HarfBuzz Multi-Script Shaping & Overflow Auto-Fit, FontAssetRegistry & LyricPulseFontCollection (SIL OFL), Frame-Budget Telemetry & GPU/Software Fallback Matrix, LyricDrawOperation (ICustomDrawOperation + ISkiaSharpApiLeaseFeature), RenderResourceCache (Zero-Alloc Pool & Disposal), EllipticalBulge SkSL Shader (Rendering Contract), 13-Tap DirectionalMotionBlur SkSL Shader (Rendering Contract)

### Community 6 - "Roadmap, RAF Host & Broadcast Scope"
Cohesion: 0.33
Nodes (5): LyricPulse Platform Vision, 6-Phase GSD Roadmap, OBS / Streamer Broadcast & Chroma-Key Capture Window, Universal GSMTC Mode + Smart Browser/Video Filter, LyricCanvasControl (RequestAnimationFrame Host)

### Community 7 - "StageViewModel"
Cohesion: 0.01
Nodes (145): StageViewModel, ActiveFlyout, ActivePaletteColor1, ActivePaletteColor2, ActivePaletteColor3, ActivePinTooltip, ActiveStyle, ActiveStyleIndex (+137 more)

### Community 8 - "RgbColor"
Cohesion: 0.06
Nodes (17): OklabColorMath, DeterministicPrng, OklabCluster, OklabPaletteExtractor, WeightedPixel, ScrimIntensity, Balanced, Off (+9 more)

### Community 9 - "FontAssetRegistry"
Cohesion: 0.07
Nodes (24): Non-Blocking Phase 4/5 Hygiene Notes (LOW), Blocker Closure Matrix (`UI-01` – `UI-12`), `UI-04` — HarfBuzz Multi-Script Shaping & System Font Fallback Are Unwired (Tofu Boxes on All Non-Latin Lyrics), `UI-16` — `CutoutWord` & `Messages` Bypass HarfBuzz Multi-Script Fallback + Unsprung `FollowingDot` in 4 Styles + `NodeLine` RM Long-Line Overflow, `UX-08` — Live Reading Ergonomics: `Codestyle` Mid-Line Vertical Jump, Offset Toast Contrast/Collision, Pause-Dim Drift, and Unwired Non-Latin Fallback, FallbackFontCacheKey, FontAssetRegistry, AllOpticalMetrics (+16 more)

### Community 10 - "FrameRenderPacket"
Cohesion: 0.09
Nodes (16): FrameRenderPacket, EffectiveHeroFontSizeDip, EffectiveStyle, IsCompactCard, SafeHeroScale, SafePartyEnergy, ShouldDrawStageBackdrop, SpringState (+8 more)

### Community 11 - "NetEaseLyricsProvider"
Cohesion: 0.08
Nodes (16): Remediated During Phase 1 QA Gate (CRITICAL / HIGH), Phase 1: Foundation, Media Engine & Synced Lyrics Core, HttpFetchResult, NetEaseLyricsProvider, Name, PriorityOrder, SourceKind, ProviderHttpResilience (+8 more)

### Community 12 - "AppSettings"
Cohesion: 0.05
Nodes (41): AppSettings, ActiveStyle, AnchorZone, AutoSyncColors, BlockedAppIds, Default, DesktopOverlayEnabled, DisabledDisplays (+33 more)

### Community 13 - "List"
Cohesion: 0.07
Nodes (26): Phase 3: Working 3-Style MVP + Tray Control (Milestone 1A Check-in), Phase 4: Offline GloVe/Fluent/Emoji Semantic Engine + Remaining 7 Kinetic Styles, `UI-09` — 3-Style Choreography & Lifecycle Spec Drifts (`MOTION_AND_STYLES.md` §§6 & 8.1), AestheticsOverlayRenderer, ConstellationStyleRenderer, StyleId, StarNode, FisheyeStyleRenderer (+18 more)

### Community 14 - "CancellationToken"
Cohesion: 0.07
Nodes (19): 3. Architectural Community Map, IAlbumColorExtractor, ILyricsOrchestrator, IMediaSessionProvider, CurrentSession, AlbumArtPayload, MediaSessionSnapshot, FakeMediaSessionProvider (+11 more)

### Community 15 - "LyricPulse.Core.Abstractions"
Cohesion: 0.12
Nodes (11): LyricPulse.Core.Settings, LyricPulse.Core.Serialization, LyricPulse.Core, LyricPulse.Core.Lyrics, LyricPulse.Tests.UI, LyricPulse.Tests, LyricPulse.Core.Lyrics.Dtos, LyricPulse.Core.Abstractions (+3 more)

### Community 16 - "StyleRenderContext"
Cohesion: 0.12
Nodes (6): StyleRenderContext, CodestyleStyleRenderer, StyleId, CodestyleVisualStyleRenderer, StyleId, StyleLayoutHelper

### Community 17 - "OverlayRuntimeController"
Cohesion: 0.10
Nodes (11): LOW Findings, IFullscreenDetector, IWordVisualMatcher, IsReady, Mode, CachedDemoDocument, OverlayRuntimeController, IsDemoMode (+3 more)

### Community 18 - "StageWindow"
Cohesion: 0.07
Nodes (20): 3. Severity-Classified Findings (`b88131e` — Read-Only Audit), CRITICAL / HIGH Findings, LOW Findings, Blocker Closure Matrix (`UI-13` – `UI-17`), HIGH, LOW, MEDIUM, Phase 6 Detailed Findings & Concrete Fixes (+12 more)

### Community 19 - "skiasharp"
Cohesion: 0.17
Nodes (10): LyricPulse.Rendering.Styles, LyricPulse.Rendering.Host, LyricPulse.Rendering.Legibility, LyricPulse.Core.Runtime, LyricPulse.Rendering.Shaders, LyricPulse.Rendering.Fonts, LyricPulse.Rendering.Color, LyricPulse.Rendering.Motion (+2 more)

### Community 20 - "TrackIdentity"
Cohesion: 0.13
Nodes (10): ILyricsCache, DiskLyricsCache, LocalLrcSidecarProvider, Name, PriorityOrder, SourceKind, LyricsOrchestrator, LyricsResult (+2 more)

### Community 21 - ".RenderingUtilities_HandleAllBoundaryGuards_NaN_Empty_Fallback_AndDisposal"
Cohesion: 0.09
Nodes (10): 2. Security, AOT, Resource-Hygiene & Thread-Safety Verification, Blocker-by-Blocker Closure Proof, HIGH Findings (Milestone 1A Gate Blockers), `UI-05` — Per-Frame Unmanaged `SKRoundRect` Leak, Hot-Path `SKImageFilter` Allocations, and UI/Render Thread `List<T>` Race, LyricCanvasControl, IsAnimationLoopActive, IsPaused, TargetMaxFps (+2 more)

### Community 22 - "Win32Native"
Cohesion: 0.10
Nodes (8): Win32 Overlay Host & System Integration (`WIN`), `UI-12` — Secondary Legibility & Typography Craft Polish, MONITORINFOEXW, MsgStruct, PointStruct, RectStruct, Win32Native, Win32OverlayInterop

### Community 23 - "LyricPulse.Core.Models"
Cohesion: 0.09
Nodes (6): LyricPulse.Core.Color, LyricPulse.Core.Media, LyricPulse.Tests.Media, LyricPulse.Core.Models, LyricPulseIndentedJsonContext, LyricPulseJsonContext

### Community 24 - "Fact"
Cohesion: 0.13
Nodes (7): DemoSongProvider, DemoPalette, LyricsDocument, PlaybackLifecycleChoreographer, Milestone1ARemediationTests, PlaybackLifecycleChoreographerTests, TrayAndLaunchTests

### Community 25 - "HotkeyAction"
Cohesion: 0.08
Nodes (20): 2. Remaining Non-Blocking Notes (Post-v1.0 Polish), 3. Verified UX Strengths (Preserve During Remediation), `UX6-02` — Hotkey Recorder Destroys Valid Previous Shortcut on Conflict & Rejects `D0–D9` / `Backspace` / `Delete`, GlobalHotkeyManager, HotkeyCommand, Action, CommandKind, Completion (+12 more)

### Community 26 - "MultiMonitorOverlayCoordinator"
Cohesion: 0.10
Nodes (19): `UI-06` — Runtime `AnchorZone` / `SafeMarginPx` / `PerDisplayBounds` Changes Never Apply to Existing Overlays + Unclamped Offscreen `destRect` Clipping, `UI-10` — Mixed-DPI Secondary Monitor Initial `ApplyPlacement` Ordering + Root DI Retention of Transient `ILyricSceneRenderer` Disposables, 2. Bonus MEDIUM / LOW Fixes Confirmed in `c6420bb`, MEDIUM, `UX-09` — Zero-Interruption Desktop & Multi-Monitor Edge Cases (`_hiddenAnchorWindow` Flash, `WS_EX_APPWINDOW`, Frozen `BoundsConfig`, Mixed-DPI Ordering), `UX-10` — Zero Visual Confirmation When Cycling Styles (`Ctrl+Shift+S`) or Unhiding Lyrics (`Ctrl+Shift+L`) Outside Active Vocals, MultiMonitorOverlayCoordinator, ActiveOverlayCount (+11 more)

### Community 27 - "App"
Cohesion: 0.08
Nodes (21): 1. Build, Test, Smoke & Coverage Summary (`371bfa5`), 1. Build, Test, Smoke & Coverage Summary (`b88131e`), 2. Test Rigor & Phase 1–3 Verification Audit, 3. Severity-Classified Findings (`371bfa5` — Read-Only Audit), CRITICAL Findings, LyricPulse — Quality Assurance Verification Report, `LyricPulse.Rendering` Per-File Coverage Breakdown (`371bfa5`), MEDIUM Findings (+13 more)

### Community 28 - "WindowsGsmtcSessionProvider"
Cohesion: 0.10
Nodes (6): 1. Build, Test & Coverage Summary, `LyricPulse.Core` Per-File Coverage Table (Post-Remediation), GsmtcSessionHandle, Session, WindowsGsmtcSessionProvider, CurrentSession

### Community 29 - "TimeSpan"
Cohesion: 0.15
Nodes (4): LyricsParser, LyricLine, ActiveLyricSlice, Empty

### Community 30 - ".Render"
Cohesion: 0.16
Nodes (4): 4. Recommended Remediation Priority Order, `UI-02` — Layer 0 Radial Ambient Scrim Hard-Clips Into a Boxy Rectangle & Violates Corner Transparency in `Fisheye` / `EllipticalBulge.sksl` and `VisualStack`, PooledSlot, MotionBlurMath

### Community 31 - "GloveSemanticVisualMatcher"
Cohesion: 0.17
Nodes (9): LyricWord, VisualPreference, Balanced, EmojiOnly, FluentIconsOnly, WordVisualMatch, GloveSemanticVisualMatcher, IsReady (+1 more)

### Community 32 - "App.axaml.cs"
Cohesion: 0.11
Nodes (6): LyricPulse.App.Platform, LyricPulse.App, LyricPulse.UI.Views, LyricPulse.UI.Controls, LyricPulse.App.Tray, LyricPulse.UI.ViewModels

### Community 33 - "ILogger"
Cohesion: 0.17
Nodes (3): AppLogMessages, CoreLogMessages, RenderingLogMessages

### Community 34 - "OverlayStyleId"
Cohesion: 0.11
Nodes (17): OverlayStyleId, Codestyle, CodestyleVisual, Constellation, Fisheye, FisheyeVisual, KineticWall, Messages (+9 more)

### Community 35 - "DisplayOptionViewModel"
Cohesion: 0.08
Nodes (13): DisplayMonitorInfo, DisplayOptionViewModel, DisplayKey, DpiScale, FriendlyName, HeaderText, HeightPx, Index (+5 more)

### Community 36 - "TimingOffsetService"
Cohesion: 0.13
Nodes (11): Non-Blocking Findings Logged for Phase 2 / Phase 3 Follow-Up (MEDIUM / LOW — `src/` Left Untouched), 5. Prioritized Feature Additions Schedule (Cross-Referencing `DEVELOPER_ADDITIONS.md`), 1. CRITICAL & HIGH Remediation Verification Matrix, 3. Remaining Non-Blocking Notes (Tracked for Phases 4–6), Re-Verification Addendum (`c6420bb`), `UX-04` — Global Hotkey Conflicts Silently Discarded + Unguarded Offset Hotkeys, Track-Switch Offset Leak, and Non-Smooth Spring Resets, ITrackOffsetStore, TimingOffsetService (+3 more)

### Community 37 - "StageCardCanvasHost"
Cohesion: 0.08
Nodes (22): MEDIUM, `UX6-06` — Second-Instance Wakeup & `Ctrl+Shift+G` Force Global Demo Mode When Paused / Instrumental / No-Lyrics, `UX6-07` — `"Play previews"` Button Is a Dead No-Op Under Reduced Motion, `UX6-08` — `RebuildStyleStrip()` Accumulates `PropertyChanged` Subscriptions on Filter Changes, `UX6-09` — Startup Hotkey Conflicts Are Not Synced to `StageViewModel`, StageBackdropMode, AmbientPartyMesh, PureBlack (+14 more)

### Community 38 - "StarterLexiconCatalog"
Cohesion: 0.14
Nodes (11): LyricTextNormalizationData, IrregularLemmas, Stopwords, SemanticIdiomLexicon, AllMoodConceptIds, Bigrams, StarterLexiconCatalog, Concepts (+3 more)

### Community 39 - "ReadOnlySpan"
Cohesion: 0.15
Nodes (4): QuantizedEmbeddingTable, Dimensions, Version, VocabCount

### Community 40 - "SKCanvas"
Cohesion: 0.15
Nodes (4): MessagesStyleRenderer, StyleId, StageBackdropRenderer, AllowCpuShadersForTesting

### Community 41 - "StarterLexiconVisualMatcher"
Cohesion: 0.19
Nodes (6): StarterLexiconVisualMatcher, IsReady, Mode, CoreInfrastructureTests, TitleNormalizerTests, StarterLexiconVisualMatcherTests

### Community 42 - "awesomeassertions"
Cohesion: 0.14
Nodes (9): LyricPulse.Rendering, LyricPulse.Tests.Rendering, LyricPulse.Tests.Visuals, LyricPulse.Tests.Settings, LyricPulse.Tests.Lyrics, LyricPulse.Tests.Color, RenderingModule, ModuleName (+1 more)

### Community 43 - "ShaderLibrary"
Cohesion: 0.10
Nodes (9): ShaderLibrary, AreAllShadersReady, BulgeCompileError, IsBulgeReady, IsMotionBlurReady, IsPartyMeshReady, MotionBlurCompileError, PartyMeshCompileError (+1 more)

### Community 44 - ".ParseLrc"
Cohesion: 0.14
Nodes (9): CachedLyricsEnvelopeDto, LyricsSourceKind, Demo, DiskCache, LocalSidecar, LrcLib, NetEase, None (+1 more)

### Community 45 - "FrameBudgetTelemetry"
Cohesion: 0.13
Nodes (17): FrameBudgetTelemetry, ActiveBackend, DroppedFramesCount, LastFrameMs, ShaderState, TotalFrames, FrameTelemetrySnapshot, RenderBackendKind (+9 more)

### Community 46 - "build_glove_index.py"
Cohesion: 0.14
Nodes (6): load_glove_text(), main(), _normalize(), postprocess_all_but_the_top_numpy(), postprocess_all_but_the_top_pure_python(), quantize_and_write_lpqv()

### Community 47 - "FullscreenAutoHideMonitor"
Cohesion: 0.12
Nodes (10): FullscreenAutoHideMonitor, IsCurrentlyFullscreen, QueryUserNotificationState, AcceptsNotifications, App, Busy, NotPresent, PresentationMode (+2 more)

### Community 50 - "MediaPlayerKind"
Cohesion: 0.11
Nodes (14): PlayerClassifier, MediaItemKind, Advertisement, Music, PodcastOrLongForm, Unknown, Video, MediaPlayerKind (+6 more)

### Community 51 - "HotkeyBindingViewModel"
Cohesion: 0.10
Nodes (11): HotkeyBindingViewModel, Action, BeginRecordCommand, ClearCommand, DisplayButtonText, ErrorMessage, GestureText, HasError (+3 more)

### Community 52 - "MediaSessionSelector"
Cohesion: 0.29
Nodes (4): MediaSessionSelector, ActiveAumid, SessionCandidate, MediaSessionSelectorTests

### Community 53 - "LrcLibLyricsProvider"
Cohesion: 0.19
Nodes (7): LrclibTrackDto, HttpFetchResult, LrcLibLyricsProvider, Name, PriorityOrder, SourceKind, LyricsProvidersAndOrchestratorTests

### Community 54 - "LyricsModels.cs"
Cohesion: 0.10
Nodes (17): TimingKind, LineSynced, None, PlainUnsynced, WordInterpolated, WordNative, VisualKind, Emoji (+9 more)

### Community 55 - "MonotonicPlaybackClock"
Cohesion: 0.29
Nodes (4): ClockAnchorBox, Empty, MonotonicPlaybackClock, MonotonicPlaybackClockTests

### Community 56 - "IDisposable"
Cohesion: 0.17
Nodes (11): Core Engine, Media Sessions & State (`CORE`), 2.1 Top 10 God Nodes (Highest Structural Degree), IGlobalHotkeyService, ILyricsParser, ILyricsProvider, Name, PriorityOrder, SourceKind (+3 more)

### Community 57 - "ImmutableArray"
Cohesion: 0.21
Nodes (10): LrclibErrorDto, NetEaseLyricBlockDto, NetEaseLyricResponseDto, NetEaseNamedDto, NetEaseSearchResponseDto, NetEaseSearchResultDto, NetEaseSongDto, MediaSourcePolicy (+2 more)

### Community 58 - "AlbumAccentPalette"
Cohesion: 0.24
Nodes (9): AlbumAccentPalette, Default, DemoNeonHorizon, MonochromeStudio, PaletteMorphState, RuntimeStateBox, Initial, IsEffectiveReducedMotion (+1 more)

### Community 59 - ".ComputePlacements"
Cohesion: 0.24
Nodes (5): MonitorDescriptor, OverlayBoundsCalculator, OverlayWindowPlacement, SafeZoneRect, PlatformOverlayAndGuardTests

### Community 60 - "TrayMenuState"
Cohesion: 0.26
Nodes (4): `UX-05` — Unpredictable 3-Way Tray Left-Click, Misleading Paused/Fullscreen Icon & Tooltip States, and No Milestone 1A Universal Player Fallback Toggle, TrayMenuState, TrayOffsetOption, TrayStyleOption

### Community 61 - "OffscreenSurfacePool"
Cohesion: 0.21
Nodes (4): OffscreenSurfacePool, PurgeCount, Phase4BRenderingTests, RenderHostInfrastructureTests

### Community 62 - "MediaSessionCoordinator"
Cohesion: 0.20
Nodes (7): MediaSessionCoordinator, BlockedAppIds, CachedAlbumArt, CurrentSession, IgnoreBrowserVideoSessions, SourcePolicy, MediaSessionCoordinatorTests

### Community 63 - "TrayIconFactory"
Cohesion: 0.24
Nodes (6): TrayIconFactory, TrayIconVisualState, Error, Idle, OverlayHidden, Playing

### Community 64 - "SettingsView"
Cohesion: 0.14
Nodes (13): Accessibility & User Experience (`UX`), Desktop UI — System Tray, Gallery & Settings (`UI`), Offline Semantic Word-to-Icon Engine (`VIS`), Quality Assurance, Performance & Packaging (`QA`), Synced Lyrics Pipeline (`LYR`), v1 Requirements, 1. Knowledge Graph Statistics & Integrity Audit, 2.2 Top Betweenness-Centrality Bridge Nodes (+5 more)

### Community 65 - "ObsBroadcastWindow"
Cohesion: 0.13
Nodes (14): 2. Severity-Classified Findings, Executive Verdict: **FAIL** *(0 CRITICAL, 5 HIGH, 5 MEDIUM, 4 LOW — 5 HIGH UX/A11y/Contrast Blockers Before v1.0 Release Sign-Off)*, HIGH (Release Blockers), LOW, Phase 6 v1.0 Release UX Audit, `UX6-01` — Settings Drawer Always Docks Inline (`380px`), Squeezing `StageWindow` & Clipping Top Bar Below `880px`, `UX6-03` — Bottom 10-Card Strip Unreachable via Keyboard + Missing Settings/Flyout Focus Management, `UX6-04` — Unbound Media Health Dot / Troubleshooting Pill & Missing Active-State Indicators on 7 Control Groups (+6 more)

### Community 66 - "ILyricSceneRenderer"
Cohesion: 0.17
Nodes (8): LyricSceneSnapshot, Empty, ILyricSceneRenderer, StyleId, ILyricSceneRendererFactory, LyricSceneRendererFactory, SolidDiagnosticSceneRenderer, StyleId

### Community 67 - "Phase 2: Click-Through Overlay Host, SkiaSharp Pipeline & Shaders"
Cohesion: 0.14
Nodes (11): Risk 5 — Font Asset Assembly URI Ownership *(MEDIUM — `MASTER.md` vs `RENDERING_AND_WIN32.md`)*, Canonical Architecture & Spec Alignment, Overview, Phase 2: Click-Through Overlay Host, SkiaSharp Pipeline & Shaders, Phase 5: Acoustic Studio Lightbox Gallery, Settings Drawer & ColorPicker, Phase 6: Full QA/UX/UI Audit, Performance Tuning & Release Packaging, Phase Details, Phases (+3 more)

### Community 68 - "TrayController"
Cohesion: 0.18
Nodes (6): MEDIUM Findings, Deferred Phase 4–6 Minor Polish Backlog (Non-Blocking Low Notes), HIGH, `UX-03` — Broken Lyrics State Handling: `PlainOnly` Freezes Forever on Line 1, Every Track Change Flashes False Error, Zero Offline Retry, Wrong `Instrumental` Warning, & Frozen Outros, `UX-06` — Reduced Motion OS Desync, Un-Calmed Mid-Flight Palette Morphs, Inverse Onset Hierarchy Dip, and Blocking Quit Shutdown Race, TrayController

### Community 69 - "LegibilityPipeline"
Cohesion: 0.21
Nodes (6): LegibilityPipeline, CachedBlurFilterCount, CachedShadowBucketCount, CachedShapedBlobCount, ShadowBucketKey, ShadowFilterPair

### Community 70 - "StyleCardViewModel"
Cohesion: 0.13
Nodes (15): StyleCardViewModel, CapabilityChip, Description, DisplayName, HasVisualIcons, IsActive, IsHovered, IsPinned (+7 more)

### Community 72 - "TrackOffsetStore"
Cohesion: 0.37
Nodes (3): TrackOffsetEntryDto, TrackOffsetsEnvelopeDto, TrackOffsetStore

### Community 73 - "LyricPulse.App.csproj"
Cohesion: 0.14
Nodes (13): Avalonia.Desktop, Avalonia.Diagnostics, Avalonia.Win32, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging, Avalonia.Controls.ColorPicker, Avalonia.Fonts.Inter, Avalonia.Skia (+5 more)

### Community 74 - ".OnKeyDown"
Cohesion: 0.18
Nodes (3): Phase 6 Re-Verification Addendum (`e0dfe47`), `UX6-10` — Unmodified `Left` / `Right` Arrow Keys Hijack Keyboard Navigation Inside Settings & Toolbar, Phase5StageAndSettingsTests

### Community 75 - "LyricPulse — Milestone 1A (Phases 1–3) UX Audit"
Cohesion: 0.15
Nodes (12): 1. Milestone 1A User Journey Scorecard, 2. Detailed UX Findings & Concrete Fixes, 4. Recommended Remediation Priority Order (For Milestone 1A Re-Verification), Baseline Findings Summary (`371bfa5` — Historical Record), CRITICAL, Executive Verdict: **PASS WITH NOTES** *(Re-Verified on `70876e0` + `c6420bb` — 0 Critical, 0 High Blockers Remaining; Cleared for Milestone 1A CEO Check-In)*, LOW, LyricPulse — Milestone 1A (Phases 1–3) UX Audit (+4 more)

### Community 76 - "Project State — LyricPulse"
Cohesion: 0.18
Nodes (9): Deferred to v1.1 / v2 (Tracked Cleanly — Not Blocking v1.0), Permanently Out of Scope, Requirement-to-Phase Traceability, Requirements: LyricPulse, v2 / Deferred & Permanently Out of Scope, Progress Snapshot, Project Reference, Project State — LyricPulse (+1 more)

### Community 77 - "SecondaryLineMode"
Cohesion: 0.17
Nodes (12): 10 Kinetic Lyric Styles & Lifecycle Choreography (`STY`), 4. Surprising Connections & Cross-Document Risks (MUST RESOLVE BEFORE CODING), Risk 1 — Timing Offset Sign Inversion *(CRITICAL — `gallery.md`/`tray.md`/`settings.md` vs `CORE_ENGINE.md`)*, Risk 2 — SkSL Shader Uniform Names, Optical Sign & Angle Safety *(HIGH — `RENDERING_AND_WIN32.md` vs `MOTION_AND_STYLES.md`)*, Risk 3 — OKLab / OKLCH Album Palette Extraction Divergence *(HIGH — `CORE_ENGINE.md` vs `MOTION_AND_STYLES.md`)*, Risk 4 — Avalonia, SkiaSharp & `.NET 10` ABI Lockstep *(HIGH — `PROJECT.md` / `CORE_ENGINE.md` / `RENDERING_AND_WIN32.md`)*, Risk 7 — Global Hotkey Defaults & Safe-Zone Anchor Schema *(MEDIUM — `settings.md` vs `DEVELOPER_ADDITIONS.md`)*, Risk 8 — Missing Fields on `AppSettings` (`CORE_ENGINE.md` §6.1) & FontScale Formula *(MEDIUM)* (+4 more)

### Community 78 - "OversizedReadStream"
Cohesion: 0.17
Nodes (6): OversizedReadStream, CanRead, CanSeek, CanWrite, Length, Position

### Community 82 - ".ResolveIconState"
Cohesion: 0.18
Nodes (8): 2. Detailed Findings & Concrete Fixes, CRITICAL, LOW, MEDIUM, `UI-01` — 179 of 180 Fluent Icon Codepoints in `StarterLexicon.tsv` Are Wrong (100% of Demo Keywords Broken), `UI-07` — Light Windows Taskbar Status Dots Fail WCAG 3.0:1 (`2.18:1` / `2.05:1`), Single-Size `32×32` PNG, and Stale Taskbar Polarity on OS Theme Switch, `UI-08` — `Codestyle` Mid-Line Vertical Jump (~21 DIP Snap), Past-Line Horizontal Clipping, and UTF-16 Surrogate Split Risk, `UI-11` — Tray Menu Copy, Paused Icon Semantics, and Menu Rebuild Polish (`pages/tray.md`)

### Community 83 - "SpringParams"
Cohesion: 0.18
Nodes (7): SpringParams, DampingRatio, NaturalFrequency, SafeDamping, SafeMass, SafeStiffness, SpringTokens

### Community 84 - "LyricPulse.Tests.csproj"
Cohesion: 0.20
Nodes (9): AwesomeAssertions, coverlet.collector, Microsoft.NET.Test.Sdk, NSubstitute, xunit.runner.visualstudio, xunit.v3, HarfBuzzSharp.NativeAssets.Win32, SkiaSharp.NativeAssets.Win32 (+1 more)

### Community 85 - ".ScaleAlpha"
Cohesion: 0.27
Nodes (3): HIGH, `UI-03` — `VisualStack` Icon Badges Fail White-Page Contrast (`1.04:1 – 2.72:1`) by Bypassing `LegibilityPipeline`, LegibilityStyleSpec

### Community 87 - "LyricPulse.Rendering.csproj"
Cohesion: 0.22
Nodes (8): HarfBuzzSharp, SkiaSharp, SkiaSharp.HarfBuzz, Avalonia, Avalonia.Skia, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions, Microsoft.NET.Sdk

### Community 88 - "ClockUpdateKind"
Cohesion: 0.25
Nodes (7): ClockUpdateKind, HardSnap, IgnoredJitter, PausedFreeze, SeekSnap, SmoothSlew, ClockUpdateResult

### Community 89 - "OverlayPlaybackSnapshot"
Cohesion: 0.22
Nodes (9): OverlayPlaybackSnapshot, ActiveLineIndex, ActiveWordIndex, CurrentLine, IsEffectiveReducedMotion, IsLyricsVisible, NextLine, PreviousLine (+1 more)

### Community 90 - "LyricPulse.Core.csproj"
Cohesion: 0.25
Nodes (7): Microsoft.Extensions.Http, System.Numerics.Tensors, System.Threading.RateLimiting, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions, Microsoft.Windows.CsWinRT, Microsoft.NET.Sdk

### Community 91 - "LyricPulse — Milestone 1A (Phases 1–3) UI & Visual Design Audit"
Cohesion: 0.25
Nodes (7): 1. Dimension Scorecard, 3. Verified Passing Controls (Preserve During Remediation), Executive Verdict: **PASS WITH NOTES** *(Re-Verified on `70876e0` + `c6420bb` — Cleared for Milestone 1A Sign-Off)*, Initial Baseline Summary (Pre-Remediation), LyricPulse — Milestone 1A (Phases 1–3) UI & Visual Design Audit, Re-Verification Addendum (`c6420bb`), Remaining Non-Blocking Low Notes (Deferrable to Phase 4/5)

### Community 93 - "LyricsStatus"
Cohesion: 0.29
Nodes (7): LyricsStatus, Error, Instrumental, Loading, NotFound, PlainOnly, Synced

### Community 94 - "MediaPlaybackState"
Cohesion: 0.29
Nodes (7): MediaPlaybackState, Changing, Closed, Opened, Paused, Playing, Stopped

### Community 96 - "MediaHealthKind"
Cohesion: 0.29
Nodes (7): MediaHealthKind, Error, Idle, Loading, Paused, PlayingSynced, Warning

### Community 97 - "StageFlyoutKind"
Cohesion: 0.29
Nodes (7): StageFlyoutKind, Aesthetics, Diagnostics, Font, None, Screen, Size

### Community 98 - "StyleCatalog"
Cohesion: 0.43
Nodes (5): ColorPresetOption, StyleCatalog, All, StudioPresets, StyleMetadata

### Community 100 - "LyricPulse.UI.csproj"
Cohesion: 0.33
Nodes (5): Avalonia, Avalonia.Controls.ColorPicker, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent, Microsoft.NET.Sdk

### Community 102 - "ConceptCentroidIndex"
Cohesion: 0.40
Nodes (5): ConceptCentroidIndex, ConceptCount, Concepts, Dimensions, Vectors

### Community 103 - "ObsChromaKeyMode"
Cohesion: 0.40
Nodes (4): ObsChromaKeyMode, Black, Blue, Green

### Community 104 - "UiModule.cs"
Cohesion: 0.50
Nodes (3): LyricPulse.UI, UiModule, ModuleName

### Community 105 - "2. Critical Invariant & Edge-Case Audit Matrix"
Cohesion: 0.50
Nodes (4): 2. Critical Invariant & Edge-Case Audit Matrix, 3. Audit Findings, 4. Modified / Added Files Pending Commit, Phase 1 QA Gate

### Community 106 - "OffscreenSurfaceSlot"
Cohesion: 0.50
Nodes (4): OffscreenSurfaceSlot, Bulge, MotionBlur, Scratch

## Ambiguous Edges - Review These
- `SkSL Shader Uniform Animation Curves (Motion Spec)` → `EllipticalBulge SkSL Shader (Rendering Contract)`  [AMBIGUOUS]
  design-system/lyricpulse/MOTION_AND_STYLES.md · relation: semantically_similar_to
- `SkSL Shader Uniform Animation Curves (Motion Spec)` → `13-Tap DirectionalMotionBlur SkSL Shader (Rendering Contract)`  [AMBIGUOUS]
  design-system/lyricpulse/MOTION_AND_STYLES.md · relation: semantically_similar_to
- `Accessibility Toggles & Global Hotkey Keycap Recorder` → `Global System Hotkeys (RegisterHotKey)`  [AMBIGUOUS]
  design-system/lyricpulse/pages/settings.md · relation: semantically_similar_to
- `Multi-Monitor Selector Cards & 5-Zone Safe Anchor Grid` → `9-Point Safe-Zone Anchors & Unlock-to-Drag Mode`  [AMBIGUOUS]
  design-system/lyricpulse/pages/settings.md · relation: semantically_similar_to
- `OKLCH Perceptual Album Color Derivation (Motion Spec)` → `SkiaAlbumColorExtractor (OKLab K-Means++ K=8)`  [AMBIGUOUS]
  design-system/lyricpulse/MOTION_AND_STYLES.md · relation: semantically_similar_to
- `Sticky Media Capsule, Diagnostics Flyout & Timing Bar` → `TimingOffsetState Machine (Per-Song Offset)`  [AMBIGUOUS]
  design-system/lyricpulse/pages/gallery.md · relation: shares_data_with
- `AccentPalette Domain Contract` → `Typography Picker (2x2 SIL OFL) & ColorPicker Trio`  [AMBIGUOUS]
  design-system/lyricpulse/pages/settings.md · relation: shares_data_with
- `TimingOffsetState Machine (Per-Song Offset)` → `Dynamic NativeMenu Hierarchy (Pinned Styles & Timing)`  [AMBIGUOUS]
  design-system/lyricpulse/pages/tray.md · relation: shares_data_with
- `App Chrome & Stage Font URI Specification (MASTER.md)` → `FontAssetRegistry & LyricPulseFontCollection (SIL OFL)`  [AMBIGUOUS]
  design-system/lyricpulse/MASTER.md · relation: semantically_similar_to

## Knowledge Gaps
- **563 isolated node(s):** `ActiveSingleInstanceGuard`, `LaunchOptions`, `Avalonia.Desktop`, `Avalonia.Skia`, `Avalonia.Win32` (+558 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 739 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **13 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `StageViewModel` connect `StageViewModel` to `RgbColor`, `NetEaseLyricsProvider`, `AppSettings`, `OverlayRuntimeController`, `.RenderingUtilities_HandleAllBoundaryGuards_NaN_Empty_Fallback_AndDisposal`, `HotkeyAction`, `MultiMonitorOverlayCoordinator`, `App`, `App.axaml.cs`, `ILogger`, `OverlayStyleId`, `DisplayOptionViewModel`, `TimingOffsetService`, `StageCardCanvasHost`, `Task`, `HotkeyBindingViewModel`, `ImmutableArray`, `TrayController`, `StyleCardViewModel`, `.OnKeyDown`, `SecondaryLineMode`, `OverlayPlaybackSnapshot`, `MediaHealthKind`, `StageFlyoutKind`, `StyleCatalog`, `ObsChromaKeyMode`, `Color`?**
  _High betweenness centrality (0.157) - this node is a cross-community bridge._
- **Are the 18 inferred relationships involving `StageViewModel` (e.g. with `2. Security, AOT, Resource-Hygiene & Thread-Safety Verification` and ``UX6-09` — Startup Hotkey Conflicts Are Not Synced to `StageViewModel``) actually correct?**
  _`StageViewModel` has 18 INFERRED edges - model-reasoned connections that need verification._
- **What connects `ActiveSingleInstanceGuard`, `LaunchOptions`, `Avalonia.Desktop` to the rest of the system?**
  _563 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `StageViewModel` be split into smaller, more focused modules?**
  _Cohesion score 0.013605442176870748 - nodes in this community are weakly interconnected._
- **Why does `AppSettings` connect `AppSettings` to `RgbColor`, `NetEaseLyricsProvider`, `CancellationToken`, `OverlayRuntimeController`, `.RenderingUtilities_HandleAllBoundaryGuards_NaN_Empty_Fallback_AndDisposal`, `LyricPulse.Core.Models`, `Fact`, `HotkeyAction`, `MultiMonitorOverlayCoordinator`, `App`, `GloveSemanticVisualMatcher`, `OverlayStyleId`, `TimingOffsetService`, `StageCardCanvasHost`, `StarterLexiconVisualMatcher`, `Task`, `IDisposable`, `ImmutableArray`, `AlbumAccentPalette`, `.ComputePlacements`, `TrayMenuState`, `OffscreenSurfacePool`, `SettingsView`, `TrackOffsetStore`, `Project State — LyricPulse`, `SecondaryLineMode`, `OverlayPlaybackSnapshot`?**
  _High betweenness centrality (0.062) - this node is a cross-community bridge._

### Low-confidence Hints
_AMBIGUOUS edges — the extractor was unsure. Verify before acting on these._

- **What is the exact relationship between `SkSL Shader Uniform Animation Curves (Motion Spec)` and `EllipticalBulge SkSL Shader (Rendering Contract)`?**
  _Edge tagged AMBIGUOUS (relation: semantically_similar_to) - confidence is low._
- **What is the exact relationship between `SkSL Shader Uniform Animation Curves (Motion Spec)` and `13-Tap DirectionalMotionBlur SkSL Shader (Rendering Contract)`?**
  _Edge tagged AMBIGUOUS (relation: semantically_similar_to) - confidence is low._