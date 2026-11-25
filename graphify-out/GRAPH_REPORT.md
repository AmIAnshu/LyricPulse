# Graph Report - LyricPulse  (2026-11-26)

## Corpus Check
- 176 files · ~182,702 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 23 file(s) not represented in the graph (top: .axaml 7, .ttf 5, .sksl 3)

## Summary
- 2765 nodes · 9114 edges · 111 communities (93 shown, 18 thin omitted)
- Extraction: 79% EXTRACTED · 21% INFERRED · 0% AMBIGUOUS · INFERRED: 1940 edges (avg confidence: 0.87)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `b52275c3`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Kinetic Styles & Semantic Visuals
- Multi-Monitor & Per-Monitor V2 DPI Overlay Coordinator
- PreserveSig
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
- Task
- LyricPulse.Core.Abstractions
- StyleRenderContext
- OverlayRuntimeController
- StageWindow
- skiasharp
- TrackIdentity
- LyricCanvasControl
- Win32Native
- LyricPulse.Core.Models
- LyricsDocument
- HotkeyAction
- MultiMonitorOverlayCoordinator
- App
- WindowsGsmtcSessionProvider
- TimeSpan
- ConstellationStyleRenderer
- GloveSemanticVisualMatcher
- LyricPulse.Core.Abstractions
- FakeMediaSessionProvider
- OverlayStyleId
- DisplayOptionViewModel
- TimingOffsetService
- StageCardCanvasHost
- StarterLexiconCatalog
- ReadOnlySpan
- AudioFrameMetrics
- StarterLexiconVisualMatcher
- awesomeassertions
- ShaderLibrary
- .ParseLrc
- FrameBudgetTelemetry
- build_glove_index.py
- FullscreenAutoHideMonitor
- TitleNormalizer
- .CreateStartedControllerAsync
- MediaPlayerKind
- HotkeyBindingViewModel
- MediaSessionSelector
- WasapiLoopbackAudioProvider
- CountingVisualMatcher
- MonotonicPlaybackClock
- .AddLyricPulseCore
- ImmutableArray
- AlbumAccentPalette
- .ComputePlacements
- OverlayPlaybackSnapshot
- OffscreenSurfacePool
- MediaSessionCoordinator
- TrayIconFactory
- SettingsView
- ObsBroadcastWindow
- ILyricSceneRenderer
- Phase 1: Foundation, Media Engine & Synced Lyrics Core
- AppSettings.cs
- StageWindow.axaml.cs
- StyleCardViewModel
- ITitleNormalizer
- Milestone 1A (Phases 1-3) QA Gate
- LyricPulse.App.csproj
- .OnKeyDown
- .ApplySnapshotToHost
- Project State — LyricPulse
- ScrimIntensity
- OversizedReadStream
- WordTimingInterpolator
- SingleInstanceGuard
- .Enter
- LyricPulse.Core.Visuals
- SpringParams
- LyricPulse.Tests.csproj
- RgbColor
- ViewModelBase
- LyricPulse.Rendering.csproj
- ClockUpdateKind
- ThemeVariantMode
- LyricPulse.Core.csproj
- LyricPulse — Milestone 1A (Phases 1–3) UI & Visual Design Audit
- ProviderDtos.cs
- LyricsStatus
- MediaPlaybackState
- GloveSemanticVisualMatcher
- MediaHealthKind
- StageFlyoutKind
- QueryUserNotificationState
- Milestone1ARemediationTests
- LyricPulse.UI.csproj
- LyricDrawOperation
- StageRenderSurfaceMode
- ObsChromaKeyMode
- UiModule.cs
- 2. Critical Invariant & Edge-Case Audit Matrix
- OffscreenSurfaceSlot
- .Build

## God Nodes (most connected - your core abstractions)
1. `StageViewModel` - 242 edges
2. `LyricPulse.Core.Models` - 104 edges
3. `AppSettings` - 101 edges
4. `OverlayRuntimeController` - 94 edges
5. `FontAssetRegistry` - 88 edges
6. `FrameRenderPacket` - 72 edges
7. `App` - 68 edges
8. `RgbColor` - 68 edges
9. `LegibilityPipeline` - 67 edges
10. `StyleRenderContext` - 66 edges

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

## Communities (111 total, 18 thin omitted)

### Community 0 - "Kinetic Styles & Semantic Visuals"
Cohesion: 0.18
Nodes (18): Neon Horizon Built-In Demo Song (12-Line LRC), SkSL Shader Uniform Animation Curves (Motion Spec), Damped Harmonic Spring & Easing Token System, Style 6: Constellation, Styles 1-2: Fisheye & FisheyeVisual, Style 9: KineticWall (3D Drift), Style 8: NodeLine, Style 7: SpidersWeb (+10 more)

### Community 1 - "Multi-Monitor & Per-Monitor V2 DPI Overlay Coordinator"
Cohesion: 0.43
Nodes (6): Multi-Monitor Selector Cards & 5-Zone Safe Anchor Grid, Fullscreen Game/Presentation Auto-Hide (SHQueryUserNotificationState), 9-Point Safe-Zone Anchors & Unlock-to-Drag Mode, Multi-Monitor & Per-Monitor V2 DPI Overlay Coordinator, Windows 10 22H2 / Windows 11 Transparent Overlay Pitfalls Checklist, Win32 Click-Through Overlay Interop ([LibraryImport])

