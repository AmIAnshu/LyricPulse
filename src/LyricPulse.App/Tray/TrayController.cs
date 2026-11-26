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
/// Mutates menu items in-place whenever the menu structure is unchanged (zero menu flicker),
/// provides direct access to the Fullscreen Stage Window, Desktop Overlay toggle, all 11 styles,
/// and timing offsets with zero emoji chrome.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly OverlayRuntimeController _runtime;
    private readonly TrayIconFactory _iconFactory;
    private readonly Action _onQuitRequested;
    private readonly Action<bool>? _onOpenStageRequested;
    private readonly Action? _onToggleFullscreenRequested;
    private readonly ILogger<TrayController> _logger;
    private readonly TrayIcon _trayIcon;

    private IReadOnlyDictionary<HotkeyAction, string> _hotkeyConflicts = ImmutableDictionary<HotkeyAction, string>.Empty;
    private OverlayPlaybackSnapshot? _latestSnapshot;
    private TrayMenuState? _lastState;
    private bool _disposed;

    // In-place mutable menu items (rebuilt only when pinned list or conflict presence changes)
    private NativeMenu? _menu;
    private NativeMenuItem? _headerPrimaryItem;
    private NativeMenuItem? _headerSecondaryItem;
    private NativeMenuItem? _conflictItem;
    private NativeMenuItem? _showLyricsItem;
    private NativeMenuItem? _desktopOverlayItem;
    private readonly Dictionary<OverlayStyleId, NativeMenuItem> _pinnedItems = new();
    private readonly Dictionary<OverlayStyleId, NativeMenuItem> _allStyleItems = new();
    private NativeMenuItem? _timingOffsetParent;
    private readonly List<NativeMenuItem> _offsetOptionItems = [];
    private NativeMenuItem? _offsetCaptionItem;
    private NativeMenuItem? _retryItem;
    private NativeMenuItem? _demoItem;
    private NativeMenuItem? _universalItem;
    private NativeMenuItem? _reducedMotionItem;
    private string _lastStructuralKey = string.Empty;

    public TrayController(
        OverlayRuntimeController runtime,
        TrayIconFactory iconFactory,
        Action onQuitRequested,
        ILogger<TrayController>? logger = null,
        Action<bool>? onOpenStageRequested = null,
        Action? onToggleFullscreenRequested = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(iconFactory);
        ArgumentNullException.ThrowIfNull(onQuitRequested);

        _runtime = runtime;
        _iconFactory = iconFactory;
        _onQuitRequested = onQuitRequested;
        _onOpenStageRequested = onOpenStageRequested;
        _onToggleFullscreenRequested = onToggleFullscreenRequested;
        _logger = logger ?? NullLogger<TrayController>.Instance;

        var initialSnapshot = _runtime.SampleFrame();
        var initialState = TrayMenuState.FromSnapshot(initialSnapshot, _hotkeyConflicts);
        _latestSnapshot = initialSnapshot;
        _lastState = initialState;

        _menu = BuildNativeMenu(initialState);
        _lastStructuralKey = ComputeStructuralKey(initialState);

        _trayIcon = new TrayIcon
        {
            IsVisible = true,
            ToolTipText = initialState.TooltipText,
            Icon = _iconFactory.GetIcon(initialState.IconState),
            Menu = _menu
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

        var structuralKey = ComputeStructuralKey(nextState);
        if (structuralKey == _lastStructuralKey && _menu is not null)
        {
            UpdateMenuItemsInPlace(nextState);
        }
        else
        {
            _lastStructuralKey = structuralKey;
            _menu = BuildNativeMenu(nextState);
            _trayIcon.Menu = _menu;
        }
    }

    private static string ComputeStructuralKey(TrayMenuState state) =>
        string.Join('|', state.PinnedStyles.Select(static s => $"{(int)s.StyleId}:{s.Label}")) +
        $"|hasConflict={!string.IsNullOrEmpty(state.HotkeyConflictNotice)}";

    private void UpdateMenuItemsInPlace(TrayMenuState state)
    {
        if (_headerPrimaryItem is not null) _headerPrimaryItem.Header = state.HeaderPrimaryLine;
        if (_headerSecondaryItem is not null) _headerSecondaryItem.Header = state.HeaderSecondaryLine;
        if (_conflictItem is not null && state.HotkeyConflictNotice is not null) _conflictItem.Header = state.HotkeyConflictNotice;

        if (_showLyricsItem is not null)
        {
            _showLyricsItem.Header = string.IsNullOrEmpty(state.ShowLyricsGestureHint)
                ? "Show Lyrics"
                : $"Show Lyrics\t{state.ShowLyricsGestureHint}";
            _showLyricsItem.IsChecked = state.IsLyricsVisible;
        }

        if (_desktopOverlayItem is not null) _desktopOverlayItem.IsChecked = state.IsDesktopOverlayEnabled;

        foreach (var opt in state.PinnedStyles)
        {
            if (_pinnedItems.TryGetValue(opt.StyleId, out var item))
            {
                item.IsChecked = opt.IsActive;
            }
        }

        foreach (var opt in state.AllStyles)
        {
            if (_allStyleItems.TryGetValue(opt.StyleId, out var item))
            {
                item.IsChecked = opt.IsActive;
            }
        }

        if (_timingOffsetParent is not null) _timingOffsetParent.Header = state.TimingOffsetHeader;
        for (var i = 0; i < state.OffsetOptions.Length && i < _offsetOptionItems.Count; i++)
        {
            _offsetOptionItems[i].IsEnabled = state.OffsetOptions[i].IsEnabled;
        }
        if (_offsetCaptionItem is not null) _offsetCaptionItem.Header = state.OffsetPersistenceCaption;

        if (_retryItem is not null) _retryItem.IsEnabled = state.CanRetryLyricsFetch;
        if (_demoItem is not null) _demoItem.IsChecked = state.IsDemoMode;
        if (_universalItem is not null) _universalItem.IsChecked = state.IsUniversalPlayerMode;
        if (_reducedMotionItem is not null)
        {
            _reducedMotionItem.Header = state.ReducedMotionLabel;
            _reducedMotionItem.IsChecked = state.IsEffectiveReducedMotion;
            _reducedMotionItem.IsEnabled = !state.IsOsReducedMotion;
        }
    }

    private NativeMenu BuildNativeMenu(TrayMenuState state)
    {
        _pinnedItems.Clear();
        _allStyleItems.Clear();
        _offsetOptionItems.Clear();

        var menu = new NativeMenu();

        _headerPrimaryItem = new NativeMenuItem(state.HeaderPrimaryLine) { IsEnabled = false };
        _headerSecondaryItem = new NativeMenuItem(state.HeaderSecondaryLine) { IsEnabled = false };
        menu.Add(_headerPrimaryItem);
        menu.Add(_headerSecondaryItem);

        _conflictItem = null;
        if (!string.IsNullOrEmpty(state.HotkeyConflictNotice))
        {
            _conflictItem = new NativeMenuItem(state.HotkeyConflictNotice) { IsEnabled = false };
            menu.Add(_conflictItem);
        }

        menu.Add(new NativeMenuItemSeparator());

        var showLabel = string.IsNullOrEmpty(state.ShowLyricsGestureHint)
            ? "Show Lyrics"
            : $"Show Lyrics\t{state.ShowLyricsGestureHint}";

        _showLyricsItem = new NativeMenuItem(showLabel)
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = state.IsLyricsVisible
        };
        _showLyricsItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.ToggleLyricsVisibilityAsync(), "ToggleLyricsVisibility");
        menu.Add(_showLyricsItem);

        _desktopOverlayItem = new NativeMenuItem("Desktop Click-Through Overlay")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = state.IsDesktopOverlayEnabled
        };
        _desktopOverlayItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.ToggleDesktopOverlayAsync(), "ToggleDesktopOverlay");
        menu.Add(_desktopOverlayItem);

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
            _pinnedItems[capturedId] = styleItem;
            menu.Add(styleItem);
        }

        // All Styles (10) submenu
        var allStylesSubmenu = new NativeMenu();
        foreach (var opt in state.AllStyles)
        {
            var capturedId = opt.StyleId;
            var allItem = new NativeMenuItem(opt.Label)
            {
                ToggleType = NativeMenuItemToggleType.Radio,
                IsChecked = opt.IsActive
            };
            allItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.SetActiveStyleAsync(capturedId), "SetActiveStyle");
            _allStyleItems[capturedId] = allItem;
            allStylesSubmenu.Add(allItem);
        }
        menu.Add(new NativeMenuItem("All Styles") { Menu = allStylesSubmenu });

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
            _offsetOptionItems.Add(offsetItem);
            offsetSubmenu.Add(offsetItem);
        }

        offsetSubmenu.Add(new NativeMenuItemSeparator());
        _offsetCaptionItem = new NativeMenuItem(state.OffsetPersistenceCaption) { IsEnabled = false };
        offsetSubmenu.Add(_offsetCaptionItem);

        _timingOffsetParent = new NativeMenuItem(state.TimingOffsetHeader) { Menu = offsetSubmenu };
        menu.Add(_timingOffsetParent);

        _retryItem = new NativeMenuItem("Retry Lyrics Fetch") { IsEnabled = state.CanRetryLyricsFetch };
        _retryItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.RetryLyricsFetchAsync(), "RetryLyricsFetch");
        menu.Add(_retryItem);

        menu.Add(new NativeMenuItemSeparator());

        _demoItem = new NativeMenuItem("Demo Mode (Neon Horizon)")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = state.IsDemoMode
        };
        _demoItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.ToggleDemoModeAsync(), "ToggleDemoMode");
        menu.Add(_demoItem);

        _universalItem = new NativeMenuItem("Follow All Media Players")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = state.IsUniversalPlayerMode
        };
        _universalItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.ToggleUniversalPlayerModeAsync(), "ToggleUniversalPlayerMode");
        menu.Add(_universalItem);

        _reducedMotionItem = new NativeMenuItem(state.ReducedMotionLabel)
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = state.IsEffectiveReducedMotion,
            IsEnabled = !state.IsOsReducedMotion
        };
        _reducedMotionItem.Click += (_, _) => _ = SafeInvokeAsync(() => _runtime.ToggleReducedMotionAsync(), "ToggleReducedMotion");
        menu.Add(_reducedMotionItem);

        menu.Add(new NativeMenuItemSeparator());

        var openStageItem = new NativeMenuItem("Open LyricPulse Stage");
        openStageItem.Click += (_, _) => _onOpenStageRequested?.Invoke(false);
        menu.Add(openStageItem);

        var fullscreenItem = new NativeMenuItem("Toggle Fullscreen Party\tF11");
        fullscreenItem.Click += (_, _) => _onToggleFullscreenRequested?.Invoke();
        menu.Add(fullscreenItem);

        var settingsItem = new NativeMenuItem("Settings...\tCtrl+,");
        settingsItem.Click += (_, _) => _onOpenStageRequested?.Invoke(true);
        menu.Add(settingsItem);

        menu.Add(new NativeMenuItemSeparator());

        var quitItem = new NativeMenuItem("Quit LyricPulse");
        quitItem.Click += (_, _) => _onQuitRequested();
        menu.Add(quitItem);

        return menu;
    }

    private void OnTrayIconLeftClicked(object? sender, EventArgs e)
    {
        if (_onOpenStageRequested is not null)
        {
            _onOpenStageRequested(false);
            return;
        }

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
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogHostActionError(ex, actionName);
        }
    }
}
