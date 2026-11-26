# Graph Report - LyricPulse  (2026-11-26)

## Corpus Check
- 182 files · ~199,473 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 23 file(s) not represented in the graph (top: .axaml 7, .ttf 5, .sksl 3)

## Summary
- 2875 nodes · 9735 edges · 111 communities (101 shown, 10 thin omitted)
- Extraction: 78% EXTRACTED · 21% INFERRED · 0% AMBIGUOUS · INFERRED: 2093 edges (avg confidence: 0.87)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `b0d466a7`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Kinetic Styles & Semantic Visuals
- Multi-Monitor & Per-Monitor V2 DPI Overlay Coordinator
- ILogger
- Studio UI, Gallery & System Tray
- Multi-Provider Lyrics Pipeline (ILyricsOrchestrator)
- Task
- NativeAOT & Self-Contained Publish Profiles
- StageViewModel
- RgbColor
- FontAssetRegistry
- FrameRenderPacket
- NetEaseLyricsProvider
- AppSettings
- StyleRenderContext
- awesomeassertions
- RuntimeStateBox
- ShaderLibrary
- OverlayStyleId
- StageCardCanvasHost
- skiasharp
- TrackIdentity
- LyricCanvasControl
- Win32Native
- LyricPulse.Core.Models
- ConstellationStyleRenderer
- HotkeyAction
- .ComputePlacements
- App
- WasapiLoopbackAudioProvider
- .AmbientPartyMesh_CompilesCleanly_RendersRichGradient_PureBlack_AndStaticFallback
- LegibilityPipeline
- WordVisualMatch
- system_collections_immutable
- AlbumArtPayload
- OverlayRuntimeController
- .CreateAndShowPrimaryStageWindow
- TimingOffsetService
- WindowsGsmtcSessionProvider
- StarterLexiconCatalog
- ReadOnlySpan
- ThemeVariantMode
- StarterLexiconVisualMatcher
- LyricPulse.Core.Lyrics
- MessagesStyleRenderer
- StageWindow
- FrameBudgetTelemetry
- build_glove_index.py
- FullscreenAutoHideMonitor
- .SurfaceAndFocus
- .CreateStartedControllerAsync
- PlayerClassifier
- HotkeyBindingViewModel
- Fact
- ImmutableArray
- 1. v1.0 User Journey & CEO Requirements Scorecard
- MonotonicPlaybackClock
- NodeLineStyleRenderer
- SingleInstanceGuard
- AlbumAccentPalette
- .CreatePacket
- OverlayPlaybackSnapshot
- IDisposable
- MediaSessionCoordinator
- TrayIconFactory
- LyricPulse — Milestone 1A (Phases 1–3) UX Audit
- .OnKeyDown
- SettingsView
- TimingKind
- AudioVisualizerMode
- App.axaml.cs
- StyleCardViewModel
- 1. Remediation Verification Matrix (`e0dfe47`)
- AnchorZone
- LyricPulse.App.csproj
- ConceptCentroidIndex
- PlaybackLifecycleChoreographer
- Project State — LyricPulse
- .RenderingUtilities_HandleAllBoundaryGuards_NaN_Empty_Fallback_AndDisposal
- OversizedReadStream
- TimeSpan
- .BuildServiceProvider
- Phase 6 v1.0 Release QA Audit
- Milestone1ARemediationTests
- SpringParams
- LyricPulse.Tests.csproj
- RgbColor
- SecondaryLineMode
- LyricPulse.Rendering.csproj
- ClockUpdateKind
- DemoSongProvider
- LyricPulse.Core.csproj
- LyricPulse — Milestone 1A (Phases 1–3) UI & Visual Design Audit
- IWordVisualMatcher
- ThemeVariantMode
- MediaPlaybackState
- GloveSemanticVisualMatcher
- MediaHealthKind
- StageFlyoutKind
- LyricPulse.UI.csproj
- ObsChromaKeyMode
- UiModule.cs
- .FromPlaybackSnapshot
- Color
- ScrimIntensity
- AppSettings.cs
- RenderResourceCache (Zero-Alloc Pool & Disposal)
- LyricsStatus
- StyleCatalog
- LyricPulse.Rendering
- .AddLyricPulseCore

## God Nodes (most connected - your core abstractions)
1. `StageViewModel` - 248 edges
2. `LyricPulse.Core.Models` - 110 edges
3. `AppSettings` - 104 edges
4. `OverlayRuntimeController` - 97 edges
5. `FontAssetRegistry` - 96 edges
6. `RgbColor` - 87 edges
7. `FrameRenderPacket` - 83 edges
8. `StyleRenderContext` - 78 edges
9. `LegibilityPipeline` - 74 edges
10. `App` - 68 edges

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

## Communities (111 total, 10 thin omitted)

### Community 0 - "Kinetic Styles & Semantic Visuals"
Cohesion: 0.14
Nodes (24): Neon Horizon Built-In Demo Song (12-Line LRC), 4-Layer Composed Overlay Legibility Pipeline, SkSL Shader Uniform Animation Curves (Motion Spec), Damped Harmonic Spring & Easing Token System, Styles 3-4: Codestyle & CodestyleVisual, Style 6: Constellation, Styles 1-2: Fisheye & FisheyeVisual, Style 9: KineticWall (3D Drift) (+16 more)

