# Graph Report - LyricPulse  (2026-11-26)

## Corpus Check
- 185 files · ~206,680 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 23 file(s) not represented in the graph (top: .axaml 7, .ttf 5, .sksl 3)

## Summary
- 2942 nodes · 10062 edges · 107 communities (92 shown, 15 thin omitted)
- Extraction: 78% EXTRACTED · 22% INFERRED · 0% AMBIGUOUS · INFERRED: 2179 edges (avg confidence: 0.87)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `60cd182f`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Kinetic Styles & Semantic Visuals
- Multi-Monitor & Per-Monitor V2 DPI Overlay Coordinator
- TrackIdentity
- Studio UI, Gallery & System Tray
- Sticky Media Capsule, Diagnostics Flyout & Timing Bar
- FakeMediaSessionProvider
- NativeAOT & Self-Contained Publish Profiles
- StageViewModel
- RgbColor
- FontAssetRegistry
- FrameRenderPacket
- NetEaseLyricsProvider
- AppSettings
- StyleRenderContext
- TitleNormalizer
- AlbumAccentPalette
- Fact
- OverlayStyleId
- StageWindow
- skiasharp
- TrackIdentity
- LyricCanvasControl
- Win32Native
- LyricPulse.Core.Color
- ConstellationStyleRenderer
- HotkeyAction
- .ComputePlacements
- App
- WasapiLoopbackAudioProvider
- IDisposable
- SKColor
- WordVisualMatch
- system_collections_immutable
- TrayController
- OverlayRuntimeController
- .CreateAndShowPrimaryStageWindow
- TimingOffsetService
- WindowsGsmtcSessionProvider
- StarterLexiconCatalog
- ReadOnlySpan
- .Render
- StarterLexiconVisualMatcher
- LyricPulse.Core.Lyrics
- MessagesStyleRenderer
- VisualStackStyleRenderer
- FrameBudgetTelemetry
- build_glove_index.py
- FullscreenAutoHideMonitor
- SingleInstanceGuard
- .CreateStartedControllerAsync
- PlayerClassifier
- HotkeyBindingViewModel
- MediaSessionSelector
- ImmutableArray
- 1. v1.0 User Journey & CEO Requirements Scorecard
- MonotonicPlaybackClock
- NodeLineStyleRenderer
- SingleInstanceGuard
- Multi-Provider Lyrics Pipeline (ILyricsOrchestrator)
- IAudioSpectrumProvider
- OverlayPlaybackSnapshot
- OffscreenSurfacePool
- MediaSessionCoordinator
- TrayIconFactory
- LyricPulse — Milestone 1A (Phases 1–3) UX Audit
- .OnKeyDown
- SettingsView
- .Score
- AnchorZone
- App.axaml.cs
- StyleCardViewModel
- QueryUserNotificationState
- MultiMonitorOverlayCoordinator
- LyricPulse.App.csproj
- ConceptCentroidIndex
- PlaybackLifecycleChoreographer
- .SolidDiagnosticSceneRenderer_AnimatesToRestAndDrawsNonEmptyPixels
- .RenderingUtilities_HandleAllBoundaryGuards_NaN_Empty_Fallback_AndDisposal
- OversizedReadStream
- LyricsParser
- .BuildServiceProvider
- .VinylGrooveStyleRenderer_RendersAlbumArtAndMoodFallback_WithTransparentDesktopCorners
- Milestone1ARemediationTests
- SpringParams
- LyricPulse.Tests.csproj
- RgbColor
- LyricPulse.Rendering.csproj
- ClockUpdateKind
- TimeSpan
- LyricPulse.Core.csproj
- LyricPulse — Milestone 1A (Phases 1–3) UI & Visual Design Audit
- MediaPlaybackState
- GloveSemanticVisualMatcher
- MediaHealthKind
- StageFlyoutKind
- LyricPulse.UI.csproj
- ObsChromaKeyMode
- UiModule.cs
- StageRenderSurfaceMode
- Color
- ProviderDtos.cs
- RenderResourceCache (Zero-Alloc Pool & Disposal)
- LyricsModels.cs
- StyleCatalog
- microsoft_extensions_logging
- .AddLyricPulseCore

## God Nodes (most connected - your core abstractions)
1. `StageViewModel` - 256 edges
2. `LyricPulse.Core.Models` - 113 edges
3. `AppSettings` - 108 edges
4. `OverlayRuntimeController` - 102 edges
5. `FontAssetRegistry` - 98 edges
6. `FrameRenderPacket` - 92 edges
7. `RgbColor` - 89 edges
8. `StyleRenderContext` - 84 edges
9. `LegibilityPipeline` - 76 edges
10. `LyricsParser` - 69 edges

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

## Communities (107 total, 15 thin omitted)

### Community 0 - "Kinetic Styles & Semantic Visuals"
Cohesion: 0.17
Nodes (19): Neon Horizon Built-In Demo Song (12-Line LRC), 4-Layer Composed Overlay Legibility Pipeline, SkSL Shader Uniform Animation Curves (Motion Spec), Damped Harmonic Spring & Easing Token System, Styles 3-4: Codestyle & CodestyleVisual, Style 6: Constellation, Styles 1-2: Fisheye & FisheyeVisual, Style 9: KineticWall (3D Drift) (+11 more)

