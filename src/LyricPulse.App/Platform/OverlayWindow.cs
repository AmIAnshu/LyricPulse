using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;
using LyricPulse.Rendering.Host;

namespace LyricPulse.App.Platform;

/// <summary>
/// Borderless, transparent, always-on-top click-through desktop overlay window sized strictly
/// within <c>screen.WorkingArea</c> using <see cref="WindowState.Normal"/> (never Maximized overshoot).
/// Stores mutable <see cref="CurrentBoundsConfig"/> updated atomically in <see cref="ApplyPlacement"/>
/// so runtime AnchorZone / SafeMarginPx / PerDisplayBounds changes take effect immediately.
/// </summary>
public sealed class OverlayWindow : Window, IDisposable
{
    private readonly IOverlayWindowPlatform _platform;
    private readonly LyricCanvasControl _canvas;
    private OverlayBoundsConfig _currentBoundsConfig;
    private bool _disposed;

    public OverlayWindow(
        string displayKey,
        IOverlayWindowPlatform platform,
        OverlayWindowPlacement initialPlacement,
        Func<OverlayWindow, LyricCanvasControl> canvasFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayKey);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(initialPlacement);
        ArgumentNullException.ThrowIfNull(canvasFactory);

        DisplayKey = displayKey;
        _platform = platform;
        _currentBoundsConfig = initialPlacement.BoundsConfig;

        Title = $"LyricPulse Overlay ({displayKey})";
        WindowState = WindowState.Normal;
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = 0;

        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;

        _canvas = canvasFactory(this);
        Content = _canvas;
    }

    public string DisplayKey { get; }

    public LyricCanvasControl CanvasControl => _canvas;

    public OverlayBoundsConfig CurrentBoundsConfig => Volatile.Read(ref _currentBoundsConfig);

    public void ApplyPlacement(OverlayWindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        Volatile.Write(ref _currentBoundsConfig, placement.BoundsConfig);
        WindowState = WindowState.Normal;
        Position = new PixelPoint(placement.PositionXPx, placement.PositionYPx);
        Width = placement.WidthDip;
        Height = placement.HeightDip;
        ApplyWin32ClickThrough();
        _canvas.RequestWake();
    }

    public void ApplyWin32ClickThrough()
    {
        if (TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: not 0 } platformHandle)
        {
            _platform.ApplyClickThroughOverlayStyles(platformHandle.Handle);
        }
    }

    public void SetOverlayPaused(bool paused)
    {
        _canvas.IsVisible = !paused;
        if (paused)
        {
            _canvas.ReleaseGpuResources();
        }
        else
        {
            _canvas.RequestWake();
        }
    }

    public void InvalidateGpuResources()
    {
        _canvas.ReleaseGpuResources();
        _canvas.RequestWake();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _canvas.Dispose();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyWin32ClickThrough();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Dispose();
    }
}
