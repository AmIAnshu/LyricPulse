# Graph Report - LyricPulse  (2026-11-26)

## Corpus Check
- 178 files · ~190,143 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 23 file(s) not represented in the graph (top: .axaml 7, .ttf 5, .sksl 3)

## Summary
- 2797 nodes · 9340 edges · 111 communities (97 shown, 14 thin omitted)
- Extraction: 78% EXTRACTED · 21% INFERRED · 0% AMBIGUOUS · INFERRED: 2001 edges (avg confidence: 0.87)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `68ea34ca`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Kinetic Styles & Semantic Visuals
- Multi-Monitor & Per-Monitor V2 DPI Overlay Coordinator
- App.axaml.cs
- Studio UI, Gallery & System Tray
- Lyrics Pipeline & Core Test Suite
- RenderResourceCache (Zero-Alloc Pool & Disposal)
- NativeAOT & Self-Contained Publish Profiles
- StageViewModel
- RgbColor
- FontAssetRegistry
- FrameRenderPacket
- NetEaseLyricsProvider
- AppSettings
- MessagesStyleRenderer
- IDisposable
- LrcLibLyricsProvider.cs
- CodestyleVisualStyleRenderer
- OverlayRuntimeController
- StageWindow
- skiasharp
- TrackIdentity
- LyricCanvasControl
- Win32Native
- LyricPulse.Core.Audio
- ConstellationStyleRenderer
- HotkeyAction
- MultiMonitorOverlayCoordinator
- App
- ILogger
- .RegisterHotKey
- LegibilityPipeline
- GloveSemanticVisualMatcher
- LyricPulse.Core.Abstractions
- AlbumArtPayload
- OverlayStyleId
- DisplayOptionViewModel
- TimingOffsetService
- WindowsGsmtcSessionProvider
- StarterLexiconCatalog
- ReadOnlySpan
- Win32OverlayInterop
- StarterLexiconVisualMatcher
- LyricPulse.Core.Lyrics
- .Render
- ShaderLibrary
- FrameBudgetTelemetry
- build_glove_index.py
- FullscreenAutoHideMonitor
- CodestyleStyleRenderer
- Fact
- MediaPlayerKind
- HotkeyBindingViewModel
- MediaSessionSelector
- ImmutableArray
- .TryParse
- MonotonicPlaybackClock
- ScrimIntensity
- MessagesStyleRenderer
- AlbumAccentPalette
- .ComputePlacements
- TrayMenuState
- OffscreenSurfacePool
- MediaSessionCoordinator
- TrayIconFactory
- SingleInstanceGuard
- Phase Details
- TimingKind
- AudioVisualizerMode
- App.axaml.cs
- StyleCardViewModel
- ITitleNormalizer
- Milestone 1A (Phases 1-3) QA Gate
- LyricPulse.App.csproj
- LyricsDocument
- `UX-08` — Live Reading Ergonomics: `Codestyle` Mid-Line Vertical Jump, Offset Toast Contrast/Collision, Pause-Dim Drift, and Unwired Non-Latin Fallback
- Project State — LyricPulse
- VisualStackStyleRenderer
- OversizedReadStream
- TimeSpan
- 4. Surprising Connections & Cross-Document Risks (MUST RESOLVE BEFORE CODING)
- Milestone1ARemediationTests
- MediaItemKind
- SpringParams
- LyricPulse.Tests.csproj
- RgbColor
- .TrayMenuState_FormatsLiveDemoPausedIdleLoadingInstrumentalFullscreenAndErrorStates
- LyricPulse.Rendering.csproj
- ClockUpdateKind
- ThemeVariantMode
- LyricPulse.Core.csproj
- .ResolveIconState
- ProviderDtos.cs
- OverlayPlaybackSnapshot
- MediaPlaybackState
- GloveSemanticVisualMatcher
- MediaHealthKind
- StageFlyoutKind
- GsmtcSessionHandle
- TrayController
- LyricPulse.UI.csproj
- StageBackdropMode
- StageRenderSurfaceMode
- ObsChromaKeyMode
- UiModule.cs
- Sticky Media Capsule, Diagnostics Flyout & Timing Bar
- StyleCatalog
- AppSettings.cs
- EdgeGlowMode

## God Nodes (most connected - your core abstractions)
1. `StageViewModel` - 242 edges
2. `LyricPulse.Core.Models` - 106 edges
3. `AppSettings` - 101 edges
4. `OverlayRuntimeController` - 94 edges
5. `FontAssetRegistry` - 92 edges
6. `RgbColor` - 80 edges
7. `FrameRenderPacket` - 77 edges
8. `StyleRenderContext` - 70 edges
9. `LegibilityPipeline` - 70 edges
10. `App` - 68 edges

## Surprising Connections (you probably didn't know these)
- `SkSL Shader Uniform Animation Curves (Motion Spec)` --semantically_similar_to--> `EllipticalBulge SkSL Shader (Rendering Contract)`  [AMBIGUOUS] [semantically similar]
  design-system/lyricpulse/MOTION_AND_STYLES.md → .planning/research/RENDERING_AND_WIN32.md
- `SkSL Shader Uniform Animation Curves (Motion Spec)` --semantically_similar_to--> `13-Tap DirectionalMotionBlur SkSL Shader (Rendering Contract)`  [AMBIGUOUS] [semantically similar]
  design-system/lyricpulse/MOTION_AND_STYLES.md → .planning/research/RENDERING_AND_WIN32.md
- `App Chrome & Stage Font URI Specification (MASTER.md)` --semantically_similar_to--> `FontAssetRegistry & LyricPulseFontCollection (SIL OFL)`  [AMBIGUOUS] [semantically similar]
  design-system/lyricpulse/MASTER.md → .planning/research/RENDERING_AND_WIN32.md
