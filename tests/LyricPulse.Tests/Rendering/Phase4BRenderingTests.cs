using System.Collections.Immutable;
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

public sealed class Phase4BRenderingTests
{
    [Fact]
    public void AmbientPartyMesh_CompilesCleanly_RendersRichGradient_PureBlack_AndStaticFallback()
    {
        using var shaders = new ShaderLibrary();
        shaders.IsPartyMeshReady.Should().BeTrue();
        shaders.PartyMeshCompileError.Should().BeNull();
        shaders.AreAllShadersReady.Should().BeTrue();

        using var broken = ShaderLibrary.CreateFromSources(
            ShaderLibrary.LoadEmbeddedShaderSource(ShaderLibrary.EllipticalBulgeResourceName),
            ShaderLibrary.LoadEmbeddedShaderSource(ShaderLibrary.DirectionalMotionBlurResourceName),
            "invalid sksl {{{");
        broken.IsPartyMeshReady.Should().BeFalse();
        broken.PartyMeshCompileError.Should().NotBeNullOrWhiteSpace();
        broken.AreAllShadersReady.Should().BeFalse();

        using var fonts = new FontAssetRegistry();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var backdrop = new StageBackdropRenderer { AllowCpuShadersForTesting = true };

        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        var snap = BuildSnapshot(doc, TimeSpan.FromSeconds(1.5), AppSettings.Default with
        {
            StageBackdropMode = StageBackdropMode.AmbientPartyMesh,
            PartyEnergyLevel = 1.35f
        });

        var info = new SKImageInfo(320, 180, SKColorType.Bgra8888, SKAlphaType.Premul);

        // 1. DesktopOverlay MUST skip stage backdrop completely (100% transparent)
        var desktopPacket = OverlayFrameAdapter.FromPlaybackSnapshot(
            snap, 320, 180, 1f, 1.5, 1f / 60f, surfaceMode: StageRenderSurfaceMode.DesktopOverlay);
        desktopPacket.ShouldDrawStageBackdrop.Should().BeFalse();
        backdrop.Advance(in desktopPacket).Should().BeFalse();

        using (var desktopSurf = SKSurface.Create(info))
        {
            desktopSurf.Canvas.Clear(SKColors.Transparent);
            var ctx = new StyleRenderContext(desktopSurf.Canvas, null, fonts, shaders, pool, legibility, desktopPacket, false, false);
            backdrop.Render(in ctx);
            using var bmp = SKBitmap.FromImage(desktopSurf.Snapshot());
            bmp.GetPixel(160, 90).Alpha.Should().Be(0);
        }

        // 2. StageCanvas AmbientPartyMesh SkSL shader renders rich opaque non-uniform colors
        var meshPacket = OverlayFrameAdapter.FromPlaybackSnapshot(
            snap, 320, 180, 1f, 2.5, 1f / 60f,
            surfaceMode: StageRenderSurfaceMode.StageCanvas,
            stageBackdropOverride: StageBackdropMode.AmbientPartyMesh);
        meshPacket.ShouldDrawStageBackdrop.Should().BeTrue();
        backdrop.Advance(in meshPacket).Should().BeTrue();

        SKColor meshCenter;
        SKColor meshCorner;
        using (var meshSurf = SKSurface.Create(info))
        {
            meshSurf.Canvas.Clear(SKColors.Transparent);
            var ctx = new StyleRenderContext(meshSurf.Canvas, null, fonts, shaders, pool, legibility, meshPacket, false, false);
            backdrop.Render(in ctx);
            using var bmp = SKBitmap.FromImage(meshSurf.Snapshot());
            meshCenter = bmp.GetPixel(160, 90);
            meshCorner = bmp.GetPixel(2, 2);
            meshCenter.Alpha.Should().Be(255);
            meshCorner.Alpha.Should().Be(255);
            (meshCenter.Red + meshCenter.Green + meshCenter.Blue).Should().BeGreaterThan(35, "AmbientPartyMesh must emit rich colored orbs");
        }

        // 3. StageCanvas PureBlack renders crisp dark #050508 stage
        var blackPacket = meshPacket with { StageBackdrop = StageBackdropMode.PureBlack };
        backdrop.Advance(in blackPacket).Should().BeFalse();
        using (var blackSurf = SKSurface.Create(info))
        {
            blackSurf.Canvas.Clear(SKColors.Transparent);
            var ctx = new StyleRenderContext(blackSurf.Canvas, null, fonts, shaders, pool, legibility, blackPacket, false, false);
            backdrop.Render(in ctx);
            using var bmp = SKBitmap.FromImage(blackSurf.Snapshot());
            var corner = bmp.GetPixel(2, 2);
            corner.Alpha.Should().Be(255);
            (corner.Red + corner.Green + corner.Blue).Should().BeLessThan(35);
            corner.Should().NotBe(meshCenter);
        }

        // 4. Reduced Motion / Software fallback draws static gradient without SkSL dispatch
        var reducedPacket = meshPacket with { ReducedMotion = true };
        backdrop.Advance(in reducedPacket).Should().BeFalse();
        using (var reducedSurf = SKSurface.Create(info))
        {
            reducedSurf.Canvas.Clear(SKColors.Transparent);
            var ctx = new StyleRenderContext(reducedSurf.Canvas, null, fonts, shaders, pool, legibility, reducedPacket, false, false);
            backdrop.Render(in ctx);
            using var bmp = SKBitmap.FromImage(reducedSurf.Snapshot());
            var px = bmp.GetPixel(80, 54);
            px.Alpha.Should().Be(255);
            (px.Red + px.Green + px.Blue).Should().BeGreaterThan(25);
        }

        backdrop.Reset();
        backdrop.Dispose();
        backdrop.Dispose(); // idempotent
    }

