<p align="center">
  <img src="src/LyricPulse.App/Assets/LyricPulse.ico" alt="LyricPulse" width="128" height="128" />
</p>

<h1 align="center">LyricPulse</h1>

<p align="center">
  <b>Real-time desktop lyric visualizer with 11 cinematic styles, harmonious album color theming, and GPU-accelerated rendering</b>
</p>

<p align="center">
  <a href="https://lyricpulse.vercel.app"><img src="https://img.shields.io/badge/Live%20Demo-lyricpulse.vercel.app-5B54EC?style=for-the-badge&logo=vercel&logoColor=white" alt="Live Demo on Vercel"></a>
  <a href="https://github.com/AmIAnshu/LyricPulse/releases/tag/v4.0"><img src="https://img.shields.io/badge/Download-v4.0%20(.EXE)-22C55E?style=for-the-badge&logo=windows&logoColor=white" alt="Download Windows v4.0 EXE"></a>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="MIT License"></a>
  <img src="https://img.shields.io/badge/.NET-10.0-purple.svg" alt=".NET 10">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%2B-0078D6.svg" alt="Windows 10+">
  <img src="https://img.shields.io/badge/Rendering-SkiaSharp%20GPU-orange.svg" alt="SkiaSharp">
  <img src="https://img.shields.io/badge/UI-Avalonia%20UI-green.svg" alt="Avalonia">
</p>

---

## 🌐 Live Interactive Web Demo

Try **LyricPulse Studio** directly in your browser without installing anything:

