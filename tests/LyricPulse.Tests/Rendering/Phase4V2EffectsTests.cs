using System.Collections.Immutable;
using AwesomeAssertions;
using LyricPulse.Core.Audio;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Shaders;
using LyricPulse.Rendering.Styles;
using SkiaSharp;
using Xunit;

namespace LyricPulse.Tests.Rendering;

public sealed class Phase4V2EffectsTests
{
    private static AudioFrameMetrics CreateLiveAudioMetrics()
    {
        var bands = new float[AudioFrameMetrics.BandCount];
        for (var i = 0; i < bands.Length; i++)
        {
            bands[i] = Math.Clamp(0.35f + (0.55f * MathF.Sin((i * 0.45f) + 0.3f)), 0.15f, 0.95f);
        }

        return new AudioFrameMetrics(
            BassEnergy: 0.78f,
            MidEnergy: 0.56f,
            TrebleEnergy: 0.42f,
            OverallLevel: 0.64f,
            BeatPulse: 0.85f,
            IsLiveAudioActive: true,
            SpectrumBands: ImmutableArray.Create(bands));
    }

    private static FrameRenderPacket CreatePacket(
        AppSettings settings,
        StageRenderSurfaceMode surfaceMode,
        AudioFrameMetrics? audio = null,
        bool bottomFadeToBlack = false)
    {
        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        var pos = TimeSpan.FromSeconds(3.2);
        var snap = new OverlayPlaybackSnapshot(
            Track: doc.Track,
            Document: doc,
            LyricsStatus: LyricsStatus.Synced,
            PlaybackState: MediaPlaybackState.Playing,
            PlayerKind: MediaPlayerKind.Spotify,
            SourceAppUserModelId: "Spotify.exe",
            RawPosition: pos,
            EffectivePosition: pos,
            ActiveSlice: ActiveLyricSlice.Resolve(doc, pos),
            ActivePalette: AlbumAccentPalette.DemoNeonHorizon,
            TargetPalette: AlbumAccentPalette.DemoNeonHorizon,
            TimingOffset: new TimingOffsetState(doc.Track.CacheKey, TimeSpan.Zero, false),
            Settings: settings,
            IsPlaying: true,
            IsDemoMode: true,
            IsFullscreenHidden: false,
            PausedSinceUtc: null,
            LastOffsetNudgeUtc: null,
            TrackGeneration: 1,
            SeekGeneration: 0,
            IsOsReducedMotion: false,
            OffsetGeneration: 0,
            IsStageWindowOpen: true,
            AudioMetrics: audio);

        return OverlayFrameAdapter.FromPlaybackSnapshot(
            snap,
            viewportWidthDip: 640f,
            viewportHeightDip: 360f,
            renderScaling: 1.0f,
            renderTimeSeconds: 2.4,
            deltaSeconds: 1f / 60f,
            surfaceMode: surfaceMode,
            bottomFadeToBlack: bottomFadeToBlack);
    }