### Community 2 - "PreserveSig"
Cohesion: 0.17
Nodes (3): IAudioCaptureClient, IAudioClient, IMMDeviceEnumerator

### Community 3 - "Studio UI, Gallery & System Tray"
Cohesion: 0.29
Nodes (11): Acoustic Studio Lightbox Design System (MASTER.md), App Chrome & Stage Font URI Specification (MASTER.md), WCAG 2.2 AA/AAA ThemeDictionaries Token System, Live SkiaSharp Style Preview Cards (30/60 FPS Stagger), GalleryWindow (2-Column Live Style Gallery), SettingsView Inline/Overlay Drawer, Typography Picker (2x2 SIL OFL) & ColorPicker Trio, TrayController (Avalonia TrayIcon + NativeMenu) (+3 more)

### Community 4 - "Lyrics Pipeline & Core Test Suite"
Cohesion: 0.21
Nodes (13): Keyboard Navigation, Focus Rings & Live Regions, Style 10: Messages (Phone Thread), Sticky Media Capsule, Diagnostics Flyout & Timing Bar, Sharded Atomic Disk Lyrics Cache, IMediaSessionProvider, LRCLIB Lyrics Provider, System.Text.Json AOT Source Generation (LyricsJsonContext), Multi-Provider Lyrics Pipeline (ILyricsOrchestrator) (+5 more)

### Community 5 - "RenderResourceCache (Zero-Alloc Pool & Disposal)"
Cohesion: 0.13
Nodes (18): 4-Layer Composed Overlay Legibility Pipeline, HarfBuzz Multi-Script Shaping & Overflow Auto-Fit, OKLCH Perceptual Album Color Derivation (Motion Spec), Styles 3-4: Codestyle & CodestyleVisual, Accessibility Toggles & Global Hotkey Keycap Recorder, AccentPalette Domain Contract, Central Package Management & SkiaSharp ABI Lockstep, SkiaAlbumColorExtractor (OKLab K-Means++ K=8) (+10 more)

### Community 6 - "NativeAOT & Self-Contained Publish Profiles"
Cohesion: 0.15
Nodes (13): Avalonia 11 XAML & Compiled Bindings Standards, LyricPulse Platform Vision, 6-Phase GSD Roadmap, CsWinRT 2.3.1 AOT GSMTC Projection, MediaSessionSelector (Priority + Hysteresis), MonotonicPlaybackClock & Drift Clamping, TimingOffsetState Machine (Per-Song Offset), WindowsGsmtcSessionProvider (+5 more)

### Community 7 - "StageViewModel"
Cohesion: 0.01
Nodes (169): StageViewModel, ActiveFlyout, ActivePaletteColor1, ActivePaletteColor2, ActivePaletteColor3, ActivePinTooltip, ActiveStyle, ActiveStyleIndex (+161 more)

### Community 8 - "RgbColor"
Cohesion: 0.08
Nodes (10): OklabColorMath, DeterministicPrng, OklabCluster, OklabPaletteExtractor, WeightedPixel, OklabColor, OklchColor, SkiaAlbumColorExtractor (+2 more)

### Community 9 - "FontAssetRegistry"
Cohesion: 0.05
Nodes (40): 2. Security, AOT, Resource-Hygiene & Thread-Safety Verification, HIGH Findings (Milestone 1A Gate Blockers), 4. Recommended Remediation Priority Order, Blocker Closure Matrix (`UI-01` – `UI-12`), HIGH, Re-Verification Addendum (`c6420bb`), Remaining Non-Blocking Low Notes (Deferrable to Phase 4/5), `UI-02` — Layer 0 Radial Ambient Scrim Hard-Clips Into a Boxy Rectangle & Violates Corner Transparency in `Fisheye` / `EllipticalBulge.sksl` and `VisualStack` (+32 more)

### Community 10 - "FrameRenderPacket"
Cohesion: 0.09
Nodes (15): FrameRenderPacket, EffectiveAudioMetrics, EffectiveHeroFontSizeDip, EffectiveStyle, IsCompactCard, SafeHeroScale, SafePartyEnergy, ShouldDrawStageBackdrop (+7 more)

### Community 11 - "NetEaseLyricsProvider"
Cohesion: 0.06
Nodes (20): LrclibTrackDto, HttpFetchResult, LrcLibLyricsProvider, Name, PriorityOrder, SourceKind, HttpFetchResult, NetEaseLyricsProvider (+12 more)

### Community 12 - "AppSettings"
Cohesion: 0.05
Nodes (42): AppSettings, ActiveStyle, AnchorZone, AudioVisualizerIntensity, AudioVisualizerMode, AutoSyncColors, BlockedAppIds, Default (+34 more)