👉 **[Launch Live Web Demo — lyricpulse.vercel.app](https://lyricpulse.vercel.app)**

What you can test right on the web demo:
- Switch live across all **11 kinetic visualization styles** (including the flagship **Glass Stage**, **Kinetic Wall**, and **Constellation**)
- Enable the built-in **Web Audio Synth Soundtrack** or upload your own **`.mp3` / album cover / `.lrc`** files locally in your browser
- Experience **Glass Stage**: lightly blurred full-bleed album cover background, a sleek medium Apple GlassOS cover card, and translucent hover transport controls (**Previous**, **Play / Pause**, **Next**)
- Tune **Lumn-inspired Neon Rush** 1-color or 2-color (60% / 40%) rounded perimeter glow, inward spill, thickness, speed, animation mode, and custom color overrides

---

## ✨ What is LyricPulse?

LyricPulse is a **transparent desktop overlay** that syncs real-time lyrics with whatever music is playing on your system (Spotify, Apple Music, YouTube Music, any app). It detects the currently playing song via **Windows GSMTC** (Global System Media Transport Controls), fetches time-synced lyrics, and renders them as stunning visual animations directly on your desktop — like a live music video for every song.

> Think of it as **Verci / Moodsync / Musixmatch desktop lyrics, but with 11 cinematic visualization styles and album-synced 3D visual atmospheres.**

## 🎨 11 Visualization Styles

| # | Style | Category | Description |
|---|-------|----------|-------------|
| 1 | **Fisheye** | Kinetic Type | Tight 3-word stack bulging outward through a spherical SkSL lens distortion |
| 2 | **Fisheye Visual** | Kinetic Type | Spherical Fisheye typography paired with semantic icons beside matching words |
| 3 | **Codestyle** | Monospace | Lyrics typed out live as syntax-highlighted code beneath a blinking red cursor |
| 4 | **Codestyle Visual** | Monospace | Live syntax-highlighted code lyrics enhanced with inline semantic word icons |
| 5 | **Visual Stack** | Visuals | Centered kinetic word column pairing every lyric word with an expressive icon |
| 6 | **Constellation** | Geometry | Each lyric line traces across space as a connected glowing constellation |
| 7 | **Spider's Web** | Geometry | Words spin outward into an intricate geometric web with luminous strands |
| 8 | **Node Line** | Geometry | A flowing line draws itself as every word illuminates a glowing node |
| 9 | **Kinetic Wall** | 3D Stage | Words land onto a typographic wall drifting in 3D perspective with depth |
| 10 | **Messages** | Phone Screen | Song lines arrive as a sleek phone message thread with timed chat bubbles |
| 11 | **Glass Stage** | Album Glass | Lightly blurred full-bleed album backdrop, medium Apple GlassOS cover card with hover transport controls, and 3D depth lyrics |

## 🎵 Key Features

- 🎧 **Universal Music Detection** — Works with Spotify, Apple Music, YouTube Music, Tidal, or any media app via Windows GSMTC
- 📝 **Real-Time Lyric Sync** — Fetches time-synced LRC lyrics and renders word-by-word with sub-200ms accuracy
- 🎨 **Album Color Extraction & Cycling** — Extracts dominant OKLCH hues from album art, renders a blurred ambient room glow, and cycles matching accent tones harmoniously across player and lyrics
- 🔊 **WASAPI Audio FFT** — Real-time loopback audio capture with FFT spectrum analysis for reactive visuals
- 🖥️ **Transparent Desktop Overlay** — Click-through glass overlay sits on top of everything, no window borders
- ⚡ **GPU-Accelerated** — SkiaSharp hardware-accelerated rendering at 60+ FPS via ANGLE/D3D11 with lock-free song switching
- 🎛️ **Lumn-Inspired Neon Rush Screen Glow** — System-wide rounded Windows monitor bezel lighting (even with lyrics hidden), 1-color or 2-color (60% / 40%) gradients, Music Sync / Drift / Still modes, inward spill, thickness, and custom color overrides
- ♿ **ReducedMotion Support** — Respects system accessibility settings for motion-sensitive users
- 🪟 **Glass Stage & Apple GlassOS Controls** — Medium rounded album cover over a lightly-blurred full-bleed artwork backdrop, plus frosted hover transport buttons (`Previous`, `Play / Pause`, `Next`) wired to real Windows GSMTC players & Demo Mode
- 🎭 **4 Color Themes** — Pure Studio, Vinyl Warm, Mint Pulse, and Monochrome
- 🔄 **Demo Mode & Web Studio** — Built-in demo song for previewing all styles offline or live in the browser at **[lyricpulse.vercel.app](https://lyricpulse.vercel.app)**

## 📸 Inspiration

LyricPulse is inspired by apps like [Verci](https://www.instagram.com/verci), bringing cinematic lyric visualization to your Windows desktop as a free, open-source alternative.

## 🚀 Quick Start

### Option 1 — Download Ready-to-Run Windows Executable (No Setup Required)

1. Download **`LyricPulse.App.exe`** from **[GitHub Releases (v4.0)](https://github.com/AmIAnshu/LyricPulse/releases/tag/v4.0)**.
2. Double-click `LyricPulse.App.exe` on Windows 10 (1903+) / Windows 11 (x64) — self-contained single-file binary, no `.NET` install needed.

### Option 2 — Build from Source

#### Prerequisites

- **Windows 10** version 1903 (19041) or later
- **.NET 10 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build & Run

```bash
# Clone the repository
git clone https://github.com/AmIAnshu/LyricPulse.git
cd LyricPulse

# Build in Release mode
dotnet build LyricPulse.sln -c Release

# Run
dotnet run --project src/LyricPulse.App -c Release
```

### Publish a Self-Contained Executable

```bash
dotnet publish src/LyricPulse.App -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish/win-x64
```

Then run `publish/win-x64/LyricPulse.App.exe` — no .NET runtime install needed.

### Run Tests

```bash
dotnet test LyricPulse.sln -c Release --verbosity minimal
```

## 🏗️ Architecture

```
LyricPulse.sln
├── src/
│   ├── LyricPulse.Core/           # Domain models, media detection, lyrics, audio
│   │   ├── Audio/                  # WASAPI loopback FFT capture
│   │   ├── Lyrics/                 # LRC parser, timing, sync engine
│   │   ├── Media/                  # Windows GSMTC session provider
│   │   ├── Models/                 # Style IDs, render packets, settings
│   │   ├── Runtime/                # Overlay controller, demo song provider
│   │   └── Settings/               # JSON settings store
│   │
│   ├── LyricPulse.Rendering/      # GPU rendering engine (SkiaSharp)
│   │   ├── Color/                  # Album art color extraction (k-means)
│   │   ├── Fonts/                  # Font management
│   │   ├── Host/                   # Canvas control, overlay frame adapter
│   │   ├── Legibility/             # Text legibility pipeline (shadows, contrast)
│   │   ├── Motion/                 # Animation physics, easing, interpolation
│   │   ├── Shaders/                # SkSL shader pool (fisheye lens, etc.)
│   │   └── Styles/                 # All 11 visualization style renderers
│   │
│   ├── LyricPulse.UI/             # Avalonia UI layer
│   │   ├── Controls/              # Stage canvas host, card gallery
│   │   ├── ViewModels/            # Style catalog, stage ViewModel
│   │   └── Views/                 # Stage window, settings panel
│   │
│   └── LyricPulse.App/            # Entry point, DI, tray icon, platform bootstrap
│       ├── Assets/                # App icon
│       ├── Platform/              # Win32 interop, transparency, overlay window
│       └── Tray/                  # System tray menu, style switcher
│
├── web-demo/                      # Interactive LyricPulse Studio browser demo (Vercel)
│   ├── index.html
│   ├── styles.css
│   └── src/                       # Web Audio synth, FFT & 11 Canvas renderers
│
└── tests/
    └── LyricPulse.Tests/          # xUnit + FluentAssertions test suite
        ├── Audio/                 # FFT and audio metric tests
        ├── Color/                 # Album color extraction tests
        ├── Lyrics/                # LRC parser and sync tests
        ├── Rendering/             # GPU surface, pixel, transparency tests
        └── UI/                    # ViewModel and settings tests
```

## 🔧 Tech Stack

| Component | Technology |
|-----------|------------|
| **Framework** | .NET 10 (C# latest) |
| **UI** | Avalonia UI |
| **GPU Rendering** | SkiaSharp (ANGLE/D3D11 backend) |
| **Shaders** | SkSL (Skia Shading Language) |
| **Audio Capture** | WASAPI Loopback (via CsWinRT) |
| **Media Detection** | Windows GSMTC (CsWinRT) |
| **Testing** | xUnit, FluentAssertions, NSubstitute |
| **Build** | MSBuild, single-file publish, TrimAnalyzer |

## ⌨️ Usage

1. **Launch LyricPulse** — the app starts in the system tray
2. **Play music** in any media player (Spotify, YouTube Music, Apple Music, etc.)
3. **LyricPulse auto-detects** the song and fetches synced lyrics
4. **Switch styles** via the tray icon right-click menu or stage gallery
5. **Enable Demo Mode** to preview styles offline without active music

### System Tray Controls

- **Right-click tray icon** → Switch between 11 styles
- **Demo Mode** → Preview with built-in demo song
- **Settings** → Adjust Neon Rush speed/thickness, typography, visualizer mode, and colors
- **Exit** → Close the overlay

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'feat: add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Development Guidelines

- Follow [Conventional Commits](https://www.conventionalcommits.org/) format
- Ensure all tests pass: `dotnet test LyricPulse.sln -c Release`
- Build with warnings as errors: `dotnet build -p:TreatWarningsAsErrors=true`
- Target 80%+ test coverage

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

```
Copyright (c) 2026 Anshu (AmIAnshu)
```

## 👨‍💻 Made By

**[AmIAnshu](https://github.com/AmIAnshu)**

---

<p align="center">
  <i>🎵 Turn your desktop into a live music video 🎵</i>
</p>
