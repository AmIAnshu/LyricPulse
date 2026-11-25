# Project State — LyricPulse

## Project Reference

See: [.planning/PROJECT.md](PROJECT.md) · [.planning/ROADMAP.md](ROADMAP.md) · [.planning/REQUIREMENTS.md](REQUIREMENTS.md)

**Core value:** Zero-friction, butter-smooth (60+ FPS) real-time synced kinetic lyrics rendered in an ambient party fullscreen stage (or optional click-through desktop overlay / OBS broadcast window) without DRM or cloud lock-in.
**Current focus:** **v1.0 Complete & Packaged** — All 6 phases done, 3/3 ECC audit gates passed, single-file `win-x64` release published and verified.

## Progress Snapshot

| Phase | Name | Status | Commit(s) | Audit / Verification |
|---:|---|---|---|---|
| **0** | GSD Init, Research, Design System & Graphify | **DONE** | `4d1c015` | CEO Roadmap Approved |
| **1** | Foundation, Media Engine & Synced Lyrics Core | **DONE** | `ed79508`, `650b07f` | QA PASS |
| **2** | Click-Through Overlay Host, SkiaSharp Pipeline & Shaders | **DONE** | `ee70d4a` | Integrated into M1A Gate |
| **3** | Working 3-Style MVP + Adaptive System Tray *(Milestone 1A)* | **DONE** | `c7c04b1`, `371bfa5`, `70876e0`, `c6420bb`, `b73b00b` | 3/3 ECC PASS |
| **4A** | Offline GloVe 100d-q8 Engine & Poetic Visual Lexicon | **DONE** | `26ff0da` | 270/270 tests passed |
| **4B & 5** | All 10 Kinetic Styles, Ambient Party Stage, Gallery, Settings & OBS Capture | **DONE** | `b88131e` | Stage + 10 styles + Tray + 25s soak verified |
| **6** | v1.0 ECC Release Audits, Blocker Remediation, Packaging & Graphify Sync | **DONE** | `e0dfe47` *(+ release docs commit)* | **3/3 ECC PASS / PASS WITH NOTES** |

## Verified v1.0 Release Metrics (`e0dfe47`)

- **Build (`.NET 10.0.404`, `net10.0-windows10.0.19041.0`):**
  - `dotnet build LyricPulse.sln -c Debug --no-restore -warnaserror`: **0 Warnings, 0 Errors**
  - `dotnet build LyricPulse.sln -c Release --no-restore -warnaserror`: **0 Warnings, 0 Errors**
- **Automated Test Suite (`dotnet test`):**
  - **282 / 282 Passed** (`0 Failed, 0 Skipped`)
- **Latest Assembly Coverlet Coverage (`e0dfe47`):**
  - `LyricPulse.Core`: **84.23%** Line / **68.91%** Branch (Release), **81.62%** Line / **66.56%** Branch (Debug)
  - `LyricPulse.Rendering`: **95.96%** Line / **83.86%** Branch (Release), **94.44%** Line / **84.56%** Branch (Debug)
- **Self-Contained Single-File Release (`win-x64`):**
  - Binary: `publish/win-x64/LyricPulse.App.exe`
  - Size: **`133,550,610` bytes (`127.36 MB`)**
  - Published `--smoke-test`: **Exit `0`** (`[PASS] LyricPulse Phase 5 smoke test succeeded | Fonts=5 | Shaders=3/3 OK | DemoLines=12 | Styles=10/10 | StageCards=10 | TrayHeader='Demo: Neon Horizon'`)
- **ECC Release Audit Verdicts:**
  - [.planning/QA_REPORT.md](QA_REPORT.md): **PASS** (`0 CRITICAL, 0 HIGH`)
  - [.planning/UI_AUDIT.md](UI_AUDIT.md): **PASS WITH NOTES** (`0 CRITICAL, 0 HIGH` open; `UI-13` & `UI-14` resolved on `e0dfe47`)
  - [.planning/UX_AUDIT.md](UX_AUDIT.md): **PASS WITH NOTES** (`0 CRITICAL, 0 HIGH` open; `UX6-01`–`UX6-05`, `UX6-06`, `UX6-08`, `UX6-09` resolved on `e0dfe47`)
- **Graphify Knowledge Graph ([graphify-out/GRAPH_REPORT.md](../graphify-out/GRAPH_REPORT.md)):**
  - **`2,581` nodes · `8,513` edges · `109` communities** across `166` corpus files (`78%` extracted, `22%` inferred, `0%` ambiguous)

## Post-v1.0 Non-Blocking Polish Backlog
1. Skip empty/unassigned hotkey strings during `GlobalHotkeyManager.RegisterAll` so cleared shortcuts do not emit `"Shortcut cannot be empty."` until reassigned.
2. Disable or clarify `"Play previews"` when app-level `ReducedMotion` is manually toggled (`UX6-07`).
3. Route `DrawInvertedCutoutWord` and `MessagesStyleRenderer` through HarfBuzz script fallback, add `AdvanceDot` to remaining 4 styles, and wrap long `NodeLine` lines under Reduced Motion (`UI-16`).
4. Remove inline `Background`/`BorderThickness` overrides on Top Glass pill chips (`StageWindow.axaml:91-98`) and polish `UI-17` / `UX6-11`–`UX6-14`.

---
*Last updated: 2026-11-25 after LyricPulse v1.0 release packaging, 3/3 ECC audit pass, and Graphify sync*
