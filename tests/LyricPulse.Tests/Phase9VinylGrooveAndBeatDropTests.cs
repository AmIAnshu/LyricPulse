using System.Collections.Immutable;
using System.Security.Cryptography;
using AwesomeAssertions;
using LyricPulse.App.Tray;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Audio;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Media;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Core.Settings;
using LyricPulse.Core.Visuals;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Shaders;
using LyricPulse.Rendering.Styles;
using LyricPulse.UI.ViewModels;
using NSubstitute;
using SkiaSharp;
using Xunit;

namespace LyricPulse.Tests;

public sealed class Phase9VinylGrooveAndBeatDropTests
{
    private static AudioFrameMetrics CreateAudio(
        float bass = 0.72f,
        float beat = 0.78f)
    {
        var bands = new float[AudioFrameMetrics.BandCount];
        for (var i = 0; i < bands.Length; i++)
        {
            bands[i] = Math.Clamp(0.38f + (0.42f * MathF.Sin((i * 0.45f) + 0.1f)), 0.12f, 0.95f);
        }

        return new AudioFrameMetrics(
            BassEnergy: bass,
            MidEnergy: 0.56f,
            TrebleEnergy: 0.44f,
            OverallLevel: 0.62f,
            BeatPulse: beat,
            IsLiveAudioActive: true,
            SpectrumBands: ImmutableArray.Create(bands));
    }

