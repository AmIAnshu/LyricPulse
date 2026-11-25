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

    private WindowState _stateBeforeFullscreen = WindowState.Normal;
    private bool _allowShutdownClose;
    private bool _disposed;

    public StageWindow()
    {
        InitializeComponent();
        _fullscreenIdleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.0) };
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

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        StyleStripScroller.ScrollChanged += (_, _) => UpdateVisibleStripCards();
        SizeChanged += (_, _) =>
        {
            if (DataContext is StageViewModel vm)
            {
                vm.IsCompactDrawerMode = Bounds.Width < 880.0;
            }

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
            }

            PauseAllPreviewCards(true);
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
                host.TargetMaxFps = card.IsActive ? 60 : 30;
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
            UpdateVisibleStripCards();
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

    private void ApplyFullscreenWindowState(bool fullscreen)
    {
        if (fullscreen)
        {
            _stateBeforeFullscreen = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState;
            ExtendClientAreaTitleBarHeightHint = 0;
            WindowState = WindowState.FullScreen;
            _fullscreenIdleTimer.Stop();
            _fullscreenIdleTimer.Start();
        }
        else
        {
            _fullscreenIdleTimer.Stop();
            ExtendClientAreaTitleBarHeightHint = 40;
            WindowState = _stateBeforeFullscreen;
        }
    }

    private void UpdateVisibleStripCards()
    {
        if (DataContext is not StageViewModel vm)
        {
            return;
        }

        var pauseAll = vm.EffectivePreviewsPaused || vm.IsFullscreen || !IsVisible;
        if (pauseAll)
        {
            PauseAllPreviewCards(true);
            return;
        }

        var offsetX = StyleStripScroller.Offset.X;
        var viewportW = Math.Max(StyleStripScroller.Viewport.Width, 320.0);
        const double cardStride = 232.0; // 220 + 12 spacing

        for (var i = 0; i < vm.FilteredStyleCards.Count; i++)
        {
            var card = vm.FilteredStyleCards[i];
            if (!_cardHosts.TryGetValue(card.StyleId, out var host))
            {
                continue;
            }

            var cardLeft = i * cardStride;
            var cardRight = cardLeft + 220.0;
            var inViewport = cardRight >= offsetX - 40 && cardLeft <= offsetX + viewportW + 40;
            host.IsCardPaused = !inViewport;
            host.TargetMaxFps = (card.IsActive || card.IsHovered) ? 60 : 30;
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
