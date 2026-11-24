# Architectural Baseline (Clean-Room Specification)

This document captures the technical findings from static analysis of desktop kinetic-lyrics overlay architecture to inform LyricPulse's clean-room C# / Avalonia / SkiaSharp design.

## 1. Platform & Packaging Architecture
- **Runtime**: .NET (self-contained / NativeAOT capable), targeting Windows 10 22H2 (19045) and Windows 11.
- **Desktop Framework**: Avalonia UI 11.x (`Avalonia.Win32`, `Avalonia.Skia`, `Avalonia.HarfBuzz`, `Avalonia.OpenGL` / ANGLE `av_libglesv2.dll`, `Avalonia.Themes.Fluent`, `Avalonia.Controls.ColorPicker`).
- **Solution Topology**:
  - `LyricPulse.Core`: Domain models, GSMTC media tracker, lyrics providers, LRC/word-timing engine, offline GloVe/Fluent/Emoji semantic visuals engine, album color extraction, settings persistence.
  - `LyricPulse.Rendering`: SkiaSharp custom canvas scenes for all 10 styles, SkSL shader compilation (`SKRuntimeEffect`), spring/easing physics, glyph layout via HarfBuzz/Skia.
  - `LyricPulse.UI`: Avalonia views (`OverlayWindow`, `GalleryWindow`, `SettingsView`, `ColorPickerTheme.axaml`, `TrayController`).
  - `LyricPulse.App`: Desktop entry point, single-instance named mutex (`Local\LyricPulse.SingleInstance`), show-window event (`Local\LyricPulse.ShowWindow`), Win32 click-through window interop.
  - `LyricPulse.Tests`: xUnit unit, integration, and snapshot/parity tests (80%+ coverage).

## 2. Media Tracking (Windows GSMTC)
- Uses `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager` via `Microsoft.Windows.SDK.NET` / CsWinRT.
- Targets Spotify (`Spotify.exe` / Store AUMID) and Apple Music (`AppleInc.AppleMusicWin_nzyj5cx40ttqa`), plus optional fallback to any active GSMTC session.
- Priority rule: Follow whichever supported player is actively playing; if both Spotify and Apple Music play simultaneously, prioritize Spotify.
- Timeline accuracy: GSMTC `TimelineProperties.Position` updates discretely; interpolate playback position smoothly between updates using `Stopwatch` anchored to `LastUpdatedTime` when `PlaybackStatus == Playing`.
- Album art: Read `MediaProperties.Thumbnail` stream into `SKBitmap` to compute 3 perceptual accent colors (`AccentColor1`, `AccentColor2`, `AccentColor3`).

## 3. Lyrics Pipeline & Normalization
- **Primary Source**: LRCLIB (`https://lrclib.net/api/get?track_name=...&artist_name=...` and `https://lrclib.net/api/search?track_name=...`).
- **Fallback Source**: NetEase Cloud Music (`https://music.163.com/api/search/get?s=...&type=1&limit=30&offset=0` and `https://music.163.com/api/song/lyric/v1?id=...`).
- **Title Normalization Regexes**:
  - Strip `(2024 Remaster)`, `(Deluxe Edition)`, `(Bonus Track)`, `(Explicit)`, `(Radio Edit)`
  - Strip `(feat. ...)`, `(ft. ...)`, `(with ...)`, `(featuring ...)`
  - Strip `(From ...)`, `(Theme From ...)`, `(Music From ...)`
  - Strip `[...]` bracket tags
  - Strip `- Remix / Version / Edit / Mix / Remaster / Acoustic / Live / Instrumental` trailing clauses
- **Timing Offset**: Per-song offset shift (e.g., ±0.5s / ±1.0s earlier or later) that automatically resets on track change.
- **Word-by-Word Timing**: Parse enhanced LRC word tags (`<mm:ss.xx>`) when available; otherwise synthesize smooth per-word intervals within each line weighted by character/syllable count and punctuation pauses.

## 4. Offline Semantic Word-to-Icon ("Word Pictures") Engine
- **Word Embeddings**: GloVe 6B 100d (`glove-wiki-gigaword-100`, Public Domain Dedication and License 1.0):
  - Filter vocabulary to common content words (excluding stopwords).
  - Mean-center all vectors, remove top 2 principal components ("All-but-the-Top" isotropy improvement), L2-normalize to unit sphere, and quantize to `int8` (`glove-100d-q8.bin` + `glove-vocab.txt`).
- **Visual Lexicons**:
  - Microsoft Fluent System Icons (`FluentSystemIcons-Filled.ttf` + `fluent-icons.tsv` + `fluent-index.tsv`, MIT License).
  - Unicode CLDR 16.0 Emoji keywords (`emoji-index.tsv` derived from `en.xml` + `emoji-test.txt`, Unicode License v3).
- **Matching**: Map lyric tokens (lowercased, lemmatized/stemmed) to their quantized GloVe vector, compute cosine similarity (dot product) against icon/emoji concept vectors, and attach the top visual match above a tuned confidence threshold.

## 5. Custom SkSL Shaders & 10 Kinetic Styles
- **Open-Source Typefaces (SIL OFL)**: `BodoniModa-Bold.ttf`, `Jost-Bold.ttf`, `Mulish-Bold.ttf`, `Nunito-SemiBold.ttf`.
- **Shader 1 — Elliptical Bulge / Spherical Lens Distortion (SkSL)**:
  Normalizes pixel delta `d = position - center` by `max(size * 0.5, float2(1.0))`, computes `r = length(norm)`, and samples `content.eval(center + d * (1.0 + strength * pow(r, power)))`.
- **Shader 2 — 13-Tap Directional Motion Blur (SkSL)**:
  Samples `13` linear taps from `-0.5` to `+0.5` along `velocity`, weighted by `1.0 - abs(offset) * 1.2`, normalized by `weightSum`.
- **10 Visual Styles**:
  1. `Fisheye` — Tight 3-word stack bulging through the spherical lens shader.
  2. `FisheyeVisual` — Fisheye paired with semantic icons next to matching words.
  3. `Codestyle` — Lyric typed out like monospace/syntax-highlighted code with a blinking red cursor.
  4. `CodestyleVisual` — Codestyle paired with inline semantic word icons.
  5. `VisualStack` — Centered vertical word stack with an icon for every matching word.
  6. `Constellation` — Each line traced as a glowing constellation, lighting star by star.
  7. `SpidersWeb` — Each line spun into a geometric spider's web with a strand per word.
  8. `NodeLine` — A flowing line draws itself across the viewport and each word lights a node on it.
  9. `KineticWall` — Words land one by one onto a typography wall that drifts smoothly in 3D perspective.
  10. `Messages` — Song rendered as a phone-style message thread with a chat bubble for every line (fixed system typeface & chat palette).

## 6. Win32 Overlay & Tray Architecture
- Transparent click-through window via `SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)`.
- Multi-monitor support: Spawn one overlay window per active non-disabled display (`DisabledDisplays`), scaled by `GetDpiForMonitor`.
- System tray icon with pinned styles list, Show/Hide Lyrics toggle, timing offset nudges, Open Gallery, and Quit.
- Single-instance enforcement via `Local\LyricPulse.SingleInstance` mutex + `Local\LyricPulse.ShowWindow` signal.
