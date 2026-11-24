using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Shaders;
using SkiaSharp;

namespace LyricPulse.Rendering.Host;

/// <summary>
/// GPU-accelerated SkiaSharp Avalonia control driven by vsync-aligned
/// <see cref="TopLevel.RequestAnimationFrame"/>. Automatically halts animation frames when idle,
/// hidden, or detached to guarantee &lt;3% idle CPU.
/// </summary>
public sealed class LyricCanvasControl : Control, IDisposable
{
    private readonly FontAssetRegistry _fonts;
    private readonly ShaderLibrary _shaders;
    private readonly OffscreenSurfacePool _surfacePool;
    private readonly LegibilityPipeline _legibility;
    private readonly FrameBudgetTelemetry _telemetry;
    private readonly Func<float, float, float, double, float, FrameRenderPacket> _packetFactory;

    private ILyricSceneRenderer _renderer;
    private TopLevel? _topLevel;
    private bool _isAttached;
    private bool _rafScheduled;
    private bool _hasLastFrameTime;
    private TimeSpan _lastFrameTime;
    private double _lastRenderTimeSeconds;
    private float _lastDeltaSeconds = 1f / 60f;
    private bool _disposed;

    public LyricCanvasControl(
        FontAssetRegistry fonts,
        ShaderLibrary shaders,
        FrameBudgetTelemetry telemetry,
        ILyricSceneRenderer renderer,
        Func<float, float, float, double, float, FrameRenderPacket>? packetFactory = null,
        OffscreenSurfacePool? surfacePool = null,
        LegibilityPipeline? legibility = null)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(renderer);

        _fonts = fonts;
        _shaders = shaders;
        _telemetry = telemetry;
        _renderer = renderer;
        _surfacePool = surfacePool ?? new OffscreenSurfacePool();
        _legibility = legibility ?? new LegibilityPipeline();
        _packetFactory = packetFactory ?? FrameRenderPacket.CreateDefault;

        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    public bool IsAnimationLoopActive => _rafScheduled;

    public void SetRenderer(ILyricSceneRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        RequestWake();
    }

    /// <summary>
    /// Wakes the vsync RAF loop after lyrics, playback state, style, visibility, or settings change.
    /// </summary>
    public void RequestWake()
    {
        if (_disposed || !_isAttached || !IsVisible)
        {
            return;
        }

        if (!_rafScheduled)
        {
            _hasLastFrameTime = false;
            ScheduleNextFrame();
        }
    }

    public void ReleaseGpuResources()
    {
        _surfacePool.ReleaseAllSurfaces();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isAttached = false;
        _rafScheduled = false;
        _topLevel = null;
        _surfacePool.Dispose();
        _legibility.Dispose();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_disposed)
        {
            return;
        }

        _isAttached = true;
        _topLevel = TopLevel.GetTopLevel(this);
        _hasLastFrameTime = false;
        if (IsVisible)
        {
            ScheduleNextFrame();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isAttached = false;
        _rafScheduled = false;
        _topLevel = null;
        _surfacePool.ReleaseAllSurfaces();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            if (IsVisible)
            {
                RequestWake();
            }
            else
            {
                _rafScheduled = false;
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_disposed || !IsVisible)
        {
            return;
        }

        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 1.0 || bounds.Height <= 1.0)
        {
            return;
        }

        var scaling = (float)(_topLevel?.RenderScaling ?? 1.0);
        var packet = _packetFactory(
            (float)bounds.Width,
            (float)bounds.Height,
            scaling,
            _lastRenderTimeSeconds,
            _lastDeltaSeconds);

