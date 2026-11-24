namespace LyricPulse.Rendering.Host;

public enum RenderBackendKind
{
    Unknown = 0,
    GpuAngleEgl = 1,
    SoftwareCpu = 2
}

public enum ShaderPipelineState
{
    Unknown = 0,
    Active = 1,
    DisabledSoftwareFallback = 2,
    DisabledReducedMotion = 3,
    DegradedCompilationFailed = 4
}

public readonly record struct FrameTelemetrySnapshot(
    long TotalFrames,
    long DroppedFramesCount,
    double LastFrameMs,
    double AverageFrameMs,
    double P95FrameMs,
    double MaxFrameMs,
    RenderBackendKind ActiveBackend,
    ShaderPipelineState ShaderState,
    bool RecommendReducedShaderScale);

/// <summary>
/// Lock-free 120-sample circular ring buffer tracking per-frame SkiaSharp render timings
/// with zero per-frame managed heap allocations.
/// </summary>
public sealed class FrameBudgetTelemetry
{
    public const int SampleCapacity = 120;
    public const double DefaultFrameBudgetMs = 16.67;
    public const double AdaptiveScaleDownThresholdMs = 14.0;

    private readonly double[] _samples = new double[SampleCapacity];
    private long _totalFrames;
    private long _droppedFrames;
    private double _lastFrameMs;
    private int _backend = (int)RenderBackendKind.Unknown;
    private int _shaderState = (int)ShaderPipelineState.Unknown;

    public long TotalFrames => Volatile.Read(ref _totalFrames);

    public long DroppedFramesCount => Volatile.Read(ref _droppedFrames);

    public double LastFrameMs => Volatile.Read(ref _lastFrameMs);

    public RenderBackendKind ActiveBackend => (RenderBackendKind)Volatile.Read(ref _backend);

    public ShaderPipelineState ShaderState => (ShaderPipelineState)Volatile.Read(ref _shaderState);

    public void RecordFrame(
        double elapsedMs,
        bool isGpuBackend,
        bool reducedMotion,
        bool shadersReady,
        double targetFrameBudgetMs = DefaultFrameBudgetMs)
    {
        var safeMs = double.IsFinite(elapsedMs) && elapsedMs >= 0.0 ? elapsedMs : 0.0;
        var frameNumber = Interlocked.Increment(ref _totalFrames);
        var slot = (int)((frameNumber - 1) % SampleCapacity);

        Volatile.Write(ref _samples[slot], safeMs);
        Volatile.Write(ref _lastFrameMs, safeMs);

        if (safeMs > targetFrameBudgetMs)
        {
            Interlocked.Increment(ref _droppedFrames);
        }

        Volatile.Write(ref _backend, (int)(isGpuBackend ? RenderBackendKind.GpuAngleEgl : RenderBackendKind.SoftwareCpu));
        Volatile.Write(ref _shaderState, (int)ResolveShaderState(isGpuBackend, reducedMotion, shadersReady));
    }

    public FrameTelemetrySnapshot GetSnapshot()
    {
        var total = Volatile.Read(ref _totalFrames);
        var count = (int)Math.Min(total, SampleCapacity);
        if (count == 0)
        {
            return new FrameTelemetrySnapshot(
                TotalFrames: 0,
                DroppedFramesCount: 0,
                LastFrameMs: 0.0,
                AverageFrameMs: 0.0,
                P95FrameMs: 0.0,
                MaxFrameMs: 0.0,
                ActiveBackend: ActiveBackend,
                ShaderState: ShaderState,
                RecommendReducedShaderScale: false);
        }

        Span<double> scratch = stackalloc double[count];
        var sum = 0.0;
        var max = 0.0;

        for (var i = 0; i < count; i++)
        {
            var value = Volatile.Read(ref _samples[i]);
            scratch[i] = value;
            sum += value;
            if (value > max)
            {
                max = value;
            }
        }

        scratch.Sort();
        var p95Index = Math.Clamp((int)Math.Ceiling(count * 0.95) - 1, 0, count - 1);
        var p95 = scratch[p95Index];

        return new FrameTelemetrySnapshot(
            TotalFrames: total,
            DroppedFramesCount: DroppedFramesCount,
            LastFrameMs: LastFrameMs,
            AverageFrameMs: sum / count,
            P95FrameMs: p95,
            MaxFrameMs: max,
            ActiveBackend: ActiveBackend,
            ShaderState: ShaderState,
            RecommendReducedShaderScale: count >= 60 && p95 > AdaptiveScaleDownThresholdMs);
    }

    public void Reset()
    {
        for (var i = 0; i < SampleCapacity; i++)
        {
            Volatile.Write(ref _samples[i], 0.0);
        }

        Volatile.Write(ref _totalFrames, 0);
        Volatile.Write(ref _droppedFrames, 0);
        Volatile.Write(ref _lastFrameMs, 0.0);
        Volatile.Write(ref _backend, (int)RenderBackendKind.Unknown);
        Volatile.Write(ref _shaderState, (int)ShaderPipelineState.Unknown);
    }

    private static ShaderPipelineState ResolveShaderState(bool isGpuBackend, bool reducedMotion, bool shadersReady)
    {
        if (reducedMotion)
        {
            return ShaderPipelineState.DisabledReducedMotion;
        }

        if (!isGpuBackend)
        {
            return ShaderPipelineState.DisabledSoftwareFallback;
        }

        return shadersReady
            ? ShaderPipelineState.Active
            : ShaderPipelineState.DegradedCompilationFailed;
    }
}