    [Fact]
    public async Task AllTenKineticStyles_RenderDistinctVisibleFramesAcrossStageGalleryDesktopAndReducedMotion()
    {
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();

        using var matcher = new GloveSemanticVisualMatcher();
        await matcher.InitializeAsync();
        var rawDoc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        var annotated = matcher.AnnotateDocument(rawDoc, VisualPreference.Balanced);
        var moodVisual = matcher.InferTrackMoodVisual(annotated.Track, annotated, VisualPreference.Balanced);

        // Enrich lines with bilingual translation + romanization to exercise SecondaryLineMode across all 10 styles
        var bilingualLines = annotated.Lines.Select(static l => l with
        {
            TranslationText = "Translated luminous verse",
            RomanizedText = "Romanized neon pulse"
        }).ToImmutableArray();
        var doc = annotated with { Lines = bilingualLines };

        using var router = new LyricStyleRouter();
        router.RegisteredStyles.Should().HaveCount(11);
        router.BackdropRenderer.AllowCpuShadersForTesting = true;

        var allStyles = Enum.GetValues<OverlayStyleId>();
        var distinctStyleSignatures = new HashSet<long>();

        foreach (var styleId in allStyles)
        {
            router.ResolveRenderer(styleId).StyleId.Should().Be(styleId);

            // Enable CPU shaders for Fisheye / FisheyeVisual corner checks
            if (router.ResolveRenderer(styleId) is FisheyeStyleRenderer fisheye)
            {
                fisheye.AllowCpuShadersForTesting = true;
            }
            else if (router.ResolveRenderer(styleId) is FisheyeVisualStyleRenderer fisheyeVis)
            {
                fisheyeVis.AllowCpuShadersForTesting = true;
            }
            else if (router.ResolveRenderer(styleId) is VisualStackStyleRenderer visStack)
            {
                visStack.AllowCpuShadersForTesting = true;
            }

            var phaseSec = StyleLayoutHelper.GetGalleryPreviewPhaseOffsetSeconds(styleId);
            var settings = AppSettings.Default with
            {
                ActiveStyle = styleId,
                PartyEnergyLevel = 1.35f,
                FullscreenStageScale = 1.85f,
                ShowFollowingDot = true,
                ShowColorSweepLayer = true,
                SecondaryLineMode = SecondaryLineMode.Translation,
                StageBackdropMode = StageBackdropMode.AmbientPartyMesh
            };

            // A. StageCanvas at 1.85x Hero Scale + AmbientPartyMesh
            var stageInfo = new SKImageInfo(960, 540, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var stageSurface = SKSurface.Create(stageInfo))
            {
                for (var step = 0; step < 4; step++)
                {
                    var t = phaseSec + (step * 0.45);
                    var snap = BuildSnapshot(doc, TimeSpan.FromSeconds(t), settings, moodVisual: moodVisual);
                    var packet = OverlayFrameAdapter.FromPlaybackSnapshot(
                        snap, 960, 540, 1.0f, t, 1f / 60f,
                        surfaceMode: StageRenderSurfaceMode.StageCanvas);
                    packet.SafeHeroScale.Should().BeApproximately(1.85f, 0.001f);
                    router.AdvancePhysics(in packet);

                    if (step == 3)
                    {
                        var ctx = new StyleRenderContext(stageSurface.Canvas, null, fonts, shaders, pool, legibility, packet, false, false);
                        router.Render(in ctx);
                    }
                }

                using var stageBmp = SKBitmap.FromImage(stageSurface.Snapshot());
                stageBmp.GetPixel(480, 270).Alpha.Should().Be(255);
            }

            // B. DesktopOverlay (100% transparent backdrop, distinct style pixels)
            var desktopInfo = new SKImageInfo(640, 320, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var deskSurface = SKSurface.Create(desktopInfo))
            {
                var snap = BuildSnapshot(doc, TimeSpan.FromSeconds(phaseSec + 0.55), settings, moodVisual: moodVisual);
                var packet = OverlayFrameAdapter.FromPlaybackSnapshot(
                    snap, 640, 320, 1.0f, phaseSec + 0.55, 1f / 60f,
                    surfaceMode: StageRenderSurfaceMode.DesktopOverlay);
                packet.SafeHeroScale.Should().Be(1.0f);
                packet.ShouldDrawStageBackdrop.Should().BeFalse();

                router.AdvancePhysics(in packet);
                var ctx = new StyleRenderContext(deskSurface.Canvas, null, fonts, shaders, pool, legibility, packet, false, false);
                router.Render(in ctx);

                using var deskBmp = SKBitmap.FromImage(deskSurface.Snapshot());
                var lit = CountLitPixels(deskBmp, out var signature);
                lit.Should().BeGreaterThan(20, $"Style {styleId} must render visible pixels on DesktopOverlay");
                distinctStyleSignatures.Add(signature);

                // Fisheye & FisheyeVisual must keep all 4 corners strictly transparent even with CPU shaders
                if (styleId is OverlayStyleId.Fisheye or OverlayStyleId.FisheyeVisual)
                {
                    deskBmp.GetPixel(0, 0).Alpha.Should().Be(0);
                    deskBmp.GetPixel(639, 0).Alpha.Should().Be(0);
                    deskBmp.GetPixel(0, 319).Alpha.Should().Be(0);
                    deskBmp.GetPixel(639, 319).Alpha.Should().Be(0);
                }
            }

            // C. GalleryCard compact preview using StyleOverride (without changing ActiveStyle, even when LyricsVisible=false!)
            var cardInfo = new SKImageInfo(300, 180, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var cardSurface = SKSurface.Create(cardInfo))
            {
                var hiddenDesktopSettings = settings with { ActiveStyle = OverlayStyleId.Fisheye, LyricsVisible = false };
                var snap = BuildSnapshot(doc, TimeSpan.FromSeconds(phaseSec + 0.65), hiddenDesktopSettings, moodVisual: moodVisual);
                var packet = OverlayFrameAdapter.FromPlaybackSnapshot(
                    snap, 300, 180, 1.0f, phaseSec + 0.65, 1f / 60f,
                    surfaceMode: StageRenderSurfaceMode.GalleryCard,
                    styleOverride: styleId);
                packet.EffectiveStyle.Should().Be(styleId);
                packet.ActiveStyle.Should().Be(OverlayStyleId.Fisheye);
                packet.IsCompactCard.Should().BeTrue();

                router.AdvancePhysics(in packet);
                var ctx = new StyleRenderContext(cardSurface.Canvas, null, fonts, shaders, pool, legibility, packet, false, false);
                router.Render(in ctx);

                using var cardBmp = SKBitmap.FromImage(cardSurface.Snapshot());
                cardBmp.GetPixel(150, 90).Alpha.Should().Be(255);
            }

            // D. Reduced Motion + ShowFollowingDot=false + ShowColorSweepLayer=false + Romanization
            var reducedSettings = settings with
            {
                ReducedMotion = true,
                ShowFollowingDot = false,
                ShowColorSweepLayer = false,
                SecondaryLineMode = SecondaryLineMode.Romanization,
                HighContrastScrim = true
            };
            using (var rmSurface = SKSurface.Create(desktopInfo))
            {
                var snap = BuildSnapshot(doc, TimeSpan.FromSeconds(phaseSec + 0.90), reducedSettings, moodVisual: moodVisual);
                var packet = OverlayFrameAdapter.FromPlaybackSnapshot(
                    snap, 640, 320, 1.0f, phaseSec + 0.90, 1f / 60f,
                    surfaceMode: StageRenderSurfaceMode.DesktopOverlay);

                router.AdvancePhysics(in packet);
                var ctx = new StyleRenderContext(rmSurface.Canvas, null, fonts, shaders, pool, legibility, packet, false, false);
                router.Render(in ctx);

                using var rmBmp = SKBitmap.FromImage(rmSurface.Snapshot());
                CountLitPixels(rmBmp, out _).Should().BeGreaterThan(15, $"Style {styleId} must render cleanly under ReducedMotion");
            }
        }

        distinctStyleSignatures.Count.Should().BeGreaterThanOrEqualTo(8, "All 10 kinetic styles must produce visually distinct renders");
    }