### Community 1 - "Multi-Monitor & Per-Monitor V2 DPI Overlay Coordinator"
Cohesion: 0.18
Nodes (12): Avalonia 11 XAML & Compiled Bindings Standards, Multi-Monitor Selector Cards & 5-Zone Safe Anchor Grid, CsWinRT 2.3.1 AOT GSMTC Projection, Central Package Management & SkiaSharp ABI Lockstep, Fullscreen Game/Presentation Auto-Hide (SHQueryUserNotificationState), 9-Point Safe-Zone Anchors & Unlock-to-Drag Mode, AppBuilder & Win32PlatformOptions Pipeline, Avalonia 11.3.x + SkiaSharp 3.119.x + HarfBuzzSharp Stack (+4 more)

### Community 2 - "TrackIdentity"
Cohesion: 0.14
Nodes (14): LrclibTrackDto, LyricsDocument, LyricsSourceKind, Demo, DiskCache, LocalSidecar, LrcLib, NetEase (+6 more)

### Community 3 - "Studio UI, Gallery & System Tray"
Cohesion: 0.29
Nodes (11): Acoustic Studio Lightbox Design System (MASTER.md), App Chrome & Stage Font URI Specification (MASTER.md), WCAG 2.2 AA/AAA ThemeDictionaries Token System, Live SkiaSharp Style Preview Cards (30/60 FPS Stagger), GalleryWindow (2-Column Live Style Gallery), SettingsView Inline/Overlay Drawer, Typography Picker (2x2 SIL OFL) & ColorPicker Trio, TrayController (Avalonia TrayIcon + NativeMenu) (+3 more)

### Community 4 - "Sticky Media Capsule, Diagnostics Flyout & Timing Bar"
Cohesion: 0.19
Nodes (13): Keyboard Navigation, Focus Rings & Live Regions, OKLCH Perceptual Album Color Derivation (Motion Spec), Sticky Media Capsule, Diagnostics Flyout & Timing Bar, AccentPalette Domain Contract, IMediaSessionProvider, MediaSessionSelector (Priority + Hysteresis), MonotonicPlaybackClock & Drift Clamping, SkiaAlbumColorExtractor (OKLab K-Means++ K=8) (+5 more)

### Community 5 - "FakeMediaSessionProvider"
Cohesion: 0.11
Nodes (9): FakeMediaSessionProvider, CurrentSession, LastPolicy, Position, FakeMediaSessionProvider, AlbumArt, CurrentSession, LastPolicy (+1 more)

### Community 6 - "NativeAOT & Self-Contained Publish Profiles"
Cohesion: 0.33
Nodes (5): LyricPulse Platform Vision, 6-Phase GSD Roadmap, OBS / Streamer Broadcast & Chroma-Key Capture Window, Universal GSMTC Mode + Smart Browser/Video Filter, LyricCanvasControl (RequestAnimationFrame Host)

### Community 7 - "StageViewModel"
Cohesion: 0.01
Nodes (179): StageViewModel, ActiveFlyout, ActivePaletteColor1, ActivePaletteColor2, ActivePaletteColor3, ActivePinTooltip, ActiveStyle, ActiveStyleIndex (+171 more)

### Community 8 - "RgbColor"
Cohesion: 0.06
Nodes (11): OklabColorMath, DeterministicPrng, OklabCluster, OklabPaletteExtractor, WeightedPixel, WordTimingInterpolator, OklabColor, OklchColor (+3 more)

### Community 9 - "FontAssetRegistry"
Cohesion: 0.07
Nodes (25): MEDIUM Findings, Non-Blocking Phase 4/5 Hygiene Notes (LOW), Blocker Closure Matrix (`UI-01` – `UI-12`), Re-Verification Addendum (`c6420bb`), Remaining Non-Blocking Low Notes (Deferrable to Phase 4/5), `UI-04` — HarfBuzz Multi-Script Shaping & System Font Fallback Are Unwired (Tofu Boxes on All Non-Latin Lyrics), `UI-16` — `CutoutWord` & `Messages` Bypass HarfBuzz Multi-Script Fallback + Unsprung `FollowingDot` in 4 Styles + `NodeLine` RM Long-Line Overflow, `UX-08` — Live Reading Ergonomics: `Codestyle` Mid-Line Vertical Jump, Offset Toast Contrast/Collision, Pause-Dim Drift, and Unwired Non-Latin Fallback (+17 more)

### Community 10 - "FrameRenderPacket"
Cohesion: 0.08
Nodes (21): FrameRenderPacket, EffectiveAudioMetrics, EffectiveHeroFontSizeDip, EffectiveStyle, IsCompactCard, SafeHeroScale, SafePartyEnergy, ShouldDrawStageBackdrop (+13 more)

### Community 11 - "NetEaseLyricsProvider"
Cohesion: 0.06
Nodes (23): 2. Critical Invariant & Edge-Case Audit Matrix, HttpFetchResult, LrcLibLyricsProvider, Name, PriorityOrder, SourceKind, HttpFetchResult, NetEaseLyricsProvider (+15 more)

### Community 12 - "AppSettings"
Cohesion: 0.04
Nodes (47): AppSettings, ActiveStyle, AnchorZone, AudioVisualizerIntensity, AudioVisualizerMode, AutoSyncColors, BeatDropEffectEnabled, BeatDropIntensity (+39 more)

### Community 13 - "StyleRenderContext"
Cohesion: 0.11
Nodes (12): Phase 4: Offline GloVe/Fluent/Emoji Semantic Engine + Remaining 7 Kinetic Styles, Phase 6 Executive Verdict: **FAIL** *(0 Critical, 2 High, 2 Medium, 1 Low — Remediate `UI-13` & `UI-14` Before v1.0 Sign-Off)*, Phase 6 v1.0 Release UI Audit, StyleRenderContext, AestheticsOverlayRenderer, CodestyleVisualStyleRenderer, StyleId, FisheyeVisualStyleRenderer (+4 more)