### Community 1 - "Multi-Monitor & Per-Monitor V2 DPI Overlay Coordinator"
Cohesion: 0.18
Nodes (12): Avalonia 11 XAML & Compiled Bindings Standards, Multi-Monitor Selector Cards & 5-Zone Safe Anchor Grid, CsWinRT 2.3.1 AOT GSMTC Projection, Central Package Management & SkiaSharp ABI Lockstep, Fullscreen Game/Presentation Auto-Hide (SHQueryUserNotificationState), 9-Point Safe-Zone Anchors & Unlock-to-Drag Mode, AppBuilder & Win32PlatformOptions Pipeline, Avalonia 11.3.x + SkiaSharp 3.119.x + HarfBuzzSharp Stack (+4 more)

### Community 2 - "ILogger"
Cohesion: 0.17
Nodes (3): AppLogMessages, CoreLogMessages, RenderingLogMessages

### Community 3 - "Studio UI, Gallery & System Tray"
Cohesion: 0.29
Nodes (11): Acoustic Studio Lightbox Design System (MASTER.md), App Chrome & Stage Font URI Specification (MASTER.md), WCAG 2.2 AA/AAA ThemeDictionaries Token System, Live SkiaSharp Style Preview Cards (30/60 FPS Stagger), GalleryWindow (2-Column Live Style Gallery), SettingsView Inline/Overlay Drawer, Typography Picker (2x2 SIL OFL) & ColorPicker Trio, TrayController (Avalonia TrayIcon + NativeMenu) (+3 more)

### Community 4 - "Multi-Provider Lyrics Pipeline (ILyricsOrchestrator)"
Cohesion: 0.20
Nodes (14): Keyboard Navigation, Focus Rings & Live Regions, OKLCH Perceptual Album Color Derivation (Motion Spec), Sticky Media Capsule, Diagnostics Flyout & Timing Bar, AccentPalette Domain Contract, Sharded Atomic Disk Lyrics Cache, IMediaSessionProvider, LRCLIB Lyrics Provider, System.Text.Json AOT Source Generation (LyricsJsonContext) (+6 more)

### Community 5 - "Task"
Cohesion: 0.08
Nodes (12): `UX-02` — Silent First Launch, Unusable Initial Tray Menu (`Menu == null`), and Broken `--demo` / Second-Instance Feedback, FakeMediaSessionProvider, CurrentSession, LastPolicy, Position, FakeMediaProvider, CurrentSession, FakeMediaSessionProvider (+4 more)

### Community 6 - "NativeAOT & Self-Contained Publish Profiles"
Cohesion: 0.20
Nodes (10): LyricPulse Platform Vision, 6-Phase GSD Roadmap, MediaSessionSelector (Priority + Hysteresis), MonotonicPlaybackClock & Drift Clamping, TimingOffsetState Machine (Per-Song Offset), WindowsGsmtcSessionProvider, OBS / Streamer Broadcast & Chroma-Key Capture Window, Per-Track Persisted Timing Offsets (ITrackOffsetStore) (+2 more)

### Community 7 - "StageViewModel"
Cohesion: 0.01
Nodes (173): StageViewModel, ActiveFlyout, ActivePaletteColor1, ActivePaletteColor2, ActivePaletteColor3, ActivePinTooltip, ActiveStyle, ActiveStyleIndex (+165 more)

### Community 8 - "RgbColor"
Cohesion: 0.10
Nodes (8): OklabColorMath, DeterministicPrng, OklabCluster, OklabPaletteExtractor, WeightedPixel, OklabColor, OklchColor, OklabColorMathTests

### Community 9 - "FontAssetRegistry"
Cohesion: 0.08
Nodes (27): 2. Security, AOT, Resource-Hygiene & Thread-Safety Verification, 3. Severity-Classified Findings (`371bfa5` — Read-Only Audit), CRITICAL Findings, MEDIUM Findings, Non-Blocking Phase 4/5 Hygiene Notes (LOW), Blocker Closure Matrix (`UI-01` – `UI-12`), Re-Verification Addendum (`c6420bb`), Remaining Non-Blocking Low Notes (Deferrable to Phase 4/5) (+19 more)

### Community 10 - "FrameRenderPacket"
Cohesion: 0.09
Nodes (16): FrameRenderPacket, EffectiveAudioMetrics, EffectiveHeroFontSizeDip, EffectiveStyle, IsCompactCard, SafeHeroScale, SafePartyEnergy, ShouldDrawStageBackdrop (+8 more)

### Community 11 - "NetEaseLyricsProvider"
Cohesion: 0.06
Nodes (22): LrclibTrackDto, HttpFetchResult, LrcLibLyricsProvider, Name, PriorityOrder, SourceKind, HttpFetchResult, NetEaseLyricsProvider (+14 more)

### Community 12 - "AppSettings"
Cohesion: 0.04
Nodes (44): AppSettings, ActiveStyle, AnchorZone, AudioVisualizerIntensity, AudioVisualizerMode, AutoSyncColors, BlockedAppIds, Default (+36 more)

