# LyricPulse

## What This Is

LyricPulse is a clean-room, open, free Windows desktop application built with C# / .NET, Avalonia UI, and SkiaSharp that follows what is currently playing in Spotify, Apple Music, or any Windows media player (via GSMTC) and renders kinetic, word-synced lyrics in transparent, click-through, always-on-top overlays across one or more monitors. It combines 10 expressive visual lyric styles (powered by GPU-accelerated SkiaSharp + custom SkSL shaders), real-time album-cover color syncing, and an offline semantic word-to-icon/emoji visuals engine.

## Core Value

Zero-friction, butter-smooth (60+ FPS) real-time synced kinetic lyrics overlaid cleanly over your desktop without stealing focus or mouse clicks.

## Requirements

### Validated

(None yet — ship to validate)

### Active

- [ ] **Media Session Engine**: Track currently playing media from Spotify, Apple Music, and generic Windows GSMTC sessions with priority selection (Spotify preferred when multiple play), sub-frame stopwatch position interpolation, and album thumbnail retrieval.
- [ ] **Lyrics Fetching & Normalization**: Multi-provider synced lyrics pipeline (LRCLIB primary `/api/get` + `/api/search`, NetEase Cloud Music fallback) with regex track-title normalization, local cache, enhanced word-synced LRC parsing, proportional word-timing fallback, and per-song timing offset shifting (±0.5s / ±1s, resetting on track change).
- [ ] **Click-Through Overlay Host**: Borderless, transparent, always-on-top, click-through (`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`) Avalonia windows with multi-monitor support (`DisabledDisplays`), multi-DPI awareness, and single-instance named mutex + window activation signal.
- [ ] **GPU SkiaSharp Rendering & SkSL Shaders**: Custom `ICustomDrawOperation` (`ISkiaSharpApiLeaseFeature`) render loop with 60+ FPS animation frames, Elliptical Bulge / Fisheye SkSL lens distortion shader, 13-tap Directional Motion Blur SkSL shader, and 4 bundled SIL OFL typefaces (Bodoni Moda, Jost, Mulish, Nunito).
- [ ] **10 Kinetic Lyric Styles**:
  1. **Fisheye** — tight stack of 3 words bulging through a spherical lens shader
  2. **Fisheye Visual** — Fisheye with semantic icons beside matching words
  3. **Codestyle** — lyrics typed out as code under a blinking red cursor
  4. **Codestyle Visual** — Codestyle with inline semantic word icons
  5. **Visual Stack** — centered stack with an icon for every matching word
  6. **Constellation** — each line traced as a connected constellation star by star
  7. **Spider's Web** — each line spun into a spider's web, a strand per word
  8. **Node Line** — a line draws itself and each word lights a node along it
  9. **Kinetic Wall (3D Drift)** — words land one by one on a plane drifting in 3D perspective
  10. **Messages (Phone Screen)** — song rendered as a message thread with bubbles per line
- [ ] **Offline Semantic Word-to-Icon Engine**: 100% offline semantic matching using quantized 8-bit GloVe 100d embeddings (mean-centered, top-2 PCA removed, unit-normalized) against Microsoft Fluent System Icons (`FluentSystemIcons-Filled.ttf`) and Unicode CLDR emoji keywords.
- [ ] **Album Cover Auto-Sync Colors**: Extract 3 high-contrast accent colors (`AccentColor1`, `AccentColor2`, `AccentColor3`) from playing album art thumbnails, plus manual color overrides via Avalonia ColorPicker.
- [ ] **Style Gallery, Settings & System Tray**: Live-animated Style Gallery window previewing all 10 styles with demo lyrics, style pinning to the System Tray menu, font/size/opacity/monitor/offset controls, reduced-motion accessibility toggle, and JSON settings persistence.
- [ ] **Quality & Verification Gates**: 80%+ unit/integration test coverage on Core & Rendering logic, automated UI/UX audit against `design-system/lyricpulse/MASTER.md`, and clean NativeAOT / self-contained Windows publish pipeline.

### Out of Scope

- **Paid Licensing / DRM / Polar.sh Integration** — LyricPulse is 100% free and open-source; zero license locks or telemetry.
- **Copying Proprietary Assets or Binary Extraction** — Clean-room implementation using only public/open-source assets (SIL OFL fonts, MIT Fluent Icons, PDDL 1.0 GloVe, Unicode CLDR, public lyrics APIs).
- **Launch-at-Login Startup Task (v1)** — Deferred to v1.1 unless requested by CEO.
- **macOS / Linux Port (v1)** — Architecture cleanly abstracts platform media/window interop behind interfaces (`IMediaSessionProvider`, `IOverlayWindowPlatform`) for future portability, but v1 targets Windows 10 22H2+ / Windows 11.

## Context

- **Architecture Origin**: Informed by clean-room static architectural analysis of modern Avalonia + SkiaSharp desktop lyrics overlays (documented in `docs/REVERSE_ENGINEERING_BASELINE.md`).
- **Org & Governance**:
  - **CEO**: User (approves roadmap, major architecture changes, and system-level permissions).
  - **COO / Orchestrator**: Coordinates GSD phases and delegates across 9 specialized subagents.
  - **2 Researchers**: Media/Lyrics/NLP research + Rendering/Win32/Graphify knowledge graph curation (`graphify-out/`).
  - **2 Designers**: Design System (`ui-ux-pro-max` -> `design-system/lyricpulse/MASTER.md`) + 10-Style Motion & Shader Choreography.
  - **2 Developers**: Core Engine Developer + Avalonia UI/Skia Rendering Developer.
  - **3 QA / UX / UI Auditors**: Test Engineer (80%+ coverage), UX Evaluator, UI Auditor.

## Constraints

- **Tech Stack**: C# / .NET 9 (`net9.0-windows10.0.19041.0`), Avalonia UI 11.x, SkiaSharp 3.x, HarfBuzzSharp, CsWinRT (GSMTC), System.Text.Json (AOT-safe source generators).
- **Performance**: 60+ FPS overlay rendering with <3% idle CPU and <150 MB working set; zero UI-thread blocking during HTTP/embedding lookups.
- **Accessibility & UX**: Reduced-motion fallback for shaders/3D drift, >=4.5:1 contrast in Settings/Gallery light & dark themes, readable text scrims/outlines on desktop overlays.
- **Security & Immutability**: Immutable domain models (`record` / `ImmutableArray`), strict input/URL validation, zero hardcoded secrets, safe HTTP timeouts and cancellation tokens.

## Key Decisions

| Decision | Rationale | Outcome |
|---|---|---|
| C# / .NET + Avalonia UI + SkiaSharp | Exact stack parity with native WinRT GSMTC access and GPU SkSL runtime shaders | — Pending |
| Brand: LyricPulse (Free & Open) | Clean-room brand with zero DRM/license lock | — Pending |
| 6-Phase GSD Roadmap (3-style MVP by Phase 3, all 10 styles by Phase 6) | Fast end-to-end validation early while achieving complete 10-style feature parity | — Pending |
| Install .NET SDK via winget | Local PC has .NET 8/10 runtimes only; SDK required to compile C# projects | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition:**
1. Requirements invalidated? -> Move to Out of Scope with reason
2. Requirements validated? -> Move to Validated with phase reference
3. New requirements emerged? -> Add to Active
4. Decisions to log? -> Add to Key Decisions
5. "What This Is" still accurate? -> Update if drifted

---
*Last updated: 2026-11-25 after GSD project initialization*