- `Updated Verification Metrics (`371bfa5` → `c6420bb`)` --references--> `App`  [INFERRED]
  .planning/QA_REPORT.md → src/LyricPulse.App/App.axaml.cs
- `1. Milestone 1A User Journey Scorecard` --references--> `OverlayWindow`  [INFERRED]
  .planning/UX_AUDIT.md → src/LyricPulse.App/Platform/OverlayWindow.cs

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

## Communities (111 total, 14 thin omitted)

### Community 0 - "Kinetic Styles & Semantic Visuals"
Cohesion: 0.17
Nodes (19): Neon Horizon Built-In Demo Song (12-Line LRC), 4-Layer Composed Overlay Legibility Pipeline, SkSL Shader Uniform Animation Curves (Motion Spec), Damped Harmonic Spring & Easing Token System, Styles 3-4: Codestyle & CodestyleVisual, Style 6: Constellation, Styles 1-2: Fisheye & FisheyeVisual, Style 9: KineticWall (3D Drift) (+11 more)

### Community 1 - "Multi-Monitor & Per-Monitor V2 DPI Overlay Coordinator"
Cohesion: 0.18
Nodes (12): Avalonia 11 XAML & Compiled Bindings Standards, Multi-Monitor Selector Cards & 5-Zone Safe Anchor Grid, CsWinRT 2.3.1 AOT GSMTC Projection, Central Package Management & SkiaSharp ABI Lockstep, Fullscreen Game/Presentation Auto-Hide (SHQueryUserNotificationState), 9-Point Safe-Zone Anchors & Unlock-to-Drag Mode, AppBuilder & Win32PlatformOptions Pipeline, Avalonia 11.3.x + SkiaSharp 3.119.x + HarfBuzzSharp Stack (+4 more)

### Community 2 - "App.axaml.cs"
Cohesion: 0.10
Nodes (7): LyricPulse.Rendering, LyricPulse.App, LyricPulse.Core, LyricPulse.App.Tray, ServiceCollectionExtensions, RenderingModule, ModuleName

### Community 3 - "Studio UI, Gallery & System Tray"
Cohesion: 0.29
Nodes (11): Acoustic Studio Lightbox Design System (MASTER.md), App Chrome & Stage Font URI Specification (MASTER.md), WCAG 2.2 AA/AAA ThemeDictionaries Token System, Live SkiaSharp Style Preview Cards (30/60 FPS Stagger), GalleryWindow (2-Column Live Style Gallery), SettingsView Inline/Overlay Drawer, Typography Picker (2x2 SIL OFL) & ColorPicker Trio, TrayController (Avalonia TrayIcon + NativeMenu) (+3 more)

### Community 4 - "Lyrics Pipeline & Core Test Suite"
Cohesion: 0.24
Nodes (12): Five-Project Solution Topology, LyricPulse.Tests (xUnit v3 + AwesomeAssertions >=80%), Sharded Atomic Disk Lyrics Cache, LRCLIB Lyrics Provider, System.Text.Json AOT Source Generation (LyricsJsonContext), Multi-Provider Lyrics Pipeline (ILyricsOrchestrator), ILyricsParser (Enhanced LRC, YRC & Proportional Timing), NetEase Cloud Music Provider (YRC/LRC) (+4 more)

### Community 5 - "RenderResourceCache (Zero-Alloc Pool & Disposal)"
Cohesion: 0.38
Nodes (7): HarfBuzz Multi-Script Shaping & Overflow Auto-Fit, FontAssetRegistry & LyricPulseFontCollection (SIL OFL), Frame-Budget Telemetry & GPU/Software Fallback Matrix, LyricDrawOperation (ICustomDrawOperation + ISkiaSharpApiLeaseFeature), RenderResourceCache (Zero-Alloc Pool & Disposal), EllipticalBulge SkSL Shader (Rendering Contract), 13-Tap DirectionalMotionBlur SkSL Shader (Rendering Contract)

### Community 6 - "NativeAOT & Self-Contained Publish Profiles"
Cohesion: 0.33
Nodes (5): LyricPulse Platform Vision, 6-Phase GSD Roadmap, OBS / Streamer Broadcast & Chroma-Key Capture Window, Universal GSMTC Mode + Smart Browser/Video Filter, LyricCanvasControl (RequestAnimationFrame Host)

### Community 7 - "StageViewModel"
Cohesion: 0.01
Nodes (169): StageViewModel, ActiveFlyout, ActivePaletteColor1, ActivePaletteColor2, ActivePaletteColor3, ActivePinTooltip, ActiveStyle, ActiveStyleIndex (+161 more)

### Community 8 - "RgbColor"
Cohesion: 0.08
Nodes (10): OklabColorMath, DeterministicPrng, OklabCluster, OklabPaletteExtractor, WeightedPixel, OklabColor, OklchColor, SkiaAlbumColorExtractor (+2 more)

### Community 9 - "FontAssetRegistry"
Cohesion: 0.07
Nodes (24): Non-Blocking Phase 4/5 Hygiene Notes (LOW), Blocker Closure Matrix (`UI-01` – `UI-12`), Re-Verification Addendum (`c6420bb`), Remaining Non-Blocking Low Notes (Deferrable to Phase 4/5), `UI-04` — HarfBuzz Multi-Script Shaping & System Font Fallback Are Unwired (Tofu Boxes on All Non-Latin Lyrics), `UI-16` — `CutoutWord` & `Messages` Bypass HarfBuzz Multi-Script Fallback + Unsprung `FollowingDot` in 4 Styles + `NodeLine` RM Long-Line Overflow, `UX-08` — Live Reading Ergonomics: `Codestyle` Mid-Line Vertical Jump, Offset Toast Contrast/Collision, Pause-Dim Drift, and Unwired Non-Latin Fallback, FallbackFontCacheKey (+16 more)