### Community 13 - "StyleRenderContext"
Cohesion: 0.07
Nodes (24): Phase 3: Working 3-Style MVP + Tray Control (Milestone 1A Check-in), Phase 4: Offline GloVe/Fluent/Emoji Semantic Engine + Remaining 7 Kinetic Styles, Phase 6: Full QA/UX/UI Audit, Performance Tuning & Release Packaging, Phase Details, Phase 6 Dimension Scorecard, Phase 6 Executive Verdict: **FAIL** *(0 Critical, 2 High, 2 Medium, 1 Low — Remediate `UI-13` & `UI-14` Before v1.0 Sign-Off)*, Phase 6 v1.0 Release UI Audit, `UI-09` — 3-Style Choreography & Lifecycle Spec Drifts (`MOTION_AND_STYLES.md` §§6 & 8.1) (+16 more)

### Community 14 - "awesomeassertions"
Cohesion: 0.17
Nodes (7): LyricPulse.Tests.Rendering, LyricPulse.App, LyricPulse.Tests.Visuals, LyricPulse.Tests.Settings, LyricPulse.Rendering.Legibility, LyricPulse.Core.Runtime, LyricPulse.Rendering.Shaders

### Community 15 - "RuntimeStateBox"
Cohesion: 0.16
Nodes (8): 3. Remaining Non-Blocking Notes (Tracked for Phases 4–6), Re-Verification Addendum (`c6420bb`), PaletteMorphState, RuntimeStateBox, Initial, IsEffectiveReducedMotion, OverlaySnapshotBuilder, TimingOffsetServiceTests

### Community 16 - "ShaderLibrary"
Cohesion: 0.12
Nodes (9): ShaderLibrary, AreAllShadersReady, BulgeCompileError, IsBulgeReady, IsMotionBlurReady, IsPartyMeshReady, MotionBlurCompileError, PartyMeshCompileError (+1 more)

### Community 17 - "OverlayStyleId"
Cohesion: 0.11
Nodes (19): SmokeTestRunner, OverlayStyleId, Codestyle, CodestyleVisual, Constellation, Fisheye, FisheyeVisual, KineticWall (+11 more)

### Community 18 - "StageCardCanvasHost"
Cohesion: 0.09
Nodes (13): MEDIUM, `UX6-06` — Second-Instance Wakeup & `Ctrl+Shift+G` Force Global Demo Mode When Paused / Instrumental / No-Lyrics, `UX6-07` — `"Play previews"` Button Is a Dead No-Op Under Reduced Motion, `UX6-08` — `RebuildStyleStrip()` Accumulates `PropertyChanged` Subscriptions on Filter Changes, `UX6-09` — Startup Hotkey Conflicts Are Not Synced to `StageViewModel`, StageCardCanvasHost, BottomFadeToBlack, IsCardPaused (+5 more)

### Community 19 - "skiasharp"
Cohesion: 0.20
Nodes (6): LyricPulse.Rendering.Styles, LyricPulse.Rendering.Host, LyricPulse.Rendering.Fonts, LyricPulse.Rendering.Color, LyricPulse.Rendering.Motion, RenderingServiceCollectionExtensions

### Community 20 - "TrackIdentity"
Cohesion: 0.08
Nodes (23): ILyricsCache, ILyricsParser, ILyricsProvider, Name, PriorityOrder, SourceKind, DiskLyricsCache, LocalLrcSidecarProvider (+15 more)

### Community 21 - "LyricCanvasControl"
Cohesion: 0.05
Nodes (30): 1. Build, Test, Smoke & Coverage Summary (`371bfa5`), 2. Test Rigor & Phase 1–3 Verification Audit, Blocker-by-Blocker Closure Proof, HIGH Findings (Milestone 1A Gate Blockers), `LyricPulse.Rendering` Per-File Coverage Breakdown (`371bfa5`), Milestone 1A (Phases 1-3) QA Gate, Milestone 1A Re-Verification (`c6420bb`), Phase 2: Click-Through Overlay Host, SkiaSharp Pipeline & Shaders (+22 more)

### Community 22 - "Win32Native"
Cohesion: 0.11
Nodes (7): Win32 Overlay Host & System Integration (`WIN`), MONITORINFOEXW, MsgStruct, PointStruct, RectStruct, Win32Native, Win32OverlayInterop

### Community 23 - "LyricPulse.Core.Models"
Cohesion: 0.18
Nodes (5): LyricPulse.Tests.Audio, LyricPulse.Core.Color, LyricPulse.Core.Models, LyricPulse.Tests.Color, LyricPulse.Core.Audio

### Community 24 - "ConstellationStyleRenderer"
Cohesion: 0.14
Nodes (5): SkiaSharp GPU Rendering, Typography, Color & Shaders (`RND`), Ease, ConstellationStyleRenderer, StyleId, StarNode

### Community 25 - "HotkeyAction"
Cohesion: 0.08
Nodes (21): 2. Remaining Non-Blocking Notes (Post-v1.0 Polish), 3. Verified UX Strengths (Preserve During Remediation), `UX6-02` — Hotkey Recorder Destroys Valid Previous Shortcut on Conflict & Rejects `D0–D9` / `Backspace` / `Delete`, GlobalHotkeyManager, HotkeyCommand, Action, CommandKind, Completion (+13 more)

### Community 26 - ".ComputePlacements"
Cohesion: 0.23
Nodes (5): MonitorDescriptor, OverlayBoundsCalculator, OverlayWindowPlacement, SafeZoneRect, PlatformOverlayAndGuardTests

