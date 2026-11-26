using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;
using LyricPulse.Rendering.Host;

namespace LyricPulse.App.Platform;

/// <summary>
/// Manages per-monitor transparent click-through <see cref="OverlayWindow"/> instances across
/// mixed-DPI displays with 250ms debounced hotplug, DPI-change, sleep/wake, and WM_SETTINGCHANGE hooks.
/// </summary>
public sealed class MultiMonitorOverlayCoordinator : IDisposable
{
    public static readonly TimeSpan HotplugDebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly IOverlayWindowPlatform _platform;
    private readonly Func<OverlayWindow, LyricCanvasControl> _canvasFactory;
    private readonly Dictionary<string, OverlayWindow> _overlays = new(StringComparer.OrdinalIgnoreCase);

    private Window? _anchorWindow;
    private Win32Properties.CustomWndProcHookCallback? _wndProcCallback;
    private DispatcherTimer? _reconcileTimer;
    private AppSettings _currentSettings = AppSettings.Default;
    private bool _isFullscreenPaused;
    private bool _pendingGpuReset;
    private bool _disposed;

    public MultiMonitorOverlayCoordinator(
        IOverlayWindowPlatform platform,
        Func<OverlayWindow, LyricCanvasControl> canvasFactory)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(canvasFactory);