### Community 15 - "AlbumAccentPalette"
Cohesion: 0.05
Nodes (23): AudioFrameMetrics, SafeBands, Silent, AudioSpectrumAnalyzer, DropSensitivity, SampleRateHz, AlbumAccentPalette, Default (+15 more)

### Community 16 - "Fact"
Cohesion: 0.10
Nodes (11): ShaderLibrary, AreAllShadersReady, BulgeCompileError, IsBulgeReady, IsMotionBlurReady, IsPartyMeshReady, MotionBlurCompileError, PartyMeshCompileError (+3 more)

### Community 17 - "OverlayStyleId"
Cohesion: 0.07
Nodes (26): SmokeTestRunner, OverlayStyleId, Codestyle, CodestyleVisual, Constellation, Fisheye, FisheyeVisual, KineticWall (+18 more)

### Community 18 - "StageWindow"
Cohesion: 0.06
Nodes (20): LOW Findings, Blocker Closure Matrix (`UI-13` – `UI-17`), Phase 6 Dimension Scorecard, Phase 6 Re-Verification Addendum (`e0dfe47`), Remaining Non-Blocking Notes Observed in `e0dfe47`, `UI-14` — Hidden `StageWindow` (`OnClosing` → Tray) Never Pauses `_heroHost` + Unreachable `IsFullscreen` Branch & Over-Wide Strip Viewport Floor, 1. Remediation Verification Matrix (`e0dfe47`), 1. v1.0 User Journey & CEO Requirements Scorecard (+12 more)

### Community 19 - "skiasharp"
Cohesion: 0.15
Nodes (9): LyricPulse.Tests.Rendering, LyricPulse.Rendering.Styles, LyricPulse.Rendering.Host, LyricPulse.Rendering.Legibility, LyricPulse.Core.Runtime, LyricPulse.Rendering.Shaders, LyricPulse.Rendering.Fonts, LyricPulse.Rendering.Color (+1 more)

### Community 20 - "TrackIdentity"
Cohesion: 0.10
Nodes (12): ILyricsCache, ILyricsProvider, Name, PriorityOrder, SourceKind, DiskLyricsCache, LocalLrcSidecarProvider, Name (+4 more)

### Community 21 - "LyricCanvasControl"
Cohesion: 0.08
Nodes (7): Phase 2: Click-Through Overlay Host, SkiaSharp Pipeline & Shaders, LyricCanvasControl, IsAnimationLoopActive, IsPaused, TargetMaxFps, LyricDrawOperation, Bounds

### Community 22 - "Win32Native"
Cohesion: 0.11
Nodes (7): Win32 Overlay Host & System Integration (`WIN`), Canonical Architecture & Spec Alignment, MONITORINFOEXW, MsgStruct, PointStruct, RectStruct, Win32Native

### Community 24 - "ConstellationStyleRenderer"
Cohesion: 0.21
Nodes (3): ConstellationStyleRenderer, StyleId, StarNode

### Community 25 - "HotkeyAction"
Cohesion: 0.08
Nodes (19): 3. Verified UX Strengths (Preserve During Remediation), `UX6-02` — Hotkey Recorder Destroys Valid Previous Shortcut on Conflict & Rejects `D0–D9` / `Backspace` / `Delete`, GlobalHotkeyManager, HotkeyCommand, Action, CommandKind, Completion, ErrorMessage (+11 more)

### Community 26 - ".ComputePlacements"
Cohesion: 0.22
Nodes (4): MonitorDescriptor, OverlayBoundsCalculator, OverlayWindowPlacement, SafeZoneRect

### Community 27 - "App"
Cohesion: 0.07
Nodes (18): 1. Build, Test, Smoke & Coverage Summary (`371bfa5`), 1. Build, Test, Smoke & Coverage Summary (`b88131e`), 2. Test Rigor & Phase 1–3 Verification Audit, 3. Severity-Classified Findings (`371bfa5` — Read-Only Audit), 3. Severity-Classified Findings (`b88131e` — Read-Only Audit), CRITICAL Findings, CRITICAL / HIGH Findings, LyricPulse — Quality Assurance Verification Report (+10 more)

### Community 28 - "WasapiLoopbackAudioProvider"
Cohesion: 0.06
Nodes (13): AppLogMessages, IAudioCaptureClient, IAudioClient, IMMDevice, IMMDeviceEnumerator, MMDeviceEnumeratorComObject, WasapiLoopbackAudioProvider, AudioCaptureClientIid (+5 more)

### Community 29 - "IDisposable"
Cohesion: 0.17
Nodes (4): MEDIUM Findings, JsonSettingsStore, Current, JsonSettingsStoreTests

### Community 30 - "SKColor"
Cohesion: 0.10
Nodes (11): ScrimIntensity, Balanced, Off, Strong, Subtle, SkColorExtensions, ShadowBucketKey, ShadowFilterPair (+3 more)

### Community 31 - "WordVisualMatch"
Cohesion: 0.17
Nodes (11): VisualPreference, Balanced, EmojiOnly, FluentIconsOnly, WordVisualMatch, BranchNode, CountingVisualMatcher, AnnotateCalls (+3 more)

### Community 32 - "system_collections_immutable"
Cohesion: 0.10
Nodes (4): LyricPulse.Core.Media, LyricPulse.Tests.Media, LyricPulse.Core.Models, LyricPulse.Core.Audio

