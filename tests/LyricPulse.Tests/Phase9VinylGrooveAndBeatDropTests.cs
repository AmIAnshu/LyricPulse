using System.Collections.Immutable;
using System.Security.Cryptography;
using AwesomeAssertions;
using LyricPulse.App.Tray;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Audio;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Core.Settings;
using LyricPulse.Core.Visuals;
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
        float beat = 0.78f,
        float dropTrigger = 0f,
        float climaxIntensity = 0f)
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
            SpectrumBands: ImmutableArray.Create(bands),
            DropTrigger: dropTrigger,
            ClimaxIntensity: climaxIntensity,
            IsClimaxActive: climaxIntensity >= 0.15f);
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

    private static void PushSineWavePcm(
        AudioSpectrumAnalyzer analyzer,
        double freqHz,
        float amplitude,
        int frames = 1024,
        int sampleRateHz = 48_000)
    {
        var stereo = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var sample = amplitude * MathF.Sin((float)(Math.Tau * freqHz * i / sampleRateHz));
            stereo[i * 2] = sample;
            stereo[(i * 2) + 1] = sample;
        }

        analyzer.PushInterleavedFloatSamples(stereo, 2);
    }

    private static FrameRenderPacket BuildPacket(
        LyricsDocument doc,
        AppSettings settings,
        StageRenderSurfaceMode surfaceMode,
        TimeSpan position,
        AudioFrameMetrics? audio = null,
        AlbumArtPayload? albumArt = null,
        WordVisualMatch? moodVisual = null,
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
    public void AudioSpectrumAnalyzer_DetectsBuildupAndMasteredSubBassDrop_RespectsCooldownAndResets()
    {
        var analyzer = new AudioSpectrumAnalyzer(48_000) { DropSensitivity = 1.0f };

        // 1. Mastered medium-level buildup (loud vocal/riser without sub-bass)
        AudioFrameMetrics buildupMetrics = null!;
        for (var i = 0; i < 18; i++)
        {
            PushSineWavePcm(analyzer, freqHz: 3600.0, amplitude: 0.42f);
            buildupMetrics = analyzer.ComputeMetrics(i * 0.05, isPlaying: true, partyEnergy: 1.0f, dtSecondsOverride: 0.05f);
        }

        buildupMetrics.DropTrigger.Should().Be(0f);

        // 2. Sudden 55 Hz sub-bass drop surge (even at similar mastered LUFS ceiling)
        AudioFrameMetrics dropMetrics = null!;
        for (var i = 0; i < 4; i++)
        {
            PushSineWavePcm(analyzer, freqHz: 55.0, amplitude: 0.62f);
            dropMetrics = analyzer.ComputeMetrics(1.0 + (i * 0.02), isPlaying: true, partyEnergy: 1.0f, dtSecondsOverride: 0.02f);
        }

        dropMetrics.DropTrigger.Should().BeGreaterThan(0.5f);
        dropMetrics.ClimaxIntensity.Should().BeGreaterThan(0.75f);
        dropMetrics.IsClimaxActive.Should().BeTrue();

        // 3. ResetBeatDropState clears accumulators cleanly
        analyzer.ResetBeatDropState();
        PushSineWavePcm(analyzer, freqHz: 440.0, amplitude: 0.10f);
        var postReset = analyzer.ComputeMetrics(2.0, isPlaying: true, partyEnergy: 1.0f, dtSecondsOverride: 0.02f);
        postReset.DropTrigger.Should().Be(0f);
        postReset.IsClimaxActive.Should().BeFalse();

        // 4. Synthetic demo fallback produces choreographed drop pulses cleanly
        var demoDrop = AudioSpectrumAnalyzer.ComputeSyntheticPulse(6.05, isPlaying: true, partyEnergy: 1.0f, dropSensitivity: 1.0f);
        demoDrop.DropTrigger.Should().BeGreaterThan(0.8f);
        demoDrop.ClimaxIntensity.Should().BeGreaterThan(0.8f);
        demoDrop.IsClimaxActive.Should().BeTrue();
    }

    [Fact]
    public void BeatDropEffectRenderer_SpawnsArenaStageExplosionAndTremor_SkipsReducedMotion_AndPreservesDesktopCorners()
    {
        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var dropFx = new BeatDropEffectRenderer();

        var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
        var settingsOn = AppSettings.Default with { BeatDropEffectEnabled = true, BeatDropIntensity = 1.25f };
        var triggerPkt = BuildPacket(doc, settingsOn, StageRenderSurfaceMode.StageCanvas, TimeSpan.FromSeconds(1.5), CreateAudio(dropTrigger: 1.0f, climaxIntensity: 1.0f));

        dropFx.Advance(in triggerPkt).Should().BeTrue();
        var (slamScale, dx, dy) = dropFx.GetLyricMotionTransform(in triggerPkt);
        slamScale.Should().BeGreaterThan(1.04f);
        (MathF.Abs(dx) + MathF.Abs(dy)).Should().BeGreaterThan(0.1f);

        for (var i = 0; i < 4; i++)
        {
            var stepPkt = BuildPacket(doc, settingsOn, StageRenderSurfaceMode.StageCanvas, TimeSpan.FromSeconds(1.5 + (i * 0.016)), CreateAudio(dropTrigger: 0f, climaxIntensity: 0.9f), renderTime: 2.0 + (i * 0.02));
            dropFx.Advance(in stepPkt);
        }

        using var stageSurf = SKSurface.Create(info);
        stageSurf.Canvas.Clear(SKColors.Transparent);
        var stageCtx = new StyleRenderContext(stageSurf.Canvas, null, fonts, shaders, pool, legibility, triggerPkt, false, false);
        dropFx.RenderStageDrops(in stageCtx);
        using var stageBmp = SKBitmap.FromImage(stageSurf.Snapshot());
        CountLitPixels(stageBmp).Should().BeGreaterThan(1200, "arena light cones, shockwaves, and 110 sparks should richly energize StageCanvas");

        // DesktopOverlay skips full-screen drops so all 4 corners remain strictly transparent
        var desktopPkt = BuildPacket(doc, settingsOn, StageRenderSurfaceMode.DesktopOverlay, TimeSpan.FromSeconds(1.5), CreateAudio(dropTrigger: 1.0f, climaxIntensity: 1.0f));
        using var deskSurf = SKSurface.Create(info);
        deskSurf.Canvas.Clear(SKColors.Transparent);
        var deskCtx = new StyleRenderContext(deskSurf.Canvas, null, fonts, shaders, pool, legibility, desktopPkt, false, false);
        dropFx.RenderStageDrops(in deskCtx);
        using var deskBmp = SKBitmap.FromImage(deskSurf.Snapshot());
        CountLitPixels(deskBmp).Should().Be(0);

        // ReducedMotion resets and skips drops completely
        var reducedPkt = BuildPacket(doc, settingsOn with { ReducedMotion = true }, StageRenderSurfaceMode.StageCanvas, TimeSpan.FromSeconds(1.5), CreateAudio(dropTrigger: 1.0f, climaxIntensity: 1.0f));
        dropFx.Advance(in reducedPkt).Should().BeFalse();
        dropFx.GetLyricZoomMultiplier(in reducedPkt).Should().Be(1.0f);
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

        // Non-square 360x240 JPEG (realistic browser/Spotify cover) exercises SKCodec scaled center-crop decode
        var jpegCoverBytes = EncodeCover(360, 240, SKEncodedImageFormat.Jpeg, (x, y) => x < 180 ? new SKColor(238, 42, 92) : new SKColor(22, 186, 248));
        var artPayload = new AlbumArtPayload("vinyl-jpeg-cover-v2", jpegCoverBytes);
        var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
        var settings = AppSettings.Default with { ActiveStyle = OverlayStyleId.VinylGroove, StageBackdropMode = StageBackdropMode.AmbientPartyMesh };

        // 1. Live JPEG album cover in center of platter + 3D perspective text on right
        FrameRenderPacket artPkt = null!;
        for (var i = 0; i < 8; i++)
        {
            artPkt = BuildPacket(demoDoc, settings, StageRenderSurfaceMode.StageCanvas, TimeSpan.FromSeconds(25.7), CreateAudio(climaxIntensity: 0.7f), artPayload, moodVisual, renderTime: 1.0 + (i * 0.02));
            router.AdvancePhysics(in artPkt);
        }

        using var artSurf = SKSurface.Create(info);
        var artCtx = new StyleRenderContext(artSurf.Canvas, null, fonts, shaders, pool, legibility, artPkt, false, false);
        router.Render(in artCtx);
        using var artBmp = SKBitmap.FromImage(artSurf.Snapshot());
        CountLitPixels(artBmp).Should().BeGreaterThan(700);
        var artHash = Convert.ToHexString(SHA256.HashData(artBmp.GetPixelSpan()));

        // Verify platter center region actually displays bright pixels from decoded cover art
        var platterCenterX = (int)(640 * 0.28f);
        var platterCenterY = (int)(360 * 0.50f);
        var sampleAboveSpindle = artBmp.GetPixel(platterCenterX, platterCenterY - 22);
        (sampleAboveSpindle.Red + sampleAboveSpindle.Green + sampleAboveSpindle.Blue).Should().BeGreaterThan(90);

        // 2. Without AlbumArt -> procedural sleeve cover + TrackMoodVisual emblem
        var fallbackPkt = BuildPacket(demoDoc, settings, StageRenderSurfaceMode.StageCanvas, TimeSpan.FromSeconds(25.7), CreateAudio(), albumArt: null, moodVisual, renderTime: 1.25);
        router.AdvancePhysics(in fallbackPkt);
        using var fbSurf = SKSurface.Create(info);
        var fbCtx = new StyleRenderContext(fbSurf.Canvas, null, fonts, shaders, pool, legibility, fallbackPkt, false, false);
        router.Render(in fbCtx);
        using var fbBmp = SKBitmap.FromImage(fbSurf.Snapshot());
        CountLitPixels(fbBmp).Should().BeGreaterThan(700);
        Convert.ToHexString(SHA256.HashData(fbBmp.GetPixelSpan())).Should().NotBe(artHash);

        // 3. DesktopOverlay preserves all 4 viewport corners alpha == 0
        var desktopSettings = settings with { EdgeGlowMode = EdgeGlowMode.Off, AudioVisualizerMode = AudioVisualizerMode.None };
        var deskPkt = BuildPacket(demoDoc, desktopSettings, StageRenderSurfaceMode.DesktopOverlay, TimeSpan.FromSeconds(25.7), CreateAudio(), artPayload, moodVisual, renderTime: 2.0);
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
        var rmPkt = BuildPacket(demoDoc, settings with { ReducedMotion = true }, StageRenderSurfaceMode.StageCanvas, TimeSpan.FromSeconds(25.7), CreateAudio(), artPayload, moodVisual, renderTime: 3.0);
        router.AdvancePhysics(in rmPkt);
        using var rmSurf = SKSurface.Create(info);
        var rmCtx = new StyleRenderContext(rmSurf.Canvas, null, fonts, shaders, pool, legibility, rmPkt, false, false);
        router.Render(in rmCtx);
        using var rmBmp = SKBitmap.FromImage(rmSurf.Snapshot());
        CountLitPixels(rmBmp).Should().BeGreaterThan(600);
    }

    [Fact]
    public async Task BeatDropSettings_PreviewTrigger_And_VinylCatalogWiring_WorkEndToEnd()
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
            using var store = new JsonSettingsStore(settingsPath);
            var saved = AppSettings.Default with
            {
                ActiveStyle = OverlayStyleId.VinylGroove,
                BeatDropEffectEnabled = false,
                BeatDropIntensity = 1.40f,
                BeatDropSensitivity = 1.30f
            };
            await store.SaveAsync(saved);
            var reloaded = await store.LoadAsync();
            reloaded.ActiveStyle.Should().Be(OverlayStyleId.VinylGroove);
            reloaded.BeatDropEffectEnabled.Should().BeFalse();
            reloaded.BeatDropIntensity.Should().BeApproximately(1.40f, 0.001f);
            reloaded.BeatDropSensitivity.Should().BeApproximately(1.30f, 0.001f);

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
            await controller.SetBeatDropEffectEnabledAsync(true);

            controller.TriggerBeatDropPreview();
            var snap = controller.SampleFrame();
            snap.EffectiveAudioMetrics.DropTrigger.Should().BeGreaterThan(0.75f);
            snap.EffectiveAudioMetrics.ClimaxIntensity.Should().BeGreaterThan(0.75f);
            snap.EffectiveAudioMetrics.IsClimaxActive.Should().BeTrue();

            var vm = new StageViewModel(controller);
            vm.ApplySnapshot(snap);
            vm.IsDropClimaxActive.Should().BeTrue();
            vm.DropStatusLabel.Should().Be("DROP SURGE");
            vm.GalleryCountCaption.Should().StartWith("11 styles");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
