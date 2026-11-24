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
using LyricPulse.Rendering.Styles;
using Microsoft.Extensions.DependencyInjection;

namespace LyricPulse.App;

public sealed class App : Application, IDisposable
{
    private ServiceProvider? _services;
    private OverlayRuntimeController? _runtime;
    private MultiMonitorOverlayCoordinator? _overlayCoordinator;
    private IGlobalHotkeyService? _hotkeys;
    private FullscreenAutoHideMonitor? _fullscreenMonitor;
    private TrayController? _trayController;
    private Window? _hiddenAnchorWindow;
    private AppSettings? _lastAppliedOverlaySettings;
    private bool _systemReducedMotion;
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

    private void InitializeDesktopHost(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _services = Program.BuildServiceProvider(forHeadlessSmokeTest: false);
        _systemReducedMotion = !Win32OverlayInterop.IsSystemClientAreaAnimationEnabled();

        _runtime = _services.GetRequiredService<OverlayRuntimeController>();
        var overlayPlatform = _services.GetRequiredService<IOverlayWindowPlatform>();
        var trayIconFactory = _services.GetRequiredService<TrayIconFactory>();

        _overlayCoordinator = new MultiMonitorOverlayCoordinator(
            overlayPlatform,
            CreateOverlayCanvas);

        _hiddenAnchorWindow = CreateHiddenAnchorWindow();
        _hiddenAnchorWindow.Show();
        _hiddenAnchorWindow.Hide();
        _overlayCoordinator.AttachAnchorWindow(_hiddenAnchorWindow);

        _trayController = new TrayController(_runtime, trayIconFactory, () => desktop.Shutdown(0));

        _hotkeys = _services.GetService<IGlobalHotkeyService>();
        if (_hotkeys is not null)
        {
            _hotkeys.HotkeyPressed += OnGlobalHotkeyPressed;
        }

        _fullscreenMonitor = _services.GetService<IFullscreenDetector>() as FullscreenAutoHideMonitor;
        if (_fullscreenMonitor is not null)
        {
            _fullscreenMonitor.FullscreenStateChanged += OnFullscreenStateChanged;
            _fullscreenMonitor.Start();
        }

        _runtime.StateChanged += OnRuntimeStateChanged;

        ActiveSingleInstanceGuard?.StartActivationListener(() =>
        {
            _ = HandleSecondInstanceActivationAsync();
        });

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

                RegisterGlobalHotkeys(initialSnapshot.Settings);
                ApplySnapshotToHost(initialSnapshot);
            });
        }
        catch (OperationCanceledException)
        {
            // Normal on early exit.
        }
    }

    private LyricCanvasControl CreateOverlayCanvas(OverlayWindowPlacement placement)
    {
        var fonts = _services!.GetRequiredService<FontAssetRegistry>();
        var shaders = _services!.GetRequiredService<ShaderLibrary>();
        var telemetry = _services!.GetRequiredService<FrameBudgetTelemetry>();
        var router = _services!.GetRequiredService<ILyricSceneRenderer>();
        var runtime = _runtime!;

        return new LyricCanvasControl(
            fonts,
            shaders,
            telemetry,
            router,
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
                    boundsOverride: placement.BoundsConfig,
                    systemReducedMotion: _systemReducedMotion);
            });
    }

    private void OnRuntimeStateChanged(object? sender, OverlayPlaybackSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        _trayController?.ApplySnapshot(snapshot);
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

    private void OnFullscreenStateChanged(object? sender, bool isFullscreen)
    {
        _runtime?.RefreshFullscreenState();
        Dispatcher.UIThread.Post(() => _overlayCoordinator?.SetFullscreenPaused(isFullscreen));
    }

    private void OnGlobalHotkeyPressed(object? sender, HotkeyAction action)
    {
        if (_disposed || _runtime is null)
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
                    await HandleSecondInstanceActivationAsync().ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown race.
        }
    }

    private async Task HandleSecondInstanceActivationAsync()
    {
        if (_runtime is null || _disposed)
        {
            return;
        }

        try
        {
            var snap = _runtime.SampleFrame();
            if (!snap.Settings.LyricsVisible)
            {
                await _runtime.ToggleLyricsVisibilityAsync().ConfigureAwait(false);
            }
            else if (!snap.IsDemoMode && snap.Track is null)
            {
                // Until Phase 5 Gallery window arrives, surface Demo Mode so a second launch gives immediate feedback.
                await _runtime.SetDemoModeAsync(true).ConfigureAwait(false);
            }

            Dispatcher.UIThread.Post(() => _overlayCoordinator?.WakeAllCanvases());
        }
        catch (OperationCanceledException)
        {
            // Shutdown race.
        }
    }

    private void RegisterGlobalHotkeys(AppSettings settings)
    {
        if (_hotkeys is GlobalHotkeyManager manager)
        {
            _ = manager.RegisterAll(settings.GlobalHotkeys);
        }
        else if (_hotkeys is not null)
        {
            _hotkeys.UnregisterAll();
            foreach (var (action, gesture) in settings.GlobalHotkeys)
            {
                _ = _hotkeys.TryRegister(action, gesture, out _);
            }
        }
    }

    private static bool ShouldReconcileOverlayLayout(AppSettings previous, AppSettings current) =>
        previous.LyricsVisible != current.LyricsVisible ||
        previous.HideDuringFullscreen != current.HideDuringFullscreen ||
        previous.AnchorZone != current.AnchorZone ||
        previous.SafeMarginPx != current.SafeMarginPx ||
        !previous.DisabledDisplays.SequenceEqual(current.DisabledDisplays) ||
        previous.PerDisplayBounds.Count != current.PerDisplayBounds.Count;

    private static Window CreateHiddenAnchorWindow() =>
        new()
        {
            Title = "LyricPulse Host Anchor",
            Width = 1,
            Height = 1,
            ShowInTaskbar = false,
            ShowActivated = false,
            SystemDecorations = SystemDecorations.None,
            Opacity = 0
        };

    private void CleanupDesktopHost()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_runtime is not null)
        {
            _runtime.StateChanged -= OnRuntimeStateChanged;
            try
            {
                _runtime.StopAsync().GetAwaiter().GetResult();
                _runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                // Best-effort cleanup during exit.
            }

            _runtime = null;
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

        _trayController?.Dispose();
        _trayController = null;

        _overlayCoordinator?.Dispose();
        _overlayCoordinator = null;

        _hiddenAnchorWindow?.Close();
        _hiddenAnchorWindow = null;

        if (_services is not null)
        {
            try
            {
                _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                // Best-effort cleanup during exit.
            }

            _services = null;
        }
    }
}