### Community 10 - "FrameRenderPacket"
Cohesion: 0.09
Nodes (17): FrameRenderPacket, EffectiveAudioMetrics, EffectiveHeroFontSizeDip, EffectiveStyle, IsCompactCard, SafeHeroScale, SafePartyEnergy, ShouldDrawStageBackdrop (+9 more)

### Community 11 - "NetEaseLyricsProvider"
Cohesion: 0.07
Nodes (19): Phase 1: Foundation, Media Engine & Synced Lyrics Core, ITitleNormalizer, LrclibTrackDto, HttpFetchResult, LrcLibLyricsProvider, Name, PriorityOrder, SourceKind (+11 more)

### Community 12 - "AppSettings"
Cohesion: 0.05
Nodes (42): AppSettings, ActiveStyle, AnchorZone, AudioVisualizerIntensity, AudioVisualizerMode, AutoSyncColors, BlockedAppIds, Default (+34 more)

### Community 13 - "MessagesStyleRenderer"
Cohesion: 0.06
Nodes (24): Phase 3: Working 3-Style MVP + Tray Control (Milestone 1A Check-in), Phase 4: Offline GloVe/Fluent/Emoji Semantic Engine + Remaining 7 Kinetic Styles, AestheticsOverlayRenderer, EdgeGlowRenderer, FisheyeStyleRenderer, AllowCpuShadersForTesting, StyleId, FisheyeVisualStyleRenderer (+16 more)

### Community 14 - "IDisposable"
Cohesion: 0.14
Nodes (7): Core Engine, Media Sessions & State (`CORE`), ISettingsStore, Current, JsonSettingsStore, Current, TimingOffsetServiceTests, JsonSettingsStoreTests

### Community 15 - "LrcLibLyricsProvider.cs"
Cohesion: 0.29
Nodes (3): LyricPulse.Tests.Audio, LyricPulse.Core.Serialization, LyricPulse.Core.Lyrics.Dtos

### Community 16 - "CodestyleVisualStyleRenderer"
Cohesion: 0.20
Nodes (3): CodestyleVisualStyleRenderer, StyleId, StyleLayoutHelper

### Community 17 - "OverlayRuntimeController"
Cohesion: 0.10
Nodes (12): LOW Findings, MEDIUM Findings, 1. CRITICAL & HIGH Remediation Verification Matrix, `UX-04` — Global Hotkey Conflicts Silently Discarded + Unguarded Offset Hotkeys, Track-Switch Offset Leak, and Non-Smooth Spring Resets, `UX-06` — Reduced Motion OS Desync, Un-Calmed Mid-Flight Palette Morphs, Inverse Onset Hierarchy Dip, and Blocking Quit Shutdown Race, IFullscreenDetector, ILyricsOrchestrator, OverlayRuntimeController (+4 more)

### Community 18 - "StageWindow"
Cohesion: 0.06
Nodes (25): LOW Findings, Blocker Closure Matrix (`UI-13` – `UI-17`), Phase 6 Dimension Scorecard, `UI-14` — Hidden `StageWindow` (`OnClosing` → Tray) Never Pauses `_heroHost` + Unreachable `IsFullscreen` Branch & Over-Wide Strip Viewport Floor, 1. Remediation Verification Matrix (`e0dfe47`), 1. v1.0 User Journey & CEO Requirements Scorecard, 2. Severity-Classified Findings, Executive Verdict: **FAIL** *(0 CRITICAL, 5 HIGH, 5 MEDIUM, 4 LOW — 5 HIGH UX/A11y/Contrast Blockers Before v1.0 Release Sign-Off)* (+17 more)

### Community 19 - "skiasharp"
Cohesion: 0.21
Nodes (7): LyricPulse.Rendering.Styles, LyricPulse.Rendering.Host, LyricPulse.Rendering.Legibility, LyricPulse.Core.Runtime, LyricPulse.Rendering.Fonts, LyricPulse.Rendering.Color, LyricPulse.Rendering.Motion

### Community 20 - "TrackIdentity"
Cohesion: 0.08
Nodes (24): 2. Critical Invariant & Edge-Case Audit Matrix, ILyricsCache, ILyricsParser, ILyricsProvider, Name, PriorityOrder, SourceKind, DiskLyricsCache (+16 more)

### Community 21 - "LyricCanvasControl"
Cohesion: 0.10
Nodes (10): 2. Security, AOT, Resource-Hygiene & Thread-Safety Verification, Blocker-by-Blocker Closure Proof, HIGH, `UI-05` — Per-Frame Unmanaged `SKRoundRect` Leak, Hot-Path `SKImageFilter` Allocations, and UI/Render Thread `List<T>` Race, LyricPulseIndentedJsonContext, LyricPulseJsonContext, LyricCanvasControl, IsAnimationLoopActive (+2 more)

### Community 23 - "LyricPulse.Core.Audio"
Cohesion: 0.13
Nodes (4): LyricPulse.Core.Color, LyricPulse.Core.Models, LyricPulse.Tests.Color, LyricPulse.Core.Audio

### Community 24 - "ConstellationStyleRenderer"
Cohesion: 0.21
Nodes (3): ConstellationStyleRenderer, StyleId, StarNode

### Community 25 - "HotkeyAction"
Cohesion: 0.09
Nodes (17): GlobalHotkeyManager, HotkeyCommand, Action, CommandKind, Completion, ErrorMessage, Parsed, Succeeded (+9 more)