### Community 27 - "App"
Cohesion: 0.07
Nodes (9): Updated Verification Metrics (`371bfa5` → `c6420bb`), `UX-07` — System Tray Menu Ergonomics, `tray.md` Copy Fidelity, Menu Rebuild Flicker, and Screen-Reader Accelerator Hygiene, App, ActiveSingleInstanceGuard, LaunchOptions, MultiMonitorOverlayCoordinator, ActiveOverlayCount, DisplayMonitorInfo (+1 more)

### Community 28 - "WasapiLoopbackAudioProvider"
Cohesion: 0.06
Nodes (15): IAudioCaptureClient, IAudioClient, IMMDevice, IMMDeviceEnumerator, MMDeviceEnumeratorComObject, WasapiLoopbackAudioProvider, AudioCaptureClientIid, AudioClientIid (+7 more)

### Community 30 - "LegibilityPipeline"
Cohesion: 0.10
Nodes (12): 2. Bonus MEDIUM / LOW Fixes Confirmed in `c6420bb`, `UX-01` — `Visual Stack` & Demo Mode Semantic Comprehension Failure (12/12 `Neon Horizon` Icons Wrong + Unreadable White-Page Badges), SolidDiagnosticSceneRenderer, StyleId, CachedShapedBlob, LegibilityPipeline, CachedBlurFilterCount, CachedShadowBucketCount (+4 more)

### Community 31 - "WordVisualMatch"
Cohesion: 0.20
Nodes (10): LyricsDocument, VisualKind, Emoji, FluentIcon, VisualPreference, Balanced, EmojiOnly, FluentIconsOnly (+2 more)

### Community 32 - "system_collections_immutable"
Cohesion: 0.13
Nodes (4): LyricPulse.Core.Media, LyricPulse.Tests.Media, LyricPulseIndentedJsonContext, LyricPulseJsonContext

### Community 33 - "AlbumArtPayload"
Cohesion: 0.24
Nodes (3): AlbumArtPayload, SkiaAlbumColorExtractor, SkiaAlbumColorExtractorTests

### Community 34 - "OverlayRuntimeController"
Cohesion: 0.08
Nodes (15): LOW Findings, 1. CRITICAL & HIGH Remediation Verification Matrix, HIGH, `UX-03` — Broken Lyrics State Handling: `PlainOnly` Freezes Forever on Line 1, Every Track Change Flashes False Error, Zero Offline Retry, Wrong `Instrumental` Warning, & Frozen Outros, `UX-04` — Global Hotkey Conflicts Silently Discarded + Unguarded Offset Hotkeys, Track-Switch Offset Leak, and Non-Smooth Spring Resets, `UX-06` — Reduced Motion OS Desync, Un-Calmed Mid-Flight Palette Morphs, Inverse Onset Hierarchy Dip, and Blocking Quit Shutdown Race, IAlbumColorExtractor, IFullscreenDetector (+7 more)

### Community 35 - ".CreateAndShowPrimaryStageWindow"
Cohesion: 0.14
Nodes (11): DisplayOptionViewModel, DisplayKey, DpiScale, FriendlyName, HeaderText, HeightPx, Index, IsEnabled (+3 more)

### Community 36 - "TimingOffsetService"
Cohesion: 0.16
Nodes (9): Non-Blocking Findings Logged for Phase 2 / Phase 3 Follow-Up (MEDIUM / LOW — `src/` Left Untouched), Remediated During Phase 1 QA Gate (CRITICAL / HIGH), ITrackOffsetStore, TrackOffsetEntryDto, TrackOffsetsEnvelopeDto, TimingOffsetService, Current, TrackOffsetStore (+1 more)

### Community 37 - "WindowsGsmtcSessionProvider"
Cohesion: 0.10
Nodes (5): Phase 1: Foundation, Media Engine & Synced Lyrics Core, GsmtcSessionHandle, Session, WindowsGsmtcSessionProvider, CurrentSession

### Community 38 - "StarterLexiconCatalog"
Cohesion: 0.16
Nodes (11): LyricTextNormalizationData, IrregularLemmas, Stopwords, SemanticIdiomLexicon, AllMoodConceptIds, Bigrams, StarterLexiconCatalog, Concepts (+3 more)

### Community 39 - "ReadOnlySpan"
Cohesion: 0.12
Nodes (4): QuantizedEmbeddingTable, Dimensions, Version, VocabCount

### Community 40 - "ThemeVariantMode"
Cohesion: 0.29
Nodes (7): HIGH, LOW, MEDIUM, Phase 6 Detailed Findings & Concrete Fixes, `UI-13` — Gallery Strip Cards Break Runtime Dark/Light Theme Parity (`TryFindBrush`), Render Text `"Pin"` Instead of Vector `PathIcon`, and Leak `PropertyChanged` Handlers, `UI-15` — Media Status Dot Permanently Locked to `LpLiveGreenBrush`, Missing Active Button Selection States, `< 40px` Top Icon Hit Targets, & Minor Chrome Hexes, `UI-17` — Secondary Craft, Accessibility & Reduced-Motion Polish

### Community 41 - "StarterLexiconVisualMatcher"
Cohesion: 0.18
Nodes (6): StarterLexiconVisualMatcher, IsReady, Mode, CoreInfrastructureTests, TitleNormalizerTests, StarterLexiconVisualMatcherTests

