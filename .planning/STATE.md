# Project State — LyricPulse

## Project Reference

See: [.planning/PROJECT.md](PROJECT.md) · [.planning/ROADMAP.md](ROADMAP.md) · [.planning/REQUIREMENTS.md](REQUIREMENTS.md)

**Core value:** Zero-friction, butter-smooth (60+ FPS) real-time synced kinetic lyrics rendered in an ambient party fullscreen stage (or optional click-through desktop overlay / OBS broadcast window) without DRM or cloud lock-in.
**Current focus:** **v2.3 Performance Polish, Music Detection Overhaul & Neon Rush Edge Glow Complete & Packaged** — Scrollable Verci-style Studio UI, real-time WASAPI Loopback FFT visualizers (with 11ms frame caching & endpoint hot-switching), Dual Chasing Neon Beams (`EdgeGlowMode.NeonRush` default), universal music player/browser-stream detection, fullscreen exit flicker eliminated, Celestial Observatory Constellation (`68ea34c`), Optical Lens Fisheye Visual (`97adeca`), and focused gallery thumbnail rendering.

## Progress Snapshot

| Phase | Name | Status | Commit(s) | Audit / Verification |
|---:|---|---|---|---|
| **0** | GSD Init, Research, Design System & Graphify | **DONE** | `4d1c015` | CEO Roadmap Approved |
| **1** | Foundation, Media Engine & Synced Lyrics Core | **DONE** | `ed79508`, `650b07f` | QA PASS |
| **2** | Click-Through Overlay Host, SkiaSharp Pipeline & Shaders | **DONE** | `ee70d4a` | Integrated into M1A Gate |
| **3** | Working 3-Style MVP + Adaptive System Tray *(Milestone 1A)* | **DONE** | `c7c04b1`, `371bfa5`, `70876e0`, `c6420bb`, `b73b00b` | 3/3 ECC PASS |
| **4A** | Offline GloVe 100d-q8 Engine & Poetic Visual Lexicon | **DONE** | `26ff0da` | 270/270 tests passed |
| **4B & 5** | All 10 Kinetic Styles, Ambient Party Stage, Gallery, Settings & OBS Capture | **DONE** | `b88131e` | Stage + 10 styles + Tray + 25s soak verified |
| **6** | v1.0 ECC Release Audits, Blocker Remediation, Packaging & Graphify Sync | **DONE** | `e0dfe47`, `b52275c` | **3/3 ECC PASS / PASS WITH NOTES** |
| **v2.0** | Sync/Clock Overhaul, WASAPI FFT + Edges Glow + 5 Visualizers, Flicker Fix, 10-Style Upgrade & Scrollable Studio UI | **DONE** | `ab46fd8` | **291/291 tests pass · 0 warnings · Published `win-x64` verified** |
| **v2.1** | Constellation Celestial Observatory Overhaul (Full-Sky 2D Scatter, Nebula, Meteors, Curved Silk Threads, JWST Flares & Supernova Burst) | **DONE** | `68ea34c` | **293/293 tests pass · 0 warnings · Published `win-x64` verified** |
| **v2.2** | Fisheye Visual Optical Lens Overhaul (5-Word Curved Depth, RGB Chromatic Split, Optical Lens Glass Bezel & 24 Vector Concepts) | **DONE** | `97adeca` | **295/295 tests pass · 0 warnings · Published `win-x64` verified** |
| **v2.3** | Performance Polish (11ms FFT Cache, Blur Removal & Focused Gallery Cards), Universal Music Detection + WASAPI Hot-Switch & Neon Rush Edge Glow | **DONE** | *v2.3 release commit* | **301/301 tests pass · 0 warnings · Published `win-x64` verified** |

## Verified v2.3 Release Metrics

- **Build (`.NET 10`, `net10.0-windows10.0.19041.0`):**
  - `dotnet build LyricPulse.sln -c Debug -p:TreatWarningsAsErrors=true`: **0 Warnings, 0 Errors**
  - `dotnet build LyricPulse.sln -c Release -p:TreatWarningsAsErrors=true`: **0 Warnings, 0 Errors**
- **Automated Test Suite (`dotnet test LyricPulse.sln -c Release --no-build`):**
  - **301 / 301 Passed** (`0 Failed, 0 Skipped`)
- **Self-Contained Single-File Release (`win-x64`):**
  - Binary: `publish/win-x64/LyricPulse.App.exe`
  - Size: **`133,680,146` bytes (`127.49 MB`)**
  - Release `--smoke-test`: **Exit `0`**
  - Published `--smoke-test`: **Exit `0`** (`[PASS] LyricPulse Phase 5 smoke test succeeded | Fonts=5 | Shaders=3/3 OK | DemoLines=12 | Styles=10/10 | StageCards=10 | TrayHeader='Demo: Neon Horizon'`)
- **Graphify Knowledge Graph ([graphify-out/GRAPH_REPORT.md](../graphify-out/GRAPH_REPORT.md)):**
  - **`2,820` nodes · `9,424` edges · `112` communities**

---
*Last updated: 2026-11-26 after v2.3 performance polish, universal music detection, Neon Rush edge glow, full verification, packaging, and Graphify sync*