### Community 26 - "MultiMonitorOverlayCoordinator"
Cohesion: 0.10
Nodes (18): HIGH Findings (Milestone 1A Gate Blockers), `UI-06` — Runtime `AnchorZone` / `SafeMarginPx` / `PerDisplayBounds` Changes Never Apply to Existing Overlays + Unclamped Offscreen `destRect` Clipping, `UI-10` — Mixed-DPI Secondary Monitor Initial `ApplyPlacement` Ordering + Root DI Retention of Transient `ILyricSceneRenderer` Disposables, `UX-09` — Zero-Interruption Desktop & Multi-Monitor Edge Cases (`_hiddenAnchorWindow` Flash, `WS_EX_APPWINDOW`, Frozen `BoundsConfig`, Mixed-DPI Ordering), MultiMonitorOverlayCoordinator, ActiveOverlayCount, OverlayWindow, CanvasControl (+10 more)

### Community 27 - "App"
Cohesion: 0.07
Nodes (14): MEDIUM Findings, 2. Remaining Non-Blocking Notes (Post-v1.0 Polish), MEDIUM, `UX6-07` — `"Play previews"` Button Is a Dead No-Op Under Reduced Motion, `UX6-08` — `RebuildStyleStrip()` Accumulates `PropertyChanged` Subscriptions on Filter Changes, `UX6-09` — Startup Hotkey Conflicts Are Not Synced to `StageViewModel`, `UX-13` — Phase 5 Readiness Bridges (Runtime Hotkey Re-Registration & All-Displays-Disabled Guardrail), App (+6 more)

### Community 28 - "ILogger"
Cohesion: 0.06
Nodes (15): AppLogMessages, IAudioCaptureClient, IAudioClient, IMMDevice, IMMDeviceEnumerator, MMDeviceEnumeratorComObject, WasapiLoopbackAudioProvider, AudioCaptureClientIid (+7 more)

### Community 29 - ".RegisterHotKey"
Cohesion: 0.18
Nodes (5): Win32 Overlay Host & System Integration (`WIN`), Risk 7 — Global Hotkey Defaults & Safe-Zone Anchor Schema *(MEDIUM — `settings.md` vs `DEVELOPER_ADDITIONS.md`)*, Canonical Architecture & Spec Alignment, 3. Verified UX Strengths (Preserve During Remediation), `UX6-02` — Hotkey Recorder Destroys Valid Previous Shortcut on Conflict & Rejects `D0–D9` / `Backspace` / `Delete`

### Community 30 - "LegibilityPipeline"
Cohesion: 0.15
Nodes (8): `UI-03` — `VisualStack` Icon Badges Fail White-Page Contrast (`1.04:1 – 2.72:1`) by Bypassing `LegibilityPipeline`, LegibilityPipeline, CachedBlurFilterCount, CachedShadowBucketCount, CachedShapedBlobCount, ShadowBucketKey, ShadowFilterPair, ShapedBlobKey

### Community 31 - "GloveSemanticVisualMatcher"
Cohesion: 0.11
Nodes (20): IWordVisualMatcher, IsReady, Mode, LyricWord, VisualKind, Emoji, FluentIcon, VisualMatcherMode (+12 more)

### Community 33 - "AlbumArtPayload"
Cohesion: 0.06
Nodes (15): IMediaSessionProvider, CurrentSession, AlbumArtPayload, MediaSessionSnapshot, FakeMediaSessionProvider, CurrentSession, LastPolicy, Position (+7 more)

### Community 34 - "OverlayStyleId"
Cohesion: 0.13
Nodes (12): SmokeTestRunner, OverlayStyleId, Codestyle, CodestyleVisual, Constellation, Fisheye, FisheyeVisual, KineticWall (+4 more)

### Community 35 - "DisplayOptionViewModel"
Cohesion: 0.14
Nodes (11): DisplayOptionViewModel, DisplayKey, DpiScale, FriendlyName, HeaderText, HeightPx, Index, IsEnabled (+3 more)

### Community 36 - "TimingOffsetService"
Cohesion: 0.14
Nodes (12): 3. Audit Findings, 4. Modified / Added Files Pending Commit, Non-Blocking Findings Logged for Phase 2 / Phase 3 Follow-Up (MEDIUM / LOW — `src/` Left Untouched), Phase 1 QA Gate, Remediated During Phase 1 QA Gate (CRITICAL / HIGH), ITrackOffsetStore, TrackOffsetEntryDto, TrackOffsetsEnvelopeDto (+4 more)

### Community 37 - "WindowsGsmtcSessionProvider"
Cohesion: 0.16
Nodes (3): `UX-02` — Silent First Launch, Unusable Initial Tray Menu (`Menu == null`), and Broken `--demo` / Second-Instance Feedback, WindowsGsmtcSessionProvider, CurrentSession

### Community 38 - "StarterLexiconCatalog"
Cohesion: 0.17
Nodes (11): LyricTextNormalizationData, IrregularLemmas, Stopwords, SemanticIdiomLexicon, AllMoodConceptIds, Bigrams, StarterLexiconCatalog, Concepts (+3 more)

### Community 39 - "ReadOnlySpan"
Cohesion: 0.11
Nodes (9): ConceptCentroidIndex, ConceptCount, Concepts, Dimensions, Vectors, QuantizedEmbeddingTable, Dimensions, Version (+1 more)

### Community 40 - "Win32OverlayInterop"
Cohesion: 0.18
Nodes (4): MONITORINFOEXW, MsgStruct, PointStruct, Win32OverlayInterop

### Community 41 - "StarterLexiconVisualMatcher"
Cohesion: 0.18
Nodes (7): StarterLexiconVisualMatcher, IsReady, Mode, CoreInfrastructureTests, TitleNormalizerTests, PlatformOverlayAndGuardTests, StarterLexiconVisualMatcherTests

### Community 42 - "LyricPulse.Core.Lyrics"
Cohesion: 0.13
Nodes (12): LyricPulse.Tests.Rendering, LyricPulse.Core.Settings, LyricPulse.Core.Media, LyricPulse.Core.Lyrics, LyricPulse.Tests.Visuals, LyricPulse.Tests.Media, LyricPulse.Tests.UI, LyricPulse.Tests (+4 more)

