using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LyricPulse.App.Platform;
using LyricPulse.Core.Models;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Shaders;

namespace LyricPulse.App;

public sealed class App : Application, IDisposable
{
    private FontAssetRegistry? _fonts;
    private ShaderLibrary? _shaders;
    private FrameBudgetTelemetry? _telemetry;
    private MultiMonitorOverlayCoordinator? _overlayCoordinator;
    private GlobalHotkeyManager? _hotkeys;
    private FullscreenAutoHideMonitor? _fullscreenMonitor;
    private Window? _hiddenAnchorWindow;

    public static SingleInstanceGuard? ActiveSingleInstanceGuard { get; set; }

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

    private void InitializeDesktopHost(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var settings = AppSettings.Default;
        var reducedMotion = settings.ReducedMotion || !Win32OverlayInterop.IsSystemClientAreaAnimationEnabled();

        _fonts = new FontAssetRegistry();
        _shaders = new ShaderLibrary();
        _telemetry = new FrameBudgetTelemetry();
        var overlayPlatform = new Win32OverlayInterop();

        _overlayCoordinator = new MultiMonitorOverlayCoordinator(
            overlayPlatform,
            placement => CreateOverlayCanvas(placement, settings, reducedMotion));

        _hiddenAnchorWindow = CreateHiddenAnchorWindow();
        _hiddenAnchorWindow.Show();
        _hiddenAnchorWindow.Hide();
        _overlayCoordinator.AttachAnchorWindow(_hiddenAnchorWindow);
        _overlayCoordinator.UpdateSettings(settings);

        _hotkeys = new GlobalHotkeyManager(action => Dispatcher.UIThread.Post(action));
        _hotkeys.HotkeyPressed += (_, action) => HandleGlobalHotkey(action);
        _ = _hotkeys.RegisterAll(settings.GlobalHotkeys);

        _fullscreenMonitor = new FullscreenAutoHideMonitor();
        _fullscreenMonitor.FullscreenStateChanged += (_, isFullscreen) =>
            Dispatcher.UIThread.Post(() => _overlayCoordinator?.SetFullscreenPaused(isFullscreen));
        _fullscreenMonitor.Start();

        ActiveSingleInstanceGuard?.StartActivationListener(() =>
        {
            _overlayCoordinator?.WakeAllCanvases();
        });

        _ = desktop;
    }

    private LyricCanvasControl CreateOverlayCanvas(
        OverlayWindowPlacement placement,
        AppSettings settings,
        bool effectiveReducedMotion)
    {
        var renderer = new SolidDiagnosticSceneRenderer();
        var fontId = FontAssetRegistry.ResolveFontId(settings.FontFamily);
        var baseFontDip = (float)settings.ComputeEffectiveFontSizeDip();

        return new LyricCanvasControl(
            _fonts!,
            _shaders!,
            _telemetry!,
            renderer,
            packetFactory: (w, h, scale, timeSec, dt) => new FrameRenderPacket(
                ActiveStyle: settings.ActiveStyle,
                RenderTimeSeconds: timeSec,
                DeltaSeconds: dt,
                ViewportWidthDip: w,
                ViewportHeightDip: h,
                RenderScaling: scale,
                GlobalOpacity: (float)settings.OverlayOpacity,
                ReducedMotion: effectiveReducedMotion,
                HighContrastScrim: settings.HighContrastScrim,
                ScrimIntensity: settings.ScrimIntensity,
                ActiveFontId: fontId,
                BaseFontSizeDip: baseFontDip,
                Palette: AlbumAccentPalette.Default,
                BoundsConfig: placement.BoundsConfig,
                SceneSnapshot: LyricSceneSnapshot.Empty));
    }

    private void HandleGlobalHotkey(HotkeyAction action)
    {
        if (action == HotkeyAction.ToggleLyrics && _overlayCoordinator is not null)
        {
            _overlayCoordinator.WakeAllCanvases();
        }
    }

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

    public void Dispose() => CleanupDesktopHost();

    private void CleanupDesktopHost()
    {
        _fullscreenMonitor?.Dispose();
        _fullscreenMonitor = null;

        _hotkeys?.Dispose();
        _hotkeys = null;

        _overlayCoordinator?.Dispose();
        _overlayCoordinator = null;

        _hiddenAnchorWindow?.Close();
        _hiddenAnchorWindow = null;

        _shaders?.Dispose();
        _shaders = null;

        _fonts?.Dispose();
        _fonts = null;
    }
}