        context.Custom(new LyricDrawOperation(
            bounds,
            packet,
            _renderer,
            _fonts,
            _shaders,
            _surfacePool,
            _legibility,
            _telemetry));
    }

    private void ScheduleNextFrame()
    {
        if (_disposed || !_isAttached || !IsVisible || _rafScheduled || _topLevel is null)
        {
            return;
        }

        _rafScheduled = true;
        _topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan frameTime)
    {
        _rafScheduled = false;
        if (_disposed || !_isAttached || !IsVisible)
        {
            return;
        }

        var hasActiveMotion = AdvanceAnimationFrame(frameTime, (float)Math.Max(Bounds.Width, 1.0), (float)Math.Max(Bounds.Height, 1.0), (float)(_topLevel?.RenderScaling ?? 1.0));
        InvalidateVisual();

        if (hasActiveMotion)
        {
            ScheduleNextFrame();
        }
    }

    internal bool AdvanceAnimationFrame(TimeSpan frameTime, float widthDip, float heightDip, float scaling)
    {
        var dt = _hasLastFrameTime
            ? Math.Clamp((float)(frameTime - _lastFrameTime).TotalSeconds, 0f, Motion.SpringState.MaxDeltaSeconds)
            : 1f / 60f;

        _lastFrameTime = frameTime;
        _hasLastFrameTime = true;
        _lastRenderTimeSeconds = frameTime.TotalSeconds;
        _lastDeltaSeconds = dt;

        var packet = _packetFactory(
            Math.Max(widthDip, 1f),
            Math.Max(heightDip, 1f),
            scaling,
            _lastRenderTimeSeconds,
            dt);

        return _renderer.AdvancePhysics(in packet);
    }

    internal void RenderToCanvas(SKCanvas canvas, GRContext? grContext, float widthDip, float heightDip, float scaling)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (_disposed)
        {
            return;
        }

        var packet = _packetFactory(
            Math.Max(widthDip, 1f),
            Math.Max(heightDip, 1f),
            scaling,
            _lastRenderTimeSeconds,
            _lastDeltaSeconds);

        ExecuteSkiaRender(canvas, grContext, packet, _renderer, _fonts, _shaders, _surfacePool, _legibility, _telemetry);
    }

    private static void ExecuteSkiaRender(
        SKCanvas canvas,
        GRContext? grContext,
        FrameRenderPacket packet,
        ILyricSceneRenderer renderer,
        FontAssetRegistry fonts,
        ShaderLibrary shaders,
        OffscreenSurfacePool surfacePool,
        LegibilityPipeline legibility,
        FrameBudgetTelemetry telemetry)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        surfacePool.EnsureContextCurrent(grContext);

        var isGpuBackend = grContext is not null && !grContext.IsAbandoned;
        var shadersEnabled = isGpuBackend && !packet.ReducedMotion && shaders.AreAllShadersReady;

        var saveCount = canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(0f, 0f, packet.ViewportWidthDip, packet.ViewportHeightDip));

            var renderCtx = new StyleRenderContext(
                Canvas: canvas,
                GrContext: grContext,
                Fonts: fonts,
                Shaders: shaders,
                SurfacePool: surfacePool,
                Legibility: legibility,
                Packet: packet,
                IsGpuBackend: isGpuBackend,
                ShadersEnabled: shadersEnabled);

            renderer.Render(in renderCtx);
        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }

        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        telemetry.RecordFrame(elapsedMs, isGpuBackend, packet.ReducedMotion, shaders.AreAllShadersReady);
    }

    private sealed class LyricDrawOperation(
        Rect bounds,
        FrameRenderPacket packet,
        ILyricSceneRenderer renderer,
        FontAssetRegistry fonts,
        ShaderLibrary shaders,
        OffscreenSurfacePool surfacePool,
        LegibilityPipeline legibility,
        FrameBudgetTelemetry telemetry) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
            // Shared rendering resources are owned by LyricCanvasControl/DI container.
        }

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null)
            {
                return;
            }

            using var lease = leaseFeature.Lease();
            ExecuteSkiaRender(
                lease.SkCanvas,
                lease.GrContext,
                packet,
                renderer,
                fonts,
                shaders,
                surfacePool,
                legibility,
                telemetry);
        }
    }
}