### Community 42 - "LyricPulse.Core.Lyrics"
Cohesion: 0.11
Nodes (11): LyricPulse.Core.Settings, LyricPulse.Core.Serialization, LyricPulse.Core, LyricPulse.Core.Lyrics, LyricPulse.Tests.UI, LyricPulse.Tests, LyricPulse.Tests.Lyrics, LyricPulse.Core.Lyrics.Dtos (+3 more)

### Community 43 - "MessagesStyleRenderer"
Cohesion: 0.13
Nodes (5): 4. Recommended Remediation Priority Order, `UI-02` — Layer 0 Radial Ambient Scrim Hard-Clips Into a Boxy Rectangle & Violates Corner Transparency in `Fisheye` / `EllipticalBulge.sksl` and `VisualStack`, MotionBlurMath, MessagesStyleRenderer, StyleId

### Community 45 - "FrameBudgetTelemetry"
Cohesion: 0.13
Nodes (17): FrameBudgetTelemetry, ActiveBackend, DroppedFramesCount, LastFrameMs, ShaderState, TotalFrames, FrameTelemetrySnapshot, RenderBackendKind (+9 more)

### Community 46 - "build_glove_index.py"
Cohesion: 0.14
Nodes (6): load_glove_text(), main(), _normalize(), postprocess_all_but_the_top_numpy(), postprocess_all_but_the_top_pure_python(), quantize_and_write_lpqv()

### Community 47 - "FullscreenAutoHideMonitor"
Cohesion: 0.12
Nodes (10): FullscreenAutoHideMonitor, IsCurrentlyFullscreen, QueryUserNotificationState, AcceptsNotifications, App, Busy, NotPresent, PresentationMode (+2 more)

### Community 48 - ".SurfaceAndFocus"
Cohesion: 0.20
Nodes (5): LOW Findings, Blocker Closure Matrix (`UI-13` – `UI-17`), Phase 6 Re-Verification Addendum (`e0dfe47`), Remaining Non-Blocking Notes Observed in `e0dfe47`, `UI-14` — Hidden `StageWindow` (`OnClosing` → Tray) Never Pauses `_heroHost` + Unreachable `IsFullscreen` Branch & Over-Wide Strip Viewport Floor

### Community 50 - "PlayerClassifier"
Cohesion: 0.24
Nodes (6): 1. Build, Test & Coverage Summary, 3. Audit Findings, 4. Modified / Added Files Pending Commit, `LyricPulse.Core` Per-File Coverage Table (Post-Remediation), Phase 1 QA Gate, PlayerClassifier

### Community 51 - "HotkeyBindingViewModel"
Cohesion: 0.09
Nodes (12): HotkeyBindingViewModel, Action, BeginRecordCommand, ClearCommand, DisplayButtonText, ErrorMessage, GestureText, HasError (+4 more)

### Community 53 - "ImmutableArray"
Cohesion: 0.19
Nodes (11): CachedLyricsEnvelopeDto, LrclibErrorDto, NetEaseLyricBlockDto, NetEaseLyricResponseDto, NetEaseNamedDto, NetEaseSearchResponseDto, NetEaseSearchResultDto, NetEaseSongDto (+3 more)

### Community 54 - "1. v1.0 User Journey & CEO Requirements Scorecard"
Cohesion: 0.14
Nodes (14): 1. v1.0 User Journey & CEO Requirements Scorecard, 2. Severity-Classified Findings, Executive Verdict: **FAIL** *(0 CRITICAL, 5 HIGH, 5 MEDIUM, 4 LOW — 5 HIGH UX/A11y/Contrast Blockers Before v1.0 Release Sign-Off)*, HIGH (Release Blockers), LOW, Phase 6 v1.0 Release UX Audit, `UX6-01` — Settings Drawer Always Docks Inline (`380px`), Squeezing `StageWindow` & Clipping Top Bar Below `880px`, `UX6-03` — Bottom 10-Card Strip Unreachable via Keyboard + Missing Settings/Flyout Focus Management (+6 more)

### Community 55 - "MonotonicPlaybackClock"
Cohesion: 0.27
Nodes (4): ClockAnchorBox, Empty, MonotonicPlaybackClock, MonotonicPlaybackClockTests

### Community 56 - "NodeLineStyleRenderer"
Cohesion: 0.33
Nodes (3): BranchNode, NodeLineStyleRenderer, StyleId

### Community 57 - "SingleInstanceGuard"
Cohesion: 0.13
Nodes (3): HotkeyGestureParser, ParsedHotkeyGesture, SingleInstanceGuard

### Community 58 - "AlbumAccentPalette"
Cohesion: 0.26
Nodes (8): AudioFrameMetrics, SafeBands, Silent, AlbumAccentPalette, Default, DemoNeonHorizon, MonochromeStudio, AudioVisualizerRenderer

### Community 60 - "OverlayPlaybackSnapshot"
Cohesion: 0.10
Nodes (17): 1. Dimension Scorecard, `UI-11` — Tray Menu Copy, Paused Icon Semantics, and Menu Rebuild Polish (`pages/tray.md`), `UX-05` — Unpredictable 3-Way Tray Left-Click, Misleading Paused/Fullscreen Icon & Tooltip States, and No Milestone 1A Universal Player Fallback Toggle, TrayController, TrayMenuState, TrayOffsetOption, TrayStyleOption, OverlayPlaybackSnapshot (+9 more)

### Community 61 - "IDisposable"
Cohesion: 0.18
Nodes (7): OffscreenSurfacePool, PurgeCount, OffscreenSurfaceSlot, Bulge, MotionBlur, Scratch, PooledSlot

