using System.Collections.Immutable;
using System.Security.Cryptography;
using AwesomeAssertions;
using LyricPulse.App.Platform;
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

        // Verify medium GlassOS album cover card center displays bright decoded album cover pixels (no vinyl spindle hole)
        var cardRect = VinylGrooveStyleRenderer.ResolveCoverCardBounds(640f, 360f, StageRenderSurfaceMode.StageCanvas);
        var sampleCardCenter = artBmp.GetPixel((int)cardRect.MidX, (int)cardRect.MidY);
        sampleCardCenter.Alpha.Should().BeGreaterThan(220);
        (sampleCardCenter.Red + sampleCardCenter.Green + sampleCardCenter.Blue).Should().BeGreaterThan(90);

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
        StyleCatalog.GetMetadata(OverlayStyleId.VinylGroove).DisplayName.Should().Be("Glass Stage");
        TrayMenuState.GetStyleDisplayName(OverlayStyleId.VinylGroove).Should().Be("Glass Stage");
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

    [Fact]
    public void SkiaAlbumColorExtractor_DecodeGlassStageTextures_ProducesHighResCoverAndSoftBackdrop()
    {
        var redCoverBytes = EncodeCover(240, 180, SKEncodedImageFormat.Png, (_, _) => new SKColor(228, 36, 58));
        var (coverArt, lightBlur) = SkiaAlbumColorExtractor.DecodeGlassStageTextures(
            redCoverBytes,
            coverDimension: 200,
            backdropDimension: 180);

        coverArt.Should().NotBeNull();
        lightBlur.Should().NotBeNull();
        using (coverArt!)
        using (lightBlur!)
        {
            coverArt.Width.Should().Be(coverArt.Height);
            lightBlur.Width.Should().Be(180);
            lightBlur.Height.Should().Be(180);

            using var coverBmp = SKBitmap.FromImage(coverArt);
            var center = coverBmp.GetPixel(coverBmp.Width / 2, coverBmp.Height / 2);
            center.Alpha.Should().Be(255);
            center.Red.Should().BeGreaterThan((byte)(center.Green + 80));

            using var backdropBmp = SKBitmap.FromImage(lightBlur);
            var backPx = backdropBmp.GetPixel(90, 90);
            backPx.Red.Should().BeGreaterThan((byte)(backPx.Green + 60));
        }
    }

    [Fact]
    public async Task OverlayRuntimeController_TransportControls_ToggleDemoPauseAndSkipLines_AndDelegateLiveGsmtc()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lp-transport-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var normalizer = new TitleNormalizer();
            var demo = new DemoSongProvider(new LyricsParser(), normalizer);
            var demoDoc = demo.GetDemoDocument();

            var media = Substitute.For<IMediaSessionProvider>();
            media.TryTogglePlayPauseAsync(Arg.Any<CancellationToken>()).Returns(true);
            media.TrySkipNextAsync(Arg.Any<CancellationToken>()).Returns(true);
            media.TrySkipPreviousAsync(Arg.Any<CancellationToken>()).Returns(true);

            var lyrics = Substitute.For<ILyricsOrchestrator>();
            var matcher = new StarterLexiconVisualMatcher();
            await matcher.InitializeAsync();
            using var store = new JsonSettingsStore(Path.Combine(tempDir, "settings.json"));
            var offsetStore = Substitute.For<ITrackOffsetStore>();
            offsetStore.TryGetOffsetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((TimeSpan?)null);
            var timing = new TimingOffsetService(offsetStore);

            await using var controller = new OverlayRuntimeController(media, lyrics, matcher, store, timing, demo);
            await controller.StartAsync();

            // 1. Demo Mode pause/resume & skip next/previous
            await controller.SetDemoModeAsync(true);
            var playingSnap = controller.SampleFrame();
            playingSnap.IsPlaying.Should().BeTrue();
            playingSnap.PlaybackState.Should().Be(MediaPlaybackState.Playing);

            (await controller.TogglePlayPauseAsync()).Should().BeTrue();
            var pausedSnap = controller.SampleFrame();
            pausedSnap.IsPlaying.Should().BeFalse();
            pausedSnap.PlaybackState.Should().Be(MediaPlaybackState.Paused);

            var seekBefore = pausedSnap.SeekGeneration;
            (await controller.SkipNextAsync()).Should().BeTrue();
            var skippedSnap = controller.SampleFrame();
            skippedSnap.SeekGeneration.Should().Be(seekBefore + 1);
            skippedSnap.ActiveLineIndex.Should().BeGreaterThanOrEqualTo(0);

            (await controller.SkipPreviousAsync()).Should().BeTrue();
            controller.SampleFrame().SeekGeneration.Should().Be(seekBefore + 2);

            (await controller.TogglePlayPauseAsync()).Should().BeTrue();
            controller.SampleFrame().IsPlaying.Should().BeTrue();

            // 2. Live session delegation -> invokes GSMTC transport commands
            await controller.SetDemoModeAsync(false);
            var liveTrack = normalizer.CreateIdentity("Midnight Drive", "Synth Artist", "Night Album", TimeSpan.FromMinutes(3));
            var liveSession = new MediaSessionSnapshot(
                SourceAppUserModelId: "Spotify.exe",
                PlayerKind: MediaPlayerKind.Spotify,
                ItemKind: MediaItemKind.Music,
                PlaybackState: MediaPlaybackState.Playing,
                Track: liveTrack,
                ReportedPosition: TimeSpan.FromSeconds(5),
                StartTime: TimeSpan.Zero,
                EndTime: TimeSpan.FromMinutes(3),
                PlaybackRate: 1.0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                AlbumArtHash: null);

            lyrics.GetLyricsAsync(Arg.Any<TrackIdentity>(), Arg.Any<CancellationToken>())
                .Returns(new LyricsResult(LyricsStatus.Synced, demoDoc with { Track = liveTrack }));
            media.SessionChanged += Raise.Event<EventHandler<MediaSessionSnapshot?>>(media, liveSession);
            await Task.Delay(50);

            (await controller.TogglePlayPauseAsync()).Should().BeTrue();
            (await controller.SkipNextAsync()).Should().BeTrue();
            (await controller.SkipPreviousAsync()).Should().BeTrue();

            await media.Received(1).TryTogglePlayPauseAsync(Arg.Any<CancellationToken>());
            await media.Received(1).TrySkipNextAsync(Arg.Any<CancellationToken>());
            await media.Received(1).TrySkipPreviousAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void EdgeGlowRenderer_LumnPerimeterMode_SupportsTwoColorOneColorAndSystemWideDesktopOverlay()
    {
        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var edgeGlow = new EdgeGlowRenderer();

        var settings = AppSettings.Default with
        {
            EdgeGlowMode = EdgeGlowMode.NeonRush,
            EdgeGlowDesktopEnabled = true,
            EdgeGlowTwoColorGradient = true,
            EdgeGlowOverrideAlbumColors = true,
            EdgeGlowPrimaryColor = new RgbColor(0x00, 0xD2, 0xFF),
            EdgeGlowSecondaryColor = new RgbColor(0xFF, 0x2A, 0x7A),
            EdgeGlowIntensity = 1.20f,
            EdgeGlowThickness = 1.50f,
            EdgeGlowSpill = 1.25f
        };

        var snap = new OverlayPlaybackSnapshot(
            Track: doc.Track,
            Document: doc,
            LyricsStatus: LyricsStatus.Synced,
            PlaybackState: MediaPlaybackState.Playing,
            PlayerKind: MediaPlayerKind.Unknown,
            SourceAppUserModelId: "Demo",
            RawPosition: TimeSpan.FromSeconds(5),
            EffectivePosition: TimeSpan.FromSeconds(5),
            ActiveSlice: ActiveLyricSlice.Resolve(doc, TimeSpan.FromSeconds(5)),
            ActivePalette: AlbumAccentPalette.DemoNeonHorizon,
            TargetPalette: AlbumAccentPalette.DemoNeonHorizon,
            TimingOffset: new TimingOffsetState(null, TimeSpan.Zero, false),
            Settings: settings,
            IsPlaying: true,
            IsDemoMode: true,
            IsFullscreenHidden: false,
            PausedSinceUtc: null,
            LastOffsetNudgeUtc: null,
            TrackGeneration: 1,
            SeekGeneration: 0,
            AudioMetrics: CreateAudio());

        var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);

        // 1. Default DesktopOverlay without AllowDesktopEdgeGlow keeps screen perimeter clean/transparent
        var gatedPkt = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 640f, 360f, 1.0f, 1.5, 1f / 60f, surfaceMode: StageRenderSurfaceMode.DesktopOverlay, allowDesktopEdgeGlow: false);
        using (var s0 = SKSurface.Create(info))
        {
            s0.Canvas.Clear(SKColors.Transparent);
            var ctx0 = new StyleRenderContext(s0.Canvas, null, fonts, shaders, pool, legibility, gatedPkt, false, false);
            edgeGlow.Render(in ctx0);
            using var b0 = SKBitmap.FromImage(s0.Snapshot());
            b0.GetPixel(320, 4).Alpha.Should().Be(0);
        }

        // 2. System-wide Lumn DesktopOverlay (AllowDesktopEdgeGlow = true) glows brightly along screen edges while center stays clear!
        var lumnPkt = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 640f, 360f, 1.0f, 1.5, 1f / 60f, surfaceMode: StageRenderSurfaceMode.DesktopOverlay, allowDesktopEdgeGlow: true, renderDesktopLyrics: false);
        using var s1 = SKSurface.Create(info);
        s1.Canvas.Clear(SKColors.Transparent);
        var ctx1 = new StyleRenderContext(s1.Canvas, null, fonts, shaders, pool, legibility, lumnPkt, false, false);
        edgeGlow.Render(in ctx1);
        using var b1 = SKBitmap.FromImage(s1.Snapshot());

        b1.GetPixel(320, 4).Alpha.Should().BeGreaterThan(80, "top screen edge must emit Lumn bezel glow");
        b1.GetPixel(4, 180).Alpha.Should().BeGreaterThan(80, "left screen edge must emit Lumn bezel glow");
        b1.GetPixel(635, 180).Alpha.Should().BeGreaterThan(80, "right screen edge must emit Lumn bezel glow");
        b1.GetPixel(320, 355).Alpha.Should().BeGreaterThan(80, "bottom screen edge must emit Lumn bezel glow");
        b1.GetPixel(320, 180).Alpha.Should().Be(0, "center desktop apps must remain unobstructed");
    }

    [Fact]
    public void OverlayBoundsCalculator_ExpandsLumnEdgeGlowToFullMonitor_WhileKeepingSafeZoneInsideWorkingArea()
    {
        var monitor = new MonitorDescriptor(
            DisplayKey: @"\\.\DISPLAY1",
            FriendlyName: "Primary Studio",
            WorkingLeftPx: 0,
            WorkingTopPx: 0,
            WorkingWidthPx: 1920,
            WorkingHeightPx: 1032,
            EffectiveDpiScale: 1.0,
            IsPrimary: true,
            MonitorLeftPx: 0,
            MonitorTopPx: 0,
            MonitorWidthPx: 1920,
            MonitorHeightPx: 1080);

        var lumnSettings = AppSettings.Default with
        {
            EdgeGlowDesktopEnabled = true,
            EdgeGlowMode = EdgeGlowMode.NeonRush,
            AnchorZone = AnchorZone.BottomThird,
            SafeMarginPx = 32
        };

        var placements = OverlayBoundsCalculator.ComputePlacements([monitor], lumnSettings);
        placements.Should().HaveCount(1);
        var p = placements[0];

        p.WidthDip.Should().BeApproximately(1920.0, 0.01);
        p.HeightDip.Should().BeApproximately(1080.0, 0.01, "Lumn edge glow covers the full 1080px monitor bezel behind taskbar edge");
        (p.SafeZoneDip.TopDip + p.SafeZoneDip.HeightDip).Should().BeLessThanOrEqualTo(1032.0 - 32.0 + 0.01, "lyric SafeZone remains inside WorkingArea above taskbar");
    }

    [Fact]
    public void EdgeGlowRenderer_NeonRush_VisiblyRevolvesPerimeterAcrossFramesWhileCenterStaysClear()
    {
        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var edgeGlow = new EdgeGlowRenderer();

        var settings = AppSettings.Default with
        {
            EdgeGlowMode = EdgeGlowMode.NeonRush,
            EdgeGlowDesktopEnabled = true,
            EdgeGlowAnimation = EdgeGlowAnimationMode.AmbientDrift,
            EdgeGlowSpeed = 1.4f,
            EdgeGlowThickness = 1.35f,
            EdgeGlowSpill = 1.2f,
            EdgeGlowOverrideAlbumColors = true,
            EdgeGlowPrimaryColor = new RgbColor(0x00, 0xE5, 0xFF),
            EdgeGlowSecondaryColor = new RgbColor(0xFF, 0x2E, 0x93)
        };

        var pos = TimeSpan.FromSeconds(4.0);
        var snap = new OverlayPlaybackSnapshot(
            Track: doc.Track,
            Document: doc,
            LyricsStatus: LyricsStatus.Synced,
            PlaybackState: MediaPlaybackState.Playing,
            PlayerKind: MediaPlayerKind.Unknown,
            SourceAppUserModelId: "Demo",
            RawPosition: pos,
            EffectivePosition: pos,
            ActiveSlice: ActiveLyricSlice.Resolve(doc, pos),
            ActivePalette: AlbumAccentPalette.DemoNeonHorizon,
            TargetPalette: AlbumAccentPalette.DemoNeonHorizon,
            TimingOffset: new TimingOffsetState(null, TimeSpan.Zero, false),
            Settings: settings,
            IsPlaying: true,
            IsDemoMode: true,
            IsFullscreenHidden: false,
            PausedSinceUtc: null,
            LastOffsetNudgeUtc: null,
            TrackGeneration: 1,
            SeekGeneration: 0,
            AudioMetrics: CreateAudio());

        var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);

        static long ComputePerimeterSampleHash(SKBitmap bmp)
        {
            long hash = 17;
            for (var x = 20; x < bmp.Width - 20; x += 24)
            {
                var top = bmp.GetPixel(x, 3);
                var bottom = bmp.GetPixel(x, bmp.Height - 4);
                hash = unchecked((hash * 31) + top.Red + (top.Green * 3) + (top.Blue * 7) + top.Alpha);
                hash = unchecked((hash * 31) + bottom.Red + (bottom.Green * 3) + (bottom.Blue * 7) + bottom.Alpha);
            }
            for (var y = 20; y < bmp.Height - 20; y += 24)
            {
                var left = bmp.GetPixel(3, y);
                var right = bmp.GetPixel(bmp.Width - 4, y);
                hash = unchecked((hash * 31) + left.Red + (left.Green * 3) + (left.Blue * 7) + left.Alpha);
                hash = unchecked((hash * 31) + right.Red + (right.Green * 3) + (right.Blue * 7) + right.Alpha);
            }
            return hash;
        }

        // Frame A (t = 0.05s)
        using var sA = SKSurface.Create(info);
        sA.Canvas.Clear(SKColors.Transparent);
        var pktA = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 640f, 360f, 1.0f, 0.05, 1f / 60f, surfaceMode: StageRenderSurfaceMode.DesktopOverlay, allowDesktopEdgeGlow: true, renderDesktopLyrics: false);
        var ctxA = new StyleRenderContext(sA.Canvas, null, fonts, shaders, pool, legibility, pktA, false, false);
        edgeGlow.Render(in ctxA);
        using var bmpA = SKBitmap.FromImage(sA.Snapshot());
        var hashA = ComputePerimeterSampleHash(bmpA);

        // Advance phase ~0.72s across 36 frames so the dual beams travel visibly around edges
        for (var i = 1; i <= 36; i++)
        {
            using var sStep = SKSurface.Create(info);
            sStep.Canvas.Clear(SKColors.Transparent);
            var pktStep = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 640f, 360f, 1.0f, 0.05 + (i * 0.02), 0.02f, surfaceMode: StageRenderSurfaceMode.DesktopOverlay, allowDesktopEdgeGlow: true, renderDesktopLyrics: false);
            var ctxStep = new StyleRenderContext(sStep.Canvas, null, fonts, shaders, pool, legibility, pktStep, false, false);
            edgeGlow.Render(in ctxStep);
        }

        // Frame B (t = 0.77s)
        using var sB = SKSurface.Create(info);
        sB.Canvas.Clear(SKColors.Transparent);
        var pktB = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 640f, 360f, 1.0f, 0.79, 0.02f, surfaceMode: StageRenderSurfaceMode.DesktopOverlay, allowDesktopEdgeGlow: true, renderDesktopLyrics: false);
        var ctxB = new StyleRenderContext(sB.Canvas, null, fonts, shaders, pool, legibility, pktB, false, false);
        edgeGlow.Render(in ctxB);
        using var bmpB = SKBitmap.FromImage(sB.Snapshot());
        var hashB = ComputePerimeterSampleHash(bmpB);

        hashB.Should().NotBe(hashA, "Neon Rush perimeter beams must visibly revolve around the screen edges across time");
        bmpB.GetPixel(320, 180).Alpha.Should().Be(0, "center desktop must remain unobstructed during Neon Rush revolution");
    }

    [Fact]
    public void StageBackdropRenderer_AlbumArtBlur_WorksAcrossAllStylesAndDoesNotBlackOutUnderNeonRush()
    {
        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var backdrop = new StageBackdropRenderer();

        using var artBmp = new SKBitmap(48, 48);
        artBmp.Erase(new SKColor(0xE0, 0x24, 0x24, 0xFF));
        using var artImg = SKImage.FromBitmap(artBmp);
        using var artData = artImg.Encode(SKEncodedImageFormat.Png, 100);
        var pngBytes = artData.ToArray();

        var artPayload = new AlbumArtPayload("test-red-art", pngBytes);

        var settings = AppSettings.Default with
        {
            ActiveStyle = OverlayStyleId.Fisheye,
            StageBackdropMode = StageBackdropMode.AlbumArtBlur,
            EdgeGlowMode = EdgeGlowMode.NeonRush
        };

        var pos = TimeSpan.FromSeconds(5.0);
        var snap = new OverlayPlaybackSnapshot(
            Track: doc.Track,
            Document: doc,
            LyricsStatus: LyricsStatus.Synced,
            PlaybackState: MediaPlaybackState.Playing,
            PlayerKind: MediaPlayerKind.Unknown,
            SourceAppUserModelId: "Demo",
            RawPosition: pos,
            EffectivePosition: pos,
            ActiveSlice: ActiveLyricSlice.Resolve(doc, pos),
            ActivePalette: AlbumAccentPalette.DemoNeonHorizon,
            TargetPalette: AlbumAccentPalette.DemoNeonHorizon,
            TimingOffset: new TimingOffsetState(null, TimeSpan.Zero, false),
            Settings: settings,
            IsPlaying: true,
            IsDemoMode: true,
            IsFullscreenHidden: false,
            PausedSinceUtc: null,
            LastOffsetNudgeUtc: null,
            TrackGeneration: 1,
            SeekGeneration: 0,
            AlbumArt: artPayload,
            AudioMetrics: CreateAudio());

        var pkt = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 640f, 360f, 1.0f, 1.2, 1f / 60f, surfaceMode: StageRenderSurfaceMode.StageCanvas);
        pkt.StageBackdrop.Should().Be(StageBackdropMode.AlbumArtBlur);
        pkt.Palette.ScrimTint.Should().Be(AlbumAccentPalette.DemoNeonHorizon.ScrimTint, "AlbumArtBlur mode must preserve palette and artwork backdrop even when NeonRush is selected");

        var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Transparent);
        var ctx = new StyleRenderContext(surface.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
        backdrop.Render(in ctx);
        using var bmp = SKBitmap.FromImage(surface.Snapshot());

        var p1 = bmp.GetPixel(160, 90);
        var p2 = bmp.GetPixel(500, 250);
        ((int)p1.Red).Should().BeGreaterThan(p1.Green + 35, "blurred red album cover must color non-Glass stages");
        ((int)p2.Red).Should().BeGreaterThan(p2.Green + 35, "full-bleed album blur must extend across the entire stage");
    }

    [Fact]
    public void VinylGrooveStyleRenderer_AppleMusicSplitStage_RendersLeftCoverScrubberAndRightContextLyrics()
    {
        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var router = new LyricStyleRouter();

        using var artBmp = new SKBitmap(64, 64);
        artBmp.Erase(new SKColor(0xD9, 0x38, 0x1E, 0xFF));
        using var artImg = SKImage.FromBitmap(artBmp);
        using var artData = artImg.Encode(SKEncodedImageFormat.Png, 100);
        var pngBytes = artData.ToArray();

        var artPayload = new AlbumArtPayload("test-coral-art", pngBytes);

        var settings = AppSettings.Default with
        {
            ActiveStyle = OverlayStyleId.VinylGroove,
            StageBackdropMode = StageBackdropMode.PureBlack
        };

        var pos = TimeSpan.FromSeconds(5.2);
        var snap = new OverlayPlaybackSnapshot(
            Track: doc.Track,
            Document: doc,
            LyricsStatus: LyricsStatus.Synced,
            PlaybackState: MediaPlaybackState.Playing,
            PlayerKind: MediaPlayerKind.Unknown,
            SourceAppUserModelId: "Demo",
            RawPosition: pos,
            EffectivePosition: pos,
            ActiveSlice: ActiveLyricSlice.Resolve(doc, pos),
            ActivePalette: AlbumAccentPalette.DemoNeonHorizon,
            TargetPalette: AlbumAccentPalette.DemoNeonHorizon,
            TimingOffset: new TimingOffsetState(null, TimeSpan.Zero, false),
            Settings: settings,
            IsPlaying: true,
            IsDemoMode: true,
            IsFullscreenHidden: false,
            PausedSinceUtc: null,
            LastOffsetNudgeUtc: null,
            TrackGeneration: 1,
            SeekGeneration: 0,
            AlbumArt: artPayload,
            AudioMetrics: CreateAudio());

        var pkt = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 1280f, 720f, 1.0f, 2.0, 1f / 60f, surfaceMode: StageRenderSurfaceMode.StageCanvas);
        for (var step = 0; step < 12; step++)
        {
            var stepPkt = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 1280f, 720f, 1.0f, 1.6 + (step * 0.04), 0.04f, surfaceMode: StageRenderSurfaceMode.StageCanvas);
            router.AdvancePhysics(in stepPkt);
            pkt = stepPkt;
        }

        var cardRect = VinylGrooveStyleRenderer.ResolveCoverCardBounds(1280f, 720f, StageRenderSurfaceMode.StageCanvas);
        cardRect.MidX.Should().BeLessThan(400f, "cover card sits on left column like Apple Music layout");
        Math.Abs(cardRect.Width - cardRect.Height).Should().BeLessThan(1.0f, "cover card is square rounded art");

        var info = new SKImageInfo(1280, 720, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Black);
        var ctx = new StyleRenderContext(surface.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
        router.Render(in ctx);
        using var bmp = SKBitmap.FromImage(surface.Snapshot());

        // Scrubber / metadata pixels under left cover card
        var metaLitPixels = 0;
        for (var y = (int)cardRect.Bottom + 8; y < Math.Min(710, (int)cardRect.Bottom + 90); y += 2)
        {
            for (var x = (int)cardRect.Left; x < (int)cardRect.Right; x += 4)
            {
                var px = bmp.GetPixel(x, y);
                if (px.Red > 90 && px.Green > 90 && px.Blue > 90) metaLitPixels++;
            }
        }
        metaLitPixels.Should().BeGreaterThan(20, "left column under cover card must render scrubber track, timestamps, and metadata");

        // Right column scrolling context lyrics
        var lyricLitPixels = 0;
        for (var y = 120; y < 600; y += 3)
        {
            for (var x = 620; x < 1180; x += 4)
            {
                var px = bmp.GetPixel(x, y);
                if (px.Red > 160 && px.Green > 160 && px.Blue > 160) lyricLitPixels++;
            }
        }
        lyricLitPixels.Should().BeGreaterThan(120, "right column must render crisp white active and contextual 3D lyrics");
    }

    [Fact]
    public void SkiaAlbumColorExtractor_CropsSpotifyBadgeAndMarginsEdgeToEdge_WithoutBreakingNormalCovers()
    {
        // 1. Simulated Spotify Desktop GSMTC transparent-padded badge with bottom white Spotify logo strip
        byte[] BuildTransparentSpotifyThumbnail()
        {
            using var bmp = new SKBitmap(320, 320, SKColorType.Bgra8888, SKAlphaType.Premul);
            bmp.Erase(SKColors.Transparent);
            using var canvas = new SKCanvas(bmp);
            using var artPaint = new SKPaint { Color = new SKColor(0xE8, 0x5D, 0x22, 0xFF) };
            // Inner centered square cover art (~75% width, top ~2%..77%), transparent margins left/right
            canvas.DrawRect(new SKRect(40f, 4f, 280f, 244f), artPaint);
            // Detached bottom white Spotify logo strip (y = 264..300)
            using var logoPaint = new SKPaint { Color = SKColors.White };
            canvas.DrawCircle(104f, 282f, 16f, logoPaint);
            canvas.DrawRect(new SKRect(128f, 272f, 224f, 292f), logoPaint);
            using var img = SKImage.FromBitmap(bmp);
            using var encoded = img.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }

        // 2. Simulated opaque dark-matte padded composite thumbnail with bottom white Spotify logo
        byte[] BuildOpaqueMatteSpotifyThumbnail()
        {
            using var bmp = new SKBitmap(320, 320, SKColorType.Bgra8888, SKAlphaType.Premul);
            bmp.Erase(new SKColor(0x22, 0x22, 0x22, 0xFF));
            using var canvas = new SKCanvas(bmp);
            using var artPaint = new SKPaint { Color = new SKColor(0x1D, 0xA1, 0xF2, 0xFF) };
            canvas.DrawRect(new SKRect(40f, 6f, 280f, 246f), artPaint);
            using var logoPaint = new SKPaint { Color = SKColors.White };
            canvas.DrawCircle(104f, 284f, 15f, logoPaint);
            canvas.DrawRect(new SKRect(128f, 274f, 222f, 294f), logoPaint);
            using var img = SKImage.FromBitmap(bmp);
            using var encoded = img.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }

        // 3. Normal full-bleed solid crimson cover (must stay completely uncropped!)
        byte[] BuildFullBleedCover()
        {
            using var bmp = new SKBitmap(200, 200, SKColorType.Bgra8888, SKAlphaType.Premul);
            bmp.Erase(new SKColor(0xCC, 0x18, 0x38, 0xFF));
            using var img = SKImage.FromBitmap(bmp);
            using var encoded = img.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }

        var transPng = BuildTransparentSpotifyThumbnail();
        using var transDecoded = SkiaAlbumColorExtractor.DecodeBoundedCoverImage(transPng, 320);
        transDecoded.Should().NotBeNull();
        using (var transBmp = SKBitmap.FromImage(transDecoded!))
        {
            var topLeftCorner = transBmp.GetPixel(4, 4);
            var bottomCenter = transBmp.GetPixel(transBmp.Width / 2, transBmp.Height - 4);
            topLeftCorner.Alpha.Should().Be(255, "transparent side padding must be cropped out edge-to-edge");
            topLeftCorner.Red.Should().BeGreaterThan((byte)(topLeftCorner.Blue + 120), "edge-to-edge cover color must fill card corners");
            bottomCenter.Red.Should().BeGreaterThan((byte)(bottomCenter.Blue + 120), "bottom white Spotify logo bar must be stripped completely");
        }

        var mattePng = BuildOpaqueMatteSpotifyThumbnail();
        using var matteDecoded = SkiaAlbumColorExtractor.DecodeBoundedCoverImage(mattePng, 320);
        matteDecoded.Should().NotBeNull();
        using (var matteBmp = SKBitmap.FromImage(matteDecoded!))
        {
            var topLeftCorner = matteBmp.GetPixel(4, 4);
            var bottomCenter = matteBmp.GetPixel(matteBmp.Width / 2, matteBmp.Height - 4);
            topLeftCorner.Blue.Should().BeGreaterThan((byte)(topLeftCorner.Red + 120), "opaque dark matte side padding must be cropped out edge-to-edge");
            bottomCenter.Blue.Should().BeGreaterThan((byte)(bottomCenter.Red + 120), "bottom white Spotify logo bar on opaque matte must be cropped completely");
        }

        var extractor = new SkiaAlbumColorExtractor();
        var extractedTrans = extractor.Extract(new AlbumArtPayload("spotify-trans", transPng));
        extractedTrans.IsMonochromeFallback.Should().BeFalse();
        extractedTrans.AccentColor1.R.Should().BeGreaterThan((byte)(extractedTrans.AccentColor1.B + 80), "dominant accent must come from orange cover art rather than bottom white Spotify logo");

        var fullBleedPng = BuildFullBleedCover();
        using var fullBmp = SKBitmap.Decode(fullBleedPng);
        var fullCrop = SkiaAlbumColorExtractor.ComputeSmartArtworkCropRect(fullBmp);
        fullCrop.Width.Should().BeApproximately(200f, 0.1f, "normal full-bleed cover must retain full bounds");
    }

    [Fact]
    public void Win32StageChromeAndGlassStage3DTypography_ExecuteCleanlyWithoutAllocOrErrors()
    {
        // Zero-handle guard works safely without crashing in headless test runners
        Win32OverlayInterop.ApplyStageWindowDarkChrome(0);

        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var router = new LyricStyleRouter();

        var settings = AppSettings.Default with
        {
            ActiveStyle = OverlayStyleId.VinylGroove,
            StageBackdropMode = StageBackdropMode.AlbumArtBlur
        };

        // Transition across line boundary so scroll spring, word lift spring, 3D extrusion & DOF blurs exercise cleanly
        var snap1 = new OverlayPlaybackSnapshot(
            Track: doc.Track,
            Document: doc,
            LyricsStatus: LyricsStatus.Synced,
            PlaybackState: MediaPlaybackState.Playing,
            PlayerKind: MediaPlayerKind.Unknown,
            SourceAppUserModelId: "Demo",
            RawPosition: TimeSpan.FromSeconds(1.5),
            EffectivePosition: TimeSpan.FromSeconds(1.5),
            ActiveSlice: ActiveLyricSlice.Resolve(doc, TimeSpan.FromSeconds(1.5)),
            ActivePalette: AlbumAccentPalette.DemoNeonHorizon,
            TargetPalette: AlbumAccentPalette.DemoNeonHorizon,
            TimingOffset: new TimingOffsetState(null, TimeSpan.Zero, false),
            Settings: settings,
            IsPlaying: true,
            IsDemoMode: true,
            IsFullscreenHidden: false,
            PausedSinceUtc: null,
            LastOffsetNudgeUtc: null,
            TrackGeneration: 1,
            SeekGeneration: 0,
            AudioMetrics: CreateAudio());

        for (var step = 0; step < 12; step++)
        {
            var warmupPkt = OverlayFrameAdapter.FromPlaybackSnapshot(snap1, 1280f, 720f, 1.0f, 1.0 + (step * 0.04), 0.04f, surfaceMode: StageRenderSurfaceMode.StageCanvas);
            router.AdvancePhysics(in warmupPkt);
        }

        var snap2 = snap1 with
        {
            RawPosition = TimeSpan.FromSeconds(6.2),
            EffectivePosition = TimeSpan.FromSeconds(6.2),
            ActiveSlice = ActiveLyricSlice.Resolve(doc, TimeSpan.FromSeconds(6.2))
        };
        var pkt2 = OverlayFrameAdapter.FromPlaybackSnapshot(snap2, 1280f, 720f, 1.0f, 2.2, 0.03f, surfaceMode: StageRenderSurfaceMode.StageCanvas);
        router.AdvancePhysics(in pkt2).Should().BeTrue("carousel scroll & 3D word springs stay active after line transition");

        for (var step = 1; step <= 4; step++)
        {
            pkt2 = OverlayFrameAdapter.FromPlaybackSnapshot(snap2, 1280f, 720f, 1.0f, 2.2 + (step * 0.03), 0.03f, surfaceMode: StageRenderSurfaceMode.StageCanvas);
            router.AdvancePhysics(in pkt2);
        }

        var info = new SKImageInfo(1280, 720, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Black);
        var ctx = new StyleRenderContext(surface.Canvas, null, fonts, shaders, pool, legibility, pkt2, false, false);
        router.Render(in ctx);

        using var bmp = SKBitmap.FromImage(surface.Snapshot());
        var rightBrightPixels = 0;
        for (var y = 120; y < 620; y += 3)
        {
            for (var x = 610; x < 1190; x += 4)
            {
                var px = bmp.GetPixel(x, y);
                if (px.Red > 180 && px.Green > 180 && px.Blue > 180)
                {
                    rightBrightPixels++;
                }
            }
        }
        rightBrightPixels.Should().BeGreaterThan(80, "Glass Stage 3D extruded active word & context lines must render brightly");
    }

    [Fact]
    public async Task HindiAndHinglishVisualMatcher_ResolvesDevanagariNuktaVariantsHinglishWordsBigramsAndTrackMood()
    {
        using var starter = new StarterLexiconVisualMatcher();
        await starter.InitializeAsync();

        using var semantic = new GloveSemanticVisualMatcher();
        await semantic.InitializeAsync();

        // Romanized Hinglish & Devanagari exact matches across both Balanced & EmojiOnly modes
        var hinglishCases = new (string Word, string ExpectedConcept)[]
        {
            ("dil", "heart"),
            ("pyaar", "heart"),
            ("baarish", "rain"),
            ("chaand", "night"),
            ("aag", "fire"),
            ("aansu", "cry"),
            ("zindagi", "life"),
            ("dhadkan", "heartbeat_pulse"),
            ("दिल", "heart"),
            ("प्यार", "heart"),
            ("बारिश", "rain"),
            ("आग", "fire"),
            ("धड़कन", "heartbeat_pulse")
        };

        foreach (var (word, expectedConcept) in hinglishCases)
        {
            var mStarter = starter.MatchWord(word, VisualPreference.Balanced);
            mStarter.Should().NotBeNull($"Starter matcher must match Hindi/Hinglish word '{word}'");
            mStarter!.ConceptName.Should().Be(expectedConcept);

            var mEmoji = semantic.MatchWord(word, VisualPreference.EmojiOnly);
            mEmoji.Should().NotBeNull($"Semantic matcher must produce an emoji for '{word}'");
            mEmoji!.ConceptName.Should().Be(expectedConcept);
            mEmoji.Kind.Should().Be(VisualKind.Emoji);
            mEmoji.Value.Should().NotBeNullOrWhiteSpace();
        }

        // Nukta & chandrabindu folding guarantees spelling variants map to the exact same concept
        semantic.MatchWord("इश्क़", VisualPreference.EmojiOnly)!.ConceptName
            .Should().Be(semantic.MatchWord("इश्क", VisualPreference.EmojiOnly)!.ConceptName)
            .And.Be("heart");
        semantic.MatchWord("चाँद", VisualPreference.EmojiOnly)!.ConceptName
            .Should().Be(semantic.MatchWord("चांद", VisualPreference.EmojiOnly)!.ConceptName)
            .And.Be("night");
        semantic.MatchWord("आँसू", VisualPreference.EmojiOnly)!.ConceptName
            .Should().Be(semantic.MatchWord("आंसू", VisualPreference.EmojiOnly)!.ConceptName)
            .And.Be("cry");
        semantic.MatchWord("ज़िंदगी", VisualPreference.EmojiOnly)!.ConceptName
            .Should().Be(semantic.MatchWord("जिंदगी", VisualPreference.EmojiOnly)!.ConceptName)
            .And.Be("life");

        // Common Hindi & Hinglish grammatical stopwords must not spawn noisy icons
        foreach (var stopword in new[] { "hai", "mera", "nahi", "है", "मेरा", "नहीं" })
        {
            starter.MatchWord(stopword, VisualPreference.EmojiOnly).Should().BeNull($"Stopword '{stopword}' should be filtered");
            semantic.MatchWord(stopword, VisualPreference.EmojiOnly).Should().BeNull($"Stopword '{stopword}' should be filtered");
        }

        // Poetic Hindi & Hinglish bigrams via Layer-1 MatchLine
        var bigramLine = new[]
        {
            new LyricWord("dil", TimeSpan.Zero, TimeSpan.FromSeconds(1), true),
            new LyricWord("toota", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), true),
            new LyricWord("चाँद", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), true),
            new LyricWord("तारे", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), true),
            new LyricWord("दिल", TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), true),
            new LyricWord("धड़के", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), true)
        };
        var matches = semantic.MatchLine(bigramLine, VisualPreference.EmojiOnly);
        matches[0].Should().BeNull();
        matches[1]!.ConceptName.Should().Be("broken_heart");
        matches[2].Should().BeNull();
        matches[3]!.ConceptName.Should().Be("star");
        matches[4].Should().BeNull();
        matches[5]!.ConceptName.Should().Be("heartbeat_pulse");

        // Hindi track mood fallback inference
        var hindiTrack = new TrackIdentity("दिल ये मेरा", "Arijit Singh", "दिल ये मेरा", "arijit singh", "Love Songs", TimeSpan.FromMinutes(4), "arijit-dil-ye-mera");
        var parser = new LyricsParser();
        var hindiLrc = parser.ParseLrc(
            hindiTrack,
            "[00:01.00]दिल में प्यार और इश्क़ है\n[00:04.00]चाँद रात और सपने हैं",
            LyricsSourceKind.Demo,
            "Test",
            false);
        var mood = semantic.InferTrackMoodVisual(hindiTrack, hindiLrc, VisualPreference.EmojiOnly);
        mood.Should().NotBeNull();
        mood!.ConceptName.Should().Be("heart");
        mood.Kind.Should().Be(VisualKind.Emoji);
    }

    [Fact]
    public async Task GlassStage3D_RendersHindiLyricsWithFloatingVisualOrbAndPreservesDesktopTransparency()
    {
        using var matcher = new GloveSemanticVisualMatcher();
        await matcher.InitializeAsync();

        var track = new TrackIdentity("Tum Hi Ho", "Arijit Singh", "tum hi ho", "arijit singh", "Aashiqui 2", TimeSpan.FromSeconds(210), "arijit-tum-hi-ho");
        var rawDoc = new LyricsParser().ParseLrc(
            track,
            "[00:01.00]दिल ये मेरा चाँद तारे\n" +
            "[00:04.00]pyaar aur baarish ki raat\n" +
            "[00:08.00]aag jale khwaabon mein",
            LyricsSourceKind.Demo,
            "Test",
            false);

        var annotatedLines = rawDoc.Lines.Select(line =>
        {
            var matched = matcher.MatchLine(line.Words, VisualPreference.EmojiOnly);
            var words = line.Words.Select((w, idx) => w with { Visual = matched[idx] }).ToImmutableArray();
            return line with { Words = words };
        }).ToImmutableArray();
        var doc = rawDoc with { Lines = annotatedLines };
        doc.Lines[0].Words[0].Visual.Should().NotBeNull("Hindi active word 'दिल' must carry an attached visual for the 3D floating orb");

        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var router = new LyricStyleRouter();

        var redCoverBytes = EncodeCover(128, 128, SKEncodedImageFormat.Png, (_, _) => new SKColor(0xD6, 0x28, 0x39));
        var albumArt = new AlbumArtPayload("hindi-red-cover", redCoverBytes);

        var settings = AppSettings.Default with
        {
            ActiveStyle = OverlayStyleId.VinylGroove,
            StageBackdropMode = StageBackdropMode.AlbumArtBlur,
            VisualPreference = VisualPreference.EmojiOnly,
            ReducedMotion = false
        };

        byte[] RenderStage(double tSec, StageRenderSurfaceMode mode, bool reducedMotion = false)
        {
            var effectiveSettings = settings with { ReducedMotion = reducedMotion };
            var pkt = BuildPacket(
                doc,
                effectiveSettings,
                mode,
                TimeSpan.FromSeconds(1.2),
                audio: CreateAudio(bass: 0.84f, beat: 0.90f),
                albumArt: albumArt,
                renderTime: tSec);
            router.AdvancePhysics(in pkt);

            var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            surface.Canvas.Clear(mode == StageRenderSurfaceMode.DesktopOverlay ? SKColors.Transparent : SKColors.Black);
            var ctx = new StyleRenderContext(surface.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            router.Render(in ctx);

            using var bmp = SKBitmap.FromImage(surface.Snapshot());
            if (mode == StageRenderSurfaceMode.DesktopOverlay)
            {
                bmp.GetPixel(0, 0).Alpha.Should().Be(0);
                bmp.GetPixel(639, 0).Alpha.Should().Be(0);
                bmp.GetPixel(0, 359).Alpha.Should().Be(0);
                bmp.GetPixel(639, 359).Alpha.Should().Be(0);
            }
            else
            {
                var card = VinylGrooveStyleRenderer.ResolveCoverCardBounds(640f, 360f, StageRenderSurfaceMode.StageCanvas);
                var centerPx = bmp.GetPixel((int)card.MidX, (int)card.MidY);
                centerPx.Alpha.Should().BeGreaterThan(220);
                centerPx.Red.Should().BeGreaterThan(100);
            }

            return SHA256.HashData(bmp.Bytes);
        }

        var hash1 = RenderStage(1.0, StageRenderSurfaceMode.StageCanvas);
        var hash2 = RenderStage(2.4, StageRenderSurfaceMode.StageCanvas);
        hash1.Should().NotEqual(hash2, "Spatial 3D orbital rings, motes, and active word lighting must animate across frames");

        _ = RenderStage(2.6, StageRenderSurfaceMode.DesktopOverlay);
        _ = RenderStage(2.8, StageRenderSurfaceMode.StageCanvas, reducedMotion: true);
    }

    [Fact]
    public async Task GlassStageSpatialPrismStage_RendersBoldDevanagariFocusDeckPersistentCharmsAndSmoothFluidWaves()
    {
        using var matcher = new GloveSemanticVisualMatcher();
        await matcher.InitializeAsync();

        // Newly expanded Tum Ho / Hindi poetic vocabulary
        matcher.MatchWord("महसूस", VisualPreference.EmojiOnly)!.ConceptName.Should().Be("heartbeat_pulse");
        matcher.MatchWord("फ़िकर", VisualPreference.EmojiOnly)!.ConceptName.Should().Be("peace");
        matcher.MatchWord("वफ़ा", VisualPreference.EmojiOnly)!.ConceptName.Should().Be("heart");
        matcher.MatchWord("सजदा", VisualPreference.EmojiOnly)!.ConceptName.Should().Be("angel");
        matcher.MatchWord("छीनेगा", VisualPreference.EmojiOnly)!.ConceptName.Should().Be("broken_heart");

        using var fonts = new FontAssetRegistry();
        var devFallback = fonts.MatchFallbackForCodepoint('त');
        devFallback.ContainsGlyph('त').Should().BeTrue("Devanagari fallback typeface must render Devanagari glyphs cleanly");
        if (devFallback.FamilyName.Contains("Nirmala", StringComparison.OrdinalIgnoreCase))
        {
            devFallback.FontWeight.Should().BeGreaterThanOrEqualTo((int)SKFontStyleWeight.SemiBold);
        }

        var track = new TrackIdentity("Tum Ho", "Mohit Chauhan", "tum ho", "mohit chauhan", "Rockstar", TimeSpan.FromSeconds(318), "mohit-tum-ho");
        var rawDoc = new LyricsParser().ParseLrc(
            track,
            "[00:00.50]उतना ही पा भी लूँ\n" +
            "[00:01.00]किस तरह छीनेगा मुझसे ये जहां तुम्हें\n" +
            "[00:05.00]तुम भी हो मैं\n" +
            "[00:08.00]क्या फ़िकर अब हमें\n" +
            "[00:11.00]तुम हो मेरे लिये",
            LyricsSourceKind.Demo,
            "Test",
            false);

        var annotatedLines = rawDoc.Lines.Select(line =>
        {
            var matched = matcher.MatchLine(line.Words, VisualPreference.EmojiOnly);
            var words = line.Words.Select((w, idx) => w with { Visual = matched[idx] }).ToImmutableArray();
            return line with { Words = words };
        }).ToImmutableArray();
        var doc = rawDoc with { Lines = annotatedLines };
        var moodVisual = matcher.InferTrackMoodVisual(track, doc, VisualPreference.EmojiOnly);

        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var router = new LyricStyleRouter();

        var redCoverBytes = EncodeCover(128, 128, SKEncodedImageFormat.Png, (_, _) => new SKColor(0x9E, 0x24, 0x24));
        var albumArt = new AlbumArtPayload("tum-ho-cover", redCoverBytes);

        var settings = AppSettings.Default with
        {
            ActiveStyle = OverlayStyleId.VinylGroove,
            StageBackdropMode = StageBackdropMode.AlbumArtBlur,
            AudioVisualizerMode = AudioVisualizerMode.FluidWave,
            VisualPreference = VisualPreference.EmojiOnly,
            ReducedMotion = false
        };

        // Warm up lifecycle fade-in & springs over 14 frames so ContentOpacity reaches 1.0 (matches live playback)
        // Pos 4.5s puts active word on "तुम्हें" (last word of line 1) while "जहां" & "छीनेगा" are already sung -> exercises persistent charms + focus deck + FluidWave
        FrameRenderPacket pkt = null!;
        for (var step = 0; step < 14; step++)
        {
            pkt = BuildPacket(
                doc,
                settings,
                StageRenderSurfaceMode.StageCanvas,
                TimeSpan.FromSeconds(4.5),
                audio: CreateAudio(bass: 0.78f, beat: 0.85f),
                albumArt: albumArt,
                moodVisual: moodVisual,
                renderTime: 1.9 + (step * 0.025));
            router.AdvancePhysics(in pkt);
        }

        var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var stageSurface = SKSurface.Create(info))
        {
            stageSurface.Canvas.Clear(SKColors.Black);
            var ctx = new StyleRenderContext(stageSurface.Canvas, null, fonts, shaders, pool, legibility, pkt, false, false);
            router.Render(in ctx);
            using var stageBmp = SKBitmap.FromImage(stageSurface.Snapshot());

            var rightBrightPixels = 0;
            for (var y = 70; y < 300; y += 2)
            {
                for (var x = 290; x < 620; x += 2)
                {
                    var px = stageBmp.GetPixel(x, y);
                    if (px.Red > 175 && px.Green > 175 && px.Blue > 175)
                    {
                        rightBrightPixels++;
                    }
                }
            }

            rightBrightPixels.Should().BeGreaterThan(80, "Spatial Prism Focus Deck + Bold Devanagari active words must render crisply");
        }

        var deskPkt = BuildPacket(
            doc,
            settings with { EdgeGlowMode = EdgeGlowMode.Off, AudioVisualizerMode = AudioVisualizerMode.None },
            StageRenderSurfaceMode.DesktopOverlay,
            TimeSpan.FromSeconds(4.5),
            audio: CreateAudio(bass: 0.78f, beat: 0.85f),
            albumArt: albumArt,
            moodVisual: moodVisual,
            renderTime: 2.2);
        router.AdvancePhysics(in deskPkt);

        using var deskSurface = SKSurface.Create(new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul));
        deskSurface.Canvas.Clear(SKColors.Transparent);
        var deskCtx = new StyleRenderContext(deskSurface.Canvas, null, fonts, shaders, pool, legibility, deskPkt, false, false);
        router.Render(in deskCtx);
        using var deskBmp = SKBitmap.FromImage(deskSurface.Snapshot());
        deskBmp.GetPixel(0, 0).Alpha.Should().Be(0);
        deskBmp.GetPixel(639, 0).Alpha.Should().Be(0);
        deskBmp.GetPixel(0, 359).Alpha.Should().Be(0);
        deskBmp.GetPixel(639, 359).Alpha.Should().Be(0);
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

        public Task<bool> TryTogglePlayPauseAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> TrySkipNextAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> TrySkipPreviousAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public ValueTask DisposeAsync()
        {
            _artTcs.TrySetCanceled();
            return ValueTask.CompletedTask;
        }
    }
}