### Community 33 - "TrayController"
Cohesion: 0.16
Nodes (4): 6. Knowledge Gaps & Weakly-Connected Components to Monitor, 2. Remaining Non-Blocking Notes (Post-v1.0 Polish), `UX-04` — Global Hotkey Conflicts Silently Discarded + Unguarded Offset Hotkeys, Track-Switch Offset Leak, and Non-Smooth Spring Resets, TrayController

### Community 34 - "OverlayRuntimeController"
Cohesion: 0.08
Nodes (11): 1. CRITICAL & HIGH Remediation Verification Matrix, `UX-06` — Reduced Motion OS Desync, Un-Calmed Mid-Flight Palette Morphs, Inverse Onset Hierarchy Dip, and Blocking Quit Shutdown Race, AlbumArtPayload, OverlayRuntimeController, IsDemoMode, IsEffectiveReducedMotion, IsStageWindowOpen, Settings (+3 more)

### Community 35 - ".CreateAndShowPrimaryStageWindow"
Cohesion: 0.18
Nodes (11): DisplayOptionViewModel, DisplayKey, DpiScale, FriendlyName, HeaderText, HeightPx, Index, IsEnabled (+3 more)

### Community 36 - "TimingOffsetService"
Cohesion: 0.18
Nodes (7): Remediated During Phase 1 QA Gate (CRITICAL / HIGH), Phase 1: Foundation, Media Engine & Synced Lyrics Core, TrackOffsetEntryDto, TrackOffsetsEnvelopeDto, TimingOffsetService, Current, TrackOffsetStore

### Community 37 - "WindowsGsmtcSessionProvider"
Cohesion: 0.10
Nodes (5): `UX-02` — Silent First Launch, Unusable Initial Tray Menu (`Menu == null`), and Broken `--demo` / Second-Instance Feedback, GsmtcSessionHandle, Session, WindowsGsmtcSessionProvider, CurrentSession

### Community 38 - "StarterLexiconCatalog"
Cohesion: 0.15
Nodes (11): LyricTextNormalizationData, IrregularLemmas, Stopwords, SemanticIdiomLexicon, AllMoodConceptIds, Bigrams, StarterLexiconCatalog, Concepts (+3 more)

### Community 39 - "ReadOnlySpan"
Cohesion: 0.10
Nodes (5): QuantizedEmbeddingTable, Dimensions, Version, VocabCount, Phase4ASemanticAndSettingsTests

### Community 41 - "StarterLexiconVisualMatcher"
Cohesion: 0.17
Nodes (7): StarterLexiconVisualMatcher, IsReady, Mode, CoreInfrastructureTests, TitleNormalizerTests, PlatformOverlayAndGuardTests, StarterLexiconVisualMatcherTests

### Community 42 - "LyricPulse.Core.Lyrics"
Cohesion: 0.13
Nodes (12): LyricPulse.Core.Settings, LyricPulse.Core, LyricPulse.Core.Lyrics, LyricPulse.Tests.Visuals, LyricPulse.Tests.UI, LyricPulse.Tests, LyricPulse.Tests.Settings, LyricPulse.Tests.Lyrics (+4 more)

### Community 43 - "MessagesStyleRenderer"
Cohesion: 0.13
Nodes (5): 4. Recommended Remediation Priority Order, `UI-02` — Layer 0 Radial Ambient Scrim Hard-Clips Into a Boxy Rectangle & Violates Corner Transparency in `Fisheye` / `EllipticalBulge.sksl` and `VisualStack`, MotionBlurMath, MessagesStyleRenderer, StyleId

### Community 44 - "VisualStackStyleRenderer"
Cohesion: 0.24
Nodes (8): HIGH, `UI-03` — `VisualStack` Icon Badges Fail White-Page Contrast (`1.04:1 – 2.72:1`) by Bypassing `LegibilityPipeline`, `UI-05` — Per-Frame Unmanaged `SKRoundRect` Leak, Hot-Path `SKImageFilter` Allocations, and UI/Render Thread `List<T>` Race, `UI-09` — 3-Style Choreography & Lifecycle Spec Drifts (`MOTION_AND_STYLES.md` §§6 & 8.1), VisualStackRow, VisualStackStyleRenderer, AllowCpuShadersForTesting, StyleId

### Community 45 - "FrameBudgetTelemetry"
Cohesion: 0.13
Nodes (17): FrameBudgetTelemetry, ActiveBackend, DroppedFramesCount, LastFrameMs, ShaderState, TotalFrames, FrameTelemetrySnapshot, RenderBackendKind (+9 more)

### Community 46 - "build_glove_index.py"
Cohesion: 0.14
Nodes (6): load_glove_text(), main(), _normalize(), postprocess_all_but_the_top_numpy(), postprocess_all_but_the_top_pure_python(), quantize_and_write_lpqv()

### Community 47 - "FullscreenAutoHideMonitor"
Cohesion: 0.22
Nodes (3): LOW Findings, FullscreenAutoHideMonitor, IsCurrentlyFullscreen

### Community 51 - "HotkeyBindingViewModel"
Cohesion: 0.09
Nodes (12): HotkeyBindingViewModel, Action, BeginRecordCommand, ClearCommand, DisplayButtonText, ErrorMessage, GestureText, HasError (+4 more)

### Community 52 - "MediaSessionSelector"
Cohesion: 0.33
Nodes (3): MediaSessionSelector, ActiveAumid, MediaSessionSelectorTests

### Community 53 - "ImmutableArray"
Cohesion: 0.33
Nodes (3): MediaSourcePolicy, SupportedPlayersOnly, UniversalFallback

