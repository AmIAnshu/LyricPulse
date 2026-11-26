using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
/// <see cref="TopLevel.RequestAnimationFrame"/>. Synchronizes physics advancement,
/// compositor rendering, GPU surface release, and renderer disposal under a dedicated lock
/// so native Skia handles are never accessed or freed concurrently across UI and compositor threads.
/// </summary>
public sealed class LyricCanvasControl : Control, IDisposable
{
    private readonly FontAssetRegistry _fonts;
    private readonly ShaderLibrary _shaders;
    private readonly OffscreenSurfacePool _surfacePool;
    private readonly LegibilityPipeline _legibility;
    private readonly FrameBudgetTelemetry _telemetry;
    private readonly Func<float, float, float, double, float, FrameRenderPacket> _packetFactory;
    private readonly Lock _renderSync = new();
    private readonly bool _ownsRenderer;

    private ILyricSceneRenderer _renderer;
    private TopLevel? _topLevel;
    private bool _isAttached;
    private bool _rafScheduled;
    private bool _hasLastFrameTime;
    private TimeSpan _lastFrameTime;
    private double _lastRenderTimeSeconds;
    private float _lastDeltaSeconds = 1f / 60f;
    private TimeSpan _lastCommittedFrameTime;
    private bool _hasCommittedFrame;
    private int _targetMaxFps;
    private bool _isPaused;
    private bool _singleFramePending;
    private bool _disposed;

    public LyricCanvasControl(
        FontAssetRegistry fonts,
        ShaderLibrary shaders,
        FrameBudgetTelemetry telemetry,
        ILyricSceneRenderer renderer,
        Func<float, float, float, double, float, FrameRenderPacket>? packetFactory = null,
        OffscreenSurfacePool? surfacePool = null,
        LegibilityPipeline? legibility = null,
        bool ownsRenderer = true)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(renderer);

        _fonts = fonts;
        _shaders = shaders;
        _telemetry = telemetry;
        _renderer = renderer;
        _ownsRenderer = ownsRenderer;
        _surfacePool = surfacePool ?? new OffscreenSurfacePool();
        _legibility = legibility ?? new LegibilityPipeline();
        _packetFactory = packetFactory ?? FrameRenderPacket.CreateDefault;

        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    public bool IsAnimationLoopActive => _rafScheduled;

    /// <summary>Optional frame-rate cap (e.g., 30 for unfocused Gallery cards; 0 = native vsync).</summary>
    public int TargetMaxFps
    {
        get => Volatile.Read(ref _targetMaxFps);
        set => Volatile.Write(ref _targetMaxFps, Math.Max(0, value));
    }

    /// <summary>When true, pauses RAF scheduling (e.g., offscreen Gallery cards or Pause Previews).</summary>
    public bool IsPaused
    {
        get => Volatile.Read(ref _isPaused);
        set
        {
            if (Volatile.Read(ref _isPaused) == value)
            {
                return;
            }

            Volatile.Write(ref _isPaused, value);
            if (!value)
            {
                RequestWake();
            }
            else
            {
                _rafScheduled = false;
            }
        }
    }

    public void SetRenderer(ILyricSceneRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        lock (_renderSync)
        {
            if (_disposed)
            {
                return;
            }

            if (_ownsRenderer && !ReferenceEquals(_renderer, renderer) && _renderer is IDisposable oldDisposable)
            {
                oldDisposable.Dispose();
            }

            _renderer = renderer;
        }

        RequestWake();
    }

    /// <summary>
    /// Wakes the vsync RAF loop after lyrics, playback state, style, visibility, or settings change.
    /// </summary>
    public void RequestWake()
    {
        if (Volatile.Read(ref _disposed) || Volatile.Read(ref _isPaused) || !_isAttached || !IsVisible)
        {
            return;
        }

        if (!_rafScheduled)
        {
            _hasLastFrameTime = false;
            _hasCommittedFrame = false;
            ScheduleNextFrame();
        }
    }

    /// <summary>
    /// Renders a single up-to-date frame even when <see cref="IsPaused"/> is true (zero idle RAF spin).
    /// </summary>
    public void RequestSingleFrame()
    {
        if (Volatile.Read(ref _disposed) || !_isAttached || !IsVisible)
        {
            return;
        }

        Volatile.Write(ref _singleFramePending, true);
        if (!_rafScheduled && _topLevel is not null)
        {
            _hasLastFrameTime = false;
            _hasCommittedFrame = false;
            _rafScheduled = true;
            _topLevel.RequestAnimationFrame(OnAnimationFrame);
        }
    }

    public void ReleaseGpuResources()
    {
        lock (_renderSync)
        {
            if (!_disposed)
            {
                _surfacePool.ReleaseAllSurfaces();
            }
        }
    }

    public void Dispose()
    {
        lock (_renderSync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _isAttached = false;
            _rafScheduled = false;
            _topLevel = null;

            if (_ownsRenderer && _renderer is IDisposable disposableRenderer)
            {
                disposableRenderer.Dispose();
            }

            _surfacePool.Dispose();
            _legibility.Dispose();
        }
    }

