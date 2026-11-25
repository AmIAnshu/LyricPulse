using AwesomeAssertions;
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

public sealed class PlaybackLifecycleChoreographerTests
{
    [Fact]
    public void PauseDimAndAutoFade_FollowsExact300msDimAnd8SecondFadeCurve()
    {
        // Pure curve verification (spec STY-04: 300ms dim -> 0.62 opacity, 8s hold, 450ms fade -> 0.00)
        PlaybackLifecycleChoreographer.EvaluatePauseOpacity(0.0f, out var active0).Should().Be(1.0f);
        active0.Should().BeTrue();

        var midDim = PlaybackLifecycleChoreographer.EvaluatePauseOpacity(0.15f, out var activeMid);
        midDim.Should().BeInRange(0.62f, 1.0f);
        activeMid.Should().BeTrue();

        var held = PlaybackLifecycleChoreographer.EvaluatePauseOpacity(2.5f, out var activeHold);
        held.Should().BeApproximately(0.62f, 0.001f);
        activeHold.Should().BeTrue();

        var fading = PlaybackLifecycleChoreographer.EvaluatePauseOpacity(8.22f, out var activeFade);
        fading.Should().BeInRange(0.01f, 0.62f);
        activeFade.Should().BeTrue();

        var gone = PlaybackLifecycleChoreographer.EvaluatePauseOpacity(8.60f, out var activeGone);
        gone.Should().Be(0.0f);
        activeGone.Should().BeFalse("after 8.45s paused, RAF must settle to idle for <3% CPU");

        // Full choreographer stateful integration
        using var choreographer = new PlaybackLifecycleChoreographer();
        var demoDoc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        var t0 = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        var pausedSnap = CreateSnapshot(demoDoc, TimeSpan.FromSeconds(1.5), MediaPlaybackState.Paused, pausedSinceUtc: t0);
        var at1s = choreographer.Advance(OverlayFrameAdapter.FromPlaybackSnapshot(pausedSnap, 800, 300, 1f, 1.0, 1f / 60f, utcNow: t0.AddSeconds(1.0)));
        at1s.ContentOpacity.Should().BeApproximately(0.62f, 0.02f);
        at1s.IsAnimating.Should().BeTrue();

        var at9s = choreographer.Advance(OverlayFrameAdapter.FromPlaybackSnapshot(pausedSnap, 800, 300, 1f, 9.0, 1f / 60f, utcNow: t0.AddSeconds(9.0)));
        at9s.ContentOpacity.Should().Be(0.0f);
        at9s.ShouldRenderLyrics.Should().BeFalse();
        at9s.IsAnimating.Should().BeFalse();
    }

    [Fact]
    public void TimingOffsetToast_FormatsCanonicalUnicodeSignsAndFadesOutAfter1Point2Seconds()
    {
        PlaybackLifecycleChoreographer.FormatOffsetToastLabel(TimeSpan.FromSeconds(-0.5))
            .Should().Be("Offset: \u22120.5s (Earlier)");
        PlaybackLifecycleChoreographer.FormatOffsetToastLabel(TimeSpan.FromSeconds(1.0))
            .Should().Be("Offset: +1.0s (Later)");
        PlaybackLifecycleChoreographer.FormatOffsetToastLabel(TimeSpan.Zero)
            .Should().Be("Offset: 0.0s (Reset)");

        using var choreographer = new PlaybackLifecycleChoreographer();
        var demoDoc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        var nudgeTime = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        var snap = CreateSnapshot(
            demoDoc,
            TimeSpan.FromSeconds(2.0),
            MediaPlaybackState.Playing,
            offset: TimeSpan.FromSeconds(-0.5),
            lastNudgeUtc: nudgeTime);

        var activeToast = choreographer.Advance(OverlayFrameAdapter.FromPlaybackSnapshot(snap, 800, 300, 1f, 2.0, 1f / 60f, utcNow: nudgeTime.AddSeconds(0.5)));
        activeToast.ShouldRenderToast.Should().BeTrue();
        activeToast.OffsetToastOpacity.Should().BeApproximately(1.0f, 0.01f);
        activeToast.OffsetToastText.Should().Be("Offset: \u22120.5s (Earlier)");

        var fadingToast = choreographer.Advance(OverlayFrameAdapter.FromPlaybackSnapshot(snap, 800, 300, 1f, 2.5, 1f / 60f, utcNow: nudgeTime.AddSeconds(1.29)));
        fadingToast.OffsetToastOpacity.Should().BeInRange(0.05f, 0.95f);

        var expiredToast = choreographer.Advance(OverlayFrameAdapter.FromPlaybackSnapshot(snap, 800, 300, 1f, 3.5, 1f / 60f, utcNow: nudgeTime.AddSeconds(1.50)));
        expiredToast.ShouldRenderToast.Should().BeFalse();
        expiredToast.OffsetToastOpacity.Should().Be(0f);
    }