### Community 54 - "1. v1.0 User Journey & CEO Requirements Scorecard"
Cohesion: 0.17
Nodes (11): 2. Severity-Classified Findings, HIGH (Release Blockers), LOW, `UX6-01` — Settings Drawer Always Docks Inline (`380px`), Squeezing `StageWindow` & Clipping Top Bar Below `880px`, `UX6-03` — Bottom 10-Card Strip Unreachable via Keyboard + Missing Settings/Flyout Focus Management, `UX6-04` — Unbound Media Health Dot / Troubleshooting Pill & Missing Active-State Indicators on 7 Control Groups, `UX6-05` — Unreadable Light-Mode Contrast on Hero Stage Counter Pill (`~1.4:1`) & Category Label (`~1.8:1`), `UX6-11` — Hit-Area & Icon Consistency Polish (`MASTER.md:297`, `gallery.md:93-94, 149-151`) (+3 more)

### Community 55 - "MonotonicPlaybackClock"
Cohesion: 0.27
Nodes (4): ClockAnchorBox, Empty, MonotonicPlaybackClock, MonotonicPlaybackClockTests

### Community 58 - "Multi-Provider Lyrics Pipeline (ILyricsOrchestrator)"
Cohesion: 0.24
Nodes (12): Five-Project Solution Topology, LyricPulse.Tests (xUnit v3 + AwesomeAssertions >=80%), Sharded Atomic Disk Lyrics Cache, LRCLIB Lyrics Provider, System.Text.Json AOT Source Generation (LyricsJsonContext), Multi-Provider Lyrics Pipeline (ILyricsOrchestrator), ILyricsParser (Enhanced LRC, YRC & Proportional Timing), NetEase Cloud Music Provider (YRC/LRC) (+4 more)

### Community 60 - "OverlayPlaybackSnapshot"
Cohesion: 0.12
Nodes (17): `UI-11` — Tray Menu Copy, Paused Icon Semantics, and Menu Rebuild Polish (`pages/tray.md`), `UX-03` — Broken Lyrics State Handling: `PlainOnly` Freezes Forever on Line 1, Every Track Change Flashes False Error, Zero Offline Retry, Wrong `Instrumental` Warning, & Frozen Outros, `UX-05` — Unpredictable 3-Way Tray Left-Click, Misleading Paused/Fullscreen Icon & Tooltip States, and No Milestone 1A Universal Player Fallback Toggle, `UX-07` — System Tray Menu Ergonomics, `tray.md` Copy Fidelity, Menu Rebuild Flicker, and Screen-Reader Accelerator Hygiene, TrayMenuState, TrayOffsetOption, TrayStyleOption, OverlayPlaybackSnapshot (+9 more)

### Community 61 - "OffscreenSurfacePool"
Cohesion: 0.10
Nodes (15): 2. Security, AOT, Resource-Hygiene & Thread-Safety Verification, Blocker-by-Blocker Closure Proof, ILyricSceneRenderer, StyleId, ILyricSceneRendererFactory, LyricSceneRendererFactory, OffscreenSurfacePool, PurgeCount (+7 more)

### Community 62 - "MediaSessionCoordinator"
Cohesion: 0.18
Nodes (8): MediaSessionCoordinator, BlockedAppIds, CachedAlbumArt, CurrentSession, IgnoreBrowserVideoSessions, SourcePolicy, SessionCandidate, MediaSessionCoordinatorTests

### Community 63 - "TrayIconFactory"
Cohesion: 0.17
Nodes (9): MEDIUM, `UI-07` — Light Windows Taskbar Status Dots Fail WCAG 3.0:1 (`2.18:1` / `2.05:1`), Single-Size `32×32` PNG, and Stale Taskbar Polarity on OS Theme Switch, `UI-08` — `Codestyle` Mid-Line Vertical Jump (~21 DIP Snap), Past-Line Horizontal Clipping, and UTF-16 Surrogate Split Risk, TrayIconFactory, TrayIconVisualState, Error, Idle, OverlayHidden (+1 more)

### Community 64 - "LyricPulse — Milestone 1A (Phases 1–3) UX Audit"
Cohesion: 0.11
Nodes (18): 1. Milestone 1A User Journey Scorecard, 2. Bonus MEDIUM / LOW Fixes Confirmed in `c6420bb`, 2. Detailed UX Findings & Concrete Fixes, 3. Remaining Non-Blocking Notes (Tracked for Phases 4–6), 4. Recommended Remediation Priority Order (For Milestone 1A Re-Verification), Baseline Findings Summary (`371bfa5` — Historical Record), CRITICAL, Executive Verdict: **PASS WITH NOTES** *(Re-Verified on `70876e0` + `c6420bb` — 0 Critical, 0 High Blockers Remaining; Cleared for Milestone 1A CEO Check-In)* (+10 more)

### Community 65 - ".OnKeyDown"
Cohesion: 0.15
Nodes (6): MEDIUM, `UX6-06` — Second-Instance Wakeup & `Ctrl+Shift+G` Force Global Demo Mode When Paused / Instrumental / No-Lyrics, `UX6-07` — `"Play previews"` Button Is a Dead No-Op Under Reduced Motion, `UX6-08` — `RebuildStyleStrip()` Accumulates `PropertyChanged` Subscriptions on Filter Changes, `UX6-09` — Startup Hotkey Conflicts Are Not Synced to `StageViewModel`, `UX6-10` — Unmodified `Left` / `Right` Arrow Keys Hijack Keyboard Navigation Inside Settings & Toolbar