### Community 43 - ".Render"
Cohesion: 0.10
Nodes (6): 4. Recommended Remediation Priority Order, `UI-02` — Layer 0 Radial Ambient Scrim Hard-Clips Into a Boxy Rectangle & Violates Corner Transparency in `Fisheye` / `EllipticalBulge.sksl` and `VisualStack`, StyleRenderContext, MotionBlurMath, StageBackdropRenderer, AllowCpuShadersForTesting

### Community 44 - "ShaderLibrary"
Cohesion: 0.07
Nodes (20): 5. Prioritized Feature Additions Schedule (Cross-Referencing `DEVELOPER_ADDITIONS.md`), LyricSceneSnapshot, Empty, ILyricSceneRenderer, StyleId, ILyricSceneRendererFactory, LyricSceneRendererFactory, SolidDiagnosticSceneRenderer (+12 more)

### Community 45 - "FrameBudgetTelemetry"
Cohesion: 0.13
Nodes (17): FrameBudgetTelemetry, ActiveBackend, DroppedFramesCount, LastFrameMs, ShaderState, TotalFrames, FrameTelemetrySnapshot, RenderBackendKind (+9 more)

### Community 46 - "build_glove_index.py"
Cohesion: 0.14
Nodes (6): load_glove_text(), main(), _normalize(), postprocess_all_but_the_top_numpy(), postprocess_all_but_the_top_pure_python(), quantize_and_write_lpqv()

### Community 47 - "FullscreenAutoHideMonitor"
Cohesion: 0.15
Nodes (10): FullscreenAutoHideMonitor, IsCurrentlyFullscreen, QueryUserNotificationState, AcceptsNotifications, App, Busy, NotPresent, PresentationMode (+2 more)

### Community 49 - "Fact"
Cohesion: 0.10
Nodes (10): Phase 6 Re-Verification Addendum (`e0dfe47`), IAlbumColorExtractor, DemoSongProvider, DemoPalette, LyricsProvidersAndOrchestratorTests, StubHttpMessageHandler, QaGateRegressionAndCoverageTests, Phase3QaPolishRegressionTests (+2 more)

### Community 50 - "MediaPlayerKind"
Cohesion: 0.15
Nodes (10): 1. Build, Test & Coverage Summary, `LyricPulse.Core` Per-File Coverage Table (Post-Remediation), PlayerClassifier, MediaPlayerKind, AppleMusic, BrowserOrGeneric, DedicatedAudioPlayer, Ignored (+2 more)

### Community 51 - "HotkeyBindingViewModel"
Cohesion: 0.10
Nodes (12): HotkeyBindingViewModel, Action, BeginRecordCommand, ClearCommand, DisplayButtonText, ErrorMessage, GestureText, HasError (+4 more)

### Community 52 - "MediaSessionSelector"
Cohesion: 0.29
Nodes (4): MediaSessionSelector, ActiveAumid, SessionCandidate, MediaSessionSelectorTests

### Community 53 - "ImmutableArray"
Cohesion: 0.42
Nodes (3): MediaSourcePolicy, SupportedPlayersOnly, UniversalFallback

### Community 55 - "MonotonicPlaybackClock"
Cohesion: 0.29
Nodes (4): ClockAnchorBox, Empty, MonotonicPlaybackClock, MonotonicPlaybackClockTests

### Community 56 - "ScrimIntensity"
Cohesion: 0.36
Nodes (5): ScrimIntensity, Balanced, Off, Strong, Subtle

### Community 58 - "AlbumAccentPalette"
Cohesion: 0.08
Nodes (19): Baseline Findings Summary (`371bfa5` — Historical Record), Executive Verdict: **PASS WITH NOTES** *(Re-Verified on `70876e0` + `c6420bb` — 0 Critical, 0 High Blockers Remaining; Cleared for Milestone 1A CEO Check-In)*, AudioFrameMetrics, SafeBands, Silent, AudioSpectrumAnalyzer, SampleRateHz, AlbumAccentPalette (+11 more)

### Community 59 - ".ComputePlacements"
Cohesion: 0.20
Nodes (4): MonitorDescriptor, OverlayBoundsCalculator, OverlayWindowPlacement, SafeZoneRect

### Community 60 - "TrayMenuState"
Cohesion: 0.24
Nodes (5): `UX-05` — Unpredictable 3-Way Tray Left-Click, Misleading Paused/Fullscreen Icon & Tooltip States, and No Milestone 1A Universal Player Fallback Toggle, `UX-07` — System Tray Menu Ergonomics, `tray.md` Copy Fidelity, Menu Rebuild Flicker, and Screen-Reader Accelerator Hygiene, TrayMenuState, TrayOffsetOption, TrayStyleOption

### Community 61 - "OffscreenSurfacePool"
Cohesion: 0.15
Nodes (8): OffscreenSurfacePool, PurgeCount, OffscreenSurfaceSlot, Bulge, MotionBlur, Scratch, PooledSlot, FisheyeVisualOverhaulTests

### Community 62 - "MediaSessionCoordinator"
Cohesion: 0.18
Nodes (7): MediaSessionCoordinator, BlockedAppIds, CachedAlbumArt, CurrentSession, IgnoreBrowserVideoSessions, SourcePolicy, MediaSessionCoordinatorTests

### Community 63 - "TrayIconFactory"
Cohesion: 0.24
Nodes (7): `UI-07` — Light Windows Taskbar Status Dots Fail WCAG 3.0:1 (`2.18:1` / `2.05:1`), Single-Size `32×32` PNG, and Stale Taskbar Polarity on OS Theme Switch, TrayIconFactory, TrayIconVisualState, Error, Idle, OverlayHidden, Playing