### Community 13 - "MessagesStyleRenderer"
Cohesion: 0.07
Nodes (15): Phase 4: Offline GloVe/Fluent/Emoji Semantic Engine + Remaining 7 Kinetic Styles, Post-v1.0 Non-Blocking Polish Backlog, Phase 6 Dimension Scorecard, Phase 6 Executive Verdict: **FAIL** *(0 Critical, 2 High, 2 Medium, 1 Low — Remediate `UI-13` & `UI-14` Before v1.0 Sign-Off)*, Phase 6 v1.0 Release UI Audit, AestheticsOverlayRenderer, EdgeGlowRenderer, FisheyeVisualStyleRenderer (+7 more)

### Community 14 - "Task"
Cohesion: 0.11
Nodes (7): `UX-02` — Silent First Launch, Unusable Initial Tray Menu (`Menu == null`), and Broken `--demo` / Second-Instance Feedback, AlbumArtPayload, JsonSettingsStore, Current, FakeMediaProvider, CurrentSession, JsonSettingsStoreTests

### Community 15 - "LyricPulse.Core.Abstractions"
Cohesion: 0.14
Nodes (8): LyricPulse.Tests.Audio, LyricPulse.Core.Settings, LyricPulse.Core.Serialization, LyricPulse.Core.Lyrics, LyricPulse.Tests, LyricPulse.Tests.Lyrics, LyricPulse.Core.Lyrics.Dtos, LyricPulse.Tests.Runtime

### Community 16 - "StyleRenderContext"
Cohesion: 0.12
Nodes (6): StyleRenderContext, CodestyleStyleRenderer, StyleId, CodestyleVisualStyleRenderer, StyleId, StyleLayoutHelper

### Community 17 - "OverlayRuntimeController"
Cohesion: 0.07
Nodes (17): LOW Findings, 1. CRITICAL & HIGH Remediation Verification Matrix, 3. Remaining Non-Blocking Notes (Tracked for Phases 4–6), HIGH, Re-Verification Addendum (`c6420bb`), `UX-04` — Global Hotkey Conflicts Silently Discarded + Unguarded Offset Hotkeys, Track-Switch Offset Leak, and Non-Smooth Spring Resets, `UX-06` — Reduced Motion OS Desync, Un-Calmed Mid-Flight Palette Morphs, Inverse Onset Hierarchy Dip, and Blocking Quit Shutdown Race, RuntimeStateBox (+9 more)

### Community 18 - "StageWindow"
Cohesion: 0.16
Nodes (8): LOW Findings, Blocker Closure Matrix (`UI-13` – `UI-17`), Phase 6 Re-Verification Addendum (`e0dfe47`), Remaining Non-Blocking Notes Observed in `e0dfe47`, `UI-14` — Hidden `StageWindow` (`OnClosing` → Tray) Never Pauses `_heroHost` + Unreachable `IsFullscreen` Branch & Over-Wide Strip Viewport Floor, 1. v1.0 User Journey & CEO Requirements Scorecard, `UX6-06` — Second-Instance Wakeup & `Ctrl+Shift+G` Force Global Demo Mode When Paused / Instrumental / No-Lyrics, StageWindow

### Community 19 - "skiasharp"
Cohesion: 0.19
Nodes (9): LyricPulse.Rendering.Styles, LyricPulse.Rendering.Host, LyricPulse.Rendering.Legibility, LyricPulse.Core.Runtime, LyricPulse.Rendering.Shaders, LyricPulse.Rendering.Fonts, LyricPulse.Rendering.Color, LyricPulse.Rendering.Motion (+1 more)

### Community 20 - "TrackIdentity"
Cohesion: 0.09
Nodes (18): DiskLyricsCache, LocalLrcSidecarProvider, Name, PriorityOrder, SourceKind, LyricsOrchestrator, LyricsResult, LyricsSourceKind (+10 more)

### Community 21 - "LyricCanvasControl"
Cohesion: 0.14
Nodes (4): LyricCanvasControl, IsAnimationLoopActive, IsPaused, TargetMaxFps

### Community 22 - "Win32Native"
Cohesion: 0.08
Nodes (14): Win32 Overlay Host & System Integration (`WIN`), Canonical Architecture & Spec Alignment, 3. Verified UX Strengths (Preserve During Remediation), `UX6-02` — Hotkey Recorder Destroys Valid Previous Shortcut on Conflict & Rejects `D0–D9` / `Backspace` / `Delete`, GlobalHotkeyManager, HotkeyCommandKind, Register, UnregisterAll (+6 more)

### Community 23 - "LyricPulse.Core.Models"
Cohesion: 0.09
Nodes (7): LyricPulse.Core.Color, LyricPulse.Tests.UI, LyricPulse.Core.Models, LyricPulse.Core.Audio, LyricPulse.UI.ViewModels, LyricPulseIndentedJsonContext, LyricPulseJsonContext

### Community 24 - "LyricsDocument"
Cohesion: 0.21
Nodes (5): DemoSongProvider, DemoPalette, LyricsDocument, PlaybackLifecycleChoreographer, PlaybackLifecycleChoreographerTests

### Community 25 - "HotkeyAction"
Cohesion: 0.09
Nodes (15): HotkeyCommand, Action, CommandKind, Completion, ErrorMessage, Parsed, Succeeded, HotkeyGestureParser (+7 more)