### Community 66 - "SettingsView"
Cohesion: 0.06
Nodes (31): 10 Kinetic Lyric Styles & Lifecycle Choreography (`STY`), Accessibility & User Experience (`UX`), Desktop UI — System Tray, Gallery & Settings (`UI`), Offline Semantic Word-to-Icon Engine (`VIS`), Permanently Out of Scope, Quality Assurance, Performance & Packaging (`QA`), Requirement-to-Phase Traceability, Requirements: LyricPulse (+23 more)

### Community 68 - "AnchorZone"
Cohesion: 0.06
Nodes (34): Risk 8 — Missing Fields on `AppSettings` (`CORE_ENGINE.md` §6.1) & FontScale Formula *(MEDIUM)*, HIGH, LOW, MEDIUM, Phase 6 Detailed Findings & Concrete Fixes, `UI-13` — Gallery Strip Cards Break Runtime Dark/Light Theme Parity (`TryFindBrush`), Render Text `"Pin"` Instead of Vector `PathIcon`, and Leak `PropertyChanged` Handlers, `UI-15` — Media Status Dot Permanently Locked to `LpLiveGreenBrush`, Missing Active Button Selection States, `< 40px` Top Icon Hit Targets, & Minor Chrome Hexes, AnchorZone (+26 more)

### Community 69 - "App.axaml.cs"
Cohesion: 0.08
Nodes (7): LyricPulse.App.Platform, LyricPulse.App, LyricPulse.UI.Theme, LyricPulse.UI.Views, LyricPulse.UI.Controls, LyricPulse.App.Tray, LyricPulse.UI.ViewModels

### Community 70 - "StyleCardViewModel"
Cohesion: 0.13
Nodes (15): StyleCardViewModel, CapabilityChip, Description, DisplayName, HasVisualIcons, IsActive, IsHovered, IsPinned (+7 more)

### Community 71 - "QueryUserNotificationState"
Cohesion: 0.25
Nodes (8): QueryUserNotificationState, AcceptsNotifications, App, Busy, NotPresent, PresentationMode, QuietTime, RunningD3DFullScreen

### Community 72 - "MultiMonitorOverlayCoordinator"
Cohesion: 0.13
Nodes (11): HIGH Findings (Milestone 1A Gate Blockers), `UI-06` — Runtime `AnchorZone` / `SafeMarginPx` / `PerDisplayBounds` Changes Never Apply to Existing Overlays + Unclamped Offscreen `destRect` Clipping, `UI-10` — Mixed-DPI Secondary Monitor Initial `ApplyPlacement` Ordering + Root DI Retention of Transient `ILyricSceneRenderer` Disposables, `UX-09` — Zero-Interruption Desktop & Multi-Monitor Edge Cases (`_hiddenAnchorWindow` Flash, `WS_EX_APPWINDOW`, Frozen `BoundsConfig`, Mixed-DPI Ordering), MultiMonitorOverlayCoordinator, ActiveOverlayCount, OverlayWindow, CanvasControl (+3 more)

### Community 73 - "LyricPulse.App.csproj"
Cohesion: 0.14
Nodes (13): Avalonia.Desktop, Avalonia.Diagnostics, Avalonia.Win32, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging, Avalonia.Controls.ColorPicker, Avalonia.Fonts.Inter, Avalonia.Skia (+5 more)

### Community 74 - "ConceptCentroidIndex"
Cohesion: 0.40
Nodes (5): ConceptCentroidIndex, ConceptCount, Concepts, Dimensions, Vectors

### Community 77 - ".RenderingUtilities_HandleAllBoundaryGuards_NaN_Empty_Fallback_AndDisposal"
Cohesion: 0.09
Nodes (13): SkiaSharp GPU Rendering, Typography, Color & Shaders (`RND`), LyricLine, LyricWord, Ease, CodestyleStyleRenderer, StyleId, IKineticStyleRenderer, StyleId (+5 more)

### Community 78 - "OversizedReadStream"
Cohesion: 0.17
Nodes (6): OversizedReadStream, CanRead, CanSeek, CanWrite, Length, Position

### Community 79 - "LyricsParser"
Cohesion: 0.12
Nodes (4): DemoSongProvider, DemoPalette, LyricsParser, DemoSongProviderTests

### Community 80 - ".BuildServiceProvider"
Cohesion: 0.15
Nodes (4): Risk 5 — Font Asset Assembly URI Ownership *(MEDIUM — `MASTER.md` vs `RENDERING_AND_WIN32.md`)*, LaunchOptions, Program, LyricPulseFontCollection

### Community 83 - "SpringParams"
Cohesion: 0.18
Nodes (7): SpringParams, DampingRatio, NaturalFrequency, SafeDamping, SafeMass, SafeStiffness, SpringTokens

### Community 84 - "LyricPulse.Tests.csproj"
Cohesion: 0.20
Nodes (9): AwesomeAssertions, coverlet.collector, Microsoft.NET.Test.Sdk, NSubstitute, xunit.runner.visualstudio, xunit.v3, HarfBuzzSharp.NativeAssets.Win32, SkiaSharp.NativeAssets.Win32 (+1 more)

### Community 85 - "RgbColor"
Cohesion: 0.16
Nodes (3): RgbColor, EdgeGlowRenderer, ReactiveVisualIconRenderer

### Community 87 - "LyricPulse.Rendering.csproj"
Cohesion: 0.22
Nodes (8): HarfBuzzSharp, SkiaSharp, SkiaSharp.HarfBuzz, Avalonia, Avalonia.Skia, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions, Microsoft.NET.Sdk

