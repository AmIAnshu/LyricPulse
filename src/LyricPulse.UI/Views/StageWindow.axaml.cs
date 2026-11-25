using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
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
        viewModel.FilteredStyleCards.CollectionChanged += OnFilteredCardsChanged;
        StyleStripScroller.ScrollChanged += (_, _) => UpdateVisibleStripCards();
        SizeChanged += (_, _) => UpdateVisibleStripCards();

        RebuildStyleStrip();
    }

    public void PrepareForShutdown()
    {
        _allowShutdownClose = true;
    }

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
            if (openSettingsDrawer)
            {
                vm.IsSettingsDrawerOpen = true;
            }

            vm.Runtime.SetStageWindowOpen(true);
        }

        Activate();
        _heroHost?.RequestWake();
        UpdateVisibleStripCards();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is StageViewModel vm)
        {
            vm.Runtime.SetStageWindowOpen(true);
        }

        _heroHost?.RequestWake();
        UpdateVisibleStripCards();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fullscreenIdleTimer.Stop();
        _heroHost?.Dispose();
        foreach (var host in _cardHosts.Values)
        {
            host.Dispose();
        }

        _cardHosts.Clear();
        GC.SuppressFinalize(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowShutdownClose)
        {
            // Close button minimizes/hides cleanly to the Windows System Tray
            e.Cancel = true;
            Hide();
            if (DataContext is StageViewModel vm)
            {
                vm.IsFullscreen = false;
                vm.Runtime.SetStageWindowOpen(false);
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

            if (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.None && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Slider)
            {
                viewModel.PreviousStyleCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Right && e.KeyModifiers == KeyModifiers.None && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Slider)
            {
                viewModel.NextStyleCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
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
        else if (e.PropertyName is nameof(StageViewModel.EffectivePreviewsPaused) or nameof(StageViewModel.IsFullscreen))
        {
            UpdateVisibleStripCards();
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

    private void OnFilteredCardsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildStyleStrip();

    private void RebuildStyleStrip()
    {
        if (DataContext is not StageViewModel vm || _fonts is null || _shaders is null || _telemetry is null || _rendererFactory is null)
        {
            return;
        }

        StyleStripPanel.Children.Clear();

        foreach (var cardVm in vm.FilteredStyleCards)
        {
            if (!_cardHosts.TryGetValue(cardVm.StyleId, out var host))
            {
                host = new StageCardCanvasHost(vm.Runtime, _fonts, _shaders, _telemetry, _rendererFactory)
                {
                    SurfaceMode = StageRenderSurfaceMode.GalleryCard,
                    StyleOverride = cardVm.StyleId,
                    PreviewPhaseSeconds = cardVm.PhaseOffsetSeconds,
                    TargetMaxFps = 30
                };
                _cardHosts[cardVm.StyleId] = host;
            }

            var stageContainer = new Border
            {
                Height = 104,
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Child = host
            };

            // Active pill & Pin button overlay inside thumbnail stage
            var badgeRow = new Grid
            {
                Margin = new Thickness(6),
                VerticalAlignment = VerticalAlignment.Top,
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
            };

            var selectedPill = new Border
            {
                Padding = new Thickness(7, 3),
                CornerRadius = new CornerRadius(999),
                Background = TryFindBrush("LpAccentPrimaryBrush", Brushes.Indigo),
                IsVisible = cardVm.IsActive,
                Child = new TextBlock
                {
                    Text = "Selected",
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White
                }
            };
            Grid.SetColumn(selectedPill, 0);

            var pinBtn = new ToggleButton
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(7),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x14, 0x16, 0x1F)),
                IsChecked = cardVm.IsPinned,
                Content = new TextBlock
                {
                    Text = cardVm.IsPinned ? "Pinned" : "Pin",
                    FontSize = 9,
                    Foreground = Brushes.White
                }
            };
            pinBtn.SetValue(ToolTip.TipProperty, cardVm.PinToolTip);
            pinBtn.Click += (_, args) =>
            {
                args.Handled = true;
                cardVm.TogglePinCommand.Execute(cardVm.StyleId);
            };
            Grid.SetColumn(pinBtn, 2);

            badgeRow.Children.Add(selectedPill);
            badgeRow.Children.Add(pinBtn);

            var stageStack = new Grid();
            stageStack.Children.Add(stageContainer);
            stageStack.Children.Add(badgeRow);

            var footer = new StackPanel
            {
                Margin = new Thickness(8, 6, 8, 6),
                Spacing = 2,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        Children =
                        {
                            new TextBlock
                            {
                                Text = cardVm.DisplayName,
                                FontSize = 12,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = TryFindBrush("LpTextPrimaryBrush", Brushes.White)
                            },
                            new Border
                            {
                                [Grid.ColumnProperty] = 1,
                                Padding = new Thickness(5, 1),
                                CornerRadius = new CornerRadius(5),
                                Background = TryFindBrush("LpSurfaceElevatedBrush", Brushes.DarkSlateGray),
                                Child = new TextBlock
                                {
                                    Text = cardVm.CapabilityChip,
                                    FontSize = 10,
                                    Foreground = TryFindBrush("LpTextSecondaryBrush", Brushes.LightGray)
                                }
                            }
                        }
                    }
                }
            };

            var cardGrid = new Grid { RowDefinitions = new RowDefinitions("104,Auto") };
            Grid.SetRow(stageStack, 0);
            Grid.SetRow(footer, 1);
            cardGrid.Children.Add(stageStack);
            cardGrid.Children.Add(footer);

            var outerCard = new Border
            {
                Width = 216,
                Padding = new Thickness(cardVm.IsActive ? 3 : 4),
                CornerRadius = new CornerRadius(12),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = TryFindBrush("LpSurfaceBrush", Brushes.Black),
                BorderThickness = new Thickness(cardVm.IsActive ? 2 : 1),
                BorderBrush = cardVm.IsActive
                    ? TryFindBrush("LpAccentBorderBrush", Brushes.MediumSlateBlue)
                    : TryFindBrush("LpBorderDefaultBrush", Brushes.Gray),
                Child = cardGrid
            };

            outerCard.PointerPressed += (_, args) =>
            {
                if (!args.Handled)
                {
                    cardVm.SelectCommand.Execute(cardVm.StyleId);
                }
            };
            outerCard.PointerEntered += (_, _) =>
            {
                cardVm.IsHovered = true;
                host.TargetMaxFps = 60;
            };
            outerCard.PointerExited += (_, _) =>
            {
                cardVm.IsHovered = false;
                host.TargetMaxFps = cardVm.IsActive ? 60 : 30;
            };

            cardVm.PropertyChanged += (_, ev) =>
            {
                if (ev.PropertyName is nameof(StyleCardViewModel.IsActive) or nameof(StyleCardViewModel.IsPinned))
                {
                    selectedPill.IsVisible = cardVm.IsActive;
                    pinBtn.IsChecked = cardVm.IsPinned;
                    if (pinBtn.Content is TextBlock tb)
                    {
                        tb.Text = cardVm.IsPinned ? "Pinned" : "Pin";
                    }
                    outerCard.Padding = new Thickness(cardVm.IsActive ? 3 : 4);
                    outerCard.BorderThickness = new Thickness(cardVm.IsActive ? 2 : 1);
                    outerCard.BorderBrush = cardVm.IsActive
                        ? TryFindBrush("LpAccentBorderBrush", Brushes.MediumSlateBlue)
                        : TryFindBrush("LpBorderDefaultBrush", Brushes.Gray);
                    host.TargetMaxFps = (cardVm.IsActive || cardVm.IsHovered) ? 60 : 30;
                }
            };

            StyleStripPanel.Children.Add(outerCard);
        }

        Dispatcher.UIThread.Post(UpdateVisibleStripCards, DispatcherPriority.Background);
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
        var viewportW = Math.Max(StyleStripScroller.Viewport.Width, 1000.0);
        const double cardStride = 228.0; // 216 + 12 spacing

        for (var i = 0; i < vm.FilteredStyleCards.Count; i++)
        {
            var card = vm.FilteredStyleCards[i];
            if (!_cardHosts.TryGetValue(card.StyleId, out var host))
            {
                continue;
            }

            var cardLeft = i * cardStride;
            var cardRight = cardLeft + 216.0;
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

    private IBrush TryFindBrush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var res) && res is IBrush b ? b : fallback;

    private static string FormatKeyGesture(Key key, KeyModifiers mods)
    {
        var parts = new List<string>(4);
        if ((mods & KeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((mods & KeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((mods & KeyModifiers.Alt) != 0) parts.Add("Alt");

        var keyName = key switch
        {
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