### Community 26 - "MultiMonitorOverlayCoordinator"
Cohesion: 0.10
Nodes (10): MEDIUM, `UX-09` — Zero-Interruption Desktop & Multi-Monitor Edge Cases (`_hiddenAnchorWindow` Flash, `WS_EX_APPWINDOW`, Frozen `BoundsConfig`, Mixed-DPI Ordering), `UX-10` — Zero Visual Confirmation When Cycling Styles (`Ctrl+Shift+S`) or Unhiding Lyrics (`Ctrl+Shift+L`) Outside Active Vocals, MultiMonitorOverlayCoordinator, ActiveOverlayCount, OverlayWindow, CanvasControl, CurrentBoundsConfig (+2 more)

### Community 27 - "App"
Cohesion: 0.09
Nodes (9): 2. Remaining Non-Blocking Notes (Post-v1.0 Polish), MEDIUM, `UX6-07` — `"Play previews"` Button Is a Dead No-Op Under Reduced Motion, `UX6-08` — `RebuildStyleStrip()` Accumulates `PropertyChanged` Subscriptions on Filter Changes, `UX6-09` — Startup Hotkey Conflicts Are Not Synced to `StageViewModel`, App, ActiveSingleInstanceGuard, LaunchOptions (+1 more)

### Community 28 - "WindowsGsmtcSessionProvider"
Cohesion: 0.07
Nodes (7): AppLogMessages, CoreLogMessages, GsmtcSessionHandle, Session, WindowsGsmtcSessionProvider, CurrentSession, RenderingLogMessages

### Community 30 - "ConstellationStyleRenderer"
Cohesion: 0.29
Nodes (3): ConstellationStyleRenderer, StyleId, StarNode

### Community 31 - "GloveSemanticVisualMatcher"
Cohesion: 0.21
Nodes (8): VisualKind, Emoji, FluentIcon, VisualPreference, Balanced, EmojiOnly, FluentIconsOnly, WordVisualMatch

### Community 32 - "LyricPulse.Core.Abstractions"
Cohesion: 0.08
Nodes (9): LyricPulse.Rendering, LyricPulse.App.Platform, LyricPulse.App, LyricPulse.Core, LyricPulse.Core.Abstractions, LyricPulse.App.Tray, ServiceCollectionExtensions, RenderingModule (+1 more)

### Community 33 - "FakeMediaSessionProvider"
Cohesion: 0.12
Nodes (10): MediaSessionSnapshot, FakeMediaSessionProvider, CurrentSession, LastPolicy, Position, FakeMediaSessionProvider, AlbumArt, CurrentSession (+2 more)

### Community 34 - "OverlayStyleId"
Cohesion: 0.06
Nodes (42): Phase 3: Working 3-Style MVP + Tray Control (Milestone 1A Check-in), MEDIUM, `UI-08` — `Codestyle` Mid-Line Vertical Jump (~21 DIP Snap), Past-Line Horizontal Clipping, and UTF-16 Surrogate Split Risk, `UI-09` — 3-Style Choreography & Lifecycle Spec Drifts (`MOTION_AND_STYLES.md` §§6 & 8.1), `UI-10` — Mixed-DPI Secondary Monitor Initial `ApplyPlacement` Ordering + Root DI Retention of Transient `ILyricSceneRenderer` Disposables, LyricLine, LyricWord, OverlayStyleId (+34 more)

### Community 35 - "DisplayOptionViewModel"
Cohesion: 0.18
Nodes (11): DisplayOptionViewModel, DisplayKey, DpiScale, FriendlyName, HeaderText, HeightPx, Index, IsEnabled (+3 more)

### Community 36 - "TimingOffsetService"
Cohesion: 0.16
Nodes (9): Non-Blocking Findings Logged for Phase 2 / Phase 3 Follow-Up (MEDIUM / LOW — `src/` Left Untouched), Remediated During Phase 1 QA Gate (CRITICAL / HIGH), ITrackOffsetStore, TrackOffsetEntryDto, TrackOffsetsEnvelopeDto, TimingOffsetService, Current, TrackOffsetStore (+1 more)

### Community 37 - "StageCardCanvasHost"
Cohesion: 0.10
Nodes (10): MEDIUM Findings, 1. Remediation Verification Matrix (`e0dfe47`), StageCardCanvasHost, BottomFadeToBlack, IsCardPaused, PreviewPhaseSeconds, StageBackdropOverride, StyleOverride (+2 more)

### Community 38 - "StarterLexiconCatalog"
Cohesion: 0.16
Nodes (11): LyricTextNormalizationData, IrregularLemmas, Stopwords, SemanticIdiomLexicon, AllMoodConceptIds, Bigrams, StarterLexiconCatalog, Concepts (+3 more)

### Community 39 - "ReadOnlySpan"
Cohesion: 0.12
Nodes (4): QuantizedEmbeddingTable, Dimensions, Version, VocabCount

### Community 40 - "AudioFrameMetrics"
Cohesion: 0.19
Nodes (6): AudioFrameMetrics, SafeBands, Silent, AudioSpectrumAnalyzer, SampleRateHz, AudioSpectrumAnalyzerTests