    private static int CountLitPixels(SKBitmap bitmap, out long signature)
    {
        var count = 0;
        long sig = 17;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                var px = bitmap.GetPixel(x, y);
                if (px.Alpha > 8)
                {
                    count++;
                    sig = unchecked((sig * 31) + (x * 7) + (y * 13) + px.Red + px.Green + px.Blue);
                }
            }
        }

        signature = sig;
        return count;
    }

    [Fact]
    public void EdgeGlowRenderer_AllFourModes_RenderStagePerimeter_AndRespectDesktopOverlayAndBottomFade()
    {
        using var edgeGlow = new EdgeGlowRenderer();
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();

        var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
        var audio = CreateLiveAudioMetrics();

        // Off emits zero pixels
        using (var surf = SKSurface.Create(info))
        {
            surf.Canvas.Clear(SKColors.Transparent);
            var pkt = CreatePacket(AppSettings.Default with { EdgeGlowMode = EdgeGlowMode.Off }, StageRenderSurfaceMode.StageCanvas, audio);
            var ctx = new StyleRenderContext(surf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            edgeGlow.Render(in ctx);
            using var bmp = SKBitmap.FromImage(surf.Snapshot());
            CountLitPixels(bmp, out _).Should().Be(0);
        }

        // SoftAmbient, BeatPulse, NeonRim render perimeter glow on StageCanvas
        foreach (var mode in new[] { EdgeGlowMode.SoftAmbient, EdgeGlowMode.BeatPulse, EdgeGlowMode.NeonRim })
        {
            using var surf = SKSurface.Create(info);
            surf.Canvas.Clear(SKColors.Transparent);
            var pkt = CreatePacket(
                AppSettings.Default with { EdgeGlowMode = mode, EdgeGlowIntensity = 1.0f },
                StageRenderSurfaceMode.StageCanvas,
                audio,
                bottomFadeToBlack: false);
            var ctx = new StyleRenderContext(surf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            edgeGlow.Render(in ctx);
            using var bmp = SKBitmap.FromImage(surf.Snapshot());
            bmp.GetPixel(1, 1).Alpha.Should().BeGreaterThan(0);
            bmp.GetPixel(320, 358).Alpha.Should().BeGreaterThan(0);
            bmp.GetPixel(320, 180).Alpha.Should().Be(0); // Center remains unobstructed
        }

        // BottomFadeToBlack suppresses bottom rim on NeonRim
        using (var fadeSurf = SKSurface.Create(info))
        {
            fadeSurf.Canvas.Clear(SKColors.Transparent);
            var pkt = CreatePacket(
                AppSettings.Default with { EdgeGlowMode = EdgeGlowMode.NeonRim, EdgeGlowIntensity = 1.0f },
                StageRenderSurfaceMode.StageCanvas,
                audio,
                bottomFadeToBlack: true);
            var ctx = new StyleRenderContext(fadeSurf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            edgeGlow.Render(in ctx);
            using var bmp = SKBitmap.FromImage(fadeSurf.Snapshot());
            bmp.GetPixel(320, 359).Alpha.Should().Be(0);
        }

        // DesktopOverlay skips edge glow completely so transparent corners remain alpha=0
        using (var desktopSurf = SKSurface.Create(info))
        {
            desktopSurf.Canvas.Clear(SKColors.Transparent);
            var pkt = CreatePacket(
                AppSettings.Default with { EdgeGlowMode = EdgeGlowMode.NeonRim, EdgeGlowIntensity = 1.2f },
                StageRenderSurfaceMode.DesktopOverlay,
                audio);
            var ctx = new StyleRenderContext(desktopSurf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            edgeGlow.Render(in ctx);
            using var bmp = SKBitmap.FromImage(desktopSurf.Snapshot());
            CountLitPixels(bmp, out _).Should().Be(0);
        }
    }

    [Fact]
    public void EdgeGlowRenderer_NeonRush_DrawsChasingBeamsAndRespectsOverlayAndBottomFade()
    {
        using var edgeGlow = new EdgeGlowRenderer();
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();

        var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
        var audio = CreateLiveAudioMetrics();

        // Fullscreen / closed perimeter renders vivid beams while keeping center unobstructed
        using (var surf = SKSurface.Create(info))
        {
            surf.Canvas.Clear(SKColors.Transparent);
            var pkt = CreatePacket(
                AppSettings.Default with { EdgeGlowMode = EdgeGlowMode.NeonRush, EdgeGlowIntensity = 1.0f },
                StageRenderSurfaceMode.StageCanvas,
                audio,
                bottomFadeToBlack: false);
            var ctx = new StyleRenderContext(surf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            edgeGlow.Render(in ctx);
            using var bmp = SKBitmap.FromImage(surf.Snapshot());
            CountLitPixels(bmp, out _).Should().BeGreaterThan(200);
            bmp.GetPixel(1, 1).Alpha.Should().BeGreaterThan(0);
            bmp.GetPixel(320, 180).Alpha.Should().Be(0);
        }

        // Windowed Top-U perimeter preserves bottomFadeToBlack so bottom row remains alpha=0
        using (var fadeSurf = SKSurface.Create(info))
        {
            fadeSurf.Canvas.Clear(SKColors.Transparent);
            var pkt = CreatePacket(
                AppSettings.Default with { EdgeGlowMode = EdgeGlowMode.NeonRush, EdgeGlowIntensity = 1.0f },
                StageRenderSurfaceMode.StageCanvas,
                audio,
                bottomFadeToBlack: true);
            var ctx = new StyleRenderContext(fadeSurf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            edgeGlow.Render(in ctx);
            using var bmp = SKBitmap.FromImage(fadeSurf.Snapshot());
            bmp.GetPixel(320, 359).Alpha.Should().Be(0);
        }

        // DesktopOverlay skips edge glow completely
        using (var desktopSurf = SKSurface.Create(info))
        {
            desktopSurf.Canvas.Clear(SKColors.Transparent);
            var pkt = CreatePacket(
                AppSettings.Default with { EdgeGlowMode = EdgeGlowMode.NeonRush, EdgeGlowIntensity = 1.2f },
                StageRenderSurfaceMode.DesktopOverlay,
                audio);
            var ctx = new StyleRenderContext(desktopSurf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            edgeGlow.Render(in ctx);
            using var bmp = SKBitmap.FromImage(desktopSurf.Snapshot());
            CountLitPixels(bmp, out _).Should().Be(0);
        }

        // Adjustable EdgeGlowThickness increases beam footprint while keeping center unobstructed
        int thinCount;
        int thickCount;
        using (var thinSurf = SKSurface.Create(info))
        {
            thinSurf.Canvas.Clear(SKColors.Transparent);
            var pkt = CreatePacket(
                AppSettings.Default with { EdgeGlowMode = EdgeGlowMode.NeonRush, EdgeGlowIntensity = 1.0f, EdgeGlowThickness = 0.45f, EdgeGlowSpeed = 0.50f },
                StageRenderSurfaceMode.StageCanvas,
                audio,
                bottomFadeToBlack: false);
            var ctx = new StyleRenderContext(thinSurf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            edgeGlow.Render(in ctx);
            using var bmp = SKBitmap.FromImage(thinSurf.Snapshot());
            thinCount = CountLitPixels(bmp, out _);
            bmp.GetPixel(320, 180).Alpha.Should().Be(0);
        }

        using (var thickSurf = SKSurface.Create(info))
        {
            thickSurf.Canvas.Clear(SKColors.Transparent);
            var pkt = CreatePacket(
                AppSettings.Default with { EdgeGlowMode = EdgeGlowMode.NeonRush, EdgeGlowIntensity = 1.0f, EdgeGlowThickness = 2.20f, EdgeGlowSpeed = 0.50f },
                StageRenderSurfaceMode.StageCanvas,
                audio,
                bottomFadeToBlack: false);
            var ctx = new StyleRenderContext(thickSurf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            edgeGlow.Render(in ctx);
            using var bmp = SKBitmap.FromImage(thickSurf.Snapshot());
            thickCount = CountLitPixels(bmp, out _);
            bmp.GetPixel(320, 180).Alpha.Should().Be(0);
        }

        thickCount.Should().BeGreaterThan(thinCount);
    }

    [Fact]
    public void AudioVisualizerRenderer_AllFiveModes_RenderDistinctPixels_AndSkipReducedMotionOrDesktopOverlay()
    {
        using var visualizer = new AudioVisualizerRenderer();
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();

        var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
        var audio = CreateLiveAudioMetrics();
        var modes = new[]
        {
            AudioVisualizerMode.SpectrumBars,
            AudioVisualizerMode.RadialHalo,
            AudioVisualizerMode.FluidWave,
            AudioVisualizerMode.ParticleField,
            AudioVisualizerMode.BassHorizon
        };

        var signatures = new HashSet<long>();
        foreach (var mode in modes)
        {
            using var surf = SKSurface.Create(info);
            surf.Canvas.Clear(SKColors.Transparent);
            var pkt = CreatePacket(
                AppSettings.Default with { AudioVisualizerMode = mode, AudioVisualizerIntensity = 1.0f },
                StageRenderSurfaceMode.StageCanvas,
                audio);
            var ctx = new StyleRenderContext(surf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            visualizer.Render(in ctx);
            using var bmp = SKBitmap.FromImage(surf.Snapshot());
            CountLitPixels(bmp, out var sig).Should().BeGreaterThan(50, $"{mode} must draw visible visualizer graphics");
            signatures.Add(sig);
        }

        signatures.Should().HaveCount(5, "each of the 5 visualizers should produce distinct geometry");

        // Skips ReducedMotion and DesktopOverlay
        foreach (var (rm, surfMode) in new[] { (true, StageRenderSurfaceMode.StageCanvas), (false, StageRenderSurfaceMode.DesktopOverlay) })
        {
            using var surf = SKSurface.Create(info);
            surf.Canvas.Clear(SKColors.Transparent);
            var pkt = CreatePacket(
                AppSettings.Default with { AudioVisualizerMode = AudioVisualizerMode.SpectrumBars, ReducedMotion = rm },
                surfMode,
                audio);
            var ctx = new StyleRenderContext(surf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            visualizer.Render(in ctx);
            using var bmp = SKBitmap.FromImage(surf.Snapshot());
            CountLitPixels(bmp, out _).Should().Be(0);
        }
    }

    [Fact]
    public void ReactiveVisualIconRenderer_DrawsAllSixteenConceptsAndFallbackOntoExplicitOffscreenCanvas()
    {
        using var renderer = new ReactiveVisualIconRenderer();
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();

        var concepts = new[]
        {
            "fire", "heart", "heart_broken", "water", "moon", "sun", "star", "lightning",
            "rain", "music", "clock", "crown", "paper_plane", "eye", "diamond", "flower", "guitar"
        };

        var info = new SKImageInfo(96, 96, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pkt = CreatePacket(AppSettings.Default with { VisualPreference = VisualPreference.Balanced }, StageRenderSurfaceMode.StageCanvas, CreateLiveAudioMetrics());

        foreach (var concept in concepts)
        {
            using var mainSurf = SKSurface.Create(info);
            using var offscreenSurf = SKSurface.Create(info);
            mainSurf.Canvas.Clear(SKColors.Transparent);
            offscreenSurf.Canvas.Clear(SKColors.Transparent);

            var ctx = new StyleRenderContext(mainSurf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            var match = new WordVisualMatch(VisualKind.Emoji, "✨", "fluent", concept, 1.0f, true, "✨");

            renderer.DrawVisual(offscreenSurf.Canvas, in ctx, match, 48f, 48f, 44f, 1.0f, isActive: true);

            using var offBmp = SKBitmap.FromImage(offscreenSurf.Snapshot());
            using var mainBmp = SKBitmap.FromImage(mainSurf.Snapshot());

            CountLitPixels(offBmp, out _).Should().BeGreaterThan(25, $"concept '{concept}' must draw visible pixels onto explicit offscreen canvas");
            CountLitPixels(mainBmp, out _).Should().Be(0, $"concept '{concept}' must not leak onto context.Canvas when passed explicit canvas");
        }
    }

    [Fact]
    public void StageBackdropRenderer_ResolveBackdropPalette_FallsBackOnlyForMonochromeOrEmptyHash()
    {
        var monochrome = AlbumAccentPalette.Default with { IsMonochromeFallback = true, SourceAlbumHash = "abc123" };
        var noHash = AlbumAccentPalette.Default with { IsMonochromeFallback = false, SourceAlbumHash = string.Empty };
        var richAlbum = new AlbumAccentPalette(
            new RgbColor(0x12, 0xAB, 0xEF),
            new RgbColor(0xEE, 0x34, 0x56),
            new RgbColor(0x90, 0xF0, 0x70),
            new RgbColor(0x10, 0x10, 0x20),
            IsMonochromeFallback: false,
            SourceAlbumHash: "deadbeef");

        var resolvedMono = StageBackdropRenderer.ResolveBackdropPalette(OverlayStyleId.Constellation, monochrome, autoSyncColors: true);
        resolvedMono.IsMonochromeFallback.Should().BeFalse();
        resolvedMono.AccentColor1.Should().NotBe(monochrome.AccentColor1);

        var resolvedNoHash = StageBackdropRenderer.ResolveBackdropPalette(OverlayStyleId.Messages, noHash, autoSyncColors: true);
        resolvedNoHash.AccentColor1.Should().NotBe(noHash.AccentColor1);

        var preserved = StageBackdropRenderer.ResolveBackdropPalette(OverlayStyleId.Fisheye, richAlbum, autoSyncColors: true);
        preserved.Should().Be(richAlbum);
    }
}
