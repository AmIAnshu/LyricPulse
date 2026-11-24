using Avalonia.Controls;
using Avalonia.Threading;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;

namespace LyricPulse.App.Tray;

/// <summary>
/// Manages the native Windows System Tray <see cref="TrayIcon"/> and <see cref="NativeMenu"/>
/// without rebuilding the menu on unchanged snapshots and ensuring clean icon removal on shutdown.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly OverlayRuntimeController _runtime;
    private readonly TrayIconFactory _iconFactory;
    private readonly Action _onQuitRequested;
    private readonly TrayIcon _trayIcon;

    private TrayMenuState? _lastState;
    private bool _disposed;

    public TrayController(
        OverlayRuntimeController runtime,
        TrayIconFactory iconFactory,
        Action onQuitRequested)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(iconFactory);
        ArgumentNullException.ThrowIfNull(onQuitRequested);

        _runtime = runtime;
        _iconFactory = iconFactory;
        _onQuitRequested = onQuitRequested;

        _trayIcon = new TrayIcon
        {
            IsVisible = true,
            ToolTipText = "LyricPulse \u2014 Ready",
            Icon = _iconFactory.GetIcon(TrayIconVisualState.Idle)
        };
        _trayIcon.Clicked += OnTrayIconLeftClicked;
    }

    public void ApplySnapshot(OverlayPlaybackSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_disposed)
        {
            return;
        }

        var nextState = TrayMenuState.FromSnapshot(snapshot);
        if (nextState.Equals(_lastState))
        {
            return;
        }

        _lastState = nextState;
        Dispatcher.UIThread.Post(() => ApplyStateOnUiThread(nextState), DispatcherPriority.Normal);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trayIcon.Clicked -= OnTrayIconLeftClicked;
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }

    private void ApplyStateOnUiThread(TrayMenuState state)
    {
        if (_disposed)
        {
            return;
        }

        _trayIcon.Icon = _iconFactory.GetIcon(state.IconState);
        _trayIcon.ToolTipText = state.TooltipText;
        _trayIcon.Menu = BuildNativeMenu(state);
    }

    private NativeMenu BuildNativeMenu(TrayMenuState state)
    {
        var menu = new NativeMenu();

        // Disabled Now Playing status header (2 rows)
        menu.Add(new NativeMenuItem(state.HeaderPrimaryLine) { IsEnabled = false });
        menu.Add(new NativeMenuItem(state.HeaderSecondaryLine) { IsEnabled = false });
        menu.Add(new NativeMenuItemSeparator());

        // Show Lyrics CheckBox toggle
        var showLyricsItem = new NativeMenuItem($"Show Lyrics   ({state.ShowLyricsGestureHint})")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = state.IsLyricsVisible
        };
        showLyricsItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.ToggleLyricsVisibilityAsync());
        menu.Add(showLyricsItem);
        menu.Add(new NativeMenuItemSeparator());

        // Pinned Styles radio list
        foreach (var styleOption in state.PinnedStyles)
        {
            var capturedId = styleOption.StyleId;
            var styleItem = new NativeMenuItem(styleOption.Label)
            {
                ToggleType = NativeMenuItemToggleType.Radio,
                IsChecked = styleOption.IsActive
            };
            styleItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.SetActiveStyleAsync(capturedId));
            menu.Add(styleItem);
        }

        menu.Add(new NativeMenuItemSeparator());

        // Timing Offset submenu
        var offsetSubmenu = new NativeMenu();
        foreach (var opt in state.OffsetOptions)
        {
            var capturedOpt = opt;
            var offsetItem = new NativeMenuItem(opt.Label) { IsEnabled = opt.IsEnabled };
            offsetItem.Click += (_, _) =>
            {
                if (capturedOpt.IsReset)
                {
                    _ = SafeInvokeAsync(() => _runtime.ResetTimingOffsetAsync());
                }
                else if (capturedOpt.Delta is { } delta)
                {
                    _ = SafeInvokeAsync(() => _runtime.NudgeTimingOffsetAsync(delta));
                }
            };
            offsetSubmenu.Add(offsetItem);
        }

        offsetSubmenu.Add(new NativeMenuItemSeparator());
        offsetSubmenu.Add(new NativeMenuItem(state.OffsetPersistenceCaption) { IsEnabled = false });

        menu.Add(new NativeMenuItem(state.TimingOffsetHeader) { Menu = offsetSubmenu });
        menu.Add(new NativeMenuItemSeparator());

        // Demo Mode (Neon Horizon) & Reduced Motion toggles
        var demoItem = new NativeMenuItem("Demo Mode (Neon Horizon)")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = state.IsDemoMode
        };
        demoItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.ToggleDemoModeAsync());
        menu.Add(demoItem);

        var reducedMotionItem = new NativeMenuItem("Reduced Motion")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = state.IsReducedMotion
        };
        reducedMotionItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.ToggleReducedMotionAsync());
        menu.Add(reducedMotionItem);

        menu.Add(new NativeMenuItemSeparator());

        // Graceful shutdown
        var quitItem = new NativeMenuItem("Quit LyricPulse");
        quitItem.Click += (_, _) => _onQuitRequested();
        menu.Add(quitItem);

        return menu;
    }

    private void OnTrayIconLeftClicked(object? sender, EventArgs e)
    {
        // Phase 3 Milestone 1A: Left-click toggles lyrics visibility or wakes Demo Mode when idle
        // (Phase 5 will open GalleryWindow directly on left-click).
        var snap = _runtime.SampleFrame();
        if (!snap.Settings.LyricsVisible)
        {
            _ = SafeInvokeAsync(() => _runtime.ToggleLyricsVisibilityAsync());
        }
        else if (!snap.IsDemoMode && snap.Track is null)
        {
            _ = SafeInvokeAsync(() => _runtime.SetDemoModeAsync(true));
        }
        else
        {
            _ = SafeInvokeAsync(() => _runtime.CyclePinnedStyleAsync());
        }
    }

    private static async Task SafeInvokeAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal during app shutdown.
        }
        catch (ObjectDisposedException)
        {
            // Normal during app shutdown.
        }
    }
}