### Community 41 - "StarterLexiconVisualMatcher"
Cohesion: 0.14
Nodes (7): StarterLexiconVisualMatcher, IsReady, Mode, CoreInfrastructureTests, DemoSongProviderTests, TitleNormalizerTests, StarterLexiconVisualMatcherTests

### Community 42 - "awesomeassertions"
Cohesion: 0.18
Nodes (6): LyricPulse.Tests.Rendering, LyricPulse.Core.Media, LyricPulse.Tests.Visuals, LyricPulse.Tests.Media, LyricPulse.Tests.Settings, LyricPulse.Tests.Color

### Community 43 - "ShaderLibrary"
Cohesion: 0.08
Nodes (12): MotionBlurMath, ShaderLibrary, AreAllShadersReady, BulgeCompileError, IsBulgeReady, IsMotionBlurReady, IsPartyMeshReady, MotionBlurCompileError (+4 more)

### Community 45 - "FrameBudgetTelemetry"
Cohesion: 0.12
Nodes (18): FrameBudgetTelemetry, ActiveBackend, DroppedFramesCount, LastFrameMs, ShaderState, TotalFrames, FrameTelemetrySnapshot, RenderBackendKind (+10 more)

### Community 46 - "build_glove_index.py"
Cohesion: 0.14
Nodes (6): load_glove_text(), main(), _normalize(), postprocess_all_but_the_top_numpy(), postprocess_all_but_the_top_pure_python(), quantize_and_write_lpqv()

### Community 50 - "MediaPlayerKind"
Cohesion: 0.11
Nodes (14): PlayerClassifier, MediaItemKind, Advertisement, Music, PodcastOrLongForm, Unknown, Video, MediaPlayerKind (+6 more)

### Community 51 - "HotkeyBindingViewModel"
Cohesion: 0.12
Nodes (11): HotkeyBindingViewModel, Action, BeginRecordCommand, ClearCommand, DisplayButtonText, ErrorMessage, GestureText, HasError (+3 more)

### Community 52 - "MediaSessionSelector"
Cohesion: 0.36
Nodes (3): MediaSessionSelector, ActiveAumid, MediaSessionSelectorTests

### Community 53 - "WasapiLoopbackAudioProvider"
Cohesion: 0.15
Nodes (7): IMMDevice, MMDeviceEnumeratorComObject, WasapiLoopbackAudioProvider, AudioCaptureClientIid, AudioClientIid, WaveFormatEx, WaveFormatExtensibleStruct

### Community 54 - "CountingVisualMatcher"
Cohesion: 0.20
Nodes (8): VisualMatcherMode, FullSemanticGloVe, StarterLexicon, CountingVisualMatcher, AnnotateCalls, DirectMatchWordCalls, IsReady, Mode

### Community 55 - "MonotonicPlaybackClock"
Cohesion: 0.29
Nodes (4): ClockAnchorBox, Empty, MonotonicPlaybackClock, MonotonicPlaybackClockTests

### Community 56 - ".AddLyricPulseCore"
Cohesion: 0.11
Nodes (25): Core Engine, Media Sessions & State (`CORE`), Deferred to v1.1 / v2 (Tracked Cleanly — Not Blocking v1.0), 2.1 Top 10 God Nodes (Highest Structural Degree), 5. Prioritized Feature Additions Schedule (Cross-Referencing `DEVELOPER_ADDITIONS.md`), IAlbumColorExtractor, IFullscreenDetector, IGlobalHotkeyService, ILyricsCache (+17 more)

### Community 57 - "ImmutableArray"
Cohesion: 0.33
Nodes (3): MediaSourcePolicy, SupportedPlayersOnly, UniversalFallback

### Community 58 - "AlbumAccentPalette"
Cohesion: 0.25
Nodes (6): AlbumAccentPalette, Default, DemoNeonHorizon, MonochromeStudio, PaletteMorphState, AudioVisualizerRenderer

### Community 59 - ".ComputePlacements"
Cohesion: 0.16
Nodes (12): MonitorDescriptor, OverlayBoundsCalculator, OverlayWindowPlacement, SafeZoneRect, AnchorZone, BottomLeft, BottomRight, BottomThird (+4 more)

### Community 60 - "OverlayPlaybackSnapshot"
Cohesion: 0.10
Nodes (18): `UI-11` — Tray Menu Copy, Paused Icon Semantics, and Menu Rebuild Polish (`pages/tray.md`), `UX-03` — Broken Lyrics State Handling: `PlainOnly` Freezes Forever on Line 1, Every Track Change Flashes False Error, Zero Offline Retry, Wrong `Instrumental` Warning, & Frozen Outros, `UX-05` — Unpredictable 3-Way Tray Left-Click, Misleading Paused/Fullscreen Icon & Tooltip States, and No Milestone 1A Universal Player Fallback Toggle, TrayController, TrayMenuState, TrayOffsetOption, TrayStyleOption, OverlayPlaybackSnapshot (+10 more)