        _platform = platform;
        _canvasFactory = canvasFactory;
    }

    public int ActiveOverlayCount => _overlays.Count;

    public IReadOnlyList<MonitorDescriptor> EnumerateCurrentMonitors() =>
        _anchorWindow is null ? [] : EnumerateMonitors(_anchorWindow.Screens.All);

    /// <summary>Raised when Windows broadcasts WM_SETTINGCHANGE (e.g. Light/Dark taskbar or Animation Effects change).</summary>
    public event EventHandler? SystemSettingsChanged;

    public void AttachAnchorWindow(Window anchorWindow)
    {
        ArgumentNullException.ThrowIfNull(anchorWindow);
        ObjectDisposedException.ThrowIf(_disposed, this);

        DetachAnchorWindow();
        _anchorWindow = anchorWindow;
        _anchorWindow.Screens.Changed += OnScreensChanged;

        _wndProcCallback = OnAnchorWndProc;
        Win32Properties.AddWndProcHookCallback(_anchorWindow, _wndProcCallback);

        _reconcileTimer = new DispatcherTimer { Interval = HotplugDebounceDelay };
        _reconcileTimer.Tick += OnDebounceTimerTick;

        ReconcileNow();
    }

    public void UpdateSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _currentSettings = settings.ValidateAndNormalize();
        ReconcileNow();
    }

    public void SetFullscreenPaused(bool paused)
    {
        if (_isFullscreenPaused == paused)
        {
            return;
        }

        _isFullscreenPaused = paused;
        ApplyVisibilityState();
    }

    public void WakeAllCanvases()
    {
        foreach (var overlay in _overlays.Values)
        {
            overlay.CanvasControl.RequestWake();
        }
    }

    public void ScheduleReconcile(bool invalidateGpu = false)
    {
        if (_disposed)
        {
            return;
        }

        if (invalidateGpu)
        {
            _pendingGpuReset = true;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _reconcileTimer is null)
            {
                return;
            }

            _reconcileTimer.Stop();
            _reconcileTimer.Start();
        });
    }

    public void ReconcileNow()
    {
        if (_disposed || _anchorWindow is null)
        {
            return;
        }

        var descriptors = EnumerateMonitors(_anchorWindow.Screens.All);
        var placements = OverlayBoundsCalculator.ComputePlacements(descriptors, _currentSettings);
        var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var placement in placements)
        {
            if (!activeKeys.Add(placement.DisplayKey))
            {
                // Guard against duplicate display keys overwriting an active window
                continue;
            }

            if (_overlays.TryGetValue(placement.DisplayKey, out var existing))
            {
                existing.ApplyPlacement(placement);
                if (_pendingGpuReset)
                {
                    existing.InvalidateGpuResources();
                }
            }
            else
            {
                var overlay = new OverlayWindow(placement.DisplayKey, _platform, placement, _canvasFactory);
                overlay.ApplyPlacement(placement);
                _overlays[placement.DisplayKey] = overlay;

                if (ShouldOverlaysBeVisible())
                {
                    overlay.Show();
                    // Re-apply placement AFTER Show() so mixed-DPI secondary monitors settle to target monitor DPI
                    overlay.ApplyPlacement(placement);
                }
            }
        }

        var staleKeys = _overlays.Keys.Where(k => !activeKeys.Contains(k)).ToList();
        foreach (var staleKey in staleKeys)
        {
            if (_overlays.Remove(staleKey, out var staleOverlay))
            {
                staleOverlay.Close();
                staleOverlay.Dispose();
            }
        }

        _pendingGpuReset = false;
        ApplyVisibilityState();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachAnchorWindow();

        foreach (var overlay in _overlays.Values)
        {
            overlay.Close();
            overlay.Dispose();
        }

        _overlays.Clear();
    }

    private bool ShouldOverlaysBeVisible()
    {
        var lyricsActive = _currentSettings.DesktopOverlayEnabled && _currentSettings.LyricsVisible;
        var lumnEdgeActive = _currentSettings.EdgeGlowDesktopEnabled && _currentSettings.EdgeGlowMode != EdgeGlowMode.Off;
        return (lyricsActive || lumnEdgeActive) &&
               !(_isFullscreenPaused && _currentSettings.HideDuringFullscreen);
    }

    private void ApplyVisibilityState()
    {
        var shouldShow = ShouldOverlaysBeVisible();
        foreach (var overlay in _overlays.Values)
        {
            if (shouldShow)
            {
                if (!overlay.IsVisible)
                {
                    overlay.Show();
                    overlay.ApplyWin32ClickThrough();
                }

                overlay.SetOverlayPaused(paused: false);
            }
            else
            {
                overlay.SetOverlayPaused(paused: true);
                if (overlay.IsVisible)
                {
                    overlay.Hide();
                }
            }
        }
    }

    private static List<MonitorDescriptor> EnumerateMonitors(IReadOnlyList<Screen> screens)
    {
        var result = new List<MonitorDescriptor>(screens.Count);
        for (var i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            var work = screen.WorkingArea;
            var bounds = screen.Bounds;
            var centerX = work.X + (work.Width / 2);
            var centerY = work.Y + (work.Height / 2);

            var deviceName = Win32OverlayInterop.TryGetMonitorDeviceName(centerX, centerY);
            var displayKey = !string.IsNullOrWhiteSpace(deviceName)
                ? deviceName
                : !string.IsNullOrWhiteSpace(screen.DisplayName)
                    ? screen.DisplayName
                    : $"DISPLAY_{work.X}_{work.Y}_{work.Width}x{work.Height}";

            var friendly = !string.IsNullOrWhiteSpace(screen.DisplayName)
                ? screen.DisplayName
                : $"Display {i + 1} ({displayKey})";

            var dpiScale = Win32OverlayInterop.GetEffectiveMonitorScale(centerX, centerY, screen.Scaling);

            result.Add(new MonitorDescriptor(
                DisplayKey: displayKey,
                FriendlyName: friendly,
                WorkingLeftPx: work.X,
                WorkingTopPx: work.Y,
                WorkingWidthPx: work.Width,
                WorkingHeightPx: work.Height,
                EffectiveDpiScale: dpiScale,
                IsPrimary: screen.IsPrimary,
                MonitorLeftPx: bounds.X,
                MonitorTopPx: bounds.Y,
                MonitorWidthPx: bounds.Width,
                MonitorHeightPx: bounds.Height));
        }

        return result;
    }

    private void OnScreensChanged(object? sender, EventArgs e) => ScheduleReconcile();

    private void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _reconcileTimer?.Stop();
        ReconcileNow();
    }

    private nint OnAnchorWndProc(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg is Win32Native.WM_DISPLAYCHANGE or Win32Native.WM_DPICHANGED)
        {
            ScheduleReconcile();
        }
        else if (msg == Win32Native.WM_POWERBROADCAST && wParam == Win32Native.PBT_APMRESUMEAUTOMATIC)
        {
            ScheduleReconcile(invalidateGpu: true);
        }
        else if (msg == Win32Native.WM_SETTINGCHANGE)
        {
            Dispatcher.UIThread.Post(() => SystemSettingsChanged?.Invoke(this, EventArgs.Empty));
        }

        return 0;
    }

    private void DetachAnchorWindow()
    {
        if (_reconcileTimer is not null)
        {
            _reconcileTimer.Stop();
            _reconcileTimer.Tick -= OnDebounceTimerTick;
            _reconcileTimer = null;
        }

        if (_anchorWindow is not null)
        {
            _anchorWindow.Screens.Changed -= OnScreensChanged;
            if (_wndProcCallback is not null)
            {
                Win32Properties.RemoveWndProcHookCallback(_anchorWindow, _wndProcCallback);
                _wndProcCallback = null;
            }

            _anchorWindow = null;
        }
    }
}
