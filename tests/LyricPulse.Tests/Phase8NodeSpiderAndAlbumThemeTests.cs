using System.Collections.Immutable;
using System.Security.Cryptography;
using AwesomeAssertions;
using LyricPulse.Core.Audio;
using LyricPulse.Core.Color;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Core.Visuals;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Shaders;
using LyricPulse.Rendering.Styles;
using LyricPulse.UI.Theme;
using SkiaSharp;
using Xunit;
using AvaloniaColor = Avalonia.Media.Color;

namespace LyricPulse.Tests;

public sealed class Phase8NodeSpiderAndAlbumThemeTests
{
    private static AudioFrameMetrics CreateAudioMetrics()
    {
        var bands = new float[AudioFrameMetrics.BandCount];
        for (var i = 0; i < bands.Length; i++)
        {
            bands[i] = Math.Clamp(0.42f + (0.45f * MathF.Sin((i * 0.5f) + 0.2f)), 0.15f, 0.95f);
        }

        return new AudioFrameMetrics(
            BassEnergy: 0.74f,
            MidEnergy: 0.62f,
            TrebleEnergy: 0.50f,
            OverallLevel: 0.65f,
            BeatPulse: 0.80f,
            IsLiveAudioActive: true,
            SpectrumBands: ImmutableArray.Create(bands));
    }

    private static FrameRenderPacket BuildPacket(
        LyricsDocument doc,
        AppSettings settings,
        AlbumAccentPalette palette,
        StageRenderSurfaceMode surfaceMode,
        TimeSpan position,
        AudioFrameMetrics? audio = null,
        bool bottomFadeToBlack = false,
        double renderTime = 2.0)
    {
        var snap = new OverlayPlaybackSnapshot(
            Track: doc.Track,
            Document: doc,
            LyricsStatus: LyricsStatus.Synced,
            PlaybackState: MediaPlaybackState.Playing,
            PlayerKind: MediaPlayerKind.Spotify,
            SourceAppUserModelId: "Spotify.exe",
            RawPosition: position,
            EffectivePosition: position,
            ActiveSlice: ActiveLyricSlice.Resolve(doc, position),
            ActivePalette: palette,
            TargetPalette: palette,
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
            renderTimeSeconds: renderTime,
            deltaSeconds: 1f / 60f,
            surfaceMode: surfaceMode,
            bottomFadeToBlack: bottomFadeToBlack);
    }

