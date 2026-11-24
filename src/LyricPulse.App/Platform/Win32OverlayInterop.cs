using LyricPulse.Core.Abstractions;

namespace LyricPulse.App.Platform;

/// <summary>
/// Win32 click-through topmost overlay configurator, DWM shadow/corner stripper,
/// Per-Monitor V2 DPI helper, and OS Reduced Motion query service.
/// </summary>
public sealed class Win32OverlayInterop : IOverlayWindowPlatform
{
    public const nint RequiredClickThroughExStyles =
        Win32Native.WS_EX_LAYERED |
        Win32Native.WS_EX_TRANSPARENT |
        Win32Native.WS_EX_TOOLWINDOW |
        Win32Native.WS_EX_NOACTIVATE;

    /// <summary>
    /// Pure helper verifying the critical DirectComposition / WinUIComposition safety rule:
    /// never invoke SetLayeredWindowAttributes when WS_EX_NOREDIRECTIONBITMAP is present.
    /// </summary>
    public static nint ComputeDesiredExStyle(nint currentExStyle, out bool shouldCallSetLayeredWindowAttributes)
    {
        var isDirectOrWinUiComposition = (currentExStyle & Win32Native.WS_EX_NOREDIRECTIONBITMAP) != 0;
        shouldCallSetLayeredWindowAttributes = !isDirectOrWinUiComposition;
        return currentExStyle | RequiredClickThroughExStyles;
    }

    public void ApplyClickThroughOverlayStyles(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return;
        }

        var currentExStyle = Win32Native.GetWindowLongPtr(windowHandle, Win32Native.GWL_EXSTYLE);
        var desiredExStyle = ComputeDesiredExStyle(currentExStyle, out var shouldCallSetLayered);

        _ = Win32Native.SetWindowLongPtr(windowHandle, Win32Native.GWL_EXSTYLE, desiredExStyle);

        // Strictly guarded to RedirectionSurface fallback ONLY; calling on WinUIComposition/DirectComposition
        // surfaces conflicts with the visual tree and turns transparent overlays opaque black or invisible.
        if (shouldCallSetLayered)
        {
            _ = Win32Native.SetLayeredWindowAttributes(windowHandle, 0, 255, Win32Native.LWA_ALPHA);
        }

        var disableNcRendering = Win32Native.DWMNCRP_DISABLED;
        _ = Win32Native.DwmSetWindowAttribute(
            windowHandle,
            Win32Native.DWMWA_NCRENDERING_POLICY,
            ref disableNcRendering,
            sizeof(int));

        var forceDisableTransitions = 1;
        _ = Win32Native.DwmSetWindowAttribute(
            windowHandle,
            Win32Native.DWMWA_TRANSITIONS_FORCEDISABLED,
            ref forceDisableTransitions,
            sizeof(int));

        var excludeFromPeek = 1;
        _ = Win32Native.DwmSetWindowAttribute(
            windowHandle,
            Win32Native.DWMWA_EXCLUDED_FROM_PEEK,
            ref excludeFromPeek,
            sizeof(int));

        // Safely ignored on Windows 10 (returns E_INVALIDARG); removes forced 8px corner rounding on Win11.
        var doNotRound = Win32Native.DWMWCP_DONOTROUND;
        _ = Win32Native.DwmSetWindowAttribute(
            windowHandle,
            Win32Native.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref doNotRound,
            sizeof(int));

        _ = Win32Native.SetWindowPos(
            windowHandle,
            Win32Native.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            Win32Native.SWP_NOMOVE |
            Win32Native.SWP_NOSIZE |
            Win32Native.SWP_NOACTIVATE |
            Win32Native.SWP_FRAMECHANGED);
    }

    public static double GetEffectiveMonitorScale(int physicalPointX, int physicalPointY, double fallbackScaling)
    {
        var fallback = double.IsFinite(fallbackScaling) && fallbackScaling > 0.25 ? fallbackScaling : 1.0;
        var hMonitor = Win32Native.MonitorFromPoint(
            new Win32Native.PointStruct { X = physicalPointX, Y = physicalPointY },
            Win32Native.MONITOR_DEFAULTTONEAREST);

        if (hMonitor == 0)
        {
            return fallback;
        }

        var hr = Win32Native.GetDpiForMonitor(hMonitor, Win32Native.MDT_EFFECTIVE_DPI, out var dpiX, out _);
        if (hr == 0 && dpiX >= 48 && dpiX <= 960)
        {
            return dpiX / 96.0;
        }

        return fallback;
    }

    public static unsafe string? TryGetMonitorDeviceName(int physicalPointX, int physicalPointY)
    {
        var hMonitor = Win32Native.MonitorFromPoint(
            new Win32Native.PointStruct { X = physicalPointX, Y = physicalPointY },
            Win32Native.MONITOR_DEFAULTTONEAREST);

        if (hMonitor == 0)
        {
            return null;
        }

        var info = new Win32Native.MONITORINFOEXW { CbSize = (uint)sizeof(Win32Native.MONITORINFOEXW) };
        if (!Win32Native.GetMonitorInfo(hMonitor, &info))
        {
            return null;
        }

        var span = new ReadOnlySpan<char>(info.SzDevice, 32);
        var nullIdx = span.IndexOf('\0');
        var raw = (nullIdx >= 0 ? span[..nullIdx] : span).Trim();
        return raw.IsEmpty ? null : raw.ToString();
    }

    /// <summary>
    /// Queries Windows SPI_GETCLIENTAREAANIMATION to honor OS-level Reduced Motion preferences.
    /// </summary>
    public static bool IsSystemClientAreaAnimationEnabled()
    {
        if (Win32Native.SystemParametersInfo(Win32Native.SPI_GETCLIENTAREAANIMATION, 0, out var enabled, 0))
        {
            return enabled != 0;
        }

        return true;
    }
}
