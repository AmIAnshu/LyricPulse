using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LyricPulse.App.Platform;
using LyricPulse.App.Tray;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Shaders;
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
    private Window? _hiddenAnchorWindow;
    private AppSettings? _lastAppliedOverlaySettings;
    private ImmutableDictionary<HotkeyAction, string>? _lastRegisteredHotkeys;
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
        if (previous.LyricsVisible != current.LyricsVisible ||
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
        _trayController = new TrayController(_runtime, trayIconFactory, () => desktop.Shutdown(0), trayLogger);

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

        _ = StartRuntimeAsync();
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
                    boundsOverride: overlayWindow.CurrentBoundsConfig);
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

        var settings = snapshot.Settings;
        if (_lastRegisteredHotkeys is null || !AreHotkeysEqual(_lastRegisteredHotkeys, settings.GlobalHotkeys))
        {
            RegisterGlobalHotkeys(settings);
        }

        if (_lastAppliedOverlaySettings is null || ShouldReconcileOverlayLayout(_lastAppliedOverlaySettings, settings))
        {
            _lastAppliedOverlaySettings = settings;
            _overlayCoordinator.UpdateSettings(settings);
        }

        _overlayCoordinator.SetFullscreenPaused(snapshot.IsFullscreenHidden);
        if (snapshot.IsLyricsVisible)
        {
            _overlayCoordinator.WakeAllCanvases();
        }
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
            else if (!snap.Settings.LyricsVisible)
            {
                await _runtime.ToggleLyricsVisibilityAsync().ConfigureAwait(false);
            }
            else if (!snap.IsDemoMode && (!snap.IsPlaying || snap.LyricsStatus != LyricsStatus.Synced))
            {
                // Until Phase 5 Gallery window arrives, surface Demo Mode so double-clicking always shows clear feedback.
                await _runtime.SetDemoModeAsync(true).ConfigureAwait(false);
            }

            Dispatcher.UIThread.Post(() => _overlayCoordinator?.WakeAllCanvases());
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

        _trayController?.SetHotkeyConflicts(conflicts);
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

        // 1. FIRST on UI thread: remove tray icon & close all overlays immediately (stops RAF & SampleFrame()!)
        _trayController?.Dispose();
        _trayController = null;

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