### Community 62 - "MediaSessionCoordinator"
Cohesion: 0.14
Nodes (11): 2. Critical Invariant & Edge-Case Audit Matrix, MediaSessionCoordinator, BlockedAppIds, CachedAlbumArt, CurrentSession, IgnoreBrowserVideoSessions, SourcePolicy, MediaSessionSelector (+3 more)

### Community 63 - "TrayIconFactory"
Cohesion: 0.22
Nodes (7): `UI-07` — Light Windows Taskbar Status Dots Fail WCAG 3.0:1 (`2.18:1` / `2.05:1`), Single-Size `32×32` PNG, and Stale Taskbar Polarity on OS Theme Switch, TrayIconFactory, TrayIconVisualState, Error, Idle, OverlayHidden, Playing

### Community 64 - "LyricPulse — Milestone 1A (Phases 1–3) UX Audit"
Cohesion: 0.17
Nodes (11): 1. Milestone 1A User Journey Scorecard, 2. Detailed UX Findings & Concrete Fixes, 4. Recommended Remediation Priority Order (For Milestone 1A Re-Verification), Baseline Findings Summary (`371bfa5` — Historical Record), CRITICAL, Executive Verdict: **PASS WITH NOTES** *(Re-Verified on `70876e0` + `c6420bb` — 0 Critical, 0 High Blockers Remaining; Cleared for Milestone 1A CEO Check-In)*, LOW, LyricPulse — Milestone 1A (Phases 1–3) UX Audit (+3 more)

### Community 66 - "SettingsView"
Cohesion: 0.07
Nodes (23): Accessibility & User Experience (`UX`), Desktop UI — System Tray, Gallery & Settings (`UI`), Offline Semantic Word-to-Icon Engine (`VIS`), Quality Assurance, Performance & Packaging (`QA`), Synced Lyrics Pipeline (`LYR`), v1 Requirements, 1. Knowledge Graph Statistics & Integrity Audit, 2.2 Top Betweenness-Centrality Bridge Nodes (+15 more)

### Community 67 - "TimingKind"
Cohesion: 0.33
Nodes (6): TimingKind, LineSynced, None, PlainUnsynced, WordInterpolated, WordNative

### Community 68 - "AudioVisualizerMode"
Cohesion: 0.29
Nodes (7): AudioVisualizerMode, BassHorizon, FluidWave, None, ParticleField, RadialHalo, SpectrumBars

### Community 69 - "App.axaml.cs"
Cohesion: 0.08
Nodes (6): LyricPulse.App.Platform, LyricPulse.UI.Theme, LyricPulse.UI.Views, LyricPulse.UI.Controls, LyricPulse.App.Tray, LyricPulse.UI.ViewModels

### Community 70 - "StyleCardViewModel"
Cohesion: 0.13
Nodes (15): StyleCardViewModel, CapabilityChip, Description, DisplayName, HasVisualIcons, IsActive, IsHovered, IsPinned (+7 more)

### Community 72 - "AnchorZone"
Cohesion: 0.29
Nodes (8): Risk 8 — Missing Fields on `AppSettings` (`CORE_ENGINE.md` §6.1) & FontScale Formula *(MEDIUM)*, AnchorZone, BottomLeft, BottomRight, BottomThird, MiddleCenter, TopCenter, OverlayBoundsConfig

### Community 73 - "LyricPulse.App.csproj"
Cohesion: 0.14
Nodes (13): Avalonia.Desktop, Avalonia.Diagnostics, Avalonia.Win32, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging, Avalonia.Controls.ColorPicker, Avalonia.Fonts.Inter, Avalonia.Skia (+5 more)

### Community 74 - "ConceptCentroidIndex"
Cohesion: 0.29
Nodes (5): ConceptCentroidIndex, ConceptCount, Concepts, Dimensions, Vectors

### Community 75 - "PlaybackLifecycleChoreographer"
Cohesion: 0.33
Nodes (3): `UX-10` — Zero Visual Confirmation When Cycling Styles (`Ctrl+Shift+S`) or Unhiding Lyrics (`Ctrl+Shift+L`) Outside Active Vocals, PlaybackLifecycleChoreographer, PlaybackLifecycleChoreographerTests

### Community 76 - "Project State — LyricPulse"
Cohesion: 0.14
Nodes (12): Permanently Out of Scope, Requirement-to-Phase Traceability, Requirements: LyricPulse, v2 / Deferred & Permanently Out of Scope, Overview, Phases, Progress, Roadmap: LyricPulse (+4 more)

### Community 77 - ".RenderingUtilities_HandleAllBoundaryGuards_NaN_Empty_Fallback_AndDisposal"
Cohesion: 0.14
Nodes (5): CodestyleStyleRenderer, StyleId, CodestyleVisualStyleRenderer, StyleId, StyleLayoutHelper

### Community 78 - "OversizedReadStream"
Cohesion: 0.17
Nodes (6): OversizedReadStream, CanRead, CanSeek, CanWrite, Length, Position

### Community 79 - "TimeSpan"
Cohesion: 0.06
Nodes (8): CandidateScorer, LyricsParser, TitleNormalizer, WordTimingInterpolator, LyricLine, ActiveLyricSlice, Empty, LyricsParserTests

