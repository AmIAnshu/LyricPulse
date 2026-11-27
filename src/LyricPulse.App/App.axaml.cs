using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using LyricPulse.App.Platform;
using LyricPulse.App.Tray;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Shaders;
using LyricPulse.Rendering.Styles;
using LyricPulse.UI.ViewModels;
using LyricPulse.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricPulse.App;

public sealed class App : Application, IDisposable
{
    private const int ShutdownTimeoutMs = 1500;

    private ServiceProvider? _services;
    private ILogger<App> _logger = NullLogger<App>.Instance;
    private OverlayRuntimeController? _runtime;
    private MultiMonitorOverlayCoordinator? _overlayCoordinator;
    private IGlobalHotkeyService? _hotkeys;
    private FullscreenAutoHideMonitor? _fullscreenMonitor;
    private TrayController? _trayController;
    private StageViewModel? _stageViewModel;
    private StageWindow? _stageWindow;
    private ObsBroadcastWindow? _obsWindow;
    private Window? _hiddenAnchorWindow;
    private AppSettings? _lastAppliedOverlaySettings;
    private ImmutableDictionary<HotkeyAction, string>? _lastRegisteredHotkeys;
    private IReadOnlyDictionary<HotkeyAction, string> _lastHotkeyConflicts = ImmutableDictionary<HotkeyAction, string>.Empty;
    private bool _disposed;

    public static SingleInstanceGuard? ActiveSingleInstanceGuard { get; set; }

    public static LaunchOptions LaunchOptions { get; set; } = new(StartInDemoMode: false, RunSmokeTest: false);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            InitializeDesktopHost(desktop);
            desktop.ShutdownRequested += (_, _) => CleanupDesktopHost();
            desktop.Exit += (_, _) => CleanupDesktopHost();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void Dispose() => CleanupDesktopHost();

    internal static bool ShouldReconcileOverlayLayout(AppSettings previous, AppSettings current)
    {
        if (previous.DesktopOverlayEnabled != current.DesktopOverlayEnabled ||
            previous.LyricsVisible != current.LyricsVisible ||
            previous.EdgeGlowDesktopEnabled != current.EdgeGlowDesktopEnabled ||
            (previous.EdgeGlowMode == EdgeGlowMode.Off) != (current.EdgeGlowMode == EdgeGlowMode.Off) ||
            previous.HideDuringFullscreen != current.HideDuringFullscreen ||
            previous.AnchorZone != current.AnchorZone ||
            previous.SafeMarginPx != current.SafeMarginPx ||
            !previous.DisabledDisplays.SequenceEqual(current.DisabledDisplays))
        {
            return true;
        }

        return !ArePerDisplayBoundsEqual(previous.PerDisplayBounds, current.PerDisplayBounds);
    }