    [Fact]
    public void Styles_HandleLongLines_TypingIndicator_AndWordlessPlainLinesCleanly()
    {
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var router = new LyricStyleRouter();

        var track = new TrackIdentity("Marathon Verse", "LyricPulse", "Album", "marathon verse", "lyricpulse", TimeSpan.FromSeconds(20), "mv");
        // 10-word line exercises NodeLine >8-word CameraDrift panning and Messages multi-line wrapping
        var longWords = Enumerable.Range(0, 10)
            .Select(i => new LyricWord($"word{i}", TimeSpan.FromSeconds(i * 0.4), TimeSpan.FromSeconds((i + 1) * 0.4), true))
            .ToImmutableArray();
        var longLine = new LyricLine(0, string.Join(' ', longWords.Select(static w => w.Text)), TimeSpan.Zero, TimeSpan.FromSeconds(4.0), longWords);
        // Wordless line exercises empty-Words fallback paths across styles
        var wordlessLine = new LyricLine(1, "Pure line synced chorus", TimeSpan.FromSeconds(4.2), TimeSpan.FromSeconds(8.0), ImmutableArray<LyricWord>.Empty);
        var doc = new LyricsDocument(track, LyricsSourceKind.Demo, "Demo", TimingKind.WordNative, [longLine, wordlessLine], TimeSpan.Zero, false);

        var info = new SKImageInfo(640, 360, SKColorType.Bgra8888, SKAlphaType.Premul);
        foreach (var styleId in new[] { OverlayStyleId.NodeLine, OverlayStyleId.Messages, OverlayStyleId.CodestyleVisual, OverlayStyleId.Constellation, OverlayStyleId.SpidersWeb, OverlayStyleId.KineticWall })
        {
            var settings = AppSettings.Default with { ActiveStyle = styleId };
            // 3.85s triggers Messages TimeUntilNextLine <= 450ms typing indicator + NodeLine activeIdx=9 camera pan!
            foreach (var sec in new[] { 3.85, 5.0 })
            {
                var snap = BuildSnapshot(doc, TimeSpan.FromSeconds(sec), settings);
                var packet = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 640, 360, 1f, sec, 1f / 60f);
                router.AdvancePhysics(in packet);

                using var surf = SKSurface.Create(info);
                var ctx = new StyleRenderContext(surf.Canvas, null, fonts, shaders, pool, legibility, packet, false, false);
                router.Render(in ctx);
                using var bmp = SKBitmap.FromImage(surf.Snapshot());
                CountLitPixels(bmp, out _).Should().BeGreaterThan(10);
            }
        }
    }

    [Fact]
    public void AestheticsOverlayRenderer_AndPerspectiveMatrix_BehaveDeterministically()
    {
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var aesthetics = new AestheticsOverlayRenderer();

        var packet = FrameRenderPacket.CreateDefault(400, 200) with
        {
            ShowFollowingDot = true,
            ShowColorSweepLayer = true,
            PartyEnergyLevel = 1.4f
        };

        aesthetics.AdvanceDot(100f, 100f, 1f / 60f, reducedMotion: false).Should().BeFalse(); // initial snap
        aesthetics.AdvanceDot(240f, 100f, 1f / 60f, reducedMotion: false).Should().BeTrue();  // hopping to next word

        var info = new SKImageInfo(400, 200, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var surf = SKSurface.Create(info))
        {
            surf.Canvas.Clear(SKColors.Transparent);
            var ctx = new StyleRenderContext(surf.Canvas, null, fonts, shaders, pool, legibility, packet, false, false);
            aesthetics.DrawWordColorSweep(surf.Canvas, new SKRect(80, 70, 320, 130), 0.55, packet, 1f);
            aesthetics.DrawInvertedCutoutWord(surf.Canvas, in ctx, fonts.GetOrCreateFont(LyricFontId.Nunito, 36f), "paper", 200f, 100f, 110f, -3.0f, 1f);
            aesthetics.DrawFollowingDot(surf.Canvas, packet, 200f, 140f, 5.5f, 1f);

            using var bmp = SKBitmap.FromImage(surf.Snapshot());
            CountLitPixels(bmp, out _).Should().BeGreaterThan(150);
        }

        // Reduced Motion suppresses Following Dot completely
        aesthetics.AdvanceDot(260f, 100f, 1f / 60f, reducedMotion: true).Should().BeFalse();
        using (var rmSurf = SKSurface.Create(info))
        {
            rmSurf.Canvas.Clear(SKColors.Transparent);
            var rmPacket = packet with { ReducedMotion = true };
            aesthetics.DrawFollowingDot(rmSurf.Canvas, rmPacket, 200f, 100f, 6f, 1f);
            using var bmp = SKBitmap.FromImage(rmSurf.Snapshot());
            CountLitPixels(bmp, out _).Should().Be(0);
        }

        // KineticWall 3D perspective matrix identity at 0 deg and valid finite homography at non-zero angles
        var identity = KineticWallStyleRenderer.Create3DPerspectiveMatrix(0f, 0f, 0f, 1100f, 400f, 200f);
        var mappedId = identity.MapPoint(new SKPoint(450f, 230f));
        mappedId.X.Should().BeApproximately(450f, 0.01f);
        mappedId.Y.Should().BeApproximately(230f, 0.01f);

        var tilted = KineticWallStyleRenderer.Create3DPerspectiveMatrix(6.5f, -10f, 1.2f, 1100f, 400f, 200f);
        var mappedTilt = tilted.MapPoint(new SKPoint(520f, 260f));
        float.IsFinite(mappedTilt.X).Should().BeTrue();
        float.IsFinite(mappedTilt.Y).Should().BeTrue();
        mappedTilt.X.Should().NotBeApproximately(520f, 0.5f);

        aesthetics.ResetDot();
        aesthetics.Dispose();
        aesthetics.Dispose(); // idempotent
    }

    [Fact]
    public async Task FontAssetRegistry_ConcurrentShaping_IsThreadSafeAcrossParallelGalleryCards()
    {
        using var fonts = new FontAssetRegistry();
        var jost = fonts.GetOrCreateFont(LyricFontId.Jost, 32f);
        var samples = new[] { "Chasing silver star", "東京の夜明け", "사랑하는 밤", "مرحبا بالنجوم", "नया सवेरा", "Überraschung!" };

        var tasks = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 12; i++)
            {
                var sample = samples[(worker + i) % samples.Length];
                var width = TextShaperService.MeasureWidth(sample, jost, fonts);
                width.Should().BeGreaterThan(0f);

                using var blob = fonts.ShapeToBlobSynchronized(jost.Typeface, sample, jost, out var adv);
                adv.Should().BeGreaterThanOrEqualTo(0f);
            }
        }));

        await Task.WhenAll(tasks);
    }

    private static OverlayPlaybackSnapshot BuildSnapshot(
        LyricsDocument doc,
        TimeSpan pos,
        AppSettings settings,
        WordVisualMatch? moodVisual = null) =>
        new(
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
            TrackMoodVisual: moodVisual);

    private static int CountLitPixels(SKBitmap bitmap, out long signature)
    {
        var count = 0;
        long sig = 17;
        for (var y = 0; y < bitmap.Height; y += 3)
        {
            for (var x = 0; x < bitmap.Width; x += 3)
            {
                var px = bitmap.GetPixel(x, y);
                if (px.Alpha > 20)
                {
                    count++;
                    sig = unchecked((sig * 31) + (x * 7) + (y * 13) + px.Red + px.Green + px.Blue);
                }
            }
        }

        signature = sig;
        return count;
    }
}