### Community 80 - ".BuildServiceProvider"
Cohesion: 0.21
Nodes (3): LaunchOptions, Program, TrayAndLaunchTests

### Community 81 - "Phase 6 v1.0 Release QA Audit"
Cohesion: 0.29
Nodes (6): 1. Build, Test, Smoke & Coverage Summary (`b88131e`), 3. Severity-Classified Findings (`b88131e` — Read-Only Audit), CRITICAL / HIGH Findings, LyricPulse — Quality Assurance Verification Report, Per-File Coverage Highlights (`b88131e` — Phase 4/5 Additions), Phase 6 v1.0 Release QA Audit

### Community 83 - "SpringParams"
Cohesion: 0.18
Nodes (7): SpringParams, DampingRatio, NaturalFrequency, SafeDamping, SafeMass, SafeStiffness, SpringTokens

### Community 84 - "LyricPulse.Tests.csproj"
Cohesion: 0.20
Nodes (9): AwesomeAssertions, coverlet.collector, Microsoft.NET.Test.Sdk, NSubstitute, xunit.runner.visualstudio, xunit.v3, HarfBuzzSharp.NativeAssets.Win32, SkiaSharp.NativeAssets.Win32 (+1 more)

### Community 85 - "RgbColor"
Cohesion: 0.11
Nodes (6): RgbColor, SkColorExtensions, LegibilityStyleSpec, ReactiveVisualIconRenderer, RgbColorExtensions, LegibilityPipelineTests

### Community 86 - "SecondaryLineMode"
Cohesion: 0.40
Nodes (5): 10 Kinetic Lyric Styles & Lifecycle Choreography (`STY`), SecondaryLineMode, Off, Romanization, Translation

### Community 87 - "LyricPulse.Rendering.csproj"
Cohesion: 0.22
Nodes (8): HarfBuzzSharp, SkiaSharp, SkiaSharp.HarfBuzz, Avalonia, Avalonia.Skia, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions, Microsoft.NET.Sdk

### Community 88 - "ClockUpdateKind"
Cohesion: 0.29
Nodes (7): ClockUpdateKind, HardSnap, IgnoredJitter, PausedFreeze, SeekSnap, SmoothSlew, ClockUpdateResult

### Community 89 - "DemoSongProvider"
Cohesion: 0.16
Nodes (6): DemoSongProvider, DemoPalette, DemoSongProviderTests, Phase8NodeSpiderAndAlbumThemeTests, ConstellationOverhaulTests, FisheyeVisualOverhaulTests

### Community 90 - "LyricPulse.Core.csproj"
Cohesion: 0.25
Nodes (7): Microsoft.Extensions.Http, System.Numerics.Tensors, System.Threading.RateLimiting, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions, Microsoft.Windows.CsWinRT, Microsoft.NET.Sdk

### Community 91 - "LyricPulse — Milestone 1A (Phases 1–3) UI & Visual Design Audit"
Cohesion: 0.17
Nodes (11): 2. Detailed Findings & Concrete Fixes, 3. Verified Passing Controls (Preserve During Remediation), CRITICAL, Executive Verdict: **PASS WITH NOTES** *(Re-Verified on `70876e0` + `c6420bb` — Cleared for Milestone 1A Sign-Off)*, Initial Baseline Summary (Pre-Remediation), LOW, LyricPulse — Milestone 1A (Phases 1–3) UI & Visual Design Audit, MEDIUM (+3 more)

### Community 92 - "IWordVisualMatcher"
Cohesion: 0.22
Nodes (8): VisualMatcherMode, FullSemanticGloVe, StarterLexicon, CountingVisualMatcher, AnnotateCalls, DirectMatchWordCalls, IsReady, Mode

### Community 93 - "ThemeVariantMode"
Cohesion: 0.40
Nodes (4): ThemeVariantMode, Dark, Light, System

### Community 94 - "MediaPlaybackState"
Cohesion: 0.10
Nodes (21): MediaItemKind, Advertisement, Music, PodcastOrLongForm, Unknown, Video, MediaPlaybackState, Changing (+13 more)

### Community 95 - "GloveSemanticVisualMatcher"
Cohesion: 0.16
Nodes (5): LyricWord, GloveSemanticVisualMatcher, IsReady, Mode, Phase4ASemanticAndSettingsTests

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

### Community 105 - ".FromPlaybackSnapshot"
Cohesion: 0.17
Nodes (10): StageRenderSurfaceMode, DesktopOverlay, GalleryCard, ObsBroadcast, StageCanvas, OverlayFrameAdapter, Milestone1ARemediationTests, ResetCountingStyleRenderer (+2 more)

### Community 112 - "ScrimIntensity"
Cohesion: 0.40
Nodes (5): ScrimIntensity, Balanced, Off, Strong, Subtle

### Community 114 - "AppSettings.cs"
Cohesion: 0.17
Nodes (10): EdgeGlowMode, BeatPulse, NeonRim, NeonRush, Off, SoftAmbient, StageBackdropMode, AmbientPartyMesh (+2 more)

### Community 119 - "RenderResourceCache (Zero-Alloc Pool & Disposal)"
Cohesion: 0.32
Nodes (8): HarfBuzz Multi-Script Shaping & Overflow Auto-Fit, One-Click Shareable Lyric Poster Export (Skia PNG), FontAssetRegistry & LyricPulseFontCollection (SIL OFL), Frame-Budget Telemetry & GPU/Software Fallback Matrix, LyricDrawOperation (ICustomDrawOperation + ISkiaSharpApiLeaseFeature), RenderResourceCache (Zero-Alloc Pool & Disposal), EllipticalBulge SkSL Shader (Rendering Contract), 13-Tap DirectionalMotionBlur SkSL Shader (Rendering Contract)