### Community 88 - "ClockUpdateKind"
Cohesion: 0.29
Nodes (7): ClockUpdateKind, HardSnap, IgnoredJitter, PausedFreeze, SeekSnap, SmoothSlew, ClockUpdateResult

### Community 89 - "TimeSpan"
Cohesion: 0.14
Nodes (4): TimingOffsetState, ActiveLyricSlice, Empty, Phase8NodeSpiderAndAlbumThemeTests

### Community 90 - "LyricPulse.Core.csproj"
Cohesion: 0.25
Nodes (7): Microsoft.Extensions.Http, System.Numerics.Tensors, System.Threading.RateLimiting, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions, Microsoft.Windows.CsWinRT, Microsoft.NET.Sdk

### Community 91 - "LyricPulse — Milestone 1A (Phases 1–3) UI & Visual Design Audit"
Cohesion: 0.18
Nodes (10): 1. Dimension Scorecard, 2. Detailed Findings & Concrete Fixes, 3. Verified Passing Controls (Preserve During Remediation), CRITICAL, Executive Verdict: **PASS WITH NOTES** *(Re-Verified on `70876e0` + `c6420bb` — Cleared for Milestone 1A Sign-Off)*, Initial Baseline Summary (Pre-Remediation), LOW, LyricPulse — Milestone 1A (Phases 1–3) UI & Visual Design Audit (+2 more)

### Community 94 - "MediaPlaybackState"
Cohesion: 0.10
Nodes (21): MediaItemKind, Advertisement, Music, PodcastOrLongForm, Unknown, Video, MediaPlaybackState, Changing (+13 more)

### Community 95 - "GloveSemanticVisualMatcher"
Cohesion: 0.15
Nodes (6): `UI-17` — Secondary Craft, Accessibility & Reduced-Motion Polish, GloveSemanticVisualMatcher, IsReady, Mode, ConstellationOverhaulTests, FisheyeVisualOverhaulTests

### Community 96 - "MediaHealthKind"
Cohesion: 0.29
Nodes (7): MediaHealthKind, Error, Idle, Loading, Paused, PlayingSynced, Warning

### Community 97 - "StageFlyoutKind"
Cohesion: 0.25
Nodes (8): StageFlyoutKind, Aesthetics, Diagnostics, Font, None, Screen, Size, Visualizer

### Community 100 - "LyricPulse.UI.csproj"
Cohesion: 0.33
Nodes (5): Avalonia, Avalonia.Controls.ColorPicker, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent, Microsoft.NET.Sdk

### Community 103 - "ObsChromaKeyMode"
Cohesion: 0.40
Nodes (4): ObsChromaKeyMode, Black, Blue, Green

### Community 104 - "UiModule.cs"
Cohesion: 0.50
Nodes (3): LyricPulse.UI, UiModule, ModuleName

### Community 105 - "StageRenderSurfaceMode"
Cohesion: 0.18
Nodes (10): StageBackdropMode, AmbientPartyMesh, PureBlack, TransparentOverlay, StageRenderSurfaceMode, DesktopOverlay, GalleryCard, ObsBroadcast (+2 more)

### Community 107 - "Color"
Cohesion: 0.23
Nodes (6): Progress Snapshot, Project Reference, Project State — LyricPulse, Verified v2.7 Release Metrics, AlbumEnvironmentPalette, AlbumUiAccentColors

### Community 114 - "ProviderDtos.cs"
Cohesion: 0.16
Nodes (14): 1. Build, Test & Coverage Summary, 3. Audit Findings, 4. Modified / Added Files Pending Commit, `LyricPulse.Core` Per-File Coverage Table (Post-Remediation), Non-Blocking Findings Logged for Phase 2 / Phase 3 Follow-Up (MEDIUM / LOW — `src/` Left Untouched), Phase 1 QA Gate, CachedLyricsEnvelopeDto, LrclibErrorDto (+6 more)

### Community 119 - "RenderResourceCache (Zero-Alloc Pool & Disposal)"
Cohesion: 0.38
Nodes (7): HarfBuzz Multi-Script Shaping & Overflow Auto-Fit, FontAssetRegistry & LyricPulseFontCollection (SIL OFL), Frame-Budget Telemetry & GPU/Software Fallback Matrix, LyricDrawOperation (ICustomDrawOperation + ISkiaSharpApiLeaseFeature), RenderResourceCache (Zero-Alloc Pool & Disposal), EllipticalBulge SkSL Shader (Rendering Contract), 13-Tap DirectionalMotionBlur SkSL Shader (Rendering Contract)

### Community 120 - "LyricsModels.cs"
Cohesion: 0.10
Nodes (19): LyricsStatus, Error, Instrumental, Loading, NotFound, PlainOnly, Synced, TimingKind (+11 more)

### Community 121 - "StyleCatalog"
Cohesion: 0.43
Nodes (5): ColorPresetOption, StyleCatalog, All, StudioPresets, StyleMetadata

### Community 122 - "microsoft_extensions_logging"
Cohesion: 0.12
Nodes (8): LyricPulse.Tests.Audio, LyricPulse.Rendering, LyricPulse.Core.Serialization, LyricPulse.Core.Lyrics.Dtos, LyricPulseIndentedJsonContext, LyricPulseJsonContext, RenderingModule, ModuleName

