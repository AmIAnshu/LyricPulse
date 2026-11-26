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

public sealed class FisheyeVisualOverhaulTests
{
    private static OverlayPlaybackSnapshot CreateSnapshot(
        LyricsDocument doc,
        TimeSpan position,
        bool reducedMotion = false,
        AudioFrameMetrics? audio = null,
        WordVisualMatch? moodVisual = null)
    {
        var settings = AppSettings.Default with
        {
            ActiveStyle = OverlayStyleId.FisheyeVisual,
            StageBackdropMode = StageBackdropMode.AmbientPartyMesh,
            PartyEnergyLevel = 1.35f,
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
            TrackMoodVisual: moodVisual,
            AudioMetrics: audio);
    }

    [Fact]
    public void ReactiveVisualIconRenderer_DrawsEightNewV22ConceptsOntoExplicitOffscreenCanvas()
    {
        using var renderer = new ReactiveVisualIconRenderer();
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();

        var newConcepts = new[] { "paper", "butterfly", "planet", "car", "ghost", "glass", "key", "sword" };
        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        var pkt = OverlayFrameAdapter.FromPlaybackSnapshot(
            CreateSnapshot(doc, TimeSpan.FromSeconds(3.0)),
            640f, 360f, 1.0f, 3.0, 1f / 60f, surfaceMode: StageRenderSurfaceMode.StageCanvas);

        var info = new SKImageInfo(96, 96, SKColorType.Bgra8888, SKAlphaType.Premul);
        foreach (var concept in newConcepts)
        {
            using var mainSurf = SKSurface.Create(info);
            using var offSurf = SKSurface.Create(info);
            mainSurf.Canvas.Clear(SKColors.Transparent);
            offSurf.Canvas.Clear(SKColors.Transparent);

            var ctx = new StyleRenderContext(mainSurf.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            var visual = new WordVisualMatch(VisualKind.Emoji, "✨", "fluent", concept, 1.0f, true, "✨");

            renderer.DrawVisual(offSurf.Canvas, in ctx, visual, 48f, 48f, 44f, 1.0f, isActive: true);

            using var offBmp = SKBitmap.FromImage(offSurf.Snapshot());
            using var mainBmp = SKBitmap.FromImage(mainSurf.Snapshot());
            CountLitPixels(offBmp).Should().BeGreaterThan(25, $"new concept '{concept}' must draw visibly");
            CountLitPixels(mainBmp).Should().Be(0, $"explicit offscreen DrawVisual must not leak onto context.Canvas");
        }
    }

    [Fact]
    public async Task FisheyeVisualOverhaul_RendersOpticalStage_ChromaticBeatSplit_TransparentDesktopCorners_AndReducedMotion()
    {
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var renderer = new FisheyeVisualStyleRenderer { AllowCpuShadersForTesting = true };

        using var matcher = new GloveSemanticVisualMatcher();
        await matcher.InitializeAsync();
        var rawDoc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        var doc = matcher.AnnotateDocument(rawDoc, VisualPreference.Balanced);
        var moodVisual = matcher.InferTrackMoodVisual(doc.Track, doc, VisualPreference.Balanced);

        var bands = new float[AudioFrameMetrics.BandCount];
        Array.Fill(bands, 0.72f);
        var audio = new AudioFrameMetrics(0.85f, 0.62f, 0.78f, 0.74f, 0.92f, true, ImmutableArray.Create(bands));
        var lifecycle = new LifecycleFrameState(1.0f, false, 0f, 0f, null, 0f, false);

        // 1. StageCanvas renders 5-word scatter, optical lens bezel, chromatic split, and beat-reactive visuals
        var stageInfo = new SKImageInfo(960, 540, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var stageSurf = SKSurface.Create(stageInfo))
        {
            stageSurf.Canvas.Clear(SKColors.Transparent);
            var pkt1 = OverlayFrameAdapter.FromPlaybackSnapshot(
                CreateSnapshot(doc, TimeSpan.FromSeconds(2.9), audio: audio, moodVisual: moodVisual),
                960f, 540f, 1.0f, 2.9, 1f / 60f, surfaceMode: StageRenderSurfaceMode.StageCanvas);
            renderer.Advance(in pkt1, in lifecycle);

            var pkt2 = OverlayFrameAdapter.FromPlaybackSnapshot(
                CreateSnapshot(doc, TimeSpan.FromSeconds(3.25), audio: audio, moodVisual: moodVisual),
                960f, 540f, 1.0f, 3.25, 1f / 60f, surfaceMode: StageRenderSurfaceMode.StageCanvas);
            renderer.Advance(in pkt2, in lifecycle).Should().BeTrue();

            var ctx = new StyleRenderContext(stageSurf.Canvas, null, fonts, shaders, pool, legibility, pkt2, false, false);
            renderer.Render(in ctx, in lifecycle);

            using var bmp = SKBitmap.FromImage(stageSurf.Snapshot());
            CountLitPixels(bmp).Should().BeGreaterThan(300);
        }

        // 2. DesktopOverlay skips optical perimeter ring and keeps all 4 screen corners Alpha == 0
        var deskInfo = new SKImageInfo(640, 320, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var deskSurf = SKSurface.Create(deskInfo))
        {
            deskSurf.Canvas.Clear(SKColors.Transparent);
            renderer.ResetState();
            var deskPkt = OverlayFrameAdapter.FromPlaybackSnapshot(
                CreateSnapshot(doc, TimeSpan.FromSeconds(3.25), audio: audio, moodVisual: moodVisual),
                640f, 320f, 1.0f, 3.25, 1f / 60f, surfaceMode: StageRenderSurfaceMode.DesktopOverlay);
            renderer.Advance(in deskPkt, in lifecycle);

            var ctx = new StyleRenderContext(deskSurf.Canvas, null, fonts, shaders, pool, legibility, deskPkt, false, false);
            renderer.Render(in ctx, in lifecycle);

            using var deskBmp = SKBitmap.FromImage(deskSurf.Snapshot());
            CountLitPixels(deskBmp).Should().BeGreaterThan(50);
            deskBmp.GetPixel(0, 0).Alpha.Should().Be(0);
            deskBmp.GetPixel(639, 0).Alpha.Should().Be(0);
            deskBmp.GetPixel(0, 319).Alpha.Should().Be(0);
            deskBmp.GetPixel(639, 319).Alpha.Should().Be(0);
        }

        // 3. ReducedMotion renders cleanly without shader warp or chromatic split
        using (var rmSurf = SKSurface.Create(deskInfo))
        {
            rmSurf.Canvas.Clear(SKColors.Transparent);
            renderer.ResetState();
            var rmPkt = OverlayFrameAdapter.FromPlaybackSnapshot(
                CreateSnapshot(doc, TimeSpan.FromSeconds(3.25), reducedMotion: true, audio: audio, moodVisual: moodVisual),
                640f, 320f, 1.0f, 3.25, 1f / 60f, surfaceMode: StageRenderSurfaceMode.StageCanvas);
            renderer.Advance(in rmPkt, in lifecycle);

            var ctx = new StyleRenderContext(rmSurf.Canvas, null, fonts, shaders, pool, legibility, rmPkt, false, false);
            renderer.Render(in ctx, in lifecycle);

            using var rmBmp = SKBitmap.FromImage(rmSurf.Snapshot());
            CountLitPixels(rmBmp).Should().BeGreaterThan(40);
        }

        renderer.Dispose();
        renderer.Dispose(); // idempotent
    }

    private static int CountLitPixels(SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
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