### Community 120 - "LyricsStatus"
Cohesion: 0.29
Nodes (7): LyricsStatus, Error, Instrumental, Loading, NotFound, PlainOnly, Synced

### Community 121 - "StyleCatalog"
Cohesion: 0.43
Nodes (5): ColorPresetOption, StyleCatalog, All, StudioPresets, StyleMetadata

### Community 122 - "LyricPulse.Rendering"
Cohesion: 0.50
Nodes (3): LyricPulse.Rendering, RenderingModule, ModuleName

### Community 128 - ".AddLyricPulseCore"
Cohesion: 0.08
Nodes (21): MEDIUM Findings, Core Engine, Media Sessions & State (`CORE`), Deferred to v1.1 / v2 (Tracked Cleanly — Not Blocking v1.0), 2.1 Top 10 God Nodes (Highest Structural Degree), 3. Architectural Community Map, 5. Prioritized Feature Additions Schedule (Cross-Referencing `DEVELOPER_ADDITIONS.md`), IMediaSessionProvider, CurrentSession (+13 more)

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
- `FontAssetRegistry & LyricPulseFontCollection (SIL OFL)` → `App Chrome & Stage Font URI Specification (MASTER.md)`  [AMBIGUOUS]
  design-system/lyricpulse/MASTER.md · relation: semantically_similar_to

## Knowledge Gaps
- **617 isolated node(s):** `ActiveSingleInstanceGuard`, `LaunchOptions`, `Avalonia.Desktop`, `Avalonia.Skia`, `Avalonia.Win32` (+612 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 804 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **10 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `StageViewModel` connect `StageViewModel` to `ILogger`, `Task`, `FontAssetRegistry`, `NetEaseLyricsProvider`, `AppSettings`, `OverlayStyleId`, `StageCardCanvasHost`, `HotkeyAction`, `App`, `OverlayRuntimeController`, `.CreateAndShowPrimaryStageWindow`, `StageWindow`, `.CreateStartedControllerAsync`, `HotkeyBindingViewModel`, `ImmutableArray`, `OverlayPlaybackSnapshot`, `IDisposable`, `.OnKeyDown`, `AudioVisualizerMode`, `App.axaml.cs`, `StyleCardViewModel`, `1. Remediation Verification Matrix (`e0dfe47`)`, `AnchorZone`, `SecondaryLineMode`, `ThemeVariantMode`, `MediaHealthKind`, `StageFlyoutKind`, `ObsChromaKeyMode`, `Color`, `ScrimIntensity`, `AppSettings.cs`, `StyleCatalog`?**
  _High betweenness centrality (0.143) - this node is a cross-community bridge._
- **Are the 22 inferred relationships involving `StageViewModel` (e.g. with `2. Security, AOT, Resource-Hygiene & Thread-Safety Verification` and ``UX6-09` — Startup Hotkey Conflicts Are Not Synced to `StageViewModel``) actually correct?**
  _`StageViewModel` has 22 INFERRED edges - model-reasoned connections that need verification._
- **What connects `ActiveSingleInstanceGuard`, `LaunchOptions`, `Avalonia.Desktop` to the rest of the system?**
  _617 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Kinetic Styles & Semantic Visuals` be split into smaller, more focused modules?**
  _Cohesion score 0.14 - nodes in this community are weakly interconnected._
- **Why does `FontAssetRegistry` connect `FontAssetRegistry` to `.AddLyricPulseCore`, `FrameRenderPacket`, `StyleRenderContext`, `OverlayStyleId`, `skiasharp`, `LyricCanvasControl`, `ConstellationStyleRenderer`, `App`, `.AmbientPartyMesh_CompilesCleanly_RendersRichGradient_PureBlack_AndStaticFallback`, `LegibilityPipeline`, `StarterLexiconCatalog`, `ReadOnlySpan`, `StageWindow`, `NodeLineStyleRenderer`, `.CreatePacket`, `OverlayPlaybackSnapshot`, `IDisposable`, `SettingsView`, `.RenderingUtilities_HandleAllBoundaryGuards_NaN_Empty_Fallback_AndDisposal`, `TimeSpan`, `Milestone1ARemediationTests`, `DemoSongProvider`, `GloveSemanticVisualMatcher`, `.FromPlaybackSnapshot`?**
  _High betweenness centrality (0.080) - this node is a cross-community bridge._

### Low-confidence Hints
_AMBIGUOUS edges — the extractor was unsure. Verify before acting on these._

- **What is the exact relationship between `SkSL Shader Uniform Animation Curves (Motion Spec)` and `EllipticalBulge SkSL Shader (Rendering Contract)`?**
  _Edge tagged AMBIGUOUS (relation: semantically_similar_to) - confidence is low._
- **What is the exact relationship between `SkSL Shader Uniform Animation Curves (Motion Spec)` and `13-Tap DirectionalMotionBlur SkSL Shader (Rendering Contract)`?**
  _Edge tagged AMBIGUOUS (relation: semantically_similar_to) - confidence is low._