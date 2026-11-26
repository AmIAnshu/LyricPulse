using System.Collections.Immutable;
using System.Net;
using System.Text;
using AwesomeAssertions;
using LyricPulse.Core.Audio;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Media;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Core.Visuals;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Shaders;
using LyricPulse.Rendering.Styles;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace LyricPulse.Tests;

public sealed class Phase7NeonLineKineticWallAndSyncTests
{
    [Fact]
    public void MonotonicPlaybackClock_PreventsIntegerSecondSawtoothBackwardJumps_WhileAllowingRealRewinds()
    {
        const long freq = 10_000_000;
        var nowTicks = 100 * freq;
        var utc = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        var clock = new MonotonicPlaybackClock(() => nowTicks, freq, () => utc);

        // Initial report at 13.000s
        clock.UpdateFromTimeline("web-track", TimeSpan.FromSeconds(13), TimeSpan.Zero, TimeSpan.FromMinutes(3), 1.0, true, utc);

        // Advance clock cleanly by 1.80s -> est = 14.800s
        nowTicks += (long)(1.80 * freq);
        utc = utc.AddSeconds(1.80);

        // Browser ticks integer second 14.000s forward from 13.000s with LastUpdatedTime=utc -> must NOT snap backward 800ms!
        var quantized = clock.UpdateFromTimeline("web-track", TimeSpan.FromSeconds(14), TimeSpan.Zero, TimeSpan.FromMinutes(3), 1.0, true, utc);
        quantized.Kind.Should().Be(ClockUpdateKind.IgnoredJitter);
        clock.GetInterpolatedPosition().Should().BeCloseTo(TimeSpan.FromSeconds(14.80), TimeSpan.FromMilliseconds(2));

        // Real manual backward rewind to 10.000s (reported < LastReportedPosition) -> MUST snap cleanly
        var rewind = clock.UpdateFromTimeline("web-track", TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromMinutes(3), 1.0, true, utc);
        rewind.Kind.Should().Be(ClockUpdateKind.SeekSnap);
        clock.GetInterpolatedPosition().Should().BeCloseTo(TimeSpan.FromSeconds(10.0), TimeSpan.FromMilliseconds(2));
    }

    [Fact]
    public async Task LrcLibLyricsProvider_FallsThroughLooseDurationGetMatch_ToExactSearchMatch()
    {
        var handler = new DelegateHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Equals("/api/get", StringComparison.OrdinalIgnoreCase))
            {
                // Radio edit 6.0s longer than target -> should not blindly win over exact /api/search match!
                return JsonResponse("""{"id":10,"trackName":"Starlight","artistName":"Muse","duration":246.0,"instrumental":false,"syncedLyrics":"[00:08.00]Offset radio edit"}""");
            }

            if (req.RequestUri.AbsolutePath.Equals("/api/search", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""[{"id":11,"trackName":"Starlight","artistName":"Muse","duration":240.1,"instrumental":false,"syncedLyrics":"[00:02.00]Exact album sync"}]""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var normalizer = new TitleNormalizer();
        var parser = new LyricsParser();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://lrclib.net") };
        using var provider = new LrcLibLyricsProvider(client, parser, normalizer, NullLogger<LrcLibLyricsProvider>.Instance, delayAsync: (_, _) => Task.CompletedTask);

        var track = normalizer.CreateIdentity("Starlight", "Muse", "Black Holes", TimeSpan.FromSeconds(240));
        var result = await provider.FetchAsync(track);

        result.Status.Should().Be(LyricsStatus.Synced);
        result.Document.Should().NotBeNull();
        result.Document!.Lines[0].RawText.Should().Be("Exact album sync");
    }

    [Fact]
    public void WordTimingInterpolator_BoundsShortPhrasesFollowedByLongPause()
    {
        var words = WordTimingInterpolator.SynthesizeWords(
            "Let's go",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(17.5),
            TimeSpan.FromMinutes(3),
            out var lineEnd);

        words.Should().HaveCount(2);
        (lineEnd - TimeSpan.FromSeconds(10)).Should().BeLessThan(TimeSpan.FromSeconds(3.5),
            "short 2-word phrases before a long instrumental gap must complete naturally instead of lingering 7s");
    }

    [Fact]
    public async Task KineticWallStyleRenderer_DrawsArchitecturalStageAndKeepsDesktopCornersTransparent()
    {
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var renderer = new KineticWallStyleRenderer();

        using var matcher = new GloveSemanticVisualMatcher();
        await matcher.InitializeAsync();
        var rawDoc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        var doc = matcher.AnnotateDocument(rawDoc, VisualPreference.Balanced);

        var bands = new float[AudioFrameMetrics.BandCount];
        Array.Fill(bands, 0.72f);
        var audio = new AudioFrameMetrics(0.80f, 0.62f, 0.55f, 0.70f, 0.88f, true, ImmutableArray.Create(bands));
        var pos = TimeSpan.FromSeconds(5.1);
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
            Settings: AppSettings.Default with { ActiveStyle = OverlayStyleId.KineticWall, PartyEnergyLevel = 1.3f },
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

        var stageInfo = new SKImageInfo(960, 540, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var surf = SKSurface.Create(stageInfo))
        {
            surf.Canvas.Clear(SKColors.Black);
            var pkt = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 960f, 540f, 1.0f, 5.1, 1f / 60f, surfaceMode: StageRenderSurfaceMode.StageCanvas);
            var lifecycle = new LifecycleFrameState(1.0f, false, 0f, 0f, null, 0f, false);
            renderer.Advance(in pkt, in lifecycle).Should().BeTrue();
            var ctx = new StyleRenderContext(surf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            renderer.Render(in ctx, in lifecycle);

            using var bmp = SKBitmap.FromImage(surf.Snapshot());
            var nonBlackCount = 0;
            for (var y = 0; y < bmp.Height; y += 4)
            {
                for (var x = 0; x < bmp.Width; x += 4)
                {
                    var px = bmp.GetPixel(x, y);
                    if (px.Red > 12 || px.Green > 12 || px.Blue > 12)
                    {
                        nonBlackCount++;
                    }
                }
            }

            nonBlackCount.Should().BeGreaterThan(200);
        }

        // DesktopOverlay skips backplane/telemetry rail so all 4 corners stay alpha=0
        var deskInfo = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var deskSurf = SKSurface.Create(deskInfo))
        {
            deskSurf.Canvas.Clear(SKColors.Transparent);
            renderer.ResetState();
            var pkt = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 640f, 360f, 1.0f, 5.1, 1f / 60f, surfaceMode: StageRenderSurfaceMode.DesktopOverlay);
            var lifecycle = new LifecycleFrameState(1.0f, false, 0f, 0f, null, 0f, false);
            renderer.Advance(in pkt, in lifecycle);
            var ctx = new StyleRenderContext(deskSurf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            renderer.Render(in ctx, in lifecycle);

            using var deskBmp = SKBitmap.FromImage(deskSurf.Snapshot());
            deskBmp.GetPixel(0, 0).Alpha.Should().Be(0);
            deskBmp.GetPixel(639, 0).Alpha.Should().Be(0);
            deskBmp.GetPixel(0, 359).Alpha.Should().Be(0);
            deskBmp.GetPixel(639, 359).Alpha.Should().Be(0);
        }
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
