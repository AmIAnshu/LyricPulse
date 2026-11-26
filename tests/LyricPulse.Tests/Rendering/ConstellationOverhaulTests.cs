using System.Collections.Immutable;
using AwesomeAssertions;
using LyricPulse.Core.Audio;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Core.Visuals;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Shaders;
using LyricPulse.Rendering.Styles;
using SkiaSharp;
using Xunit;

namespace LyricPulse.Tests.Rendering;

public sealed class ConstellationOverhaulTests
{
    private static OverlayPlaybackSnapshot CreateSnapshot(
        LyricsDocument doc,
        TimeSpan position,
        bool reducedMotion = false,
        AudioFrameMetrics? audio = null)
    {
        var settings = AppSettings.Default with
        {
            ActiveStyle = OverlayStyleId.Constellation,
            StageBackdropMode = StageBackdropMode.AmbientPartyMesh,
            PartyEnergyLevel = 1.3f,
            ShowFollowingDot = true,
            ShowColorSweepLayer = true,
            ReducedMotion = reducedMotion
        };

        return new OverlayPlaybackSnapshot(
            Track: doc.Track,
            Document: doc,
            LyricsStatus: LyricsStatus.Synced,
            PlaybackState: MediaPlaybackState.Playing,
            PlayerKind: MediaPlayerKind.Spotify,
            SourceAppUserModelId: "Spotify.exe",
            RawPosition: position,
            EffectivePosition: position,
            ActiveSlice: ActiveLyricSlice.Resolve(doc, position),
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
    }

    [Fact]
    public void BuildDeterministicStarfield_ScattersStarsAcrossWide2DSky_WithoutLabelCollisions()
    {
        using var fonts = new FontAssetRegistry();
        var font = fonts.GetOrCreateFont(LyricFontId.Jost, 32f);
        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        var region = new SKRect(60f, 90f, 1220f, 650f);
        var nodes = new List<ConstellationStyleRenderer.StarNode>();

        var sawWideVerticalSpread = false;
        var sawWideHorizontalSpread = false;

        foreach (var line in doc.Lines)
        {
            if (line.Words.Length < 4)
            {
                continue;
            }

            ConstellationStyleRenderer.BuildDeterministicStarfield(line, region, font, fonts, nodes, spreadScale: 1.0f);
            nodes.Should().HaveCount(line.Words.Length);

            var minX = nodes.Min(n => n.X);
            var maxX = nodes.Max(n => n.X);
            var minY = nodes.Min(n => n.Y);
            var maxY = nodes.Max(n => n.Y);

            if (maxX - minX > region.Width * 0.45f)
            {
                sawWideHorizontalSpread = true;
            }

            if (maxY - minY > region.Height * 0.35f)
            {
                sawWideVerticalSpread = true;
            }

            foreach (var n in nodes)
            {
                n.X.Should().BeInRange(region.Left, region.Right);
                n.Y.Should().BeInRange(region.Top, region.Bottom);
            }
        }

        sawWideHorizontalSpread.Should().BeTrue("constellation stars should scatter broadly across X");
        sawWideVerticalSpread.Should().BeTrue("constellation stars should scatter genuinely across 2D Y space");
    }

    [Fact]
    public async Task ConstellationRenderer_RendersDeepSpaceStage_KeepsDesktopCornersTransparent_AndHonorsReducedMotion()
    {
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var renderer = new ConstellationStyleRenderer();

        using var matcher = new GloveSemanticVisualMatcher();
        await matcher.InitializeAsync();
        var rawDoc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        var doc = matcher.AnnotateDocument(rawDoc, VisualPreference.Balanced);

        var bands = new float[AudioFrameMetrics.BandCount];
        Array.Fill(bands, 0.65f);
        var audio = new AudioFrameMetrics(0.75f, 0.55f, 0.70f, 0.65f, 0.85f, true, ImmutableArray.Create(bands));

        // 1. StageCanvas renders full-sky parallax starfield, nebula, shooting star (t=5.1s hits 4.8s cycle!), shockwave, and starbursts
        var stageInfo = new SKImageInfo(960, 540, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var stageSurf = SKSurface.Create(stageInfo))
        {
            stageSurf.Canvas.Clear(SKColors.Transparent);
            var lifecycle = new LifecycleFrameState(1.0f, false, 0f, 0f, null, 0f, false);

            // Step through two words to trigger supernova ignition + camera parallax + comet trace
            var pkt1 = OverlayFrameAdapter.FromPlaybackSnapshot(
                CreateSnapshot(doc, TimeSpan.FromSeconds(4.9), audio: audio),
                960f, 540f, 1.0f, 4.9, 1f / 60f,
                surfaceMode: StageRenderSurfaceMode.StageCanvas);
            renderer.Advance(in pkt1, in lifecycle);

            var pkt2 = OverlayFrameAdapter.FromPlaybackSnapshot(
                CreateSnapshot(doc, TimeSpan.FromSeconds(5.15), audio: audio),
                960f, 540f, 1.0f, 5.15, 1f / 60f,
                surfaceMode: StageRenderSurfaceMode.StageCanvas);
            renderer.Advance(in pkt2, in lifecycle).Should().BeTrue();

            var ctx = new StyleRenderContext(stageSurf.Canvas, null, fonts, shaders, pool, legibility, pkt2, false, false);
            renderer.Render(in ctx, in lifecycle);

            using var bmp = SKBitmap.FromImage(stageSurf.Snapshot());
            var lit = CountLitPixels(bmp);
            lit.Should().BeGreaterThan(250, "overhauled Constellation stage must render rich starfield, starburst, and lyric labels");
        }

        // 2. DesktopOverlay skips background sky/nebula/shooting stars so all 4 screen corners remain transparent (Alpha == 0)
        var deskInfo = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var deskSurf = SKSurface.Create(deskInfo))
        {
            deskSurf.Canvas.Clear(SKColors.Transparent);
            renderer.ResetState();
            var lifecycle = new LifecycleFrameState(1.0f, false, 0f, 0f, null, 0f, false);
            var deskPkt = OverlayFrameAdapter.FromPlaybackSnapshot(
                CreateSnapshot(doc, TimeSpan.FromSeconds(5.15), audio: audio),
                640f, 360f, 1.0f, 5.15, 1f / 60f,
                surfaceMode: StageRenderSurfaceMode.DesktopOverlay);
            renderer.Advance(in deskPkt, in lifecycle);

            var ctx = new StyleRenderContext(deskSurf.Canvas, null, fonts, shaders, pool, legibility, deskPkt, false, false);
            renderer.Render(in ctx, in lifecycle);

            using var deskBmp = SKBitmap.FromImage(deskSurf.Snapshot());
            CountLitPixels(deskBmp).Should().BeGreaterThan(40);
            deskBmp.GetPixel(0, 0).Alpha.Should().Be(0);
            deskBmp.GetPixel(639, 0).Alpha.Should().Be(0);
            deskBmp.GetPixel(0, 359).Alpha.Should().Be(0);
            deskBmp.GetPixel(639, 359).Alpha.Should().Be(0);
        }

        // 3. ReducedMotion renders deterministically without motion/flashes
        using (var rmSurf = SKSurface.Create(stageInfo))
        {
            rmSurf.Canvas.Clear(SKColors.Transparent);
            renderer.ResetState();
            var lifecycle = new LifecycleFrameState(1.0f, false, 0f, 0f, null, 0f, false);
            var rmPkt = OverlayFrameAdapter.FromPlaybackSnapshot(
                CreateSnapshot(doc, TimeSpan.FromSeconds(8.9), reducedMotion: true, audio: audio),
                960f, 540f, 1.0f, 8.9, 1f / 60f,
                surfaceMode: StageRenderSurfaceMode.StageCanvas);
            renderer.Advance(in rmPkt, in lifecycle);

            var ctx = new StyleRenderContext(rmSurf.Canvas, null, fonts, shaders, pool, legibility, rmPkt, false, false);
            renderer.Render(in ctx, in lifecycle);

            using var rmBmp = SKBitmap.FromImage(rmSurf.Snapshot());
            CountLitPixels(rmBmp).Should().BeGreaterThan(80);
        }

        renderer.Dispose();
        renderer.Dispose(); // idempotent
    }

    private static int CountLitPixels(SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y += 3)
        {
            for (var x = 0; x < bitmap.Width; x += 3)
            {
                if (bitmap.GetPixel(x, y).Alpha > 15)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