### Community 128 - ".AddLyricPulseCore"
Cohesion: 0.15
Nodes (22): Core Engine, Media Sessions & State (`CORE`), Deferred to v1.1 / v2 (Tracked Cleanly — Not Blocking v1.0), 2.1 Top 10 God Nodes (Highest Structural Degree), 3. Architectural Community Map, 5. Prioritized Feature Additions Schedule (Cross-Referencing `DEVELOPER_ADDITIONS.md`), Win32OverlayInterop, IAlbumColorExtractor, IFullscreenDetector (+14 more)

## Ambiguous Edges - Review These
- `SkSL Shader Uniform Animation Curves (Motion Spec)` → `EllipticalBulge SkSL Shader (Rendering Contract)`  [AMBIGUOUS]
  design-system/lyricpulse/MOTION_AND_STYLES.md · relation: semantically_similar_to
- `SkSL Shader Uniform Animation Curves (Motion Spec)` → `13-Tap DirectionalMotionBlur SkSL Shader (Rendering Contract)`  [AMBIGUOUS]
  design-system/lyricpulse/MOTION_AND_STYLES.md · relation: semantically_similar_to
- `Accessibility Toggles & Global Hotkey Keycap Recorder` → `Global System Hotkeys (RegisterHotKey)`  [AMBIGUOUS]
  design-system/lyricpulse/pages/settings.md · relation: semantically_similar_to
- `Multi-Monitor Selector Cards & 5-Zone Safe Anchor Grid` → `9-Point Safe-Zone Anchors & Unlock-to-Drag Mode`  [AMBIGUOUS]
  design-system/lyricpulse/pages/settings.md · relation: semantically_similar_to
- `FontAssetRegistry & LyricPulseFontCollection (SIL OFL)` → `App Chrome & Stage Font URI Specification (MASTER.md)`  [AMBIGUOUS]
  design-system/lyricpulse/MASTER.md · relation: semantically_similar_to
- `Typography Picker (2x2 SIL OFL) & ColorPicker Trio` → `AccentPalette Domain Contract`  [AMBIGUOUS]
  design-system/lyricpulse/pages/settings.md · relation: shares_data_with
- `Dynamic NativeMenu Hierarchy (Pinned Styles & Timing)` → `TimingOffsetState Machine (Per-Song Offset)`  [AMBIGUOUS]
  design-system/lyricpulse/pages/tray.md · relation: shares_data_with
- `OKLCH Perceptual Album Color Derivation (Motion Spec)` → `SkiaAlbumColorExtractor (OKLab K-Means++ K=8)`  [AMBIGUOUS]
  design-system/lyricpulse/MOTION_AND_STYLES.md · relation: semantically_similar_to
- `Sticky Media Capsule, Diagnostics Flyout & Timing Bar` → `TimingOffsetState Machine (Per-Song Offset)`  [AMBIGUOUS]
  design-system/lyricpulse/pages/gallery.md · relation: shares_data_with

## Knowledge Gaps
- **629 isolated node(s):** `ActiveSingleInstanceGuard`, `LaunchOptions`, `Avalonia.Desktop`, `Avalonia.Skia`, `Avalonia.Win32` (+624 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 817 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **15 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `StageViewModel` connect `StageViewModel` to `NetEaseLyricsProvider`, `AppSettings`, `OverlayStyleId`, `StageWindow`, `HotkeyAction`, `App`, `WasapiLoopbackAudioProvider`, `IDisposable`, `SKColor`, `TrayController`, `OverlayRuntimeController`, `.CreateAndShowPrimaryStageWindow`, `SingleInstanceGuard`, `.CreateStartedControllerAsync`, `HotkeyBindingViewModel`, `ImmutableArray`, `OverlayPlaybackSnapshot`, `OffscreenSurfacePool`, `.OnKeyDown`, `AnchorZone`, `App.axaml.cs`, `StyleCardViewModel`, `TimeSpan`, `MediaHealthKind`, `StageFlyoutKind`, `ObsChromaKeyMode`, `StageRenderSurfaceMode`, `Color`, `StyleCatalog`?**
  _High betweenness centrality (0.135) - this node is a cross-community bridge._
- **Are the 24 inferred relationships involving `StageViewModel` (e.g. with `2. Security, AOT, Resource-Hygiene & Thread-Safety Verification` and ``UX6-09` — Startup Hotkey Conflicts Are Not Synced to `StageViewModel``) actually correct?**
  _`StageViewModel` has 24 INFERRED edges - model-reasoned connections that need verification._
- **What connects `ActiveSingleInstanceGuard`, `LaunchOptions`, `Avalonia.Desktop` to the rest of the system?**
  _629 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `TrackIdentity` be split into smaller, more focused modules?**
  _Cohesion score 0.13548387096774195 - nodes in this community are weakly interconnected._
- **Why does `LyricPulse Platform Vision` connect `NativeAOT & Self-Contained Publish Profiles` to `Multi-Provider Lyrics Pipeline (ILyricsOrchestrator)`, `SettingsView`?**
  _High betweenness centrality (0.083) - this node is a cross-community bridge._

### Low-confidence Hints
_AMBIGUOUS edges — the extractor was unsure. Verify before acting on these._

- **What is the exact relationship between `SkSL Shader Uniform Animation Curves (Motion Spec)` and `EllipticalBulge SkSL Shader (Rendering Contract)`?**
  _Edge tagged AMBIGUOUS (relation: semantically_similar_to) - confidence is low._
- **What is the exact relationship between `SkSL Shader Uniform Animation Curves (Motion Spec)` and `13-Tap DirectionalMotionBlur SkSL Shader (Rendering Contract)`?**
  _Edge tagged AMBIGUOUS (relation: semantically_similar_to) - confidence is low._