### Community 64 - "SingleInstanceGuard"
Cohesion: 0.05
Nodes (24): 1. Knowledge Graph Statistics & Integrity Audit, 2.1 Top 10 God Nodes (Highest Structural Degree), 2.2 Top Betweenness-Centrality Bridge Nodes, 2. God Nodes & Cross-Community Bridges (Protect with Strict Interfaces), 3. Architectural Community Map, 6. Knowledge Gaps & Weakly-Connected Components to Monitor, LyricPulse — Graphify Knowledge Graph & Developer Integration Plan Report, Phase 2: Click-Through Overlay Host, SkiaSharp Pipeline & Shaders (+16 more)

### Community 66 - "Phase Details"
Cohesion: 0.29
Nodes (7): Overview, Phase 5: Acoustic Studio Lightbox Gallery, Settings Drawer & ColorPicker, Phase 6: Full QA/UX/UI Audit, Performance Tuning & Release Packaging, Phase Details, Phases, Progress, Roadmap: LyricPulse

### Community 67 - "TimingKind"
Cohesion: 0.33
Nodes (6): TimingKind, LineSynced, None, PlainUnsynced, WordInterpolated, WordNative

### Community 68 - "AudioVisualizerMode"
Cohesion: 0.29
Nodes (7): AudioVisualizerMode, BassHorizon, FluidWave, None, ParticleField, RadialHalo, SpectrumBars

### Community 69 - "App.axaml.cs"
Cohesion: 0.13
Nodes (4): LyricPulse.Rendering.Shaders, LyricPulse.UI.Views, LyricPulse.UI.Controls, LyricPulse.UI.ViewModels

### Community 70 - "StyleCardViewModel"
Cohesion: 0.13
Nodes (15): StyleCardViewModel, CapabilityChip, Description, DisplayName, HasVisualIcons, IsActive, IsHovered, IsPinned (+7 more)

### Community 72 - "Milestone 1A (Phases 1-3) QA Gate"
Cohesion: 0.13
Nodes (14): 1. Build, Test, Smoke & Coverage Summary (`371bfa5`), 1. Build, Test, Smoke & Coverage Summary (`b88131e`), 2. Test Rigor & Phase 1–3 Verification Audit, 3. Severity-Classified Findings (`371bfa5` — Read-Only Audit), 3. Severity-Classified Findings (`b88131e` — Read-Only Audit), CRITICAL Findings, CRITICAL / HIGH Findings, LyricPulse — Quality Assurance Verification Report (+6 more)

### Community 73 - "LyricPulse.App.csproj"
Cohesion: 0.14
Nodes (13): Avalonia.Desktop, Avalonia.Diagnostics, Avalonia.Win32, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging, Avalonia.Controls.ColorPicker, Avalonia.Fonts.Inter, Avalonia.Skia (+5 more)

### Community 74 - "LyricsDocument"
Cohesion: 0.12
Nodes (9): LyricLine, LyricsDocument, ActiveLyricSlice, Empty, CachedDemoDocument, ConstellationOverhaulTests, KineticStylesMvpTests, Milestone1ARemediationTests (+1 more)

### Community 75 - "`UX-08` — Live Reading Ergonomics: `Codestyle` Mid-Line Vertical Jump, Offset Toast Contrast/Collision, Pause-Dim Drift, and Unwired Non-Latin Fallback"
Cohesion: 0.25
Nodes (8): 2. Detailed UX Findings & Concrete Fixes, CRITICAL, LOW, MEDIUM, `UX-01` — `Visual Stack` & Demo Mode Semantic Comprehension Failure (12/12 `Neon Horizon` Icons Wrong + Unreadable White-Page Badges), `UX-10` — Zero Visual Confirmation When Cycling Styles (`Ctrl+Shift+S`) or Unhiding Lyrics (`Ctrl+Shift+L`) Outside Active Vocals, `UX-11` — Missing Windows `<ApplicationIcon>` & Stale/Single-Size Tray Icon Polarity, `UX-12` — Minor Ergonomic & Edge-Case Polish

### Community 76 - "Project State — LyricPulse"
Cohesion: 0.18
Nodes (9): Deferred to v1.1 / v2 (Tracked Cleanly — Not Blocking v1.0), Permanently Out of Scope, Requirement-to-Phase Traceability, Requirements: LyricPulse, v2 / Deferred & Permanently Out of Scope, Progress Snapshot, Project Reference, Project State — LyricPulse (+1 more)

### Community 77 - "VisualStackStyleRenderer"
Cohesion: 0.13
Nodes (9): `UI-09` — 3-Style Choreography & Lifecycle Spec Drifts (`MOTION_AND_STYLES.md` §§6 & 8.1), SkColorExtensions, LegibilityStyleSpec, RgbColorExtensions, VisualStackRow, VisualStackStyleRenderer, AllowCpuShadersForTesting, StyleId (+1 more)

### Community 78 - "OversizedReadStream"
Cohesion: 0.17
Nodes (6): OversizedReadStream, CanRead, CanSeek, CanWrite, Length, Position

### Community 79 - "TimeSpan"
Cohesion: 0.05
Nodes (15): Accessibility & User Experience (`UX`), Desktop UI — System Tray, Gallery & Settings (`UI`), Offline Semantic Word-to-Icon Engine (`VIS`), Quality Assurance, Performance & Packaging (`QA`), SkiaSharp GPU Rendering, Typography, Color & Shaders (`RND`), Synced Lyrics Pipeline (`LYR`), v1 Requirements, LyricsParser (+7 more)