    private static byte[] EncodeCover(
        int width,
        int height,
        SKEncodedImageFormat format,
        Func<int, int, SKColor> pixelAt)
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
        using var data = img.Encode(format, 92);
        return data.ToArray();
    }

    private static FrameRenderPacket BuildPacket(
        LyricsDocument doc,
        AppSettings settings,
        StageRenderSurfaceMode surfaceMode,
        TimeSpan position,
        AudioFrameMetrics? audio = null,
        AlbumArtPayload? albumArt = null,
        AlbumAccentPalette? palette = null,
        WordVisualMatch? moodVisual = null,
        double renderTime = 2.0)
    {
        var activePalette = palette ?? AlbumAccentPalette.DemoNeonHorizon;
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
            ActivePalette: activePalette,
            TargetPalette: activePalette,
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
            AlbumArt: albumArt,
            AudioMetrics: audio);

        return OverlayFrameAdapter.FromPlaybackSnapshot(
            snap,
            viewportWidthDip: 640f,
            viewportHeightDip: 360f,
            renderScaling: 1.0f,
            renderTimeSeconds: renderTime,
            deltaSeconds: 1f / 60f,
            surfaceMode: surfaceMode,
            bottomFadeToBlack: false);
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

    [Fact]
    public void SkiaAlbumColorExtractor_DecodeVinylAlbumTextures_ProducesCircularCoverWithTransparentCorners_AndBlurredAmbient()
    {
        var redCoverBytes = EncodeCover(240, 180, SKEncodedImageFormat.Png, (_, _) => new SKColor(232, 28, 42));
        var (circularCover, ambientBlur) = SkiaAlbumColorExtractor.DecodeVinylAlbumTextures(redCoverBytes, coverDimension: 160, ambientDimension: 40);

        circularCover.Should().NotBeNull();
        ambientBlur.Should().NotBeNull();
        using (circularCover!)
        using (ambientBlur!)
        {
            circularCover.Width.Should().Be(circularCover.Height);
            using var circleBmp = SKBitmap.FromImage(circularCover);

            // Outer box corners MUST be completely transparent so GPU rotation never reveals a spinning square!
            circleBmp.GetPixel(2, 2).Alpha.Should().Be(0);
            circleBmp.GetPixel(circleBmp.Width - 3, 2).Alpha.Should().Be(0);
            circleBmp.GetPixel(2, circleBmp.Height - 3).Alpha.Should().Be(0);
            circleBmp.GetPixel(circleBmp.Width - 3, circleBmp.Height - 3).Alpha.Should().Be(0);

            // Center spindle hole cutout is also transparent
            var mid = circleBmp.Width / 2;
            circleBmp.GetPixel(mid, mid).Alpha.Should().Be(0);

            // Mid-ring cover region contains opaque red cover art pixels
            var sample = circleBmp.GetPixel(mid, mid / 2);
            sample.Alpha.Should().BeGreaterThan(220);
            sample.Red.Should().BeGreaterThan((byte)(sample.Green + 80));
            sample.Red.Should().BeGreaterThan((byte)(sample.Blue + 80));

            // Ambient blur texture is 40x40 reddish blurred wash
            ambientBlur.Width.Should().Be(40);
            ambientBlur.Height.Should().Be(40);
            using var ambBmp = SKBitmap.FromImage(ambientBlur);
            var ambCenter = ambBmp.GetPixel(20, 20);
            ambCenter.Red.Should().BeGreaterThan((byte)(ambCenter.Green + 40));
        }
    }

    [Fact]
    public void VinylGrooveStyleRenderer_RedAlbumCover_SpreadsBlurredRedBackdropAndHarmoniousCyclingAcrossPlayerAndLyrics()
    {
        var demoDoc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var router = new LyricStyleRouter();

        var redCoverBytes = EncodeCover(280, 280, SKEncodedImageFormat.Png, (_, _) => new SKColor(232, 32, 48));
        var artPayload = new AlbumArtPayload("vinyl-red-album-v3", redCoverBytes);
        var extractor = new SkiaAlbumColorExtractor();
        var redPalette = extractor.Extract(artPayload);

        var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
        var settings = AppSettings.Default with
        {
            ActiveStyle = OverlayStyleId.VinylGroove,
            StageBackdropMode = StageBackdropMode.AmbientPartyMesh,
            EdgeGlowMode = EdgeGlowMode.NeonRush // Regression check: NeonRush must NOT wipe VinylGroove's blurred album room to pure black!
        };

        // Render Frame 1 (t = 1.0s)
        var pkt1 = BuildPacket(demoDoc, settings, StageRenderSurfaceMode.StageCanvas, TimeSpan.FromSeconds(25.7), CreateAudio(), artPayload, redPalette, renderTime: 1.0);
        router.AdvancePhysics(in pkt1);
        using var surf1 = SKSurface.Create(info);
        var ctx1 = new StyleRenderContext(surf1.Canvas, null, fonts, shaders, pool, legibility, pkt1, false, false);
        router.Render(in ctx1);
        using var bmp1 = SKBitmap.FromImage(surf1.Snapshot());

        // Background across the room is unmistakably blurred reddish hue (NOT pitch black or wrong hue)
        var bgTopRight = bmp1.GetPixel(560, 48);
        var bgBotLeft = bmp1.GetPixel(64, 310);
        bgTopRight.Red.Should().BeGreaterThan((byte)(bgTopRight.Green + 15));
        bgTopRight.Red.Should().BeGreaterThan((byte)(bgTopRight.Blue + 12));
        bgBotLeft.Red.Should().BeGreaterThan((byte)(bgBotLeft.Green + 15));

        var hash1 = Convert.ToHexString(SHA256.HashData(bmp1.GetPixelSpan()));

        // Render Frame 2 (t = 3.2s) -> harmonious color cycle + spin advance visibly
        var pkt2 = BuildPacket(demoDoc, settings, StageRenderSurfaceMode.StageCanvas, TimeSpan.FromSeconds(25.7), CreateAudio(), artPayload, redPalette, renderTime: 3.2);
        router.AdvancePhysics(in pkt2);
        using var surf2 = SKSurface.Create(info);
        var ctx2 = new StyleRenderContext(surf2.Canvas, null, fonts, shaders, pool, legibility, pkt2, false, false);
        router.Render(in ctx2);
        using var bmp2 = SKBitmap.FromImage(surf2.Snapshot());

        Convert.ToHexString(SHA256.HashData(bmp2.GetPixelSpan())).Should().NotBe(hash1);
        var bgTopRight2 = bmp2.GetPixel(560, 48);
        bgTopRight2.Red.Should().BeGreaterThan((byte)(bgTopRight2.Green + 15), "cycling harmonious colors keeps the blurred background unmistakably red");
    }

    [Fact]
    public async Task VinylGrooveStyleRenderer_DecodesJpegCoverArtIntoCenterAndRenders3DText_WithTransparentDesktopCorners()
    {
        using var matcher = new StarterLexiconVisualMatcher();
        await matcher.InitializeAsync();
        var demoDoc = matcher.AnnotateDocument(
            new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument(),
            VisualPreference.Balanced);
        var moodVisual = matcher.InferTrackMoodVisual(demoDoc.Track, demoDoc, VisualPreference.Balanced);

        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var router = new LyricStyleRouter();

        // Non-square 360x240 JPEG exercises SKCodec scaled center-crop decode + circular alpha cutout
        var jpegCoverBytes = EncodeCover(360, 240, SKEncodedImageFormat.Jpeg, (x, y) => x < 180 ? new SKColor(238, 42, 92) : new SKColor(22, 186, 248));
        var artPayload = new AlbumArtPayload("vinyl-jpeg-cover-v3", jpegCoverBytes);
        var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
        var settings = AppSettings.Default with { ActiveStyle = OverlayStyleId.VinylGroove, StageBackdropMode = StageBackdropMode.AmbientPartyMesh };

        // 1. Live JPEG album cover in center of platter + 3D perspective text on right
        FrameRenderPacket artPkt = null!;
        for (var i = 0; i < 8; i++)
        {
            artPkt = BuildPacket(demoDoc, settings, StageRenderSurfaceMode.StageCanvas, TimeSpan.FromSeconds(25.7), CreateAudio(), artPayload, null, moodVisual, renderTime: 1.0 + (i * 0.02));
            router.AdvancePhysics(in artPkt);
        }

        using var artSurf = SKSurface.Create(info);
        var artCtx = new StyleRenderContext(artSurf.Canvas, null, fonts, shaders, pool, legibility, artPkt, false, false);
        router.Render(in artCtx);
        using var artBmp = SKBitmap.FromImage(artSurf.Snapshot());
        CountLitPixels(artBmp).Should().BeGreaterThan(700);
        var artHash = Convert.ToHexString(SHA256.HashData(artBmp.GetPixelSpan()));

        // Verify platter center region (0.30f) displays bright decoded album cover pixels with a punched center spindle hole
        var platterCenterX = (int)(640 * 0.30f);
        var platterCenterY = (int)(360 * 0.50f);
        var sampleAboveSpindle = artBmp.GetPixel(platterCenterX, platterCenterY - 26);
        (sampleAboveSpindle.Red + sampleAboveSpindle.Green + sampleAboveSpindle.Blue).Should().BeGreaterThan(90);
        var sampleSpindleHole = artBmp.GetPixel(platterCenterX, platterCenterY);
        Math.Max(sampleSpindleHole.Red, Math.Max(sampleSpindleHole.Green, sampleSpindleHole.Blue)).Should().BeLessThan(35);

        // 2. Without AlbumArt -> circular procedural center label + TrackMoodVisual emblem
        var fallbackPkt = BuildPacket(demoDoc, settings, StageRenderSurfaceMode.StageCanvas, TimeSpan.FromSeconds(25.7), CreateAudio(), albumArt: null, null, moodVisual, renderTime: 1.25);
        router.AdvancePhysics(in fallbackPkt);
        using var fbSurf = SKSurface.Create(info);
        var fbCtx = new StyleRenderContext(fbSurf.Canvas, null, fonts, shaders, pool, legibility, fallbackPkt, false, false);
        router.Render(in fbCtx);
        using var fbBmp = SKBitmap.FromImage(fbSurf.Snapshot());
        CountLitPixels(fbBmp).Should().BeGreaterThan(700);
        Convert.ToHexString(SHA256.HashData(fbBmp.GetPixelSpan())).Should().NotBe(artHash);

        // 3. DesktopOverlay preserves all 4 viewport corners alpha == 0
        var desktopSettings = settings with { EdgeGlowMode = EdgeGlowMode.Off, AudioVisualizerMode = AudioVisualizerMode.None };
        var deskPkt = BuildPacket(demoDoc, desktopSettings, StageRenderSurfaceMode.DesktopOverlay, TimeSpan.FromSeconds(25.7), CreateAudio(), artPayload, null, moodVisual, renderTime: 2.0);
        router.AdvancePhysics(in deskPkt);
        using var deskSurf = SKSurface.Create(info);
        deskSurf.Canvas.Clear(SKColors.Transparent);
        var deskCtx = new StyleRenderContext(deskSurf.Canvas, null, fonts, shaders, pool, legibility, deskPkt, false, false);
        router.Render(in deskCtx);
        using var deskBmp = SKBitmap.FromImage(deskSurf.Snapshot());
        CountLitPixels(deskBmp).Should().BeGreaterThan(300);
        deskBmp.GetPixel(0, 0).Alpha.Should().Be(0);
        deskBmp.GetPixel(639, 0).Alpha.Should().Be(0);
        deskBmp.GetPixel(0, 359).Alpha.Should().Be(0);
        deskBmp.GetPixel(639, 359).Alpha.Should().Be(0);

        // 4. ReducedMotion stays deterministic and renders cleanly
        var rmPkt = BuildPacket(demoDoc, settings with { ReducedMotion = true }, StageRenderSurfaceMode.StageCanvas, TimeSpan.FromSeconds(25.7), CreateAudio(), artPayload, null, moodVisual, renderTime: 3.0);
        router.AdvancePhysics(in rmPkt);
        using var rmSurf = SKSurface.Create(info);
        var rmCtx = new StyleRenderContext(rmSurf.Canvas, null, fonts, shaders, pool, legibility, rmPkt, false, false);
        router.Render(in rmCtx);
        using var rmBmp = SKBitmap.FromImage(rmSurf.Snapshot());
        CountLitPixels(rmBmp).Should().BeGreaterThan(600);
    }

    [Fact]
    public async Task VinylGrooveCatalogWiring_AndSettingsBackCompat_WorkEndToEnd()
    {
        StyleCatalog.All.Should().HaveCount(11);
        StyleCatalog.GetMetadata(OverlayStyleId.VinylGroove).DisplayName.Should().Be("Vinyl Groove");
        TrayMenuState.GetStyleDisplayName(OverlayStyleId.VinylGroove).Should().Be("Vinyl Groove");
        DemoSongProvider.GetGalleryPreviewPhaseOffset(OverlayStyleId.VinylGroove).Should().Be(TimeSpan.FromSeconds(25.7));

        var tempDir = Path.Combine(Path.GetTempPath(), $"lp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var settingsPath = Path.Combine(tempDir, "settings.json");
            // Verify legacy JSON containing removed beatDrop* properties still loads cleanly without throwing
            await File.WriteAllTextAsync(settingsPath, """{"activeStyle":10,"beatDropEffectEnabled":true,"beatDropIntensity":1.4}""");

            using var store = new JsonSettingsStore(settingsPath);
            var reloaded = await store.LoadAsync();
            reloaded.ActiveStyle.Should().Be(OverlayStyleId.VinylGroove);

            var media = Substitute.For<IMediaSessionProvider>();
            var lyrics = Substitute.For<ILyricsOrchestrator>();
            var matcher = new GloveSemanticVisualMatcher();
            var offsetStore = Substitute.For<ITrackOffsetStore>();
            offsetStore.TryGetOffsetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((TimeSpan?)null);
            var timing = new TimingOffsetService(offsetStore);
            var demo = new DemoSongProvider(new LyricsParser(), new TitleNormalizer());

            await using var controller = new OverlayRuntimeController(media, lyrics, matcher, store, timing, demo);
            await controller.StartAsync();
            await controller.SetDemoModeAsync(true);

            var snap = controller.SampleFrame();
            var vm = new StageViewModel(controller);
            vm.ApplySnapshot(snap);
            vm.ActiveStyle.Should().Be(OverlayStyleId.VinylGroove);
            vm.GalleryCountCaption.Should().StartWith("11 styles");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void KineticWallStyleRenderer_Project3DPoint_And_Render_KeepCanvasStrictlyAffineAndNonDegenerate()
    {
        var (frontPt, frontScale) = KineticWallStyleRenderer.Project3DPoint(
            420f, 180f, -32f,
            KineticWallStyleRenderer.RestingPitchDegrees, -4f, 1f,
            KineticWallStyleRenderer.PerspectiveFocalLength, 320f, 180f);
        var (backPt, backScale) = KineticWallStyleRenderer.Project3DPoint(
            420f, 180f, 85f,
            KineticWallStyleRenderer.RestingPitchDegrees, -4f, 1f,
            KineticWallStyleRenderer.PerspectiveFocalLength, 320f, 180f);

        float.IsFinite(frontPt.X).Should().BeTrue();
        float.IsFinite(frontPt.Y).Should().BeTrue();
        float.IsFinite(backPt.X).Should().BeTrue();
        float.IsFinite(backPt.Y).Should().BeTrue();
        frontScale.Should().BeGreaterThan(backScale);

        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var router = new LyricStyleRouter();

        var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
        var settings = AppSettings.Default with { ActiveStyle = OverlayStyleId.KineticWall, EdgeGlowMode = EdgeGlowMode.Off, AudioVisualizerMode = AudioVisualizerMode.None };

        var stagePkt = BuildPacket(doc, settings, StageRenderSurfaceMode.StageCanvas, TimeSpan.FromSeconds(28.6), CreateAudio(), albumArt: null, null, null, renderTime: 1.5);
        router.AdvancePhysics(in stagePkt);
        using var stageSurf = SKSurface.Create(info);
        var stageCtx = new StyleRenderContext(stageSurf.Canvas, null, fonts, shaders, pool, legibility, stagePkt, false, false);
        router.Render(in stageCtx);
        stageSurf.Canvas.TotalMatrix.Persp0.Should().Be(0f);
        stageSurf.Canvas.TotalMatrix.Persp1.Should().Be(0f);
        using var stageBmp = SKBitmap.FromImage(stageSurf.Snapshot());
        CountLitPixels(stageBmp).Should().BeGreaterThan(500);

        var deskPkt = BuildPacket(doc, settings, StageRenderSurfaceMode.DesktopOverlay, TimeSpan.FromSeconds(28.6), CreateAudio(), albumArt: null, null, null, renderTime: 2.0);
        router.AdvancePhysics(in deskPkt);
        using var deskSurf = SKSurface.Create(info);
        deskSurf.Canvas.Clear(SKColors.Transparent);
        var deskCtx = new StyleRenderContext(deskSurf.Canvas, null, fonts, shaders, pool, legibility, deskPkt, false, false);
        router.Render(in deskCtx);
        deskSurf.Canvas.TotalMatrix.Persp0.Should().Be(0f);
        deskSurf.Canvas.TotalMatrix.Persp1.Should().Be(0f);
        using var deskBmp = SKBitmap.FromImage(deskSurf.Snapshot());
        deskBmp.GetPixel(0, 0).Alpha.Should().Be(0);
        deskBmp.GetPixel(639, 0).Alpha.Should().Be(0);
        deskBmp.GetPixel(0, 359).Alpha.Should().Be(0);
        deskBmp.GetPixel(639, 359).Alpha.Should().Be(0);
    }

    [Fact]
    public async Task OverlayRuntimeController_PublishesLyricsImmediatelyWithoutWaitingForSlowAlbumArt()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lp-fast-lyrics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var normalizer = new TitleNormalizer();
            var demo = new DemoSongProvider(new LyricsParser(), normalizer);
            var demoDoc = demo.GetDemoDocument();

            var track = normalizer.CreateIdentity("Fast Song", "Quick Artist", "Album", TimeSpan.FromMinutes(3));
            var liveDoc = demoDoc with { Track = track };

            var media = new FakeSlowArtMediaProvider();
            var lyrics = Substitute.For<ILyricsOrchestrator>();
            lyrics.GetLyricsAsync(Arg.Any<TrackIdentity>(), Arg.Any<CancellationToken>())
                .Returns(new LyricsResult(LyricsStatus.Synced, liveDoc));

            var matcher = new StarterLexiconVisualMatcher();
            await matcher.InitializeAsync();
            using var store = new JsonSettingsStore(Path.Combine(tempDir, "settings.json"));
            var offsetStore = Substitute.For<ITrackOffsetStore>();
            offsetStore.TryGetOffsetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((TimeSpan?)null);
            var timing = new TimingOffsetService(offsetStore);

            await using var controller = new OverlayRuntimeController(media, lyrics, matcher, store, timing, demo);
            await controller.StartAsync();

            var snapshot = new MediaSessionSnapshot(
                SourceAppUserModelId: "Spotify.exe",
                PlayerKind: MediaPlayerKind.Spotify,
                ItemKind: MediaItemKind.Music,
                PlaybackState: MediaPlaybackState.Playing,
                Track: track,
                ReportedPosition: TimeSpan.FromSeconds(1),
                StartTime: TimeSpan.Zero,
                EndTime: TimeSpan.FromMinutes(3),
                PlaybackRate: 1.0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                AlbumArtHash: null);

            media.RaiseSession(snapshot);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            OverlayPlaybackSnapshot frame = controller.SampleFrame();
            while (sw.ElapsedMilliseconds < 1500 &&
                   (frame.LyricsStatus != LyricsStatus.Synced || frame.Document is null))
            {
                await Task.Delay(20);
                frame = controller.SampleFrame();
            }

            frame.LyricsStatus.Should().Be(LyricsStatus.Synced, "lyrics must publish immediately without waiting on blocked GSMTC album art");
            frame.Document.Should().NotBeNull();
            media.ArtTaskCompleted.Should().BeFalse();

            // Unblock art afterward
            var png = EncodeCover(32, 32, SKEncodedImageFormat.Png, (_, _) => new SKColor(220, 40, 60));
            media.CompleteArt(new AlbumArtPayload("late-art-hash", png));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void MonotonicPlaybackClock_DoesNotJumpAheadOnTrackSwitchWithStaleLastUpdatedTime()
    {
        const long freq = 10_000_000;
        var nowTicks = 100 * freq;
        var staleUtc = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        var nowUtc = staleUtc;
        var clock = new MonotonicPlaybackClock(() => nowTicks, freq, () => nowUtc);

        // Track A starts at 00:15
        clock.UpdateFromTimeline("track-a", TimeSpan.FromSeconds(15), TimeSpan.Zero, TimeSpan.FromMinutes(3), 1.0, true, staleUtc);

        // Advance wall clock 12s while user listens, then skip to Track B where GSMTC still reports ReportedPosition=0s with staleUtc (12s old)
        nowTicks += 12 * freq;
        nowUtc = staleUtc.AddSeconds(12);

        clock.UpdateFromTimeline("track-b", TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMinutes(3), 1.0, true, staleUtc);

        // New track MUST stay near 0s instead of leaping +12s ahead!
        clock.GetInterpolatedPosition().Should().BeLessThan(TimeSpan.FromSeconds(1.2));
    }

    private sealed class FakeSlowArtMediaProvider : IMediaSessionProvider
    {
        private readonly TaskCompletionSource<AlbumArtPayload?> _artTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MediaSessionSnapshot? CurrentSession { get; private set; }
        public bool ArtTaskCompleted => _artTcs.Task.IsCompleted;

        public event EventHandler<MediaSessionSnapshot?>? SessionChanged;
        public event EventHandler<TrackIdentity?>? TrackChanged;
        public event EventHandler<TimeSpan>? SeekDetected;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public TimeSpan GetInterpolatedPosition() => CurrentSession?.ReportedPosition ?? TimeSpan.Zero;
        public Task<AlbumArtPayload?> GetCurrentAlbumArtAsync(CancellationToken cancellationToken = default) =>
            _artTcs.Task.WaitAsync(cancellationToken);
        public void UpdateSourcePolicy(MediaSourcePolicy policy, ImmutableArray<string> blockedAppIds, bool ignoreBrowserVideoSessions = true) { }

        public void RaiseSession(MediaSessionSnapshot snapshot)
        {
            CurrentSession = snapshot;
            SessionChanged?.Invoke(this, snapshot);
            TrackChanged?.Invoke(this, snapshot.Track);
        }

        public void CompleteArt(AlbumArtPayload? payload)
        {
            _ = SeekDetected; // silence unused event warning
            _artTcs.TrySetResult(payload);
        }

        public ValueTask DisposeAsync()
        {
            _artTcs.TrySetCanceled();
            return ValueTask.CompletedTask;
        }
    }
}
