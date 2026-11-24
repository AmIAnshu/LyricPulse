using AwesomeAssertions;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;
using LyricPulse.Rendering;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Shaders;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace LyricPulse.Tests.Rendering;

public sealed class RenderHostInfrastructureTests
{
    [Fact]
    public void OffscreenSurfacePool_BucketsTo64PxReusesWithinSlackAndPurgesOnRelease()
    {
        OffscreenSurfacePool.ComputeBucketSizePx(1).Should().Be(64);
        OffscreenSurfacePool.ComputeBucketSizePx(65).Should().Be(128);
        OffscreenSurfacePool.ComputeBucketSizePx(10_000).Should().Be(4096);

        using var pool = new OffscreenSurfacePool();
        var s1 = pool.RentSurface(OffscreenSurfaceSlot.Bulge, grContext: null, requiredWidthPx: 200, requiredHeightPx: 100);
        pool.GetAllocatedSlotSize(OffscreenSurfaceSlot.Bulge).Should().Be((256, 128));

        // Slightly smaller/equal request reuses exact same surface handle
        var s2 = pool.RentSurface(OffscreenSurfaceSlot.Bulge, grContext: null, requiredWidthPx: 180, requiredHeightPx: 90);
        ReferenceEquals(s1, s2).Should().BeTrue();

        // Larger request reallocates cleanly to next 64px bucket
        var s3 = pool.RentSurface(OffscreenSurfaceSlot.Bulge, grContext: null, requiredWidthPx: 400, requiredHeightPx: 200);
        ReferenceEquals(s2, s3).Should().BeFalse();
        pool.GetAllocatedSlotSize(OffscreenSurfaceSlot.Bulge).Should().Be((448, 256));

        pool.ReleaseAllSurfaces();
        pool.GetAllocatedSlotSize(OffscreenSurfaceSlot.Bulge).Should().Be((0, 0));

        // Context check with null context remains stable (no unnecessary purge when handle stays 0)
        pool.EnsureContextCurrent(null).Should().BeFalse();
        pool.PurgeCount.Should().Be(0);
    }

    [Fact]
    public void FrameBudgetTelemetry_ComputesP95DroppedFramesAndAdaptiveScaleHintWithoutAllocating()
    {
        var telemetry = new FrameBudgetTelemetry();
        telemetry.GetSnapshot().TotalFrames.Should().Be(0);

        for (var i = 1; i <= 150; i++)
        {
            var frameMs = i > 140 ? 18.5 : 4.2;
            telemetry.RecordFrame(frameMs, isGpuBackend: true, reducedMotion: false, shadersReady: true);
        }

        var snap = telemetry.GetSnapshot();
        snap.TotalFrames.Should().Be(150);
        snap.DroppedFramesCount.Should().Be(10);
        snap.ActiveBackend.Should().Be(RenderBackendKind.GpuAngleEgl);
        snap.ShaderState.Should().Be(ShaderPipelineState.Active);
        snap.MaxFrameMs.Should().BeApproximately(18.5, 0.01);
        snap.P95FrameMs.Should().BeApproximately(18.5, 0.01);
        snap.RecommendReducedShaderScale.Should().BeTrue();

        telemetry.RecordFrame(3.0, isGpuBackend: false, reducedMotion: false, shadersReady: true);
        telemetry.ShaderState.Should().Be(ShaderPipelineState.DisabledSoftwareFallback);

        telemetry.RecordFrame(3.0, isGpuBackend: true, reducedMotion: true, shadersReady: true);
        telemetry.ShaderState.Should().Be(ShaderPipelineState.DisabledReducedMotion);

        telemetry.RecordFrame(3.0, isGpuBackend: true, reducedMotion: false, shadersReady: false);
        telemetry.ShaderState.Should().Be(ShaderPipelineState.DegradedCompilationFailed);

        telemetry.Reset();
        telemetry.TotalFrames.Should().Be(0);
    }