    [Fact]
    public void InstrumentalGap_ShowsDotsDuringSilencePreWakesAt450msAndHonorsHideSetting()
    {
        using var choreographer = new PlaybackLifecycleChoreographer();
        var track = new TrackIdentity("Gap Song", "Artist", "Album", "gap song", "artist", TimeSpan.FromSeconds(30), "gap");
        var line0 = new LyricLine(0, "First line", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), [new LyricWord("First", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), true)]);
        var line1 = new LyricLine(1, "Second line", TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(15), [new LyricWord("Second", TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(15), true)]);
        var doc = new LyricsDocument(track, LyricsSourceKind.LrcLib, "Test", TimingKind.WordNative, [line0, line1], TimeSpan.Zero, false);

        // Deep in instrumental gap (6.0s -> 6s before next line, well past 0.9s linger)
        var gapSnap = CreateSnapshot(doc, TimeSpan.FromSeconds(6.0), MediaPlaybackState.Playing);
        LifecycleFrameState state = default;
        for (var i = 0; i < 20; i++)
        {
            state = choreographer.Advance(OverlayFrameAdapter.FromPlaybackSnapshot(gapSnap, 800, 300, 1f, 6.0 + (i * 0.02), 0.02f));
        }

        state.ContentOpacity.Should().Be(0.0f);
        state.ShowInstrumentalIndicator.Should().BeTrue();
        state.InstrumentalOpacity.Should().BeGreaterThan(0.9f);

        // 300ms before line1 -> 450ms pre-wake must bring lyrics back and dismiss dots
        var preWakeSnap = CreateSnapshot(doc, TimeSpan.FromSeconds(11.7), MediaPlaybackState.Playing);
        for (var i = 0; i < 15; i++)
        {
            state = choreographer.Advance(OverlayFrameAdapter.FromPlaybackSnapshot(preWakeSnap, 800, 300, 1f, 11.7 + (i * 0.02), 0.02f));
        }

        state.ContentOpacity.Should().BeGreaterThan(0.8f);
        state.ShowInstrumentalIndicator.Should().BeFalse();

        // HideDuringInstrumentals = true suppresses indicator completely
        var hiddenDotsSnap = gapSnap with { Settings = AppSettings.Default with { HideDuringInstrumentals = true } };
        for (var i = 0; i < 20; i++)
        {
            state = choreographer.Advance(OverlayFrameAdapter.FromPlaybackSnapshot(hiddenDotsSnap, 800, 300, 1f, 7.0 + (i * 0.02), 0.02f));
        }

        state.ShowInstrumentalIndicator.Should().BeFalse();
    }

    [Fact]
    public void InstrumentalDotsAndOffsetToast_DrawNonEmptyPixelsCleanly()
    {
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var choreographer = new PlaybackLifecycleChoreographer();

        var demoDoc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        var now = DateTimeOffset.UtcNow;
        var snap = CreateSnapshot(demoDoc, TimeSpan.FromSeconds(1.5), MediaPlaybackState.Playing, offset: TimeSpan.FromSeconds(0.5), lastNudgeUtc: now);
        var packet = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 640, 260, 1f, 1.5, 1f / 60f, utcNow: now.AddSeconds(0.2));

        var lifecycle = new LifecycleFrameState(
            ContentOpacity: 1f,
            ShowInstrumentalIndicator: true,
            InstrumentalOpacity: 1f,
            InstrumentalPulsePhase: 0.25f,
            OffsetToastText: "Offset: +0.5s (Later)",
            OffsetToastOpacity: 1f,
            IsAnimating: true);

        using var surface = SKSurface.Create(new SKImageInfo(640, 260, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);

        var ctx = new StyleRenderContext(surface.Canvas, null, fonts, shaders, pool, legibility, packet, IsGpuBackend: false, ShadersEnabled: false);
        choreographer.DrawInstrumentalIndicator(in ctx, in lifecycle);
        choreographer.DrawOffsetToast(in ctx, in lifecycle);

        using var bmp = SKBitmap.FromImage(surface.Snapshot());
        CountOpaquePixels(bmp).Should().BeGreaterThan(200);
    }

    private static OverlayPlaybackSnapshot CreateSnapshot(
        LyricsDocument doc,
        TimeSpan position,
        MediaPlaybackState state,
        TimeSpan? offset = null,
        DateTimeOffset? pausedSinceUtc = null,
        DateTimeOffset? lastNudgeUtc = null,
        bool isDemo = false)
    {
        return new OverlayPlaybackSnapshot(
            Track: doc.Track,
            Document: doc,
            LyricsStatus: LyricsStatus.Synced,
            PlaybackState: state,
            PlayerKind: MediaPlayerKind.Spotify,
            SourceAppUserModelId: "Spotify.exe",
            RawPosition: position,
            EffectivePosition: position,
            ActiveSlice: ActiveLyricSlice.Resolve(doc, position),
            ActivePalette: AlbumAccentPalette.DemoNeonHorizon,
            TargetPalette: AlbumAccentPalette.DemoNeonHorizon,
            TimingOffset: new TimingOffsetState(doc.Track.CacheKey, offset ?? TimeSpan.Zero, false),
            Settings: AppSettings.Default,
            IsPlaying: state == MediaPlaybackState.Playing,
            IsDemoMode: isDemo,
            IsFullscreenHidden: false,
            PausedSinceUtc: pausedSinceUtc,
            LastOffsetNudgeUtc: lastNudgeUtc,
            TrackGeneration: 1,
            SeekGeneration: 0);
    }

    private static int CountOpaquePixels(SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                if (bitmap.GetPixel(x, y).Alpha > 24)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
