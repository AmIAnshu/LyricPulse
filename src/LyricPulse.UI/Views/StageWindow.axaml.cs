using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LyricPulse.Core.Models;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Shaders;
using LyricPulse.Rendering.Styles;
using LyricPulse.UI.Controls;
using LyricPulse.UI.ViewModels;

namespace LyricPulse.UI.Views;

public partial class StageWindow : Window, IDisposable
{
    private readonly FontAssetRegistry? _fonts;
    private readonly ShaderLibrary? _shaders;
    private readonly FrameBudgetTelemetry? _telemetry;
    private readonly ILyricSceneRendererFactory? _rendererFactory;
    private readonly StageCardCanvasHost? _heroHost;
    private readonly Dictionary<OverlayStyleId, StageCardCanvasHost> _cardHosts = new();
    private readonly DispatcherTimer _fullscreenIdleTimer;
    private readonly DispatcherTimer _postFullscreenSettleTimer;

    private WindowState _stateBeforeFullscreen = WindowState.Normal;
    private bool _allowShutdownClose;
    private bool _disposed;

    public StageWindow()
    {
        InitializeComponent();
        _fullscreenIdleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.0) };
        _postFullscreenSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
    }

    public StageWindow(
        StageViewModel viewModel,
        FontAssetRegistry fonts,
        ShaderLibrary shaders,
        FrameBudgetTelemetry telemetry,
        ILyricSceneRendererFactory rendererFactory)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
        _shaders = shaders ?? throw new ArgumentNullException(nameof(shaders));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _rendererFactory = rendererFactory ?? throw new ArgumentNullException(nameof(rendererFactory));

        InitializeComponent();
        DataContext = viewModel;

        _heroHost = new StageCardCanvasHost(viewModel.Runtime, fonts, shaders, telemetry, rendererFactory)
        {
            SurfaceMode = StageRenderSurfaceMode.StageCanvas,
            BottomFadeToBlack = !viewModel.IsFullscreen,
            TargetMaxFps = 0 // Native vsync 60+ FPS on hero stage
        };
        HeroStageSlot.Content = _heroHost;

        _fullscreenIdleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.0) };
        _fullscreenIdleTimer.Tick += (_, _) =>
        {
            _fullscreenIdleTimer.Stop();
            if (DataContext is StageViewModel vm && vm.IsFullscreen && !vm.IsAnyFlyoutOpen && !vm.IsSettingsDrawerOpen)
            {
                vm.IsChromeVisible = false;
            }
        };

        _postFullscreenSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _postFullscreenSettleTimer.Tick += (_, _) =>
        {
            _postFullscreenSettleTimer.Stop();
            if (!_disposed)
            {
                _heroHost?.RequestWake();
                UpdateVisibleStripCards();
            }
        };

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        StudioScroller.ScrollChanged += (_, _) => UpdateVisibleStripCards();
        SizeChanged += (_, _) =>
        {
            if (DataContext is StageViewModel vm)
            {
                vm.IsCompactDrawerMode = Bounds.Width < 880.0;
            }

            UpdateHeroStageHeight();
            UpdateVisibleStripCards();
        };
    }

    public void PrepareForShutdown() => _allowShutdownClose = true;

    public void SurfaceAndFocus(bool openSettingsDrawer = false)
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (DataContext is StageViewModel vm)
        {
            vm.IsCompactDrawerMode = Bounds.Width < 880.0;
            if (openSettingsDrawer)
            {
                vm.IsSettingsDrawerOpen = true;
            }

            vm.Runtime.SetStageWindowOpen(true);
        }

        Activate();
        if (_heroHost is not null)
        {
            _heroHost.IsCardPaused = false;
            _heroHost.RequestWake();
        }

        UpdateVisibleStripCards();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (DataContext is StageViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _fullscreenIdleTimer.Stop();
        _postFullscreenSettleTimer.Stop();
        _heroHost?.Dispose();
        foreach (var host in _cardHosts.Values)
        {
            host.Dispose();
        }

        _cardHosts.Clear();
        GC.SuppressFinalize(this);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is StageViewModel vm)
        {
            vm.IsCompactDrawerMode = Bounds.Width < 880.0;
            vm.Runtime.SetStageWindowOpen(true);
        }

        UpdateHeroStageHeight();

        if (_heroHost is not null)
        {
            _heroHost.IsCardPaused = false;
            _heroHost.RequestWake();
        }

        UpdateVisibleStripCards();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowShutdownClose)
        {
            // Close button minimizes/hides cleanly to tray and pauses BOTH hero & strip canvases (0% idle GPU!)
            e.Cancel = true;
            Hide();
            if (DataContext is StageViewModel vm)
            {
                vm.IsFullscreen = false;
                vm.Runtime.SetStageWindowOpen(false);
            }

            if (_heroHost is not null)
            {
                _heroHost.IsCardPaused = true;
                _heroHost.ReleaseGpuResources();
            }

            PauseAllPreviewCards(true);
            foreach (var host in _cardHosts.Values)
            {
                host.ReleaseGpuResources();
            }
            return;
        }

        base.OnClosing(e);
        Dispose();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (DataContext is StageViewModel vm && vm.IsFullscreen)
        {
            vm.IsChromeVisible = true;
            _fullscreenIdleTimer.Stop();
            _fullscreenIdleTimer.Start();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is StageViewModel vm && vm.IsRecordingHotkey)
        {
            if (e.Key == Key.Escape)
            {
                vm.CancelHotkeyCapture();
                e.Handled = true;
                return;
            }

            if (e.Key is Key.Back or Key.Delete)
            {
                vm.ClearCurrentRecordingHotkey();
                e.Handled = true;
                return;
            }

            if (e.Key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin))
            {
                var gesture = FormatKeyGesture(e.Key, e.KeyModifiers);
                var hasMod = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0;
                vm.CommitCapturedHotkey(gesture, hasMod);
                e.Handled = true;
                return;
            }
        }

        if (DataContext is StageViewModel viewModel)
        {
            if (e.Key == Key.Left && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                viewModel.NudgeOffsetCommand.Execute(-0.5);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Right && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                viewModel.NudgeOffsetCommand.Execute(0.5);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.D0 && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                viewModel.ResetOffsetCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key is Key.Left or Key.Right &&
                e.KeyModifiers == KeyModifiers.None &&
                TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not (Slider or TextBox or Button or ToggleButton))
            {
                if (e.Key == Key.Left)
                {
                    viewModel.PreviousStyleCommand.Execute(null);
                }
                else
                {
                    viewModel.NextStyleCommand.Execute(null);
                }

                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

    private void OnThumbHostLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContentControl slot || slot.Tag is not OverlayStyleId styleId)
        {
            return;
        }

        var host = GetOrCreateCardHost(styleId);
        if (host is null)
        {
            return;
        }

        if (host.Parent is ContentControl prevParent && !ReferenceEquals(prevParent, slot))
        {
            prevParent.Content = null;
        }

        slot.Content = host;
        Dispatcher.UIThread.Post(UpdateVisibleStripCards, DispatcherPriority.Background);
    }

    private void OnThumbHostUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is ContentControl slot && slot.Content is StageCardCanvasHost host)
        {
            host.IsCardPaused = true;
            slot.Content = null;
        }
    }

    private void OnCardPinClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control ctrl && ctrl.Tag is OverlayStyleId styleId && DataContext is StageViewModel vm)
        {
            vm.TogglePinStyleCommand.Execute(styleId);
        }
    }

    private void OnCardPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: StyleCardViewModel card })
        {
            card.IsHovered = true;
            if (_cardHosts.TryGetValue(card.StyleId, out var host))
            {
                host.TargetMaxFps = 60;
            }
        }
    }

    private void OnCardPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: StyleCardViewModel card })
        {
            card.IsHovered = false;
            if (_cardHosts.TryGetValue(card.StyleId, out var host))
            {
                host.TargetMaxFps = card.IsActive ? 30 : 24;
            }
        }
    }

    private void OnDrawerScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is StageViewModel vm)
        {
            vm.IsSettingsDrawerOpen = false;
            SettingsToggleBtn.Focus();
        }
    }

    private StageCardCanvasHost? GetOrCreateCardHost(OverlayStyleId styleId)
    {
        if (DataContext is not StageViewModel vm || _fonts is null || _shaders is null || _telemetry is null || _rendererFactory is null)
        {
            return null;
        }

        if (!_cardHosts.TryGetValue(styleId, out var host))
        {
            var meta = StyleCatalog.GetMetadata(styleId);
            host = new StageCardCanvasHost(vm.Runtime, _fonts, _shaders, _telemetry, _rendererFactory)
            {
                SurfaceMode = StageRenderSurfaceMode.GalleryCard,
                StyleOverride = styleId,
                PreviewPhaseSeconds = meta.PreviewPhaseSeconds,
                TargetMaxFps = 30
            };
            _cardHosts[styleId] = host;
        }

        return host;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not StageViewModel vm)
        {
            return;
        }

        if (e.PropertyName == nameof(StageViewModel.IsFullscreen))
        {
            ApplyFullscreenWindowState(vm.IsFullscreen);
        }
        else if (e.PropertyName == nameof(StageViewModel.EffectivePreviewsPaused))
        {
            UpdateVisibleStripCards();
        }
        else if (e.PropertyName == nameof(StageViewModel.IsSettingsDrawerOpen) && !vm.IsSettingsDrawerOpen)
        {
            SettingsToggleBtn.Focus();
        }
    }

    private void UpdateHeroStageHeight()
    {
        if (HeroStageContainer is null)
        {
            return;
        }

        var isFullscreen = DataContext is StageViewModel vm && vm.IsFullscreen;
        HeroStageContainer.Height = isFullscreen
            ? Math.Max(Bounds.Height, 360.0)
            : Math.Clamp(Bounds.Height * 0.76, 440.0, 680.0);
    }

    private void ApplyFullscreenWindowState(bool fullscreen)
    {
        if (fullscreen)
        {
            PauseAllPreviewCards(true);
            _postFullscreenSettleTimer.Stop();
            _stateBeforeFullscreen = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState;
            if (_heroHost is not null)
            {
                _heroHost.BottomFadeToBlack = false;
            }

            if (StudioScroller is not null)
            {
                StudioScroller.Offset = new Vector(0, 0);
                StudioScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }

            WindowState = WindowState.FullScreen;
            UpdateHeroStageHeight();
            _fullscreenIdleTimer.Stop();
            _fullscreenIdleTimer.Start();
        }
        else
        {
            _fullscreenIdleTimer.Stop();
            if (DataContext is StageViewModel vm)
            {
                vm.IsChromeVisible = true;
            }

            if (_heroHost is not null)
            {
                _heroHost.BottomFadeToBlack = true;
            }

            if (StudioScroller is not null)
            {
                StudioScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }

            // Keep ExtendClientAreaTitleBarHeightHint constant (40) to prevent DWM/NCCALCSIZE swapchain flicker.
            WindowState = _stateBeforeFullscreen;
            UpdateHeroStageHeight();
            _heroHost?.ReleaseGpuResources();
            _postFullscreenSettleTimer.Stop();
            _postFullscreenSettleTimer.Start();
        }
    }

    private void UpdateVisibleStripCards()
    {
        if (DataContext is not StageViewModel vm || StudioScroller is null)
        {
            return;
        }

        var pauseAll = vm.EffectivePreviewsPaused || vm.IsFullscreen || !IsVisible;
        if (pauseAll)
        {
            PauseAllPreviewCards(true);
            return;
        }

        var viewportH = Math.Max(StudioScroller.Viewport.Height, Bounds.Height);
        for (var i = 0; i < vm.FilteredStyleCards.Count; i++)
        {
            var card = vm.FilteredStyleCards[i];
            if (!_cardHosts.TryGetValue(card.StyleId, out var host))
            {
                continue;
            }

            var inViewport = true;
            if (host.Bounds.Height > 1.0 && host.TranslatePoint(new Point(0, 0), StudioScroller) is { } topLeft)
            {
                var bottom = topLeft.Y + host.Bounds.Height;
                inViewport = bottom >= -120.0 && topLeft.Y <= viewportH + 120.0;
            }

            host.IsCardPaused = !inViewport;
            host.TargetMaxFps = card.IsHovered ? 60 : (card.IsActive ? 30 : 24);
        }
    }

    private void PauseAllPreviewCards(bool paused)
    {
        foreach (var host in _cardHosts.Values)
        {
            host.IsCardPaused = paused;
        }
    }

    private static string FormatKeyGesture(Key key, KeyModifiers mods)
    {
        var parts = new List<string>(4);
        if ((mods & KeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((mods & KeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((mods & KeyModifiers.Alt) != 0) parts.Add("Alt");

        var keyName = key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            _ => key.ToString()
        };
        parts.Add(keyName);
        return string.Join('+', parts);
    }
}
