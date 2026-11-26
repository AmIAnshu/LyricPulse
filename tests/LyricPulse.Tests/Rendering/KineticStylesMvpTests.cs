using System.Security.Cryptography;
using AwesomeAssertions;
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

public sealed class KineticStylesMvpTests
{
    [Fact]
    public async Task AllThreeMvpStyles_RenderDistinctNonEmptyFramesAcrossFull36sNeonHorizonTimeline()
    {
        using var matcher = new StarterLexiconVisualMatcher();
        await matcher.InitializeAsync();
        var demoProvider = new DemoSongProvider(new LyricsParser(), new TitleNormalizer());
        var annotatedDoc = matcher.AnnotateDocument(demoProvider.GetDemoDocument(), VisualPreference.Balanced);

        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();

        var fisheye = new FisheyeStyleRenderer { AllowCpuShadersForTesting = true };
        var codestyle = new CodestyleStyleRenderer();
        var visualStack = new VisualStackStyleRenderer { AllowCpuShadersForTesting = true };
        using var router = new LyricStyleRouter([fisheye, codestyle, visualStack]);

        var mvpStyles = new[] { OverlayStyleId.Fisheye, OverlayStyleId.Codestyle, OverlayStyleId.VisualStack };
        var sampleSeconds = new[] { 0.35, 1.55, 3.90, 7.50, 14.60, 15.65, 21.60, 29.80, 33.70 };
        var comparisonHashes = new Dictionary<OverlayStyleId, string>();
        var info = new SKImageInfo(720, 320, SKColorType.Bgra8888, SKAlphaType.Premul);

        foreach (var styleId in mvpStyles)
        {
            foreach (var sec in sampleSeconds)
            {
                var pos = TimeSpan.FromSeconds(sec);
                var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero).AddSeconds(sec);
                var snap = BuildDemoSnapshot(
                    annotatedDoc,
                    pos,
                    AppSettings.Default with { ActiveStyle = styleId },
                    lastNudgeUtc: Math.Abs(sec - 15.65) < 0.01 ? now.AddMilliseconds(-200) : null);

                // Step several frames so springs & fades advance naturally
                FrameRenderPacket packet = null!;
                for (var step = 0; step < 8; step++)
                {
                    packet = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 720, 320, 1f, sec + (step * 0.016), 1f / 60f, utcNow: now);
                    router.AdvancePhysics(in packet);
                }

                using var surface = SKSurface.Create(info);
                var ctx = new StyleRenderContext(surface.Canvas, null, fonts, shaders, pool, legibility, packet, IsGpuBackend: false, ShadersEnabled: false);
                router.Render(in ctx);

                using var image = surface.Snapshot();
                using var bitmap = SKBitmap.FromImage(image);

                var litPixels = CountLitPixels(bitmap);
                litPixels.Should().BeGreaterThan(150, $"{styleId} at t={sec:F2}s must produce visible rendered pixels");

                if (Math.Abs(sec - 1.55) < 0.01)
                {
                    comparisonHashes[styleId] = Convert.ToHexString(SHA256.HashData(bitmap.GetPixelSpan()));
                }
            }
        }

        // All 3 styles at identical timestamp t=1.55s ("star" line) must render visually distinct output
        comparisonHashes.Values.Distinct().Should().HaveCount(3, "Fisheye, Codestyle, and VisualStack must render distinct outputs");
    }

    [Theory]
    [InlineData(OverlayStyleId.Fisheye, true, false)]
    [InlineData(OverlayStyleId.Fisheye, false, true)]
    [InlineData(OverlayStyleId.Codestyle, true, false)]
    [InlineData(OverlayStyleId.Codestyle, false, true)]
    [InlineData(OverlayStyleId.VisualStack, true, false)]
    [InlineData(OverlayStyleId.VisualStack, false, true)]
    public async Task MvpStyles_RenderCleanlyUnderReducedMotionAndHighContrastScrimOverWhiteAndDarkBackdrops(
        OverlayStyleId styleId,
        bool reducedMotion,
        bool highContrastScrim)
    {
        using var matcher = new StarterLexiconVisualMatcher();
        await matcher.InitializeAsync();
        var doc = matcher.AnnotateDocument(new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument(), VisualPreference.Balanced);

        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var router = new LyricStyleRouter();

        var settings = AppSettings.Default with
        {
            ActiveStyle = styleId,
            ReducedMotion = reducedMotion,
            HighContrastScrim = highContrastScrim,
            FontFamily = styleId == OverlayStyleId.Fisheye ? "Bodoni Moda" : "Mulish"
        };

        var snap = BuildDemoSnapshot(doc, TimeSpan.FromSeconds(1.55), settings);
        var packet = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 720, 320, 1.5f, 1.55, 1f / 60f);

        for (var i = 0; i < 15; i++)
        {
            router.AdvancePhysics(in packet);
        }

        var info = new SKImageInfo(720, 320, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var overlaySurface = SKSurface.Create(info);
        var ctx = new StyleRenderContext(overlaySurface.Canvas, null, fonts, shaders, pool, legibility, packet, IsGpuBackend: false, ShadersEnabled: false);
        router.Render(in ctx);

        using var overlayImage = overlaySurface.Snapshot();
        using var overlayBmp = SKBitmap.FromImage(overlayImage);
        CountLitPixels(overlayBmp).Should().BeGreaterThan(150);

        // Composite onto pure #FFFFFF webpage and #0D0D14 dark IDE without error
        foreach (var backdrop in new[] { SKColors.White, new SKColor(0x0D, 0x0D, 0x14) })
        {
            using var compositeSurface = SKSurface.Create(info);
            compositeSurface.Canvas.Clear(backdrop);
            compositeSurface.Canvas.DrawImage(overlayImage, 0f, 0f);
            using var compBmp = SKBitmap.FromImage(compositeSurface.Snapshot());
            compBmp.GetPixel(360, 160).Alpha.Should().Be(255);
        }
    }

    [Fact]
    public void LyricStyleRouter_ResolvesAllElevenStylesFallsBackForUnknownAndResetsStateOnTrackChange()
    {
        using var router = new LyricStyleRouter();

        router.RegisteredStyles.Should().HaveCount(11);
        router.RegisteredStyles.Should().Contain(Enum.GetValues<OverlayStyleId>());
        router.ResolveRenderer(OverlayStyleId.Constellation).StyleId.Should().Be(OverlayStyleId.Constellation);
        router.ResolveRenderer((OverlayStyleId)999).StyleId.Should().Be(OverlayStyleId.Fisheye);
        router.ResolveRenderer(OverlayStyleId.Codestyle).StyleId.Should().Be(OverlayStyleId.Codestyle);
        router.ResolveRenderer(OverlayStyleId.VisualStack).StyleId.Should().Be(OverlayStyleId.VisualStack);

        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        var snap1 = BuildDemoSnapshot(doc, TimeSpan.FromSeconds(1.5), AppSettings.Default, trackGeneration: 1);
        var snap2 = BuildDemoSnapshot(doc, TimeSpan.FromSeconds(1.5), AppSettings.Default with { ActiveStyle = OverlayStyleId.Codestyle }, trackGeneration: 2);

        var p1 = OverlayFrameAdapter.FromPlaybackSnapshot(snap1, 640, 240, 1f, 1.5, 1f / 60f);
        router.AdvancePhysics(in p1).Should().BeTrue();
        router.StyleId.Should().Be(OverlayStyleId.Fisheye);

        var p2 = OverlayFrameAdapter.FromPlaybackSnapshot(snap2, 640, 240, 1f, 1.6, 1f / 60f);
        router.AdvancePhysics(in p2).Should().BeTrue();
        router.StyleId.Should().Be(OverlayStyleId.Codestyle);
    }

    [Fact]
    public void VisualStack_GroupsShortLeadingConnectorsOntoMatchedContentRow()
    {
        var visualStar = new WordVisualMatch(VisualKind.FluentIcon, "\uF711", "ic_fluent_star_24_filled", "star", 1.0f, true, "⭐");
        var line = new LyricLine(
            0,
            "Chasing a star in the night",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(3),
            [
                new LyricWord("Chasing", TimeSpan.FromSeconds(0.0), TimeSpan.FromSeconds(0.5), true),
                new LyricWord("a", TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.8), true),
                new LyricWord("star", TimeSpan.FromSeconds(0.8), TimeSpan.FromSeconds(1.4), true, visualStar),
                new LyricWord("in", TimeSpan.FromSeconds(1.4), TimeSpan.FromSeconds(1.6), true),
                new LyricWord("the", TimeSpan.FromSeconds(1.6), TimeSpan.FromSeconds(1.9), true),
                new LyricWord("night", TimeSpan.FromSeconds(1.9), TimeSpan.FromSeconds(2.6), true)
            ]);

        var rows = new List<VisualStackStyleRenderer.VisualStackRow>();
        VisualStackStyleRenderer.BuildGroupedRows(line, rows);

        rows.Should().HaveCount(3);
        rows[0].PrimaryWord.Should().Be("Chasing");
        rows[1].LeadingConnectors.Should().Be("a");
        rows[1].PrimaryWord.Should().Be("star");
        rows[1].Visual.Should().Be(visualStar);
        rows[2].LeadingConnectors.Should().Be("in the");
        rows[2].PrimaryWord.Should().Be("night");
    }

    private static OverlayPlaybackSnapshot BuildDemoSnapshot(
        LyricsDocument doc,
        TimeSpan pos,
        AppSettings settings,
        long trackGeneration = 1,
        DateTimeOffset? lastNudgeUtc = null)
    {
        return new OverlayPlaybackSnapshot(
            Track: doc.Track,
            Document: doc,
            LyricsStatus: LyricsStatus.Synced,
            PlaybackState: MediaPlaybackState.Playing,
            PlayerKind: MediaPlayerKind.Unknown,
            SourceAppUserModelId: "LyricPulse.Demo",
            RawPosition: pos,
            EffectivePosition: pos,
            ActiveSlice: ActiveLyricSlice.Resolve(doc, pos),
            ActivePalette: AlbumAccentPalette.DemoNeonHorizon,
            TargetPalette: AlbumAccentPalette.DemoNeonHorizon,
            TimingOffset: new TimingOffsetState(doc.Track.CacheKey, TimeSpan.FromSeconds(-0.5), false),
            Settings: settings,
            IsPlaying: true,
            IsDemoMode: true,
            IsFullscreenHidden: false,
            PausedSinceUtc: null,
            LastOffsetNudgeUtc: lastNudgeUtc,
            TrackGeneration: trackGeneration,
            SeekGeneration: 0);
    }

    private static int CountLitPixels(SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                if (bitmap.GetPixel(x, y).Alpha > 20)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