### Community 61 - "OffscreenSurfacePool"
Cohesion: 0.10
Nodes (9): Blocker-by-Blocker Closure Proof, LyricSceneSnapshot, Empty, OffscreenSurfacePool, PurgeCount, PooledSlot, SolidDiagnosticSceneRenderer, StyleId (+1 more)

### Community 62 - "MediaSessionCoordinator"
Cohesion: 0.18
Nodes (8): MediaSessionCoordinator, BlockedAppIds, CachedAlbumArt, CurrentSession, IgnoreBrowserVideoSessions, SourcePolicy, SessionCandidate, MediaSessionCoordinatorTests

### Community 63 - "TrayIconFactory"
Cohesion: 0.21
Nodes (7): `UI-07` — Light Windows Taskbar Status Dots Fail WCAG 3.0:1 (`2.18:1` / `2.05:1`), Single-Size `32×32` PNG, and Stale Taskbar Polarity on OS Theme Switch, TrayIconFactory, TrayIconVisualState, Error, Idle, OverlayHidden, Playing

### Community 64 - "SettingsView"
Cohesion: 0.09
Nodes (22): 10 Kinetic Lyric Styles & Lifecycle Choreography (`STY`), Accessibility & User Experience (`UX`), Desktop UI — System Tray, Gallery & Settings (`UI`), Offline Semantic Word-to-Icon Engine (`VIS`), Quality Assurance, Performance & Packaging (`QA`), Synced Lyrics Pipeline (`LYR`), v1 Requirements, 1. Knowledge Graph Statistics & Integrity Audit (+14 more)

### Community 65 - "ObsBroadcastWindow"
Cohesion: 0.10
Nodes (20): 1. Milestone 1A User Journey Scorecard, 2. Detailed UX Findings & Concrete Fixes, 2. Severity-Classified Findings, 4. Recommended Remediation Priority Order (For Milestone 1A Re-Verification), Baseline Findings Summary (`371bfa5` — Historical Record), CRITICAL, Executive Verdict: **FAIL** *(0 CRITICAL, 5 HIGH, 5 MEDIUM, 4 LOW — 5 HIGH UX/A11y/Contrast Blockers Before v1.0 Release Sign-Off)*, Executive Verdict: **PASS WITH NOTES** *(Re-Verified on `70876e0` + `c6420bb` — 0 Critical, 0 High Blockers Remaining; Cleared for Milestone 1A CEO Check-In)* (+12 more)

### Community 66 - "ILyricSceneRenderer"
Cohesion: 0.27
Nodes (5): ILyricSceneRenderer, StyleId, ILyricSceneRendererFactory, LyricSceneRendererFactory, RenderingServiceCollectionExtensions

### Community 67 - "Phase 1: Foundation, Media Engine & Synced Lyrics Core"
Cohesion: 0.19
Nodes (9): Phase 1: Foundation, Media Engine & Synced Lyrics Core, Phase 6: Full QA/UX/UI Audit, Performance Tuning & Release Packaging, Phase Details, TimingKind, LineSynced, None, PlainUnsynced, WordInterpolated (+1 more)

### Community 68 - "AppSettings.cs"
Cohesion: 0.12
Nodes (16): AudioVisualizerMode, BassHorizon, FluidWave, None, ParticleField, RadialHalo, SpectrumBars, EdgeGlowMode (+8 more)

### Community 70 - "StyleCardViewModel"
Cohesion: 0.10
Nodes (20): StyleCardViewModel, CapabilityChip, Description, DisplayName, HasVisualIcons, IsActive, IsHovered, IsPinned (+12 more)

### Community 72 - "Milestone 1A (Phases 1-3) QA Gate"
Cohesion: 0.12
Nodes (15): 1. Build, Test, Smoke & Coverage Summary (`371bfa5`), 1. Build, Test, Smoke & Coverage Summary (`b88131e`), 2. Test Rigor & Phase 1–3 Verification Audit, 3. Severity-Classified Findings (`371bfa5` — Read-Only Audit), 3. Severity-Classified Findings (`b88131e` — Read-Only Audit), CRITICAL Findings, CRITICAL / HIGH Findings, LyricPulse — Quality Assurance Verification Report (+7 more)

### Community 73 - "LyricPulse.App.csproj"
Cohesion: 0.14
Nodes (13): Avalonia.Desktop, Avalonia.Diagnostics, Avalonia.Win32, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging, Avalonia.Controls.ColorPicker, Avalonia.Fonts.Inter, Avalonia.Skia (+5 more)

### Community 75 - ".ApplySnapshotToHost"
Cohesion: 0.22
Nodes (6): MEDIUM Findings, LOW, `UX-07` — System Tray Menu Ergonomics, `tray.md` Copy Fidelity, Menu Rebuild Flicker, and Screen-Reader Accelerator Hygiene, `UX-11` — Missing Windows `<ApplicationIcon>` & Stale/Single-Size Tray Icon Polarity, `UX-12` — Minor Ergonomic & Edge-Case Polish, `UX-13` — Phase 5 Readiness Bridges (Runtime Hotkey Re-Registration & All-Displays-Disabled Guardrail)