    internal static bool ArePerDisplayBoundsEqual(
        ImmutableDictionary<string, OverlayBoundsConfig> a,
        ImmutableDictionary<string, OverlayBoundsConfig> b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var otherValue) || value != otherValue)
            {
                return false;
            }
        }

        return true;
    }

    private void InitializeDesktopHost(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _services = Program.BuildServiceProvider(forHeadlessSmokeTest: false);
        _logger = _services.GetService<ILogger<App>>() ?? NullLogger<App>.Instance;

        _runtime = _services.GetRequiredService<OverlayRuntimeController>();
        _runtime.SetOsReducedMotion(!Win32OverlayInterop.IsSystemClientAreaAnimationEnabled());

        var overlayPlatform = _services.GetRequiredService<IOverlayWindowPlatform>();
        var trayIconFactory = _services.GetRequiredService<TrayIconFactory>();
        var trayLogger = _services.GetService<ILogger<TrayController>>();

        _overlayCoordinator = new MultiMonitorOverlayCoordinator(
            overlayPlatform,
            CreateOverlayCanvas);
        _overlayCoordinator.SystemSettingsChanged += OnWindowsSystemSettingsChanged;

        _hiddenAnchorWindow = CreateHiddenAnchorWindow();
        _hiddenAnchorWindow.Show();
        if (_hiddenAnchorWindow.TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: not 0 } anchorHandle)
        {
            overlayPlatform.ApplyClickThroughOverlayStyles(anchorHandle.Handle);
        }

        _hiddenAnchorWindow.Hide();
        _overlayCoordinator.AttachAnchorWindow(_hiddenAnchorWindow);

        // Synchronously constructs TrayIcon + initial NativeMenu BEFORE awaiting GSMTC startup
        _trayController = new TrayController(
            _runtime,
            trayIconFactory,
            () => desktop.Shutdown(0),
            trayLogger,
            onOpenStageRequested: openSettings => OpenOrFocusStageWindow(openSettings),
            onToggleFullscreenRequested: ToggleStageFullscreen);

        _hotkeys = _services.GetService<IGlobalHotkeyService>();
        if (_hotkeys is not null)
        {
            _hotkeys.HotkeyPressed += OnGlobalHotkeyPressed;
            RegisterGlobalHotkeys(_runtime.Settings);
        }

        _fullscreenMonitor = _services.GetService<IFullscreenDetector>() as FullscreenAutoHideMonitor;
        if (_fullscreenMonitor is not null)
        {
            _fullscreenMonitor.FullscreenStateChanged += OnFullscreenStateChanged;
            _fullscreenMonitor.Start();
        }

        _runtime.StateChanged += OnRuntimeStateChanged;

        ActiveSingleInstanceGuard?.StartActivationListener(
            onShowRequested: () => _ = HandleSecondInstanceActivationAsync(forceDemo: false),
            onDemoRequested: () => _ = HandleSecondInstanceActivationAsync(forceDemo: true));

        // Primary Studio Stage Window (starts immediately with Grainy Ambient Party backdrop + Live Gallery)
        CreateAndShowPrimaryStageWindow(desktop);

        _ = StartRuntimeAsync();
    }

    private void CreateAndShowPrimaryStageWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_services is null || _runtime is null)
        {
            return;
        }

        var fonts = _services.GetRequiredService<FontAssetRegistry>();
        var shaders = _services.GetRequiredService<ShaderLibrary>();
        var telemetry = _services.GetRequiredService<FrameBudgetTelemetry>();
        var rendererFactory = _services.GetRequiredService<ILyricSceneRendererFactory>();

        _stageViewModel = new StageViewModel(
            _runtime,
            hotkeyValidator: ValidateAndRegisterSingleHotkey,
            onObsWindowRequested: open => ToggleObsWindow(open),
            onDisplaysRefreshRequested: RefreshStageDisplayList);

        _stageViewModel.UpdateDisplays(MapMonitorDisplays(_overlayCoordinator?.EnumerateCurrentMonitors() ?? []));
        _stageViewModel.SetHotkeyConflicts(_lastHotkeyConflicts);

        _stageWindow = new StageWindow(_stageViewModel, fonts, shaders, telemetry, rendererFactory);
        _stageWindow.Opened += (_, _) => ApplyStageChromeIfHandleReady();
        _stageWindow.Activated += (_, _) => ApplyStageChromeIfHandleReady();
        _stageWindow.PropertyChanged += (_, args) =>
        {
            if (args.Property == Window.WindowStateProperty)
            {
                ApplyStageChromeIfHandleReady();
            }
        };
        desktop.MainWindow = _stageWindow;
        _stageWindow.Show();
        ApplyStageChromeIfHandleReady();
        ApplyRequestedTheme(_runtime.Settings.ThemeVariantMode);
    }

    private void ApplyStageChromeIfHandleReady()
    {
        if (_stageWindow?.TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: not 0 } platformHandle)
        {
            Win32OverlayInterop.ApplyStageWindowDarkChrome(platformHandle.Handle);
        }
    }

    private void OpenOrFocusStageWindow(bool openSettingsDrawer = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            _stageWindow?.SurfaceAndFocus(openSettingsDrawer);
        });
    }

    private void ToggleStageFullscreen()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _stageViewModel is null || _stageWindow is null)
            {
                return;
            }

            _stageWindow.SurfaceAndFocus(openSettingsDrawer: false);
            _stageViewModel.IsFullscreen = !_stageViewModel.IsFullscreen;
        });
    }

    private void ToggleObsWindow(bool open)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _services is null || _stageViewModel is null)
            {
                return;
            }

            if (open)
            {
                if (_obsWindow is null || !_obsWindow.IsVisible)
                {
                    var fonts = _services.GetRequiredService<FontAssetRegistry>();
                    var shaders = _services.GetRequiredService<ShaderLibrary>();
                    var telemetry = _services.GetRequiredService<FrameBudgetTelemetry>();
                    var rendererFactory = _services.GetRequiredService<ILyricSceneRendererFactory>();
                    _obsWindow = new ObsBroadcastWindow(_stageViewModel, fonts, shaders, telemetry, rendererFactory);
                    _obsWindow.Closed += (_, _) => _obsWindow = null;
                    _obsWindow.Show();
                }
                else
                {
                    _obsWindow.Activate();
                }
            }
            else
            {
                _obsWindow?.Close();
                _obsWindow = null;
            }
        });
    }

    private void RefreshStageDisplayList()
    {
        if (_disposed || _overlayCoordinator is null || _stageViewModel is null)
        {
            return;
        }

        _overlayCoordinator.ReconcileNow();
        _stageViewModel.UpdateDisplays(MapMonitorDisplays(_overlayCoordinator.EnumerateCurrentMonitors()));
    }

    private static DisplayMonitorInfo[] MapMonitorDisplays(IReadOnlyList<MonitorDescriptor> monitors) =>
        monitors.Select(static m => new DisplayMonitorInfo(
            m.DisplayKey,
            m.FriendlyName,
            m.WorkingWidthPx,
            m.WorkingHeightPx,
            m.EffectiveDpiScale,
            m.IsPrimary)).ToArray();

    private (bool Ok, string? Error) ValidateAndRegisterSingleHotkey(HotkeyAction action, string gesture)
    {
        if (_hotkeys is null)
        {
            return (true, null);
        }

        var ok = _hotkeys.TryRegister(action, gesture, out var error);
        return (ok, error);
    }

    private void ApplyRequestedTheme(ThemeVariantMode mode)
    {
        var target = mode switch
        {
            ThemeVariantMode.Dark => ThemeVariant.Dark,
            ThemeVariantMode.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };

        if (RequestedThemeVariant != target)
        {
            RequestedThemeVariant = target;
        }
    }

    private async Task StartRuntimeAsync()
    {
        if (_runtime is null)
        {
            return;
        }

        try
        {
            await _runtime.StartAsync().ConfigureAwait(false);
            if (LaunchOptions.StartInDemoMode)
            {
                await _runtime.SetDemoModeAsync(true).ConfigureAwait(false);
            }

            var initialSnapshot = _runtime.SampleFrame();
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed)
                {
                    return;
                }

                ApplySnapshotToHost(initialSnapshot);
            });
        }
        catch (OperationCanceledException)
        {
            // Normal on early exit.
        }
        catch (Exception ex)
        {
            _logger.LogHostStartupError(ex);
        }
    }

    private LyricCanvasControl CreateOverlayCanvas(OverlayWindow overlayWindow)
    {
        var fonts = _services!.GetRequiredService<FontAssetRegistry>();
        var shaders = _services!.GetRequiredService<ShaderLibrary>();
        var telemetry = _services!.GetRequiredService<FrameBudgetTelemetry>();
        var rendererFactory = _services!.GetRequiredService<ILyricSceneRendererFactory>();
        var runtime = _runtime!;

        // Each LyricCanvasControl owns and disposes its own ILyricSceneRenderer (zero root DI leaks!)
        var renderer = rendererFactory.CreateRenderer();

        return new LyricCanvasControl(
            fonts,
            shaders,
            telemetry,
            renderer,
            packetFactory: (w, h, scale, timeSec, dt) =>
            {
                var snapshot = runtime.SampleFrame();
                return OverlayFrameAdapter.FromPlaybackSnapshot(
                    snapshot,
                    viewportWidthDip: w,
                    viewportHeightDip: h,
                    renderScaling: scale,
                    renderTimeSeconds: timeSec,
                    deltaSeconds: dt,
                    boundsOverride: overlayWindow.CurrentBoundsConfig,
                    allowDesktopEdgeGlow: snapshot.Settings.EdgeGlowDesktopEnabled,
                    renderDesktopLyrics: snapshot.Settings.DesktopOverlayEnabled && snapshot.Settings.LyricsVisible);
            },
            ownsRenderer: true);
    }

    private void OnRuntimeStateChanged(object? sender, OverlayPlaybackSnapshot snapshot)
    {
        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        Dispatcher.UIThread.Post(() => ApplySnapshotToHost(snapshot), DispatcherPriority.Normal);
    }

    private void ApplySnapshotToHost(OverlayPlaybackSnapshot snapshot)
    {
        if (_disposed || _overlayCoordinator is null)
        {
            return;
        }

        _trayController?.ApplySnapshot(snapshot);
        _stageViewModel?.ApplySnapshot(snapshot);

        var settings = snapshot.Settings;
        ApplyRequestedTheme(settings.ThemeVariantMode);

        if (_lastRegisteredHotkeys is null || !AreHotkeysEqual(_lastRegisteredHotkeys, settings.GlobalHotkeys))
        {
            RegisterGlobalHotkeys(settings);
        }

        if (_lastAppliedOverlaySettings is null || ShouldReconcileOverlayLayout(_lastAppliedOverlaySettings, settings))
        {
            _lastAppliedOverlaySettings = settings;
            _overlayCoordinator.UpdateSettings(settings);
            _stageViewModel?.UpdateDisplays(MapMonitorDisplays(_overlayCoordinator.EnumerateCurrentMonitors()));
        }

        _overlayCoordinator.SetFullscreenPaused(snapshot.IsFullscreenHidden);
        var shouldWakeOverlays =
            (snapshot.IsLyricsVisible && settings.DesktopOverlayEnabled) ||
            (settings.EdgeGlowDesktopEnabled && settings.EdgeGlowMode != EdgeGlowMode.Off);
        if (shouldWakeOverlays)
        {
            _overlayCoordinator.WakeAllCanvases();
        }

        _stageWindow?.WakeStageCanvas();
        _obsWindow?.WakeCanvas();
    }

    private void OnWindowsSystemSettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _runtime?.SetOsReducedMotion(!Win32OverlayInterop.IsSystemClientAreaAnimationEnabled());
        _trayController?.RefreshThemeIcon();
    }

    private void OnFullscreenStateChanged(object? sender, bool isFullscreen)
    {
        _runtime?.RefreshFullscreenState();
        Dispatcher.UIThread.Post(() => _overlayCoordinator?.SetFullscreenPaused(isFullscreen));
    }

    private void OnGlobalHotkeyPressed(object? sender, HotkeyAction action)
    {
        if (Volatile.Read(ref _disposed) || _runtime is null)
        {
            return;
        }

        _ = HandleHotkeyAsync(action);
    }

    private async Task HandleHotkeyAsync(HotkeyAction action)
    {
        if (_runtime is null)
        {
            return;
        }

        try
        {
            switch (action)
            {
                case HotkeyAction.ToggleLyrics:
                    await _runtime.ToggleLyricsVisibilityAsync().ConfigureAwait(false);
                    break;
                case HotkeyAction.CyclePinnedStyles:
                    await _runtime.CyclePinnedStyleAsync().ConfigureAwait(false);
                    break;
                case HotkeyAction.OffsetEarlier:
                    await _runtime.NudgeTimingOffsetAsync(TimeSpan.FromSeconds(-0.5)).ConfigureAwait(false);
                    break;
                case HotkeyAction.OffsetLater:
                    await _runtime.NudgeTimingOffsetAsync(TimeSpan.FromSeconds(0.5)).ConfigureAwait(false);
                    break;
                case HotkeyAction.OpenGallery:
                    await HandleSecondInstanceActivationAsync(forceDemo: false).ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown race.
        }
        catch (Exception ex)
        {
            _logger.LogHostActionError(ex, $"Hotkey:{action}");
        }
    }

    private async Task HandleSecondInstanceActivationAsync(bool forceDemo)
    {
        if (_runtime is null || Volatile.Read(ref _disposed))
        {
            return;
        }

        try
        {
            var snap = _runtime.SampleFrame();
            if (forceDemo)
            {
                await _runtime.SetDemoModeAsync(true).ConfigureAwait(false);
            }
            else if (!snap.IsDemoMode && snap.Track is null)
            {
                await _runtime.SetDemoModeAsync(true).ConfigureAwait(false);
            }

            Dispatcher.UIThread.Post(() =>
            {
                _stageWindow?.SurfaceAndFocus(openSettingsDrawer: false);
                _overlayCoordinator?.WakeAllCanvases();
            });
        }
        catch (OperationCanceledException)
        {
            // Shutdown race.
        }
        catch (Exception ex)
        {
            _logger.LogHostActionError(ex, "SecondInstanceActivation");
        }
    }

    private void RegisterGlobalHotkeys(AppSettings settings)
    {
        if (_hotkeys is null)
        {
            return;
        }

        IReadOnlyDictionary<HotkeyAction, string> conflicts;
        if (_hotkeys is GlobalHotkeyManager manager)
        {
            conflicts = manager.RegisterAll(settings.GlobalHotkeys);
        }
        else
        {
            _hotkeys.UnregisterAll();
            var dict = new Dictionary<HotkeyAction, string>();
            foreach (var (action, gesture) in settings.GlobalHotkeys)
            {
                if (string.IsNullOrWhiteSpace(gesture))
                {
                    continue;
                }

                if (!_hotkeys.TryRegister(action, gesture, out var err) && err is not null)
                {
                    dict[action] = err;
                }
            }

            conflicts = dict;
        }

        _lastRegisteredHotkeys = settings.GlobalHotkeys;
        foreach (var (action, reason) in conflicts)
        {
            _logger.LogHotkeyConflict(action.ToString(), reason);
        }

        _lastHotkeyConflicts = conflicts;
        _trayController?.SetHotkeyConflicts(conflicts);
        _stageViewModel?.SetHotkeyConflicts(conflicts);
    }

    private static bool AreHotkeysEqual(
        ImmutableDictionary<HotkeyAction, string> a,
        ImmutableDictionary<HotkeyAction, string> b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var ov) || !string.Equals(v, ov, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static Window CreateHiddenAnchorWindow() =>
        new()
        {
            Title = "LyricPulse Host Anchor",
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(-32000, -32000),
            Width = 1,
            Height = 1,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            IsHitTestVisible = false,
            SystemDecorations = SystemDecorations.None,
            Opacity = 0
        };

    private void CleanupDesktopHost()
    {
        if (Interlocked.Exchange(ref _disposed, true))
        {
            return;
        }

        // 1. FIRST on UI thread: remove tray icon, close Stage/OBS/overlays immediately (stops RAF & SampleFrame()!)
        _trayController?.Dispose();
        _trayController = null;

        _obsWindow?.Close();
        _obsWindow = null;

        if (_stageWindow is not null)
        {
            _stageWindow.PrepareForShutdown();
            _stageWindow.Close();
            _stageWindow = null;
        }
        _stageViewModel?.Dispose();
        _stageViewModel = null;

        if (_overlayCoordinator is not null)
        {
            _overlayCoordinator.SystemSettingsChanged -= OnWindowsSystemSettingsChanged;
            _overlayCoordinator.Dispose();
            _overlayCoordinator = null;
        }

        if (_fullscreenMonitor is not null)
        {
            _fullscreenMonitor.FullscreenStateChanged -= OnFullscreenStateChanged;
            _fullscreenMonitor.Dispose();
            _fullscreenMonitor = null;
        }

        if (_hotkeys is not null)
        {
            _hotkeys.HotkeyPressed -= OnGlobalHotkeyPressed;
            _hotkeys.UnregisterAll();
            _hotkeys.Dispose();
            _hotkeys = null;
        }

        _hiddenAnchorWindow?.Close();
        _hiddenAnchorWindow = null;

        // 2. SECOND off UI thread with strict 1.5s timeout: stop & dispose runtime + DI container cleanly
        var runtime = _runtime;
        var services = _services;
        _runtime = null;
        _services = null;

        if (runtime is not null)
        {
            runtime.StateChanged -= OnRuntimeStateChanged;
        }

        if (runtime is not null || services is not null)
        {
            var shutdownTask = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(ShutdownTimeoutMs);
                try
                {
                    if (runtime is not null)
                    {
                        await runtime.StopAsync(cts.Token).ConfigureAwait(false);
                        await runtime.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
                {
                    // Best-effort cleanup during exit.
                }

                try
                {
                    if (services is not null)
                    {
                        await services.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
                {
                    // Best-effort cleanup during exit.
                }
            });

            if (!shutdownTask.Wait(TimeSpan.FromMilliseconds(ShutdownTimeoutMs)))
            {
                _logger.LogShutdownTimeout(ShutdownTimeoutMs);
            }
        }
    }
}
