using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Threading;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricPulse.App.Tray;

/// <summary>
/// Manages the native Windows System Tray <see cref="TrayIcon"/> and <see cref="NativeMenu"/>.
/// Initializes the menu synchronously in the constructor (never null during startup),
/// serializes state updates strictly on <see cref="Dispatcher.UIThread"/>, adapts icons live
/// on taskbar theme changes, and removes the tray icon cleanly on Quit.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly OverlayRuntimeController _runtime;
    private readonly TrayIconFactory _iconFactory;
    private readonly Action _onQuitRequested;
    private readonly ILogger<TrayController> _logger;
    private readonly TrayIcon _trayIcon;

    private IReadOnlyDictionary<HotkeyAction, string> _hotkeyConflicts = ImmutableDictionary<HotkeyAction, string>.Empty;
    private OverlayPlaybackSnapshot? _latestSnapshot;
    private TrayMenuState? _lastState;
    private bool _disposed;

    public TrayController(
        OverlayRuntimeController runtime,
        TrayIconFactory iconFactory,
        Action onQuitRequested,
        ILogger<TrayController>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(iconFactory);
        ArgumentNullException.ThrowIfNull(onQuitRequested);

        _runtime = runtime;
        _iconFactory = iconFactory;
        _onQuitRequested = onQuitRequested;
        _logger = logger ?? NullLogger<TrayController>.Instance;

        var initialSnapshot = _runtime.SampleFrame();
        var initialState = TrayMenuState.FromSnapshot(initialSnapshot, _hotkeyConflicts);
        _latestSnapshot = initialSnapshot;
        _lastState = initialState;

        _trayIcon = new TrayIcon
        {
            IsVisible = true,
            ToolTipText = initialState.TooltipText,
            Icon = _iconFactory.GetIcon(initialState.IconState),
            Menu = BuildNativeMenu(initialState)
        };
        _trayIcon.Clicked += OnTrayIconLeftClicked;
    }

    public void ApplySnapshot(OverlayPlaybackSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            _latestSnapshot = snapshot;
            ApplyCurrentStateOnUiThread();
        }, DispatcherPriority.Normal);
    }

    public void SetHotkeyConflicts(IReadOnlyDictionary<HotkeyAction, string> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        var snapshotCopy = conflicts.ToImmutableDictionary();
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            _hotkeyConflicts = snapshotCopy;
            ApplyCurrentStateOnUiThread();
        }, DispatcherPriority.Normal);
    }

    public void RefreshThemeIcon()
    {
        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _lastState is null)
            {
                return;
            }

            _trayIcon.Icon = _iconFactory.GetIcon(_lastState.IconState);
        }, DispatcherPriority.Normal);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
        {
            return;
        }

        _trayIcon.Clicked -= OnTrayIconLeftClicked;
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }

    private void ApplyCurrentStateOnUiThread()
    {
        if (_disposed || _latestSnapshot is null)
        {
            return;
        }

        var nextState = TrayMenuState.FromSnapshot(_latestSnapshot, _hotkeyConflicts);
        if (nextState.Equals(_lastState))
        {
            return;
        }

        _lastState = nextState;
        _trayIcon.Icon = _iconFactory.GetIcon(nextState.IconState);
        _trayIcon.ToolTipText = nextState.TooltipText;
        _trayIcon.Menu = BuildNativeMenu(nextState);
    }

    private NativeMenu BuildNativeMenu(TrayMenuState state)
    {
        var menu = new NativeMenu();

        // Disabled Now Playing status header (2 rows)
        menu.Add(new NativeMenuItem(state.HeaderPrimaryLine) { IsEnabled = false });
        menu.Add(new NativeMenuItem(state.HeaderSecondaryLine) { IsEnabled = false });

        if (!string.IsNullOrEmpty(state.HotkeyConflictNotice))
        {
            menu.Add(new NativeMenuItem(state.HotkeyConflictNotice) { IsEnabled = false });
        }

        menu.Add(new NativeMenuItemSeparator());

        // Show Lyrics CheckBox toggle (only show gesture hint when not conflicted)
        var showLabel = string.IsNullOrEmpty(state.ShowLyricsGestureHint)
            ? "Show Lyrics"
            : $"Show Lyrics\t{state.ShowLyricsGestureHint}";

        var showLyricsItem = new NativeMenuItem(showLabel)
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = state.IsLyricsVisible
        };
        showLyricsItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.ToggleLyricsVisibilityAsync(), "ToggleLyricsVisibility");
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
            styleItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.SetActiveStyleAsync(capturedId), "SetActiveStyle");
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
                    _ = SafeInvokeAsync(() => _runtime.ResetTimingOffsetAsync(), "ResetTimingOffset");
                }
                else if (capturedOpt.Delta is { } delta)
                {
                    _ = SafeInvokeAsync(() => _runtime.NudgeTimingOffsetAsync(delta), "NudgeTimingOffset");
                }
            };
            offsetSubmenu.Add(offsetItem);
        }

        offsetSubmenu.Add(new NativeMenuItemSeparator());
        offsetSubmenu.Add(new NativeMenuItem(state.OffsetPersistenceCaption) { IsEnabled = false });

        menu.Add(new NativeMenuItem(state.TimingOffsetHeader) { Menu = offsetSubmenu });

        // Recovery action when offline / lookup failed / not found on a live track
        var retryItem = new NativeMenuItem("Retry Lyrics Fetch") { IsEnabled = state.CanRetryLyricsFetch };
        retryItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.RetryLyricsFetchAsync(), "RetryLyricsFetch");
        menu.Add(retryItem);

        menu.Add(new NativeMenuItemSeparator());

        // Demo Mode, Follow All Media Players (Universal), & Reduced Motion toggles
        var demoItem = new NativeMenuItem("Demo Mode (Neon Horizon)")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = state.IsDemoMode
        };
        demoItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.ToggleDemoModeAsync(), "ToggleDemoMode");
        menu.Add(demoItem);

        var universalItem = new NativeMenuItem("Follow All Media Players")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = state.IsUniversalPlayerMode
        };
        universalItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.ToggleUniversalPlayerModeAsync(), "ToggleUniversalPlayerMode");
        menu.Add(universalItem);

        var reducedMotionItem = new NativeMenuItem(state.ReducedMotionLabel)
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = state.IsEffectiveReducedMotion,
            IsEnabled = !state.IsOsReducedMotion
        };
        reducedMotionItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.ToggleReducedMotionAsync(), "ToggleReducedMotion");
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
        // Predictable Milestone 1A left-click: start Demo Mode when idle with no live player,
        // otherwise cleanly toggle Show/Hide Lyrics (never surprise-cycle the user's selected style!).
        var snap = _runtime.SampleFrame();
        if (!snap.IsDemoMode && snap.Track is null && snap.Settings.LyricsVisible)
        {
            _ = SafeInvokeAsync(() => _runtime.SetDemoModeAsync(true), "TrayLeftClickStartDemo");
        }
        else
        {
            _ = SafeInvokeAsync(() => _runtime.ToggleLyricsVisibilityAsync(), "TrayLeftClickToggleLyrics");
        }
    }

    private async Task SafeInvokeAsync(Func<Task> action, string actionName)
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
        catch (Exception ex)
        {
            _logger.LogHostActionError(ex, actionName);
        }
    }
}