### Community 76 - "Project State — LyricPulse"
Cohesion: 0.12
Nodes (12): Permanently Out of Scope, Requirement-to-Phase Traceability, Requirements: LyricPulse, v2 / Deferred & Permanently Out of Scope, Overview, Phases, Progress, Roadmap: LyricPulse (+4 more)

### Community 77 - "ScrimIntensity"
Cohesion: 0.20
Nodes (10): Risk 8 — Missing Fields on `AppSettings` (`CORE_ENGINE.md` §6.1) & FontScale Formula *(MEDIUM)*, ScrimIntensity, Balanced, Off, Strong, Subtle, SecondaryLineMode, Off (+2 more)

### Community 78 - "OversizedReadStream"
Cohesion: 0.17
Nodes (6): OversizedReadStream, CanRead, CanSeek, CanWrite, Length, Position

### Community 80 - "SingleInstanceGuard"
Cohesion: 0.08
Nodes (6): Risk 5 — Font Asset Assembly URI Ownership *(MEDIUM — `MASTER.md` vs `RENDERING_AND_WIN32.md`)*, Phase 2: Click-Through Overlay Host, SkiaSharp Pipeline & Shaders, LaunchOptions, SingleInstanceGuard, Program, LyricPulseFontCollection

### Community 83 - "SpringParams"
Cohesion: 0.14
Nodes (8): SpringParams, DampingRatio, NaturalFrequency, SafeDamping, SafeMass, SafeStiffness, SpringTokens, SpringPhysicsTests

### Community 84 - "LyricPulse.Tests.csproj"
Cohesion: 0.20
Nodes (9): AwesomeAssertions, coverlet.collector, Microsoft.NET.Test.Sdk, NSubstitute, xunit.runner.visualstudio, xunit.v3, HarfBuzzSharp.NativeAssets.Win32, SkiaSharp.NativeAssets.Win32 (+1 more)

### Community 85 - "RgbColor"
Cohesion: 0.12
Nodes (6): RgbColor, SkColorExtensions, LegibilityStyleSpec, ReactiveVisualIconRenderer, RgbColorExtensions, LegibilityPipelineTests

### Community 87 - "LyricPulse.Rendering.csproj"
Cohesion: 0.22
Nodes (8): HarfBuzzSharp, SkiaSharp, SkiaSharp.HarfBuzz, Avalonia, Avalonia.Skia, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions, Microsoft.NET.Sdk

### Community 88 - "ClockUpdateKind"
Cohesion: 0.29
Nodes (7): ClockUpdateKind, HardSnap, IgnoredJitter, PausedFreeze, SeekSnap, SmoothSlew, ClockUpdateResult

### Community 89 - "ThemeVariantMode"
Cohesion: 0.18
Nodes (10): HIGH, LOW, MEDIUM, Phase 6 Detailed Findings & Concrete Fixes, `UI-13` — Gallery Strip Cards Break Runtime Dark/Light Theme Parity (`TryFindBrush`), Render Text `"Pin"` Instead of Vector `PathIcon`, and Leak `PropertyChanged` Handlers, `UI-15` — Media Status Dot Permanently Locked to `LpLiveGreenBrush`, Missing Active Button Selection States, `< 40px` Top Icon Hit Targets, & Minor Chrome Hexes, ThemeVariantMode, Dark (+2 more)

### Community 90 - "LyricPulse.Core.csproj"
Cohesion: 0.25
Nodes (7): Microsoft.Extensions.Http, System.Numerics.Tensors, System.Threading.RateLimiting, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions, Microsoft.Windows.CsWinRT, Microsoft.NET.Sdk

### Community 91 - "LyricPulse — Milestone 1A (Phases 1–3) UI & Visual Design Audit"
Cohesion: 0.20
Nodes (10): 1. Dimension Scorecard, 2. Detailed Findings & Concrete Fixes, 3. Verified Passing Controls (Preserve During Remediation), CRITICAL, Executive Verdict: **PASS WITH NOTES** *(Re-Verified on `70876e0` + `c6420bb` — Cleared for Milestone 1A Sign-Off)*, Initial Baseline Summary (Pre-Remediation), LOW, LyricPulse — Milestone 1A (Phases 1–3) UI & Visual Design Audit (+2 more)

### Community 92 - "ProviderDtos.cs"
Cohesion: 0.29
Nodes (8): CachedLyricsEnvelopeDto, LrclibErrorDto, NetEaseLyricBlockDto, NetEaseLyricResponseDto, NetEaseNamedDto, NetEaseSearchResponseDto, NetEaseSearchResultDto, NetEaseSongDto

### Community 93 - "LyricsStatus"
Cohesion: 0.29
Nodes (7): LyricsStatus, Error, Instrumental, Loading, NotFound, PlainOnly, Synced

### Community 94 - "MediaPlaybackState"
Cohesion: 0.29
Nodes (7): MediaPlaybackState, Changing, Closed, Opened, Paused, Playing, Stopped

### Community 95 - "GloveSemanticVisualMatcher"
Cohesion: 0.13
Nodes (10): `UI-17` — Secondary Craft, Accessibility & Reduced-Motion Polish, ConceptCentroidIndex, ConceptCount, Concepts, Dimensions, Vectors, GloveSemanticVisualMatcher, IsReady (+2 more)