### Community 80 - "4. Surprising Connections & Cross-Document Risks (MUST RESOLVE BEFORE CODING)"
Cohesion: 0.17
Nodes (8): 4. Surprising Connections & Cross-Document Risks (MUST RESOLVE BEFORE CODING), Risk 1 — Timing Offset Sign Inversion *(CRITICAL — `gallery.md`/`tray.md`/`settings.md` vs `CORE_ENGINE.md`)*, Risk 2 — SkSL Shader Uniform Names, Optical Sign & Angle Safety *(HIGH — `RENDERING_AND_WIN32.md` vs `MOTION_AND_STYLES.md`)*, Risk 3 — OKLab / OKLCH Album Palette Extraction Divergence *(HIGH — `CORE_ENGINE.md` vs `MOTION_AND_STYLES.md`)*, Risk 4 — Avalonia, SkiaSharp & `.NET 10` ABI Lockstep *(HIGH — `PROJECT.md` / `CORE_ENGINE.md` / `RENDERING_AND_WIN32.md`)*, Risk 5 — Font Asset Assembly URI Ownership *(MEDIUM — `MASTER.md` vs `RENDERING_AND_WIN32.md`)*, Risk 6 — Custom Color Role Labels in `SettingsView` *(MEDIUM — `settings.md` vs `MOTION_AND_STYLES.md`)*, LyricPulseFontCollection

### Community 82 - "MediaItemKind"
Cohesion: 0.33
Nodes (6): MediaItemKind, Advertisement, Music, PodcastOrLongForm, Unknown, Video

### Community 83 - "SpringParams"
Cohesion: 0.18
Nodes (7): SpringParams, DampingRatio, NaturalFrequency, SafeDamping, SafeMass, SafeStiffness, SpringTokens

### Community 84 - "LyricPulse.Tests.csproj"
Cohesion: 0.20
Nodes (9): AwesomeAssertions, coverlet.collector, Microsoft.NET.Test.Sdk, NSubstitute, xunit.runner.visualstudio, xunit.v3, HarfBuzzSharp.NativeAssets.Win32, SkiaSharp.NativeAssets.Win32 (+1 more)

### Community 87 - "LyricPulse.Rendering.csproj"
Cohesion: 0.22
Nodes (8): HarfBuzzSharp, SkiaSharp, SkiaSharp.HarfBuzz, Avalonia, Avalonia.Skia, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions, Microsoft.NET.Sdk

### Community 88 - "ClockUpdateKind"
Cohesion: 0.29
Nodes (7): ClockUpdateKind, HardSnap, IgnoredJitter, PausedFreeze, SeekSnap, SmoothSlew, ClockUpdateResult

### Community 89 - "ThemeVariantMode"
Cohesion: 0.20
Nodes (10): HIGH, LOW, MEDIUM, Phase 6 Detailed Findings & Concrete Fixes, Phase 6 Executive Verdict: **FAIL** *(0 Critical, 2 High, 2 Medium, 1 Low — Remediate `UI-13` & `UI-14` Before v1.0 Sign-Off)*, Phase 6 Re-Verification Addendum (`e0dfe47`), Phase 6 v1.0 Release UI Audit, Remaining Non-Blocking Notes Observed in `e0dfe47` (+2 more)

### Community 90 - "LyricPulse.Core.csproj"
Cohesion: 0.25
Nodes (7): Microsoft.Extensions.Http, System.Numerics.Tensors, System.Threading.RateLimiting, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions, Microsoft.Windows.CsWinRT, Microsoft.NET.Sdk

### Community 91 - ".ResolveIconState"
Cohesion: 0.15
Nodes (11): 2. Detailed Findings & Concrete Fixes, 3. Verified Passing Controls (Preserve During Remediation), CRITICAL, Executive Verdict: **PASS WITH NOTES** *(Re-Verified on `70876e0` + `c6420bb` — Cleared for Milestone 1A Sign-Off)*, Initial Baseline Summary (Pre-Remediation), LOW, LyricPulse — Milestone 1A (Phases 1–3) UI & Visual Design Audit, MEDIUM (+3 more)

### Community 92 - "ProviderDtos.cs"
Cohesion: 0.39
Nodes (7): LrclibErrorDto, NetEaseLyricBlockDto, NetEaseLyricResponseDto, NetEaseNamedDto, NetEaseSearchResponseDto, NetEaseSearchResultDto, NetEaseSongDto

### Community 93 - "OverlayPlaybackSnapshot"
Cohesion: 0.10
Nodes (20): `UI-11` — Tray Menu Copy, Paused Icon Semantics, and Menu Rebuild Polish (`pages/tray.md`), HIGH, `UX-03` — Broken Lyrics State Handling: `PlainOnly` Freezes Forever on Line 1, Every Track Change Flashes False Error, Zero Offline Retry, Wrong `Instrumental` Warning, & Frozen Outros, LyricsStatus, Error, Instrumental, Loading, NotFound (+12 more)

### Community 94 - "MediaPlaybackState"
Cohesion: 0.29
Nodes (7): MediaPlaybackState, Changing, Closed, Opened, Paused, Playing, Stopped

### Community 95 - "GloveSemanticVisualMatcher"
Cohesion: 0.20
Nodes (5): `UI-17` — Secondary Craft, Accessibility & Reduced-Motion Polish, GloveSemanticVisualMatcher, IsReady, Mode, Phase4ASemanticAndSettingsTests

### Community 96 - "MediaHealthKind"
Cohesion: 0.29
Nodes (7): MediaHealthKind, Error, Idle, Loading, Paused, PlayingSynced, Warning

### Community 97 - "StageFlyoutKind"
Cohesion: 0.25
Nodes (8): StageFlyoutKind, Aesthetics, Diagnostics, Font, None, Screen, Size, Visualizer

### Community 100 - "LyricPulse.UI.csproj"
Cohesion: 0.33
Nodes (5): Avalonia, Avalonia.Controls.ColorPicker, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent, Microsoft.NET.Sdk

### Community 101 - "StageBackdropMode"
Cohesion: 0.50
Nodes (4): StageBackdropMode, AmbientPartyMesh, PureBlack, TransparentOverlay

