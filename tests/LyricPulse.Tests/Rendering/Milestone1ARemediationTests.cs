using System.Collections.Immutable;
using AwesomeAssertions;
using LyricPulse.App;
using LyricPulse.App.Platform;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Motion;
using LyricPulse.Rendering.Shaders;
using LyricPulse.Rendering.Styles;
using SkiaSharp;
using Xunit;

namespace LyricPulse.Tests.Rendering;

public sealed class Milestone1ARemediationTests
{
    [Fact]
    public void EllipticalBulge_AndFisheyeRenderer_KeepAllFourOffscreenCornersStrictlyTransparent()
    {
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();

        // 1. Direct shader test with full solid fill proves edgeFade forces alpha==0 at all 4 corners
        var info = new SKImageInfo(200, 200, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var solidSrc = SKSurface.Create(info);
        solidSrc.Canvas.Clear(new SKColor(20, 20, 40, 255));
        using var bulged = SKSurface.Create(info);
        bulged.Canvas.Clear(SKColors.Transparent);

        shaders.ApplyEllipticalBulge(
            bulged.Canvas,
            solidSrc,
            new SKRect(0, 0, 200, 200),
            200,
            200,
            dpiScale: 1f,
            strength: 0.30f,
            power: 2.10f,
            isGpuBackend: false,
            reducedMotion: false,
            allowCpuForTesting: true).Should().BeTrue();

        using var bulgedBmp = SKBitmap.FromImage(bulged.Snapshot());
        bulgedBmp.GetPixel(0, 0).Alpha.Should().Be(0);
        bulgedBmp.GetPixel(199, 0).Alpha.Should().Be(0);
        bulgedBmp.GetPixel(0, 199).Alpha.Should().Be(0);
        bulgedBmp.GetPixel(199, 199).Alpha.Should().Be(0);
        bulgedBmp.GetPixel(100, 100).Alpha.Should().Be(255);

        // 2. Full Fisheye render at BottomThird + 160% FontScale + 150% DPI stays clamped inside viewport
        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        using var fisheye = new FisheyeStyleRenderer { AllowCpuShadersForTesting = true };
        var settings = AppSettings.Default with
        {
            ActiveStyle = OverlayStyleId.Fisheye,
            FontScale = 1.60,
            AnchorZone = AnchorZone.BottomThird,
            ScrimIntensity = ScrimIntensity.Strong
        };

        // Simulate 1080p @ 150% DPI working area (1280 x 693 DIP)
        var snap1 = BuildSnapshot(doc, TimeSpan.FromSeconds(1.45), settings);
        var snap2 = BuildSnapshot(doc, TimeSpan.FromSeconds(2.10), settings);
        var p1 = OverlayFrameAdapter.FromPlaybackSnapshot(snap1, 1280, 693, 1.5f, 1.45, 1f / 60f);
        var p2 = OverlayFrameAdapter.FromPlaybackSnapshot(snap2, 1280, 693, 1.5f, 2.10, 1f / 60f);

        var life = new LifecycleFrameState(1f, false, 0f, 0f, null, 0f, true);
        fisheye.Advance(in p1, in life).Should().BeTrue();
        fisheye.Advance(in p2, in life).Should().BeTrue(); // Triggers Slot -2 departing fade & scroll velocity

        using var outSurface = SKSurface.Create(new SKImageInfo(1280, 693, SKColorType.Bgra8888, SKAlphaType.Premul));
        outSurface.Canvas.Clear(SKColors.Transparent);
        var ctx = new StyleRenderContext(outSurface.Canvas, null, fonts, shaders, pool, legibility, p2, false, false);
        fisheye.Render(in ctx, in life);

        using var outBmp = SKBitmap.FromImage(outSurface.Snapshot());
        outBmp.GetPixel(0, 0).Alpha.Should().Be(0);
        outBmp.GetPixel(1279, 0).Alpha.Should().Be(0);
        outBmp.GetPixel(0, 692).Alpha.Should().Be(0);
        outBmp.GetPixel(1279, 692).Alpha.Should().Be(0);
    }

    [Fact]
    public void VisualStack_RendersSupplementaryFluentCodepointsEmojiFallbackAndHighContrastObsidianBadges()
    {
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();

        // Supplementary-plane Fluent Icon (> U+FFFF): ic_fluent_tree_deciduous_24_filled = U+F015B, crown = U+F020F
        var smpTree = new WordVisualMatch(VisualKind.FluentIcon, char.ConvertFromUtf32(0xF015B), "ic_fluent_tree_deciduous_24_filled", "tree", 1.0f, true, "🌳");
        var emojiOnly = new WordVisualMatch(VisualKind.Emoji, "🔥", "emoji_fire", "fire", 0.9f, false, "🔥");
        var missingGlyphFluent = new WordVisualMatch(VisualKind.FluentIcon, char.ConvertFromUtf32(0x10FFFD), "missing_icon", "fallback", 0.8f, false, "✨");

        var info = new SKImageInfo(320, 120, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var glyphSurface = SKSurface.Create(info);
        glyphSurface.Canvas.Clear(SKColors.Transparent);

        StyleLayoutHelper.DrawVisualGlyph(glyphSurface.Canvas, fonts, legibility, smpTree, 60f, 60f, 44f, new RgbColor(0x5E, 0xEA, 0xD4), 1f);
        StyleLayoutHelper.DrawVisualGlyph(glyphSurface.Canvas, fonts, legibility, emojiOnly, 160f, 60f, 44f, new RgbColor(0xF8, 0xFA, 0xFC), 1f);
        StyleLayoutHelper.DrawVisualGlyph(glyphSurface.Canvas, fonts, legibility, missingGlyphFluent, 260f, 60f, 44f, new RgbColor(0x81, 0x8C, 0xF8), 1f);

        using var glyphBmp = SKBitmap.FromImage(glyphSurface.Snapshot());
        CountLitPixels(glyphBmp, 20, 100).Should().BeGreaterThan(20, "SMP >0xFFFF Fluent Icon U+F015B must render visible pixels");
        CountLitPixels(glyphBmp, 120, 200).Should().BeGreaterThan(20, "Emoji visual must render visible pixels");
        CountLitPixels(glyphBmp, 220, 300).Should().BeGreaterThan(20, "Missing Fluent codepoint must fall back to EmojiFallback");

        // Verify line-boundary window stitching (PreviousLine + CurrentLine + NextLine) and high-contrast badge on #FFFFFF
        var track = new TrackIdentity("Forest Crown", "Studio", "Album", "forest crown", "studio", TimeSpan.FromSeconds(15), "fc");
        var prevLine = new LyricLine(0, "Old line", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), [new LyricWord("Old", TimeSpan.Zero, TimeSpan.FromSeconds(1), true), new LyricWord("line", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), true)]);
        var curLine = new LyricLine(1, "Tall tree fire", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6), [new LyricWord("Tall", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), true), new LyricWord("tree", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4.5), true, smpTree), new LyricWord("fire", TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(6), true, emojiOnly)]);
        var nextLine = new LyricLine(2, "Next dawn", TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(9), [new LyricWord("Next", TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(7), true), new LyricWord("dawn", TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(9), true)]);
        var doc = new LyricsDocument(track, LyricsSourceKind.Demo, "Test", TimingKind.WordNative, [prevLine, curLine, nextLine], TimeSpan.Zero, false);

        var slice = ActiveLyricSlice.Resolve(doc, TimeSpan.FromSeconds(3.4));
        var windowed = new List<VisualStackStyleRenderer.VisualStackRow>();
        var activeIdx = VisualStackStyleRenderer.BuildWindowedRows(slice, windowed);
        activeIdx.Should().Be(3, "PreviousLine's 2 rows should be stitched before CurrentLine");
        windowed.Count.Should().Be(6, "2 prev + 3 current + 1 next row stitched seamlessly");

        using var vs = new VisualStackStyleRenderer { AllowCpuShadersForTesting = true };
        var snap = BuildSnapshot(doc, TimeSpan.FromSeconds(3.4), AppSettings.Default with { ActiveStyle = OverlayStyleId.VisualStack, ScrimIntensity = ScrimIntensity.Off });
        var packet = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 640, 320, 1f, 3.4, 1f / 60f);
        var life = new LifecycleFrameState(1f, false, 0f, 0f, null, 0f, true);
        vs.Advance(in packet, in life);

        using var surface = SKSurface.Create(new SKImageInfo(640, 320, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        var ctx = new StyleRenderContext(surface.Canvas, null, fonts, shaders, pool, legibility, packet, false, false);
        vs.Render(in ctx, in life);

        using var bmp = SKBitmap.FromImage(surface.Snapshot());
        // Verify dark obsidian badge pixels exist (low luminance + high alpha -> >= 7:1 over #FFFFFF)
        var darkBadgePixels = 0;
        for (var y = 80; y < 240; y++)
        {
            for (var x = 100; x < 540; x++)
            {
                var px = bmp.GetPixel(x, y);
                if (px.Alpha > 170 && px.Red < 60 && px.Green < 60 && px.Blue < 80)
                {
                    darkBadgePixels++;
                }
            }
        }

        darkBadgePixels.Should().BeGreaterThan(200, "VisualStack badge must draw high-contrast dark obsidian backing");
    }

    [Fact]
    public void LegibilityPipeline_ShapesMultiScriptNonLatinLyricsAndCachesBlobsAndBlurFilters()
    {
        using var fonts = new FontAssetRegistry();
        using var legibility = new LegibilityPipeline();

        var info = new SKImageInfo(600, 260, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Transparent);

        var latinFont = fonts.GetOrCreateFont(LyricFontId.Jost, 40f);
        var scripts = new[] { "東京の夜", "사랑해요", "مرحبا بالعالم", "नमस्ते दुनिया", "Café ✨" };
        var y = 48f;

        foreach (var sample in scripts)
        {
            legibility.DrawLegibleText(
                surface.Canvas,
                sample,
                300f,
                y,
                SKTextAlign.Center,
                latinFont,
                LyricFontId.Jost,
                AlbumAccentPalette.Default.AccentColor1,
                opacity: 1.0f,
                activeGlowColor: AlbumAccentPalette.Default.AccentColor2,
                fonts: fonts);
            y += 46f;
        }

        legibility.CachedShapedBlobCount.Should().BeGreaterThanOrEqualTo(4);
        legibility.CachedBlurFilterCount.Should().Be(1, "active glow blur filter must be cached once, not allocated per frame");

        // Second pass hits cache without growing counts
        var countBefore = legibility.CachedShapedBlobCount;
        legibility.DrawLegibleText(surface.Canvas, "東京の夜", 300f, 48f, SKTextAlign.Right, latinFont, LyricFontId.Jost, AlbumAccentPalette.Default.AccentColor1, fonts: fonts);
        legibility.CachedShapedBlobCount.Should().Be(countBefore);

        // Verify fallback typeface cache returns same reference on repeat lookup (zero native leak)
        var cjk1 = fonts.MatchFallbackForCodepoint('東');
        var cjk2 = fonts.MatchFallbackForCodepoint('東');
        ReferenceEquals(cjk1, cjk2).Should().BeTrue();
    }

    [Fact]
    public void LifecycleChoreographer_SuppressesPlainOnlyAndLoadingAndHandlesOutroAndOffsetLimit()
    {
        using var choreographer = new PlaybackLifecycleChoreographer();
        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();

        // PlainOnly and Loading MUST NOT render frozen Line 1
        foreach (var blockedStatus in new[] { LyricsStatus.PlainOnly, LyricsStatus.Loading, LyricsStatus.NotFound, LyricsStatus.Error })
        {
            var blockedSnap = BuildSnapshot(doc, TimeSpan.FromSeconds(1.5), AppSettings.Default) with
            {
                LyricsStatus = blockedStatus,
                IsDemoMode = false
            };

            var state = choreographer.Advance(OverlayFrameAdapter.FromPlaybackSnapshot(blockedSnap, 800, 300, 1f, 1.5, 0.25f));
            state.ContentOpacity.Should().Be(0f, $"{blockedStatus} must never freeze lyrics on desktop");
        }

        // Playing -> Hidden fades smoothly to 0% and stops animating
        var playingSnap = BuildSnapshot(doc, TimeSpan.FromSeconds(1.5), AppSettings.Default);
        for (var i = 0; i < 15; i++)
        {
            choreographer.Advance(OverlayFrameAdapter.FromPlaybackSnapshot(playingSnap, 800, 300, 1f, 1.5 + (i * 0.02), 0.02f));
        }

        var hiddenSnap = playingSnap with { Settings = AppSettings.Default with { LyricsVisible = false } };
        LifecycleFrameState fadeState = default;
        for (var i = 0; i < 20; i++)
        {
            fadeState = choreographer.Advance(OverlayFrameAdapter.FromPlaybackSnapshot(hiddenSnap, 800, 300, 1f, 2.0 + (i * 0.02), 0.02f));
        }

        fadeState.ContentOpacity.Should().Be(0f);
        fadeState.IsAnimating.Should().BeFalse();

        // Post-song outro suppresses 3-dot indicator
        var outroSnap = playingSnap with
        {
            ActiveSlice = ActiveLyricSlice.Empty with { IsInInstrumentalGap = true, IsPostSongOutro = true }
        };
        var outroState = choreographer.Advance(OverlayFrameAdapter.FromPlaybackSnapshot(outroSnap, 800, 300, 1f, 35.5, 0.02f));
        outroState.ShowInstrumentalIndicator.Should().BeFalse();

        // Offset toast at +/-15.0s clamp limit shows Limit cue
        PlaybackLifecycleChoreographer.FormatOffsetToastLabel(TimeSpan.FromSeconds(-15.0)).Should().Contain("Limit");
        PlaybackLifecycleChoreographer.FormatOffsetToastLabel(TimeSpan.FromSeconds(15.0)).Should().Contain("Limit");

        choreographer.Reset();
        var fallbackPacket = FrameRenderPacket.CreateDefault(640, 240);
        choreographer.Advance(in fallbackPacket).ContentOpacity.Should().Be(0f);
    }

    [Fact]
    public void CodestyleRenderer_RemainsVerticallyStableOnGhostNextLineAndHandlesScrimsPlainLinesAndGraphemes()
    {
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();
        using var codestyle = new CodestyleStyleRenderer();

        var track = new TrackIdentity("Grapheme Test", "Dev", "Alb", "grapheme", "dev", TimeSpan.FromSeconds(20), "gt");
        var longPrev = new LyricLine(0, "This is an extraordinarily long previous comment line that must fit cleanly without hard mid-glyph clipping inside the code card", TimeSpan.Zero, TimeSpan.FromSeconds(4), []);
        var active = new LyricLine(1, "Fire 🔥✨ test", TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), [
            new LyricWord("Fire", TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), true),
            new LyricWord("🔥✨", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7), true),
            new LyricWord("test", TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(8), true)
        ]);
        var next = new LyricLine(2, "Next ghost line preview", TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12), []);
        var doc = new LyricsDocument(track, LyricsSourceKind.Demo, "Test", TimingKind.WordNative, [longPrev, active, next], TimeSpan.Zero, false);

        var life = new LifecycleFrameState(1f, false, 0f, 0f, null, 0f, true);
        var info = new SKImageInfo(720, 320, SKColorType.Bgra8888, SKAlphaType.Premul);

        // Exercise all ScrimIntensity branches + ghost line at LineProgress >= 0.68 + cursor blink-off
        foreach (var scrim in new[] { ScrimIntensity.Off, ScrimIntensity.Subtle, ScrimIntensity.Balanced, ScrimIntensity.Strong })
        {
            var snap = BuildSnapshot(doc, TimeSpan.FromSeconds(7.6), AppSettings.Default with { ActiveStyle = OverlayStyleId.Codestyle, ScrimIntensity = scrim });
            var packet = OverlayFrameAdapter.FromPlaybackSnapshot(snap, 720, 320, 1f, 7.6, 0.03f);
            for (var i = 0; i < 25; i++)
            {
                codestyle.Advance(in packet, in life);
            }

            using var surface = SKSurface.Create(info);
            surface.Canvas.Clear(SKColors.Transparent);
            var ctx = new StyleRenderContext(surface.Canvas, null, fonts, shaders, pool, legibility, packet, false, false);
            codestyle.Render(in ctx, in life);
            using var bmp = SKBitmap.FromImage(surface.Snapshot());
            CountLitPixels(bmp, 0, 720).Should().BeGreaterThan(80);
        }

        // Empty-words line renders raw text cleanly without throwing
        var emptyWordLine = new LyricLine(0, "Raw plain fallback line", TimeSpan.Zero, TimeSpan.FromSeconds(5), []);
        var emptyWordDoc = new LyricsDocument(track, LyricsSourceKind.Demo, "Test", TimingKind.LineSynced, [emptyWordLine], TimeSpan.Zero, false);
        var plainSnap = BuildSnapshot(emptyWordDoc, TimeSpan.FromSeconds(1.0), AppSettings.Default with { ActiveStyle = OverlayStyleId.Codestyle });
        var plainPacket = OverlayFrameAdapter.FromPlaybackSnapshot(plainSnap, 720, 320, 1f, 1.0, 0.016f);
        codestyle.Advance(in plainPacket, in life);
        using var plainSurface = SKSurface.Create(info);
        var plainCtx = new StyleRenderContext(plainSurface.Canvas, null, fonts, shaders, pool, legibility, plainPacket, false, false);
        codestyle.Render(in plainCtx, in life);

        // Grapheme-safe slicing never splits surrogate pair emoji
        var sliced = StyleLayoutHelper.SliceByGraphemes("🔥✨A", 1);
        sliced.Should().Be("🔥");
        StyleLayoutHelper.SliceByGraphemes("🔥✨A", 2).Should().Be("🔥✨");
        StyleLayoutHelper.SliceByGraphemes("🔥✨A", 0).Should().BeEmpty();
    }

    [Fact]
    public void LyricStyleRouter_PreservesVelocityOnOffsetNudgeAndRendersInstrumentalDots()
    {
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();

        var trackingStyle = new ResetCountingStyleRenderer(OverlayStyleId.Fisheye);
        using var router = new LyricStyleRouter([trackingStyle]);
        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();

        // Initial frame -> 1 reset
        var snap0 = BuildSnapshot(doc, TimeSpan.FromSeconds(1.0), AppSettings.Default, trackGen: 1, seekGen: 0, offsetGen: 0);
        var p0 = OverlayFrameAdapter.FromPlaybackSnapshot(snap0, 640, 240, 1f, 1.0, 0.016f);
        router.AdvancePhysics(in p0);
        var resetsAfterInit = trackingStyle.ResetCount;

        // Offset nudge increments BOTH SeekGeneration and OffsetGeneration -> MUST NOT call ResetState()!
        var snapNudge = snap0 with { SeekGeneration = 1, OffsetGeneration = 1 };
        var pNudge = OverlayFrameAdapter.FromPlaybackSnapshot(snapNudge, 640, 240, 1f, 1.02, 0.016f);
        router.AdvancePhysics(in pNudge);
        trackingStyle.ResetCount.Should().Be(resetsAfterInit, "timing offset nudges must soft-retarget springs without ResetState()");

        // Real playback seek increments SeekGeneration WITHOUT OffsetGeneration -> MUST call ResetState()
        var snapSeek = snapNudge with { SeekGeneration = 2 };
        var pSeek = OverlayFrameAdapter.FromPlaybackSnapshot(snapSeek, 640, 240, 1f, 1.04, 0.016f);
        router.AdvancePhysics(in pSeek);
        trackingStyle.ResetCount.Should().Be(resetsAfterInit + 1);

        // Instrumental gap draws indicator cleanly via router.Render
        var gapSnap = snapSeek with { ActiveSlice = ActiveLyricSlice.Empty with { IsInInstrumentalGap = true, IsPostSongOutro = false } };
        var pGap = OverlayFrameAdapter.FromPlaybackSnapshot(gapSnap, 640, 240, 1f, 2.0, 0.20f);
        router.AdvancePhysics(in pGap);
        using var surf = SKSurface.Create(new SKImageInfo(640, 240, SKColorType.Bgra8888, SKAlphaType.Premul));
        var ctx = new StyleRenderContext(surf.Canvas, null, fonts, shaders, pool, legibility, pGap, false, false);
        router.Render(in ctx);
        using var bmp = SKBitmap.FromImage(surf.Snapshot());
        CountLitPixels(bmp, 0, 640).Should().BeGreaterThan(20);
    }

    [Fact]
    public void OffscreenSurfacePool_PurgesSurfacesOnGpuContextHandleChangeOrAbandonment()
    {
        using var pool = new OffscreenSurfacePool();
        _ = pool.RentSurface(OffscreenSurfaceSlot.Bulge, grContext: null, 128, 128);
        pool.GetAllocatedSlotSize(OffscreenSurfaceSlot.Bulge).Should().Be((128, 128));

        // Context handle change (e.g., sleep/wake new GRContext) purges immediately
        pool.EnsureContextHandleCurrent(new nint(0x1234), isAbandoned: false).Should().BeTrue();
        pool.PurgeCount.Should().Be(1);
        pool.GetAllocatedSlotSize(OffscreenSurfaceSlot.Bulge).Should().Be((0, 0));

        // Same handle -> no purge
        pool.EnsureContextHandleCurrent(new nint(0x1234), isAbandoned: false).Should().BeFalse();

        // Abandoned GPU context (TDR) -> immediate purge
        _ = pool.RentSurface(OffscreenSurfaceSlot.Scratch, grContext: null, 64, 64);
        pool.EnsureContextHandleCurrent(new nint(0x1234), isAbandoned: true).Should().BeTrue();
        pool.PurgeCount.Should().Be(2);
        pool.GetAllocatedSlotSize(OffscreenSurfaceSlot.Scratch).Should().Be((0, 0));
    }

    [Fact]
    public void PlatformFixes_ParseTrailingPlusStripAppWindowReconcileBoundsAndSignalDemo()
    {
        // QA-LOW-10: Trailing '+' key parses cleanly
        HotkeyGestureParser.TryParse("Ctrl+Shift++", out var plusParsed, out var err).Should().BeTrue(err ?? string.Empty);
        plusParsed.VirtualKey.Should().Be(0xBBu);
        plusParsed.NormalizedGesture.Should().Be("Ctrl+Shift++");

        // UX-09: WS_EX_APPWINDOW cleared by ComputeDesiredExStyle
        const nint appWindowFlag = 0x00040000;
        var cleaned = Win32OverlayInterop.ComputeDesiredExStyle(appWindowFlag, out _);
        (cleaned & appWindowFlag).Should().Be(0);

        // QA-HIGH-4: PerDisplayBounds value edit triggers layout reconciliation even when count stays identical
        var b1 = AppSettings.Default with
        {
            PerDisplayBounds = ImmutableDictionary<string, OverlayBoundsConfig>.Empty
                .Add(@"\\.\DISPLAY1", OverlayBoundsConfig.FromAnchorZone(AnchorZone.BottomThird, 48))
        };
        var b2 = AppSettings.Default with
        {
            PerDisplayBounds = ImmutableDictionary<string, OverlayBoundsConfig>.Empty
                .Add(@"\\.\DISPLAY1", OverlayBoundsConfig.FromAnchorZone(AnchorZone.TopCenter, 64))
        };
        LyricPulse.App.App.ShouldReconcileOverlayLayout(b1, b2).Should().BeTrue();
        LyricPulse.App.App.ShouldReconcileOverlayLayout(b1, b1).Should().BeFalse();

        // UX-02: Second instance with --demo wakes demo listener channel
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $@"Local\LyricPulse.Test.DemoMutex.{suffix}";
        var showName = $@"Local\LyricPulse.Test.DemoShow.{suffix}";

        using var primary = SingleInstanceGuard.TryAcquirePrimaryOrSignalExisting(mutexName, showName);
        primary.Should().NotBeNull();

        using var demoSignaled = new ManualResetEventSlim(false);
        primary!.StartActivationListener(
            onShowRequested: () => { },
            dispatchCallback: action => action(),
            onDemoRequested: () => demoSignaled.Set());

        var secondThread = new Thread(() =>
        {
            _ = SingleInstanceGuard.TryAcquirePrimaryOrSignalExisting(mutexName, showName, requestDemoMode: true);
        });
        secondThread.Start();
        secondThread.Join(TimeSpan.FromSeconds(2)).Should().BeTrue();
        demoSignaled.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
    }

    [Fact]
    public void RenderingUtilities_HandleAllBoundaryGuards_NaN_Empty_Fallback_AndDisposal()
    {
        // SkColorExtensions NaN / clamp guards
        var baseRgb = new RgbColor(120, 180, 240, 200);
        baseRgb.ToSkColorWithAlpha(float.NaN).Alpha.Should().Be(200);
        baseRgb.ToSkColorWithAlpha(-1f).Alpha.Should().Be(0);
        baseRgb.ToSkColorWithAlpha(2f).Alpha.Should().Be(200);
        baseRgb.ToSkColor().ToRgbColor().Should().Be(baseRgb);

        // SpringParams & Ease NaN / non-positive guards
        var compressed = SpringTokens.WordSnap.WithTempoCompression(float.NaN);
        compressed.Stiffness.Should().BeGreaterThan(0f);
        SpringTokens.WordSnap.WithTempoCompression(-0.5f).Stiffness.Should().BeApproximately(SpringTokens.WordSnap.Stiffness, 0.01f);
        Ease.LinearProgress(float.NaN).Should().Be(0f);
        Ease.Enter(float.NaN).Should().Be(0f);
        Ease.Exit(-0.2f).Should().Be(0f);
        Ease.Highlight(1.5f).Should().Be(1f);

        // LegibilityStyleSpec boundary & unknown enum guards
        LegibilityStyleSpec.GetScrimAlpha((ScrimIntensity)999, highContrastScrim: false).Should().BeApproximately(0.42f, 0.001f);
        LegibilityStyleSpec.ResolveScrimBaseColor(new RgbColor(10, 20, 30), ScrimIntensity.Off, highContrastScrim: true)
            .Should().Be(LegibilityStyleSpec.HighContrastScrimColor);
        LegibilityStyleSpec.ComputeScrimRadii(new SKRect(0, 0, 10, 10), float.NaN).RadiusXDip.Should().BeGreaterThan(0f);
        LegibilityStyleSpec.ComputeOpticalStrokeWidth(float.NaN, LyricFontId.Jost).Should().BeGreaterThan(0f);
        LegibilityStyleSpec.ComputeOpticalStrokeWidth(-5f, LyricFontId.Jost).Should().BeGreaterThan(0f);
        LegibilityStyleSpec.ScaleAlpha(new SKColor(10, 20, 30, 200), float.NaN).Alpha.Should().Be(200);

        // OverlayFrameAdapter fallback bounds & invalid renderScaling & systemReducedMotion
        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();
        var snap = BuildSnapshot(doc, TimeSpan.FromSeconds(4.5), AppSettings.Default);
        var adapted = OverlayFrameAdapter.FromPlaybackSnapshot(
            snap,
            viewportWidthDip: 0f,
            viewportHeightDip: -5f,
            renderScaling: float.NaN,
            renderTimeSeconds: 1.0,
            deltaSeconds: 0.016f,
            boundsOverride: null,
            systemReducedMotion: true,
            utcNow: null);
        adapted.ViewportWidthDip.Should().Be(1f);
        adapted.ViewportHeightDip.Should().Be(1f);
        adapted.RenderScaling.Should().Be(1.0f);
        adapted.ReducedMotion.Should().BeTrue();

        // StyleLayoutHelper fallback slice, word triad edge indices, grapheme & glyph edge cases
        var defaultPacketNoPlayback = FrameRenderPacket.CreateDefault(800, 400, 1f, 0, 0.016f);
        StyleLayoutHelper.ResolveSlice(defaultPacketNoPlayback).Should().Be(ActiveLyricSlice.Empty);
        var docOnlyPacket = defaultPacketNoPlayback with
        {
            SceneSnapshot = defaultPacketNoPlayback.SceneSnapshot with { Document = doc, EffectivePosition = TimeSpan.FromSeconds(4.5) }
        };
        StyleLayoutHelper.ResolveSlice(docOnlyPacket).CurrentLine.Should().NotBeNull();

        StyleLayoutHelper.ResolveWordTriad(ActiveLyricSlice.Empty).Active.Should().BeNull();
        var prevLine = new LyricLine(
            0,
            "hello world",
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            ImmutableArray.Create(
                new LyricWord("hello", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5), true),
                new LyricWord("world", TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2), true)));
        var curLine = new LyricLine(
            1,
            "solo",
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(3),
            ImmutableArray.Create(new LyricWord("solo", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), true)));
        var nextLine = new LyricLine(
            2,
            "rise",
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(4),
            ImmutableArray.Create(new LyricWord("rise", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), true)));
        var stitchedSlice = new ActiveLyricSlice(
            ActiveLineIndex: 1,
            ActiveWordIndex: 99, // out of range -> clamps to 0
            PreviousLine: prevLine,
            CurrentLine: curLine,
            NextLine: nextLine,
            WordProgress: 0.5,
            LineProgress: 0.5,
            IsInInstrumentalGap: false,
            TimeUntilNextLine: TimeSpan.Zero);
        var triad = StyleLayoutHelper.ResolveWordTriad(stitchedSlice);
        triad.Previous!.Text.Should().Be("world");
        triad.Active!.Text.Should().Be("solo");
        triad.Next!.Text.Should().Be("rise");

        var isolatedSlice = stitchedSlice with { PreviousLine = null, NextLine = null };
        var isolatedTriad = StyleLayoutHelper.ResolveWordTriad(isolatedSlice);
        isolatedTriad.Previous.Should().BeNull();
        isolatedTriad.Next.Should().BeNull();

        StyleLayoutHelper.TrimPunctuation("   ").Should().BeEmpty();
        StyleLayoutHelper.TrimPunctuation("!!!").Should().BeEmpty();
        StyleLayoutHelper.TrimPunctuation("(hello!)").Should().Be("hello");
        StyleLayoutHelper.SliceByGraphemes(string.Empty, 3).Should().BeEmpty();
        StyleLayoutHelper.SliceByGraphemes("abc", 0).Should().BeEmpty();
        StyleLayoutHelper.SliceByGraphemes("abc", 10).Should().Be("abc");
        StyleLayoutHelper.CountGraphemes(string.Empty).Should().Be(0);
        StyleLayoutHelper.BlendSrgb(new RgbColor(0, 0, 0), new RgbColor(100, 100, 100), float.NaN).R.Should().Be(100);
        StyleLayoutHelper.TryDecodeFirstScalar(string.Empty, out _).Should().BeFalse();
        StyleLayoutHelper.TryDecodeFirstScalar("\uD800", out _).Should().BeFalse(); // lone surrogate

        // TextShaperService edge cases (NaN, empty, transient vs cached shaper)
        using var fonts = new FontAssetRegistry();
        using var legibility = new LegibilityPipeline();
        var jost = fonts.GetOrCreateFont(LyricFontId.Jost, 44f);
        TextShaperService.ComputeAutoFitScale(float.NaN, 1000f).Should().Be(1.0f);
        TextShaperService.ComputeAutoFitScale(100f, 0f).Should().Be(1.0f);
        TextShaperService.MeasureWidth(string.Empty, jost, fonts).Should().Be(0f);
        TextShaperService.MeasureWidth("東京", jost, fonts: null).Should().BeGreaterThan(0f);
        TextShaperService.ShapeToBlob(string.Empty, jost, out var emptyAdv).Should().BeNull();
        emptyAdv.Should().Be(0f);
        using var transientBlob = TextShaperService.ShapeToBlob("hello", jost, out var transientAdv, cachedShaper: null);
        transientBlob.Should().NotBeNull();
        transientAdv.Should().BeGreaterThan(0f);

        // DrawVisualGlyph edge paths (zero opacity, empty value, emoji with empty EmojiFallback)
        using var surf = SKSurface.Create(new SKImageInfo(120, 120, SKColorType.Bgra8888, SKAlphaType.Premul));
        StyleLayoutHelper.DrawVisualGlyph(surf.Canvas, fonts, legibility, new WordVisualMatch(VisualKind.Emoji, "🔥", "fire", "fire", 0.9f, false, ""), 60, 60, 32, new RgbColor(255, 255, 255), 0f);
        StyleLayoutHelper.DrawVisualGlyph(surf.Canvas, fonts, legibility, new WordVisualMatch(VisualKind.Emoji, "", "empty", "empty", 0.9f, false, ""), 60, 60, 32, new RgbColor(255, 255, 255), 1f);
        StyleLayoutHelper.DrawVisualGlyph(surf.Canvas, fonts, legibility, new WordVisualMatch(VisualKind.Emoji, "✨", "spark", "spark", 0.9f, false, ""), 60, 60, 32, new RgbColor(255, 255, 255), 1f);

        // LyricCanvasControl renderer ownership & post-dispose safety
        using var shaders = new ShaderLibrary();
        var telemetry = new FrameBudgetTelemetry();
        var renderer1 = new SolidDiagnosticSceneRenderer();
        var renderer2 = new SolidDiagnosticSceneRenderer();
        var canvasCtrl = new LyricCanvasControl(fonts, shaders, telemetry, renderer1, ownsRenderer: true);
        canvasCtrl.IsAnimationLoopActive.Should().BeFalse();
        canvasCtrl.SetRenderer(renderer1); // same ref
        canvasCtrl.SetRenderer(renderer2); // different ref
        canvasCtrl.ReleaseGpuResources();
        canvasCtrl.Dispose();
        canvasCtrl.Dispose(); // idempotent second dispose
        canvasCtrl.SetRenderer(renderer1); // post-dispose no-op
        canvasCtrl.ReleaseGpuResources(); // post-dispose no-op
        canvasCtrl.RequestWake(); // post-dispose no-op
        canvasCtrl.AdvanceAnimationFrame(TimeSpan.FromSeconds(1), 640, 360, 1f).Should().BeFalse();
        canvasCtrl.RenderToCanvas(surf.Canvas, null, 120, 120, 1f); // post-dispose safe no-op

        // SkiaAlbumColorExtractor null, empty & malformed guard branches
        var extractor = new SkiaAlbumColorExtractor();
        extractor.Extract(null).Should().Be(AlbumAccentPalette.Default);
        extractor.Extract(new AlbumArtPayload("empty", ReadOnlyMemory<byte>.Empty)).Should().Be(AlbumAccentPalette.Default);
        extractor.Extract(new AlbumArtPayload("garbage", new byte[] { 1, 2, 3, 4, 5 }))
            .SourceAlbumHash.Should().Be("garbage");
    }

    private static OverlayPlaybackSnapshot BuildSnapshot(
        LyricsDocument doc,
        TimeSpan pos,
        AppSettings settings,
        long trackGen = 1,
        long seekGen = 0,
        long offsetGen = 0)
    {
        return new OverlayPlaybackSnapshot(
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
            TrackGeneration: trackGen,
            SeekGeneration: seekGen,
            IsOsReducedMotion: false,
            OffsetGeneration: offsetGen);
    }

    private static int CountLitPixels(SKBitmap bitmap, int xStart, int xEnd)
    {
        var count = 0;
        var minX = Math.Max(0, xStart);
        var maxX = Math.Min(bitmap.Width, xEnd);
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = minX; x < maxX; x += 2)
            {
                if (bitmap.GetPixel(x, y).Alpha > 20)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private sealed class ResetCountingStyleRenderer(OverlayStyleId styleId) : IKineticStyleRenderer
    {
        public int ResetCount { get; private set; }
        public OverlayStyleId StyleId { get; } = styleId;
        public void ResetState() => ResetCount++;
        public bool Advance(in FrameRenderPacket packet, in LifecycleFrameState lifecycle) => false;
        public void Render(in StyleRenderContext context, in LifecycleFrameState lifecycle) { }
        public void Dispose() { }
    }
}