### Community 96 - "MediaHealthKind"
Cohesion: 0.29
Nodes (7): MediaHealthKind, Error, Idle, Loading, Paused, PlayingSynced, Warning

### Community 97 - "StageFlyoutKind"
Cohesion: 0.25
Nodes (8): StageFlyoutKind, Aesthetics, Diagnostics, Font, None, Screen, Size, Visualizer

### Community 98 - "QueryUserNotificationState"
Cohesion: 0.22
Nodes (8): QueryUserNotificationState, AcceptsNotifications, App, Busy, NotPresent, PresentationMode, QuietTime, RunningD3DFullScreen

### Community 100 - "LyricPulse.UI.csproj"
Cohesion: 0.33
Nodes (5): Avalonia, Avalonia.Controls.ColorPicker, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent, Microsoft.NET.Sdk

### Community 102 - "StageRenderSurfaceMode"
Cohesion: 0.29
Nodes (6): StageRenderSurfaceMode, DesktopOverlay, GalleryCard, ObsBroadcast, StageCanvas, OverlayFrameAdapter

### Community 103 - "ObsChromaKeyMode"
Cohesion: 0.40
Nodes (4): ObsChromaKeyMode, Black, Blue, Green

### Community 104 - "UiModule.cs"
Cohesion: 0.50
Nodes (3): LyricPulse.UI, UiModule, ModuleName

### Community 105 - "2. Critical Invariant & Edge-Case Audit Matrix"
Cohesion: 0.33
Nodes (6): 1. Build, Test & Coverage Summary, 2. Critical Invariant & Edge-Case Audit Matrix, 3. Audit Findings, 4. Modified / Added Files Pending Commit, `LyricPulse.Core` Per-File Coverage Table (Post-Remediation), Phase 1 QA Gate

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
- **610 isolated node(s):** `ActiveSingleInstanceGuard`, `LaunchOptions`, `Avalonia.Desktop`, `Avalonia.Skia`, `Avalonia.Win32` (+605 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 792 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **18 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `StageViewModel` connect `StageViewModel` to `FontAssetRegistry`, `NetEaseLyricsProvider`, `AppSettings`, `Task`, `OverlayRuntimeController`, `LyricPulse.Core.Models`, `HotkeyAction`, `App`, `WindowsGsmtcSessionProvider`, `OverlayStyleId`, `DisplayOptionViewModel`, `StageCardCanvasHost`, `.CreateStartedControllerAsync`, `HotkeyBindingViewModel`, `ImmutableArray`, `.ComputePlacements`, `OverlayPlaybackSnapshot`, `AppSettings.cs`, `StyleCardViewModel`, `.OnKeyDown`, `ScrimIntensity`, `SingleInstanceGuard`, `ViewModelBase`, `ThemeVariantMode`, `MediaHealthKind`, `StageFlyoutKind`, `ObsChromaKeyMode`, `Color`?**
  _High betweenness centrality (0.158) - this node is a cross-community bridge._
- **Are the 21 inferred relationships involving `StageViewModel` (e.g. with `2. Security, AOT, Resource-Hygiene & Thread-Safety Verification` and ``UX6-09` — Startup Hotkey Conflicts Are Not Synced to `StageViewModel``) actually correct?**
  _`StageViewModel` has 21 INFERRED edges - model-reasoned connections that need verification._
- **What connects `ActiveSingleInstanceGuard`, `LaunchOptions`, `Avalonia.Desktop` to the rest of the system?**
  _610 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `RenderResourceCache (Zero-Alloc Pool & Disposal)` be split into smaller, more focused modules?**
  _Cohesion score 0.13450292397660818 - nodes in this community are weakly interconnected._
- **Why does `AppSettings` connect `AppSettings` to `Task`, `OverlayRuntimeController`, `HotkeyAction`, `MultiMonitorOverlayCoordinator`, `App`, `GloveSemanticVisualMatcher`, `OverlayStyleId`, `TimingOffsetService`, `StarterLexiconVisualMatcher`, `.CreateStartedControllerAsync`, `.AddLyricPulseCore`, `ImmutableArray`, `.ComputePlacements`, `OverlayPlaybackSnapshot`, `OffscreenSurfacePool`, `SettingsView`, `AppSettings.cs`, `ScrimIntensity`, `RgbColor`, `ThemeVariantMode`, `StageRenderSurfaceMode`?**
  _High betweenness centrality (0.075) - this node is a cross-community bridge._

### Low-confidence Hints
_AMBIGUOUS edges — the extractor was unsure. Verify before acting on these._

- **What is the exact relationship between `SkSL Shader Uniform Animation Curves (Motion Spec)` and `EllipticalBulge SkSL Shader (Rendering Contract)`?**
  _Edge tagged AMBIGUOUS (relation: semantically_similar_to) - confidence is low._
- **What is the exact relationship between `SkSL Shader Uniform Animation Curves (Motion Spec)` and `13-Tap DirectionalMotionBlur SkSL Shader (Rendering Contract)`?**
  _Edge tagged AMBIGUOUS (relation: semantically_similar_to) - confidence is low._