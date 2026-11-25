# Project State — LyricPulse

## Project Reference

See: [.planning/PROJECT.md](PROJECT.md) · [.planning/ROADMAP.md](ROADMAP.md) · [.planning/REQUIREMENTS.md](ROADMAP.md)

**Core value:** Zero-friction, butter-smooth (60+ FPS) real-time synced kinetic lyrics rendered in an ambient party fullscreen stage (or optional click-through desktop overlay / OBS broadcast window) without DRM or cloud lock-in.
**Current focus:** **v2.0 Complete & Packaged** — Scrollable Verci-style Studio UI, real-time WASAPI Loopback FFT visualizers + Edges Glow, fullscreen exit flicker eliminated, Spotify GSMTC sync clock & word pacing hardened, custom procedural vector icons, and all 10 kinetic styles upgraded.

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
| **v2.0** | Sync/Clock Overhaul, WASAPI FFT + Edges Glow + 5 Visualizers, Flicker Fix, 10-Style Upgrade & Scrollable Studio UI | **DONE** | *v2.0 release commit* | **291/291 tests pass · 0 warnings · Published `win-x64` verified** |

## Verified v2.0 Release Metrics

- **Build (`.NET 10`, `net10.0-windows10.0.19041.0`):**
  - `dotnet build LyricPulse.sln -c Debug -p:TreatWarningsAsErrors=true`: **0 Warnings, 0 Errors**
  - `dotnet build LyricPulse.sln -c Release -p:TreatWarningsAsErrors=true`: **0 Warnings, 0 Errors**
- **Automated Test Suite (`dotnet test LyricPulse.sln -c Release --no-build`):**
  - **291 / 291 Passed** (`0 Failed, 0 Skipped`)
- **Self-Contained Single-File Release (`win-x64`):**
  - Binary: `publish/win-x64/LyricPulse.App.exe`
  - Size: **`133,654,034` bytes (`127.46 MB`)**
  - Release `--smoke-test`: **Exit `0`**
  - Published `--smoke-test`: **Exit `0`** (`[PASS] LyricPulse Phase 5 smoke test succeeded | Fonts=5 | Shaders=3/3 OK | DemoLines=12 | Styles=10/10 | StageCards=10 | TrayHeader='Demo: Neon Horizon'`)
- **Post-v1.0 Backlog Resolved in v2.0:**
  - Empty/unassigned hotkey strings skipped cleanly in `GlobalHotkeyManager.RegisterAll`.
  - `"Paused (Reduced Motion)"` clarity and disabled button state (`IsPreviewsToggleEnabled`) under Reduced Motion (`UX6-07`).
  - HarfBuzz multi-script fallback wired into `DrawInvertedCutoutWord` and `MessagesStyleRenderer`; word-locked `AdvanceDot` across all active styles (`UI-16`).
  - Clean `Button.pill-nav` styles without inline `Background`/`BorderThickness` overrides on top bar tabs.
- **Graphify Knowledge Graph ([graphify-out/GRAPH_REPORT.md](../graphify-out/GRAPH_REPORT.md)):**
  - **`2,765` nodes · `9,114` edges · `111` communities**

---
*Last updated: 2026-11-26 after LyricPulse v2.0 implementation, full build/test verification, single-file packaging, and Graphify sync*