    [ExcludeFromCodeCoverage]
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        _isAttached = true;
        _topLevel = TopLevel.GetTopLevel(this);
        _hasLastFrameTime = false;
        if (IsVisible && (!Volatile.Read(ref _isPaused) || Volatile.Read(ref _singleFramePending)))
        {
            if (Volatile.Read(ref _singleFramePending) && _topLevel is not null && !_rafScheduled)
            {
                _rafScheduled = true;
                _topLevel.RequestAnimationFrame(OnAnimationFrame);
            }
            else
            {
                ScheduleNextFrame();
            }
        }
    }

    [ExcludeFromCodeCoverage]
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isAttached = false;
        _rafScheduled = false;
        _topLevel = null;
        ReleaseGpuResources();
    }

    [ExcludeFromCodeCoverage]
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

    [ExcludeFromCodeCoverage]
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Volatile.Read(ref _disposed) || !IsVisible)
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

        context.Custom(new LyricDrawOperation(this, bounds, packet));
    }

    [ExcludeFromCodeCoverage]
    private void ScheduleNextFrame()
    {
        if (Volatile.Read(ref _disposed) || Volatile.Read(ref _isPaused) || !_isAttached || !IsVisible || _rafScheduled || _topLevel is null)
        {
            return;
        }

        _rafScheduled = true;
        _topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    [ExcludeFromCodeCoverage]
    private void OnAnimationFrame(TimeSpan frameTime)
    {
        _rafScheduled = false;
        var isSingle = Volatile.Read(ref _singleFramePending);
        if (Volatile.Read(ref _disposed) || (Volatile.Read(ref _isPaused) && !isSingle) || !_isAttached || !IsVisible)
        {
            Volatile.Write(ref _singleFramePending, false);
            return;
        }

        if (!isSingle)
        {
            var maxFps = Volatile.Read(ref _targetMaxFps);
            if (maxFps > 0 && _hasCommittedFrame)
            {
                var minIntervalSec = 0.92 / maxFps;
                if ((frameTime - _lastCommittedFrameTime).TotalSeconds < minIntervalSec)
                {
                    ScheduleNextFrame();
                    return;
                }
            }
        }

        Volatile.Write(ref _singleFramePending, false);
        _lastCommittedFrameTime = frameTime;
        _hasCommittedFrame = true;

        var hasActiveMotion = AdvanceAnimationFrame(
            frameTime,
            (float)Math.Max(Bounds.Width, 1.0),
            (float)Math.Max(Bounds.Height, 1.0),
            (float)(_topLevel?.RenderScaling ?? 1.0));

        InvalidateVisual();

        if (hasActiveMotion && !Volatile.Read(ref _isPaused))
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

        lock (_renderSync)
        {
            return !_disposed && _renderer.AdvancePhysics(in packet);
        }
    }

    internal void RenderToCanvas(SKCanvas canvas, GRContext? grContext, float widthDip, float heightDip, float scaling)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        var packet = _packetFactory(
            Math.Max(widthDip, 1f),
            Math.Max(heightDip, 1f),
            scaling,
            _lastRenderTimeSeconds,
            _lastDeltaSeconds);

        RenderSynchronized(canvas, grContext, packet);
    }

    private void RenderSynchronized(SKCanvas canvas, GRContext? grContext, FrameRenderPacket packet)
    {
        lock (_renderSync)
        {
            if (_disposed)
            {
                return;
            }

            var startTimestamp = Stopwatch.GetTimestamp();
            _surfacePool.EnsureContextCurrent(grContext);

            var isGpuBackend = grContext is not null && !grContext.IsAbandoned;
            var shadersEnabled = isGpuBackend && !packet.ReducedMotion && _shaders.AreAllShadersReady;

            var saveCount = canvas.Save();
            try
            {
                canvas.ClipRect(new SKRect(0f, 0f, packet.ViewportWidthDip, packet.ViewportHeightDip));

                var renderCtx = new StyleRenderContext(
                    Canvas: canvas,
                    GrContext: grContext,
                    Fonts: _fonts,
                    Shaders: _shaders,
                    SurfacePool: _surfacePool,
                    Legibility: _legibility,
                    Packet: packet,
                    IsGpuBackend: isGpuBackend,
                    ShadersEnabled: shadersEnabled);

                _renderer.Render(in renderCtx);
            }
            finally
            {
                canvas.RestoreToCount(saveCount);
            }

            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            _telemetry.RecordFrame(elapsedMs, isGpuBackend, packet.ReducedMotion, _shaders.AreAllShadersReady);
        }
    }

    [ExcludeFromCodeCoverage]
    private sealed class LyricDrawOperation(
        LyricCanvasControl owner,
        Rect bounds,
        FrameRenderPacket packet) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
            // Shared rendering resources are owned by LyricCanvasControl.
        }

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null)
            {
                return;
            }

            using var lease = leaseFeature.Lease();
            owner.RenderSynchronized(lease.SkCanvas, lease.GrContext, packet);
        }
    }
}