### Community 102 - "StageRenderSurfaceMode"
Cohesion: 0.29
Nodes (6): StageRenderSurfaceMode, DesktopOverlay, GalleryCard, ObsBroadcast, StageCanvas, OverlayFrameAdapter

### Community 103 - "ObsChromaKeyMode"
Cohesion: 0.40
Nodes (4): ObsChromaKeyMode, Black, Blue, Green

### Community 104 - "UiModule.cs"
Cohesion: 0.50
Nodes (3): LyricPulse.UI, UiModule, ModuleName

### Community 106 - "Sticky Media Capsule, Diagnostics Flyout & Timing Bar"
Cohesion: 0.19
Nodes (13): Keyboard Navigation, Focus Rings & Live Regions, OKLCH Perceptual Album Color Derivation (Motion Spec), Sticky Media Capsule, Diagnostics Flyout & Timing Bar, AccentPalette Domain Contract, IMediaSessionProvider, MediaSessionSelector (Priority + Hysteresis), MonotonicPlaybackClock & Drift Clamping, SkiaAlbumColorExtractor (OKLab K-Means++ K=8) (+5 more)

### Community 111 - "StyleCatalog"
Cohesion: 0.43
Nodes (5): ColorPresetOption, StyleCatalog, All, StudioPresets, StyleMetadata

### Community 112 - "AppSettings.cs"
Cohesion: 0.18
Nodes (10): 10 Kinetic Lyric Styles & Lifecycle Choreography (`STY`), Risk 8 — Missing Fields on `AppSettings` (`CORE_ENGINE.md` §6.1) & FontScale Formula *(MEDIUM)*, SecondaryLineMode, Off, Romanization, Translation, ThemeVariantMode, Dark (+2 more)

### Community 114 - "EdgeGlowMode"
Cohesion: 0.40
Nodes (5): EdgeGlowMode, BeatPulse, NeonRim, Off, SoftAmbient

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
- `App Chrome & Stage Font URI Specification (MASTER.md)` → `FontAssetRegistry & LyricPulseFontCollection (SIL OFL)`  [AMBIGUOUS]
  design-system/lyricpulse/MASTER.md · relation: semantically_similar_to
- `Dynamic NativeMenu Hierarchy (Pinned Styles & Timing)` → `TimingOffsetState Machine (Per-Song Offset)`  [AMBIGUOUS]
  design-system/lyricpulse/pages/tray.md · relation: shares_data_with

## Knowledge Gaps
- **610 isolated node(s):** `ActiveSingleInstanceGuard`, `LaunchOptions`, `Avalonia.Desktop`, `Avalonia.Skia`, `Avalonia.Win32` (+605 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 796 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **14 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `StageViewModel` connect `StageViewModel` to `NetEaseLyricsProvider`, `AppSettings`, `IDisposable`, `OverlayRuntimeController`, `StageWindow`, `LyricCanvasControl`, `HotkeyAction`, `MultiMonitorOverlayCoordinator`, `App`, `ILogger`, `OverlayStyleId`, `DisplayOptionViewModel`, `Fact`, `HotkeyBindingViewModel`, `ImmutableArray`, `ScrimIntensity`, `.OnKeyDown`, `AudioVisualizerMode`, `App.axaml.cs`, `StyleCardViewModel`, `OverlayPlaybackSnapshot`, `MediaHealthKind`, `StageFlyoutKind`, `StageBackdropMode`, `ObsChromaKeyMode`, `Color`, `StyleCatalog`, `AppSettings.cs`, `EdgeGlowMode`?**
  _High betweenness centrality (0.153) - this node is a cross-community bridge._
- **Are the 21 inferred relationships involving `StageViewModel` (e.g. with `2. Security, AOT, Resource-Hygiene & Thread-Safety Verification` and ``UX6-09` — Startup Hotkey Conflicts Are Not Synced to `StageViewModel``) actually correct?**
  _`StageViewModel` has 21 INFERRED edges - model-reasoned connections that need verification._
- **What connects `ActiveSingleInstanceGuard`, `LaunchOptions`, `Avalonia.Desktop` to the rest of the system?**
  _610 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `App.axaml.cs` be split into smaller, more focused modules?**
  _Cohesion score 0.10052910052910052 - nodes in this community are weakly interconnected._
- **Why does `AppSettings` connect `AppSettings` to `IDisposable`, `OverlayRuntimeController`, `HotkeyAction`, `MultiMonitorOverlayCoordinator`, `App`, `GloveSemanticVisualMatcher`, `OverlayStyleId`, `TimingOffsetService`, `StarterLexiconVisualMatcher`, `ShaderLibrary`, `Fact`, `ImmutableArray`, `ScrimIntensity`, `AlbumAccentPalette`, `.ComputePlacements`, `TrayMenuState`, `SingleInstanceGuard`, `AudioVisualizerMode`, `LyricsDocument`, `Project State — LyricPulse`, `RgbColor`, `OverlayPlaybackSnapshot`, `StageBackdropMode`, `StageRenderSurfaceMode`, `AppSettings.cs`, `EdgeGlowMode`?**
  _High betweenness centrality (0.074) - this node is a cross-community bridge._

### Low-confidence Hints
_AMBIGUOUS edges — the extractor was unsure. Verify before acting on these._

- **What is the exact relationship between `SkSL Shader Uniform Animation Curves (Motion Spec)` and `EllipticalBulge SkSL Shader (Rendering Contract)`?**
  _Edge tagged AMBIGUOUS (relation: semantically_similar_to) - confidence is low._
- **What is the exact relationship between `SkSL Shader Uniform Animation Curves (Motion Spec)` and `13-Tap DirectionalMotionBlur SkSL Shader (Rendering Contract)`?**
  _Edge tagged AMBIGUOUS (relation: semantically_similar_to) - confidence is low._