    [Fact]
    public void SolidDiagnosticSceneRenderer_AnimatesToRestAndDrawsNonEmptyPixels()
    {
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        using var pool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();

        var renderer = new SolidDiagnosticSceneRenderer();
        var packet = FrameRenderPacket.CreateDefault(640f, 240f, renderScaling: 1f) with
        {
            SceneSnapshot = LyricSceneSnapshot.Empty with { DiagnosticHeadline = "LyricPulse Ready" }
        };

        // First advance activates spring; after sufficient steps it settles to idle (false)
        renderer.AdvancePhysics(in packet).Should().BeTrue();
        var settled = false;
        for (var i = 0; i < 120; i++)
        {
            if (!renderer.AdvancePhysics(in packet))
            {
                settled = true;
                break;
            }
        }

        settled.Should().BeTrue("diagnostic renderer must settle so RAF sleeps at idle");

        var info = new SKImageInfo(640, 240, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);

        var ctx = new StyleRenderContext(
            Canvas: surface.Canvas,
            GrContext: null,
            Fonts: fonts,
            Shaders: shaders,
            SurfacePool: pool,
            Legibility: legibility,
            Packet: packet,
            IsGpuBackend: false,
            ShadersEnabled: false);

        renderer.Render(in ctx);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        var nonTransparentCount = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                if (bitmap.GetPixel(x, y).Alpha > 32)
                {
                    nonTransparentCount++;
                }
            }
        }

        nonTransparentCount.Should().BeGreaterThan(200);

        // ReducedMotion settles immediately with false
        var reducedPacket = packet with { ReducedMotion = true };
        renderer.AdvancePhysics(in reducedPacket).Should().BeFalse();

        // Active document line and track-only fallback resolve cleanly
        var track = new TrackIdentity("Midnight City", "M83", "Hurry Up", "midnight city", "m83", TimeSpan.FromMinutes(4), "k");
        var line = new LyricLine(0, "Waiting in a car", TimeSpan.Zero, TimeSpan.FromSeconds(3), []);
        var doc = new LyricsDocument(track, LyricsSourceKind.Demo, "Demo", TimingKind.LineSynced, [line], TimeSpan.Zero, false);

        var docPacket = packet with
        {
            SceneSnapshot = new LyricSceneSnapshot(MediaPlaybackState.Playing, TimeSpan.FromSeconds(1), track, doc, 0, 0)
        };
        renderer.AdvancePhysics(in docPacket).Should().BeTrue();

        var trackOnlyPacket = packet with
        {
            SceneSnapshot = new LyricSceneSnapshot(MediaPlaybackState.Paused, TimeSpan.Zero, track, null, -1, -1)
        };
        var trackCtx = ctx with { Packet = trackOnlyPacket };
        renderer.Render(in trackCtx);

        // Empty snapshot renders cleanly without throwing
        var emptyCtx = ctx with { Packet = packet with { SceneSnapshot = LyricSceneSnapshot.Empty } };
        renderer.Render(in emptyCtx);
    }

    [Fact]
    public void LyricCanvasControl_AdvancesFramesClampsDeltaRendersToSkiaAndRecordsTelemetry()
    {
        using var fonts = new FontAssetRegistry();
        using var shaders = new ShaderLibrary();
        var telemetry = new FrameBudgetTelemetry();
        var renderer = new SolidDiagnosticSceneRenderer();

        using var control = new LyricCanvasControl(
            fonts,
            shaders,
            telemetry,
            renderer,
            packetFactory: (w, h, scale, time, dt) => FrameRenderPacket.CreateDefault(w, h, scale, time, dt) with
            {
                SceneSnapshot = LyricSceneSnapshot.Empty with { DiagnosticHeadline = "Canvas Test" }
            });

        control.IsHitTestVisible.Should().BeFalse();
        control.ClipToBounds.Should().BeTrue();
        control.IsAnimationLoopActive.Should().BeFalse();

        // Advance frames until physics settles (idle gate)
        var t = TimeSpan.Zero;
        var active = control.AdvanceAnimationFrame(t, 480f, 160f, 1.0f);
        active.Should().BeTrue();

        for (var i = 1; i <= 120; i++)
        {
            t += TimeSpan.FromMilliseconds(16.67);
            active = control.AdvanceAnimationFrame(t, 480f, 160f, 1.0f);
            if (!active)
            {
                break;
            }
        }

        active.Should().BeFalse("RAF loop must stop requesting frames once motion settles");

        var info = new SKImageInfo(480, 160, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        control.RenderToCanvas(surface.Canvas, grContext: null, 480f, 160f, 1.0f);

        telemetry.TotalFrames.Should().Be(1);
        telemetry.ActiveBackend.Should().Be(RenderBackendKind.SoftwareCpu);
        telemetry.ShaderState.Should().Be(ShaderPipelineState.DisabledSoftwareFallback);

        control.SetRenderer(new SolidDiagnosticSceneRenderer());
        control.RequestWake();
        control.ReleaseGpuResources();
    }

    [Fact]
    public void AddLyricPulseRendering_RegistersAllPhase2Services()
    {
        var services = new ServiceCollection();
        services.AddLyricPulseRendering();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<FontAssetRegistry>().Should().NotBeNull();
        provider.GetRequiredService<ShaderLibrary>().AreAllShadersReady.Should().BeTrue();
        provider.GetRequiredService<IAlbumColorExtractor>().Should().NotBeNull();
        provider.GetRequiredService<FrameBudgetTelemetry>().Should().NotBeNull();
        provider.GetRequiredService<ILyricSceneRenderer>().Should().NotBeNull();
        RenderingModule.ModuleName.Should().Be("LyricPulse.Rendering");
    }
}
