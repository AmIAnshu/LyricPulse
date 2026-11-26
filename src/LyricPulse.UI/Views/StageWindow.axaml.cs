using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LyricPulse.Core.Models;
using LyricPulse.UI.Theme;
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

    private readonly SolidColorBrush _dynamicAccentPrimaryBrush = new(Color.FromRgb(0x63, 0x66, 0xF1));
    private readonly SolidColorBrush _dynamicAccentPrimaryHoverBrush = new(Color.FromRgb(0x74, 0x77, 0xF4));
    private readonly SolidColorBrush _dynamicAccentPrimaryPressedBrush = new(Color.FromRgb(0x4F, 0x46, 0xE5));
    private readonly SolidColorBrush _dynamicAccentTextBrush = new(Color.FromRgb(0xC7, 0xD2, 0xFE));
    private readonly SolidColorBrush _dynamicAccentBorderBrush = new(Color.FromRgb(0x81, 0x8C, 0xF8));
    private readonly SolidColorBrush _dynamicAccentSoftBgBrush = new(Color.FromRgb(0x1E, 0x22, 0x42));
    private readonly SolidColorBrush _dynamicFocusRingBrush = new(Color.FromRgb(0xC7, 0xD2, 0xFE));

    private Color _lastAppliedAccent1;
    private Color _lastAppliedAccent2;
    private Color _lastAppliedAccent3;
    private bool _lastAppliedIsLight;
    private bool _hasAppliedAlbumChrome;

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

        RegisterAlbumChromeBrushes();
        SyncAlbumEnvironmentTheme(viewModel, force: true);
        ActualThemeVariantChanged += (_, _) =>
        {
            if (DataContext is StageViewModel currentVm)
            {
                SyncAlbumEnvironmentTheme(currentVm, force: true);
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
        if (HeroStageContainer is not null)
        {
            HeroStageContainer.SizeChanged += (_, _) => UpdateGlassCoverHoverSlot();
        }
        UpdateGlassCoverHoverSlot();
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

    public void WakeStageCanvas()
    {
        if (_disposed || !IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        if (_heroHost is not null)
        {
            _heroHost.IsCardPaused = false;
            _heroHost.RequestWake();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_disposed)
        {
            return;
        }

        if ((change.Property == WindowStateProperty && WindowState != WindowState.Minimized) ||
            (change.Property == IsVisibleProperty && IsVisible))
        {
            if (_heroHost is not null)
            {
                _heroHost.IsCardPaused = false;
                _heroHost.RequestWake();
            }

            UpdateVisibleStripCards();
        }
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
            if (_cardHosts.TryGetValue(card.StyleId, out var host) &&
                DataContext is StageViewModel vm &&
                !vm.EffectivePreviewsPaused &&
                !vm.IsFullscreen &&
                IsVisible)
            {
                host.IsCardPaused = false;
                host.TargetMaxFps = 45;
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
                var keepLive = card.IsActive &&
                               DataContext is StageViewModel vm &&
                               !vm.EffectivePreviewsPaused &&
                               !vm.IsFullscreen &&
                               IsVisible;
                host.TargetMaxFps = keepLive ? 24 : 0;
                host.IsCardPaused = !keepLive;
                if (!keepLive)
                {
                    host.RenderOnceWhenPaused();
                }
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
            var isFocused = vm.ActiveStyle == styleId;
            host = new StageCardCanvasHost(vm.Runtime, _fonts, _shaders, _telemetry, _rendererFactory)
            {
                SurfaceMode = StageRenderSurfaceMode.GalleryCard,
                StyleOverride = styleId,
                PreviewPhaseSeconds = meta.PreviewPhaseSeconds,
                IsCardPaused = !isFocused,
                TargetMaxFps = isFocused ? 24 : 0
            };
            _cardHosts[styleId] = host;
            if (!isFocused)
            {
                host.RenderOnceWhenPaused();
            }
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
        else if (e.PropertyName is nameof(StageViewModel.EffectivePreviewsPaused) or nameof(StageViewModel.ActiveStyle))
        {
            _heroHost?.RequestWake();
            UpdateVisibleStripCards();
        }
        else if (e.PropertyName is nameof(StageViewModel.IsLoadingLyrics)
                 or nameof(StageViewModel.LyricsVisible)
                 or nameof(StageViewModel.MediaHealth))
        {
            if (_heroHost is not null && IsVisible && WindowState != WindowState.Minimized)
            {
                _heroHost.IsCardPaused = false;
                _heroHost.RequestWake();
            }
        }
        else if (e.PropertyName is nameof(StageViewModel.AudioVisualizerMode)
                 or nameof(StageViewModel.EdgeGlowMode)
                 or nameof(StageViewModel.IsDemoMode)
                 or nameof(StageViewModel.AlbumArtBitmap)
                 or nameof(StageViewModel.MediaTrackTitle))
        {
            if (_heroHost is not null && IsVisible && WindowState != WindowState.Minimized)
            {
                _heroHost.IsCardPaused = false;
                _heroHost.RequestWake();
            }

            RefreshPausedCardThumbnails();
        }
        else if (e.PropertyName == nameof(StageViewModel.IsSettingsDrawerOpen) && !vm.IsSettingsDrawerOpen)
        {
            SettingsToggleBtn.Focus();
        }
        else if (e.PropertyName is nameof(StageViewModel.ActivePaletteColor1)
                 or nameof(StageViewModel.ActivePaletteColor2)
                 or nameof(StageViewModel.ActivePaletteColor3))
        {
            SyncAlbumEnvironmentTheme(vm, force: false);
        }
        else if (e.PropertyName == nameof(StageViewModel.IsGlassStageActive))
        {
            UpdateGlassCoverHoverSlot();
            if (GlassCoverTransportScrim is not null)
            {
                GlassCoverTransportScrim.Opacity = 0.0;
            }
        }
        else if (e.PropertyName is nameof(StageViewModel.AutoSyncColors)
                 or nameof(StageViewModel.ThemeVariantMode))
        {
            SyncAlbumEnvironmentTheme(vm, force: true);
        }
    }

    private void RegisterAlbumChromeBrushes()
    {
        Resources["LpAccentPrimaryBrush"] = _dynamicAccentPrimaryBrush;
        Resources["LpAccentPrimaryHoverBrush"] = _dynamicAccentPrimaryHoverBrush;
        Resources["LpAccentPrimaryPressedBrush"] = _dynamicAccentPrimaryPressedBrush;
        Resources["LpAccentTextBrush"] = _dynamicAccentTextBrush;
        Resources["LpAccentBorderBrush"] = _dynamicAccentBorderBrush;
        Resources["LpAccentSoftBgBrush"] = _dynamicAccentSoftBgBrush;
        Resources["LpFocusRingBrush"] = _dynamicFocusRingBrush;
    }

    private void SyncAlbumEnvironmentTheme(StageViewModel vm, bool force)
    {
        var isLight = ActualThemeVariant == ThemeVariant.Light;
        var c1 = vm.ActivePaletteColor1;
        var c2 = vm.ActivePaletteColor2;
        var c3 = vm.ActivePaletteColor3;

        if (!force &&
            _hasAppliedAlbumChrome &&
            _lastAppliedIsLight == isLight &&
            (ColorDelta(c1, _lastAppliedAccent1) +
             ColorDelta(c2, _lastAppliedAccent2) +
             ColorDelta(c3, _lastAppliedAccent3)) < 6)
        {
            return;
        }

        _hasAppliedAlbumChrome = true;
        _lastAppliedIsLight = isLight;
        _lastAppliedAccent1 = c1;
        _lastAppliedAccent2 = c2;
        _lastAppliedAccent3 = c3;

        var dynamicColors = AlbumEnvironmentPalette.ComputeDynamicAccentColors(c1, c2, c3, isLight);
        _dynamicAccentPrimaryBrush.Color = dynamicColors.Primary;
        _dynamicAccentPrimaryHoverBrush.Color = dynamicColors.PrimaryHover;
        _dynamicAccentPrimaryPressedBrush.Color = dynamicColors.PrimaryPressed;
        _dynamicAccentTextBrush.Color = dynamicColors.Text;
        _dynamicAccentBorderBrush.Color = dynamicColors.Border;
        _dynamicAccentSoftBgBrush.Color = dynamicColors.SoftBg;
        _dynamicFocusRingBrush.Color = dynamicColors.FocusRing;
    }

    private static int ColorDelta(Color a, Color b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

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
        UpdateGlassCoverHoverSlot();
    }

    private void UpdateGlassCoverHoverSlot()
    {
        if (GlassCoverHoverZone is null || HeroStageContainer is null)
        {
            return;
        }

        var w = (float)Math.Max(HeroStageContainer.Bounds.Width > 1.0 ? HeroStageContainer.Bounds.Width : Bounds.Width, 320.0);
        var h = (float)Math.Max(HeroStageContainer.Bounds.Height > 1.0 ? HeroStageContainer.Bounds.Height : HeroStageContainer.Height, 240.0);
        var rect = VinylGrooveStyleRenderer.ResolveCoverCardBounds(w, h, StageRenderSurfaceMode.StageCanvas);
        var radius = Math.Clamp(Math.Min(rect.Width, rect.Height) * 0.068f, 6f, 16f);

        Canvas.SetLeft(GlassCoverHoverZone, rect.Left);
        Canvas.SetTop(GlassCoverHoverZone, rect.Top);
        GlassCoverHoverZone.Width = rect.Width;
        GlassCoverHoverZone.Height = rect.Height;
        GlassCoverHoverZone.CornerRadius = new CornerRadius(radius);
        if (GlassCoverTransportScrim is not null)
        {
            GlassCoverTransportScrim.CornerRadius = new CornerRadius(radius);
        }
    }

    private void OnGlassCoverPointerEntered(object? sender, PointerEventArgs e)
    {
        if (GlassCoverTransportScrim is not null)
        {
            GlassCoverTransportScrim.Opacity = 1.0;
        }
    }

    private void OnGlassCoverPointerExited(object? sender, PointerEventArgs e)
    {
        if (GlassCoverTransportScrim is not null)
        {
            GlassCoverTransportScrim.Opacity = 0.0;
        }
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

            // CEO A3: Animate only the active or hovered visible card live;
            // keep unfocused grid cards paused after rendering one crisp frame.
            var animateLive = inViewport && (card.IsHovered || card.IsActive);
            var wasLive = !host.IsCardPaused;
            host.IsCardPaused = !animateLive;
            host.TargetMaxFps = animateLive ? (card.IsHovered ? 45 : 24) : 0;

            if (inViewport && !animateLive && wasLive)
            {
                host.RenderOnceWhenPaused();
            }
        }
    }

    private void RefreshPausedCardThumbnails()
    {
        if (DataContext is not StageViewModel vm || vm.IsFullscreen || !IsVisible)
        {
            return;
        }

        foreach (var host in _cardHosts.Values)
        {
            if (host.IsCardPaused)
            {
                host.RenderOnceWhenPaused();
            }
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
