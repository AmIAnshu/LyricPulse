using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using LyricPulse.Core.Abstractions;
using LyricPulse.Rendering.Host;

namespace LyricPulse.App.Platform;

/// <summary>
/// Borderless, transparent, always-on-top click-through desktop overlay window sized strictly
/// within <c>screen.WorkingArea</c> using <see cref="WindowState.Normal"/> (never Maximized overshoot).
/// </summary>
public sealed class OverlayWindow : Window, IDisposable
{
    private readonly IOverlayWindowPlatform _platform;
    private readonly LyricCanvasControl _canvas;
    private bool _disposed;

    public OverlayWindow(
        string displayKey,
        IOverlayWindowPlatform platform,
        LyricCanvasControl canvas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayKey);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(canvas);

        DisplayKey = displayKey;
        _platform = platform;
        _canvas = canvas;

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

        Content = _canvas;
    }

    public string DisplayKey { get; }

    public LyricCanvasControl CanvasControl => _canvas;

    public void ApplyPlacement(OverlayWindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        WindowState = WindowState.Normal;
        Position = new PixelPoint(placement.PositionXPx, placement.PositionYPx);
        Width = placement.WidthDip;
        Height = placement.HeightDip;
        ApplyWin32ClickThrough();
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
