using SkiaSharp;

namespace LyricPulse.Rendering.Host;

public enum OffscreenSurfaceSlot
{
    Bulge = 0,
    MotionBlur = 1,
    Scratch = 2
}

/// <summary>
/// 64px-bucketed physical-pixel premultiplied <see cref="SKSurface"/> pool with automatic
/// GPU <see cref="GRContext"/> handle-change / abandonment purging for sleep/wake and TDR resilience.
/// </summary>
public sealed class OffscreenSurfacePool : IDisposable
{
    public const int BucketStepPx = 64;
    public const int MinSurfaceDimensionPx = 64;
    public const int MaxSurfaceDimensionPx = 4096;
    public const int MaxOversizeSlackPx = 256;

    private readonly PooledSlot[] _slots = new PooledSlot[3];
    private nint _lastGrContextHandle;
    private int _purgeCount;
    private bool _disposed;

    public int PurgeCount => _purgeCount;

    public static int ComputeBucketSizePx(int requiredPx)
    {
        var clamped = Math.Clamp(requiredPx, 1, MaxSurfaceDimensionPx);
        var rounded = ((clamped + BucketStepPx - 1) / BucketStepPx) * BucketStepPx;
        return Math.Clamp(rounded, MinSurfaceDimensionPx, MaxSurfaceDimensionPx);
    }

    /// <summary>
    /// Validates the active <see cref="GRContext"/> handle and purges pooled GPU surfaces immediately
    /// if the context changed (monitor sleep/wake, GPU TDR, or backend switch).
    /// </summary>
    public bool EnsureContextCurrent(GRContext? grContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var currentHandle = grContext?.Handle ?? 0;
        var abandoned = grContext is not null && grContext.IsAbandoned;

        if (abandoned || currentHandle != _lastGrContextHandle)
        {
            ReleaseAllSurfaces();
            _lastGrContextHandle = abandoned ? 0 : currentHandle;
            _purgeCount++;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Rents (or lazily allocates) a 64px-bucketed premultiplied BGRA8888 <see cref="SKSurface"/>
    /// sized in physical pixels for the requested slot. Caller must NOT dispose the returned surface.
    /// </summary>
    public SKSurface RentSurface(
        OffscreenSurfaceSlot slotKind,
        GRContext? grContext,
        int requiredWidthPx,
        int requiredHeightPx)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var index = (int)slotKind;
        var bucketW = ComputeBucketSizePx(requiredWidthPx);
        var bucketH = ComputeBucketSizePx(requiredHeightPx);
        ref var slot = ref _slots[index];

        var canReuse = slot.Surface is not null &&
                       slot.WidthPx >= requiredWidthPx &&
                       slot.HeightPx >= requiredHeightPx &&
                       slot.WidthPx <= bucketW + MaxOversizeSlackPx &&
                       slot.HeightPx <= bucketH + MaxOversizeSlackPx;

        if (canReuse)
        {
            return slot.Surface!;
        }

        slot.Dispose();

        var info = new SKImageInfo(bucketW, bucketH, SKColorType.Bgra8888, SKAlphaType.Premul);
        var surface = grContext is not null && !grContext.IsAbandoned
            ? SKSurface.Create(grContext, budgeted: true, info) ?? SKSurface.Create(info)
            : SKSurface.Create(info);

        if (surface is null)
        {
            throw new InvalidOperationException($"Failed to allocate offscreen SKSurface ({bucketW}x{bucketH}).");
        }

        slot = new PooledSlot(surface, bucketW, bucketH);
        return surface;
    }

    public (int WidthPx, int HeightPx) GetAllocatedSlotSize(OffscreenSurfaceSlot slotKind)
    {
        var slot = _slots[(int)slotKind];
        return (slot.WidthPx, slot.HeightPx);
    }

    public void ReleaseAllSurfaces()
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            _slots[i].Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseAllSurfaces();
    }

    private struct PooledSlot(SKSurface? surface, int widthPx, int heightPx) : IDisposable
    {
        public SKSurface? Surface = surface;
        public int WidthPx = widthPx;
        public int HeightPx = heightPx;

        public void Dispose()
        {
            Surface?.Dispose();
            Surface = null;
            WidthPx = 0;
            HeightPx = 0;
        }
    }
}