    private static int CountLitPixels(SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                if (bitmap.GetPixel(x, y).Alpha > 18)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static byte[] EncodePng(int width, int height, Func<int, int, SKColor> pixelAt)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        using var bmp = new SKBitmap(info);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bmp.SetPixel(x, y, pixelAt(x, y));
            }
        }

        surface.Canvas.DrawBitmap(bmp, 0, 0);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task NodeLine_And_SpidersWeb_RenderRichSemanticArt_WithReducedMotionAndTransparentDesktopCorners()
    {
        using var matcher = new StarterLexiconVisualMatcher();
        await matcher.InitializeAsync();
        var demoDoc = matcher.AnnotateDocument(
            new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument(),
            VisualPreference.Balanced);

        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var router = new LyricStyleRouter();

        var audio = CreateAudioMetrics();
        var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
        var styleHashes = new Dictionary<OverlayStyleId, string>();

        foreach (var styleId in new[] { OverlayStyleId.NodeLine, OverlayStyleId.SpidersWeb })
        {
            var stageSettings = AppSettings.Default with
            {
                ActiveStyle = styleId,
                StageBackdropMode = StageBackdropMode.AmbientPartyMesh,
                PartyEnergyLevel = 1.25f,
                ShowColorSweepLayer = true,
                ShowFollowingDot = true
            };

            FrameRenderPacket stagePkt = null!;
            for (var step = 0; step < 12; step++)
            {
                stagePkt = BuildPacket(
                    demoDoc,
                    stageSettings,
                    AlbumAccentPalette.DemoNeonHorizon,
                    StageRenderSurfaceMode.StageCanvas,
                    TimeSpan.FromSeconds(1.55),
                    audio,
                    bottomFadeToBlack: false,
                    renderTime: 1.55 + (step * 0.016));
                router.AdvancePhysics(in stagePkt);
            }

            using var stageSurface = SKSurface.Create(info);
            var stageCtx = new StyleRenderContext(stageSurface.Canvas, null, fonts, shaders, pool, legibility, stagePkt, false, false);
            router.Render(in stageCtx);
            using var stageBmp = SKBitmap.FromImage(stageSurface.Snapshot());
            CountLitPixels(stageBmp).Should().BeGreaterThan(450, $"{styleId} must draw substantial botanical/silk visuals on StageCanvas");
            styleHashes[styleId] = Convert.ToHexString(SHA256.HashData(stageBmp.GetPixelSpan()));

            // ReducedMotion deterministic & high-contrast render
            var reducedSettings = stageSettings with { ReducedMotion = true, ScrimIntensity = ScrimIntensity.Strong };
            var reducedPkt = BuildPacket(
                demoDoc,
                reducedSettings,
                AlbumAccentPalette.DemoNeonHorizon,
                StageRenderSurfaceMode.StageCanvas,
                TimeSpan.FromSeconds(1.55),
                audio,
                renderTime: 1.80);
            router.AdvancePhysics(in reducedPkt);
            using var reducedSurface = SKSurface.Create(info);
            var reducedCtx = new StyleRenderContext(reducedSurface.Canvas, null, fonts, shaders, pool, legibility, reducedPkt, false, false);
            router.Render(in reducedCtx);
            using var reducedBmp = SKBitmap.FromImage(reducedSurface.Snapshot());
            CountLitPixels(reducedBmp).Should().BeGreaterThan(350);

            // DesktopOverlay must keep all 4 corners strictly alpha=0 while drawing active lyric content
            var desktopSettings = AppSettings.Default with
            {
                ActiveStyle = styleId,
                EdgeGlowMode = EdgeGlowMode.Off,
                AudioVisualizerMode = AudioVisualizerMode.None,
                ShowColorSweepLayer = true
            };
            var desktopPkt = BuildPacket(
                demoDoc,
                desktopSettings,
                AlbumAccentPalette.DemoNeonHorizon,
                StageRenderSurfaceMode.DesktopOverlay,
                TimeSpan.FromSeconds(1.55),
                audio,
                renderTime: 2.00);
            router.AdvancePhysics(in desktopPkt);
            using var desktopSurface = SKSurface.Create(info);
            desktopSurface.Canvas.Clear(SKColors.Transparent);
            var desktopCtx = new StyleRenderContext(desktopSurface.Canvas, null, fonts, shaders, pool, legibility, desktopPkt, false, false);
            router.Render(in desktopCtx);
            using var desktopBmp = SKBitmap.FromImage(desktopSurface.Snapshot());

            CountLitPixels(desktopBmp).Should().BeGreaterThan(200);
            desktopBmp.GetPixel(0, 0).Alpha.Should().Be(0);
            desktopBmp.GetPixel(639, 0).Alpha.Should().Be(0);
            desktopBmp.GetPixel(0, 359).Alpha.Should().Be(0);
            desktopBmp.GetPixel(639, 359).Alpha.Should().Be(0);
        }

        styleHashes.Values.Distinct().Should().HaveCount(2, "NodeLine and SpidersWeb must look visually distinct");
    }

    [Fact]
    public void OklabPaletteExtractor_DarkNeonCovers_PreserveHighChromaAndDistinctEnvironmentHues()
    {
        var crimsonBytes = EncodePng(96, 96, (x, y) =>
        {
            var dx = x - 48;
            var dy = y - 48;
            if ((dx * dx) + (dy * dy) <= 22 * 22)
            {
                return new SKColor(238, 34, 72);
            }

            return y < 48 ? new SKColor(18, 12, 16) : new SKColor(140, 28, 195);
        });

        var cyanBytes = EncodePng(96, 96, (x, y) =>
        {
            var dx = x - 48;
            var dy = y - 48;
            if ((dx * dx) + (dy * dy) <= 22 * 22)
            {
                return new SKColor(24, 212, 245);
            }

            return y < 48 ? new SKColor(12, 18, 24) : new SKColor(42, 232, 152);
        });

        var extractor = new SkiaAlbumColorExtractor();
        var crimsonPalette = extractor.Extract(new AlbumArtPayload("crimson-cover", crimsonBytes));
        var cyanPalette = extractor.Extract(new AlbumArtPayload("cyan-cover", cyanBytes));

        crimsonPalette.IsMonochromeFallback.Should().BeFalse();
        cyanPalette.IsMonochromeFallback.Should().BeFalse();

        var crimsonLch1 = OklabColorMath.OklabToOklch(OklabColorMath.SrgbToOklab(crimsonPalette.AccentColor1));
        var crimsonLch2 = OklabColorMath.OklabToOklch(OklabColorMath.SrgbToOklab(crimsonPalette.AccentColor2));
        var cyanLch2 = OklabColorMath.OklabToOklch(OklabColorMath.SrgbToOklab(cyanPalette.AccentColor2));

        crimsonLch1.L.Should().BeInRange(0.76, 0.94);
        crimsonLch2.L.Should().BeInRange(0.64, 0.84);
        crimsonLch2.C.Should().BeGreaterThanOrEqualTo(0.10, "dark crimson cover should retain vivid accent chroma");
        cyanLch2.C.Should().BeGreaterThanOrEqualTo(0.10, "dark cyan cover should retain vivid accent chroma");

        crimsonPalette.AccentColor2.Should().NotBe(cyanPalette.AccentColor2);
        crimsonPalette.AccentColor2.R.Should().BeGreaterThan(crimsonPalette.AccentColor2.G);
        (cyanPalette.AccentColor2.G + cyanPalette.AccentColor2.B).Should().BeGreaterThan(cyanPalette.AccentColor2.R * 2);
    }

    [Fact]
    public void EdgeGlowRenderer_NeonRush_AdaptsPerimeterToAlbumPaletteWithoutHardcodedOrange()
    {
        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var edgeGlow = new EdgeGlowRenderer();

        var audio = CreateAudioMetrics();
        var settings = AppSettings.Default with
        {
            EdgeGlowMode = EdgeGlowMode.NeonRush,
            EdgeGlowIntensity = 1.25f,
            EdgeGlowThickness = 1.40f,
            EdgeGlowSpeed = 0.55f
        };

        var crimsonPalette = new AlbumAccentPalette(
            new RgbColor(0xFF, 0xD4, 0xDC),
            new RgbColor(0xEF, 0x44, 0x44),
            new RgbColor(0xA8, 0x55, 0xF7),
            new RgbColor(0, 0, 0),
            IsMonochromeFallback: false,
            SourceAlbumHash: "crimson");

        var cyanPalette = new AlbumAccentPalette(
            new RgbColor(0xBA, 0xE6, 0xFD),
            new RgbColor(0x06, 0xB6, 0xD4),
            new RgbColor(0x10, 0xB9, 0x81),
            new RgbColor(0, 0, 0),
            IsMonochromeFallback: false,
            SourceAlbumHash: "cyan");

        (long sumR, long sumG, long sumB) RenderPerimeterChannels(AlbumAccentPalette palette)
        {
            var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surf = SKSurface.Create(info);
            surf.Canvas.Clear(SKColors.Transparent);
            var pkt = BuildPacket(doc, settings, palette, StageRenderSurfaceMode.StageCanvas, TimeSpan.FromSeconds(3.2), audio);
            var ctx = new StyleRenderContext(surf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            edgeGlow.Render(in ctx);
            using var bmp = SKBitmap.FromImage(surf.Snapshot());

            long r = 0, g = 0, b = 0;
            for (var y = 0; y < bmp.Height; y += 2)
            {
                for (var x = 0; x < bmp.Width; x += 2)
                {
                    var px = bmp.GetPixel(x, y);
                    if (px.Alpha > 20)
                    {
                        r += px.Red;
                        g += px.Green;
                        b += px.Blue;
                    }
                }
            }

            return (r, g, b);
        }

        var (crimR, crimG, _) = RenderPerimeterChannels(crimsonPalette);
        var (cyanR, cyanG, cyanB) = RenderPerimeterChannels(cyanPalette);

        crimR.Should().BeGreaterThan((long)(crimG * 1.35));
        (cyanG + cyanB).Should().BeGreaterThan((long)(cyanR * 1.80), "cool album NeonRush must not inject hardcoded orange bias");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlbumEnvironmentPalette_EnforcesThemeContrastSafeguardsAndDistinctMoods(bool isLightTheme)
    {
        var crimson = AlbumEnvironmentPalette.ComputeDynamicAccentColors(
            AvaloniaColor.FromRgb(0xFD, 0xA4, 0xAF),
            AvaloniaColor.FromRgb(0xE1, 0x1D, 0x48),
            AvaloniaColor.FromRgb(0xF5, 0x9E, 0x0B),
            isLightTheme);

        var cyan = AlbumEnvironmentPalette.ComputeDynamicAccentColors(
            AvaloniaColor.FromRgb(0x7D, 0xD3, 0xFC),
            AvaloniaColor.FromRgb(0x02, 0x84, 0xC7),
            AvaloniaColor.FromRgb(0x14, 0xB8, 0xA6),
            isLightTheme);

        var neonYellow = AlbumEnvironmentPalette.ComputeDynamicAccentColors(
            AvaloniaColor.FromRgb(0xFE, 0xF0, 0x8A),
            AvaloniaColor.FromRgb(0xFA, 0xCC, 0x15),
            AvaloniaColor.FromRgb(0xF9, 0x73, 0x16),
            isLightTheme);

        if (isLightTheme)
        {
            AlbumEnvironmentPalette.ComputeRelativeLuminance(crimson.Primary).Should().BeLessThanOrEqualTo(0.17);
            AlbumEnvironmentPalette.ComputeRelativeLuminance(neonYellow.Primary).Should().BeLessThanOrEqualTo(0.17);
            AlbumEnvironmentPalette.ComputeRelativeLuminance(cyan.Text).Should().BeLessThanOrEqualTo(0.15);
        }
        else
        {
            AlbumEnvironmentPalette.ComputeRelativeLuminance(crimson.Primary).Should().BeLessThanOrEqualTo(0.24);
            AlbumEnvironmentPalette.ComputeRelativeLuminance(neonYellow.Primary).Should().BeLessThanOrEqualTo(0.24);
            AlbumEnvironmentPalette.ComputeRelativeLuminance(crimson.Text).Should().BeGreaterThanOrEqualTo(0.42);
            AlbumEnvironmentPalette.ComputeRelativeLuminance(cyan.Text).Should().BeGreaterThanOrEqualTo(0.42);
        }

        crimson.Primary.Should().NotBe(cyan.Primary);
        crimson.SoftBg.Should().NotBe(cyan.SoftBg);
        crimson.Primary.R.Should().BeGreaterThan(crimson.Primary.B);
        cyan.Primary.B.Should().BeGreaterThan(cyan.Primary.R);
    }
}
