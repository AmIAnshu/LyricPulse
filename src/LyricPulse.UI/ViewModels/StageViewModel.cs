using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricPulse.UI.ViewModels;

public enum StageFlyoutKind
{
    None = 0,
    Size = 1,
    Font = 2,
    Screen = 3,
    Aesthetics = 4,
    Diagnostics = 5,
    Visualizer = 6
}

public enum MediaHealthKind
{
    Idle = 0,
    PlayingSynced = 1,
    Paused = 2,
    Loading = 3,
    Warning = 4,
    Error = 5
}

public enum ObsChromaKeyMode
{
    Green = 0,
    Blue = 1,
    Black = 2
}

/// <summary>
/// Primary MVVM ViewModel powering LyricPulse's Fullscreen Ambient Party Stage Window,
/// Floating Top Pill Flyouts, 10-card Live Gallery Strip, Inline/Overlay Settings Drawer,
/// and OBS Broadcast Chroma-Key Window.
/// </summary>
public sealed class StageViewModel : ViewModelBase, IDisposable
{
    private readonly OverlayRuntimeController _runtime;
    private readonly Func<HotkeyAction, string, (bool Ok, string? Error)>? _hotkeyValidator;
    private readonly Action<bool>? _onObsWindowRequested;
    private readonly Action? _onDisplaysRefreshRequested;
    private readonly ILogger<StageViewModel> _logger;
    private readonly ImmutableArray<ColorPresetOption> _studioPresets = StyleCatalog.StudioPresets;

    private StageFlyoutKind _activeFlyout = StageFlyoutKind.None;
    private bool _isSettingsDrawerOpen;
    private bool _isCompactDrawerMode;
    private bool _isFullscreen;
    private bool _isChromeVisible = true;
    private bool _previewsPaused;
    private bool _isObsWindowOpen;
    private ObsChromaKeyMode _obsChromaMode = ObsChromaKeyMode.Green;
    private bool _isResetConfirmArmed;
    private string _styleFilter = "All";
    private HotkeyAction? _recordingHotkey;
    private Bitmap? _albumArtBitmap;
    private string? _albumArtHash;
    private bool _disposed;

    private OverlayPlaybackSnapshot _snapshot;

    public StageViewModel(
        OverlayRuntimeController runtime,
        Func<HotkeyAction, string, (bool Ok, string? Error)>? hotkeyValidator = null,
        Action<bool>? onObsWindowRequested = null,
        Action? onDisplaysRefreshRequested = null,
        ILogger<StageViewModel>? logger = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _hotkeyValidator = hotkeyValidator;
        _onObsWindowRequested = onObsWindowRequested;
        _onDisplaysRefreshRequested = onDisplaysRefreshRequested;
        _logger = logger ?? NullLogger<StageViewModel>.Instance;

        _snapshot = _runtime.SampleFrame();

        SelectStyleCommand = new RelayCommand(p =>
        {
            if (p is OverlayStyleId id)
            {
                _ = SafeRunAsync(() => _runtime.SetActiveStyleAsync(id));
            }
        });
        NextStyleCommand = new RelayCommand(() => CycleActiveStyle(step: 1));
        PreviousStyleCommand = new RelayCommand(() => CycleActiveStyle(step: -1));
        TogglePinStyleCommand = new RelayCommand(p =>
        {
            if (p is OverlayStyleId id)
            {
                _ = SafeRunAsync(() => TogglePinAsync(id));
            }
        });
        NudgeOffsetCommand = new RelayCommand(p =>
        {
            if (p is double sec)
            {
                _ = SafeRunAsync(() => _runtime.NudgeTimingOffsetAsync(TimeSpan.FromSeconds(sec)));
            }
            else if (p is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                _ = SafeRunAsync(() => _runtime.NudgeTimingOffsetAsync(TimeSpan.FromSeconds(parsed)));
            }
        }, _ => CanAdjustOffset);
        ResetOffsetCommand = new RelayCommand(
            () => _ = SafeRunAsync(() => _runtime.ResetTimingOffsetAsync()),
            () => CanAdjustOffset && HasNonZeroOffset);

        ToggleFullscreenCommand = new RelayCommand(() => IsFullscreen = !IsFullscreen);
        ExitFullscreenCommand = new RelayCommand(() =>
        {
            if (ActiveFlyout != StageFlyoutKind.None)
            {
                ActiveFlyout = StageFlyoutKind.None;
                return;
            }

            if (IsSettingsDrawerOpen)
            {
                IsSettingsDrawerOpen = false;
                return;
            }

            if (IsFullscreen)
            {
                IsFullscreen = false;
            }
        });
        ToggleSettingsDrawerCommand = new RelayCommand(() =>
        {
            ActiveFlyout = StageFlyoutKind.None;
            IsSettingsDrawerOpen = !IsSettingsDrawerOpen;
        });
        ToggleFlyoutCommand = new RelayCommand(p =>
        {
            if (p is StageFlyoutKind kind)
            {
                ActiveFlyout = ActiveFlyout == kind ? StageFlyoutKind.None : kind;
                if (ActiveFlyout != StageFlyoutKind.None)
                {
                    IsSettingsDrawerOpen = false;
                }
            }
            else if (p is string name && Enum.TryParse<StageFlyoutKind>(name, ignoreCase: true, out var parsedKind))
            {
                ActiveFlyout = ActiveFlyout == parsedKind ? StageFlyoutKind.None : parsedKind;
                if (ActiveFlyout != StageFlyoutKind.None)
                {
                    IsSettingsDrawerOpen = false;
                }
            }
        });
        ClosePopupsCommand = new RelayCommand(() =>
        {
            ActiveFlyout = StageFlyoutKind.None;
            IsSettingsDrawerOpen = false;
        });

        ToggleDemoModeCommand = new RelayCommand(() => _ = SafeRunAsync(() => _runtime.ToggleDemoModeAsync()));
        RetryLyricsCommand = new RelayCommand(() => _ = SafeRunAsync(() => _runtime.RetryLyricsFetchAsync()));
        ToggleDesktopOverlayCommand = new RelayCommand(() => _ = SafeRunAsync(() => _runtime.ToggleDesktopOverlayAsync()));
        ToggleLyricsVisibleCommand = new RelayCommand(() => _ = SafeRunAsync(() => _runtime.ToggleLyricsVisibilityAsync()));
        TogglePreviewsPausedCommand = new RelayCommand(() => PreviewsPaused = !PreviewsPaused);
        RefreshDisplaysCommand = new RelayCommand(() => _onDisplaysRefreshRequested?.Invoke());
        EnablePrimaryDisplayCommand = new RelayCommand(() =>
            _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { DisabledDisplays = ImmutableArray<string>.Empty })));

        ToggleObsWindowCommand = new RelayCommand(() =>
        {
            IsObsWindowOpen = !IsObsWindowOpen;
            _onObsWindowRequested?.Invoke(IsObsWindowOpen);
        });

        SelectFontCommand = new RelayCommand(p =>
        {
            if (p is string fontName)
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { FontFamily = fontName }));
            }
        });
        SelectAnchorZoneCommand = new RelayCommand(p =>
        {
            if (p is AnchorZone z)
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { AnchorZone = z }));
            }
            else if (p is string s && Enum.TryParse<AnchorZone>(s, ignoreCase: true, out var parsed))
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(st => st with { AnchorZone = parsed }));
            }
        });
        SelectBackdropModeCommand = new RelayCommand(p =>
        {
            if (p is StageBackdropMode m)
            {
                _ = SafeRunAsync(() => _runtime.SetStageBackdropModeAsync(m));
            }
            else if (p is string s && Enum.TryParse<StageBackdropMode>(s, ignoreCase: true, out var parsed))
            {
                _ = SafeRunAsync(() => _runtime.SetStageBackdropModeAsync(parsed));
            }
        });
        SelectThemeVariantCommand = new RelayCommand(p =>
        {
            if (p is ThemeVariantMode tv)
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { ThemeVariantMode = tv }));
            }
            else if (p is string s && Enum.TryParse<ThemeVariantMode>(s, ignoreCase: true, out var parsed))
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(st => st with { ThemeVariantMode = parsed }));
            }
        });
        SelectScrimIntensityCommand = new RelayCommand(p =>
        {
            if (p is ScrimIntensity si)
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { ScrimIntensity = si }));
            }
            else if (p is string s && Enum.TryParse<ScrimIntensity>(s, ignoreCase: true, out var parsed))
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(st => st with { ScrimIntensity = parsed }));
            }
        });
        SelectSecondaryLineCommand = new RelayCommand(p =>
        {
            if (p is SecondaryLineMode sm)
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { SecondaryLineMode = sm }));
            }
            else if (p is string s && Enum.TryParse<SecondaryLineMode>(s, ignoreCase: true, out var parsed))
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(st => st with { SecondaryLineMode = parsed }));
            }
        });
        SelectEdgeGlowModeCommand = new RelayCommand(p =>
        {
            if (p is EdgeGlowMode eg)
            {
                _ = SafeRunAsync(() => _runtime.SetEdgeGlowModeAsync(eg));
            }
            else if (p is string s && Enum.TryParse<EdgeGlowMode>(s, ignoreCase: true, out var parsed))
            {
                _ = SafeRunAsync(() => _runtime.SetEdgeGlowModeAsync(parsed));
            }
        });
        SelectAudioVisualizerModeCommand = new RelayCommand(p =>
        {
            if (p is AudioVisualizerMode vmMode)
            {
                _ = SafeRunAsync(() => _runtime.SetAudioVisualizerModeAsync(vmMode));
            }
            else if (p is string s && Enum.TryParse<AudioVisualizerMode>(s, ignoreCase: true, out var parsed))
            {
                _ = SafeRunAsync(() => _runtime.SetAudioVisualizerModeAsync(parsed));
            }
        });
        SelectStyleByIndexCommand = new RelayCommand(p =>
        {
            if (p is int idx && idx >= 0 && idx < StyleCatalog.All.Length)
            {
                _ = SafeRunAsync(() => _runtime.SetActiveStyleAsync(StyleCatalog.All[idx].StyleId));
            }
            else if (p is OverlayStyleId sid)
            {
                _ = SafeRunAsync(() => _runtime.SetActiveStyleAsync(sid));
            }
        });
        ApplyColorPresetCommand = new RelayCommand(p =>
        {
            if (p is ColorPresetOption preset)
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with
                {
                    AutoSyncColors = false,
                    ManualAccent1 = preset.Accent1,
                    ManualAccent2 = preset.Accent2,
                    ManualAccent3 = preset.Accent3
                }));
            }
        });
        SetStyleFilterCommand = new RelayCommand(p =>
        {
            if (p is string f)
            {
                StyleFilter = f;
            }
        });

        ArmResetCommand = new RelayCommand(() => IsResetConfirmArmed = true);
        CancelResetCommand = new RelayCommand(() => IsResetConfirmArmed = false);
        ConfirmResetCommand = new RelayCommand(() =>
        {
            IsResetConfirmArmed = false;
            _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(_ => AppSettings.Default));
        });
        ResetHotkeysCommand = new RelayCommand(() =>
            _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { GlobalHotkeys = AppSettings.CreateDefaultHotkeys() })));

        BeginRecordHotkeyCommand = new RelayCommand(p =>
        {
            if (p is HotkeyAction act)
            {
                BeginHotkeyCapture(act);
            }
        });
        ClearHotkeyCommand = new RelayCommand(p =>
        {
            if (p is HotkeyAction act)
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with
                {
                    GlobalHotkeys = s.GlobalHotkeys.SetItem(act, string.Empty)
                }));
            }
        });

        foreach (var meta in StyleCatalog.All)
        {
            StyleCards.Add(new StyleCardViewModel(meta, SelectStyleCommand, TogglePinStyleCommand));
        }

        InitHotkeyRows();
        ApplySnapshot(_snapshot);
    }

    public OverlayRuntimeController Runtime => _runtime;
    public OverlayPlaybackSnapshot CurrentSnapshot => _snapshot;

    public ObservableCollection<StyleCardViewModel> StyleCards { get; } = [];
    public ObservableCollection<StyleCardViewModel> FilteredStyleCards { get; } = [];
    public ObservableCollection<DisplayOptionViewModel> Displays { get; } = [];
    public ObservableCollection<HotkeyBindingViewModel> HotkeyRows { get; } = [];
    public ImmutableArray<ColorPresetOption> StudioPresets => _studioPresets;

    public ICommand SelectStyleCommand { get; }
    public ICommand NextStyleCommand { get; }
    public ICommand PreviousStyleCommand { get; }
    public ICommand TogglePinStyleCommand { get; }
    public RelayCommand NudgeOffsetCommand { get; }
    public RelayCommand ResetOffsetCommand { get; }
    public ICommand ToggleFullscreenCommand { get; }
    public ICommand ExitFullscreenCommand { get; }
    public ICommand ToggleSettingsDrawerCommand { get; }
    public ICommand ToggleFlyoutCommand { get; }
    public ICommand ClosePopupsCommand { get; }
    public ICommand ToggleDemoModeCommand { get; }
    public ICommand RetryLyricsCommand { get; }
    public ICommand ToggleDesktopOverlayCommand { get; }
    public ICommand ToggleLyricsVisibleCommand { get; }
    public ICommand TogglePreviewsPausedCommand { get; }
    public ICommand RefreshDisplaysCommand { get; }
    public ICommand EnablePrimaryDisplayCommand { get; }
    public ICommand ToggleObsWindowCommand { get; }
    public ICommand SelectFontCommand { get; }
    public ICommand SelectAnchorZoneCommand { get; }
    public ICommand SelectBackdropModeCommand { get; }
    public ICommand SelectThemeVariantCommand { get; }
    public ICommand SelectScrimIntensityCommand { get; }
    public ICommand SelectSecondaryLineCommand { get; }
    public ICommand SelectEdgeGlowModeCommand { get; }
    public ICommand SelectAudioVisualizerModeCommand { get; }
    public ICommand SelectStyleByIndexCommand { get; }
    public ICommand ApplyColorPresetCommand { get; }
    public ICommand SetStyleFilterCommand { get; }
    public ICommand ArmResetCommand { get; }
    public ICommand CancelResetCommand { get; }
    public ICommand ConfirmResetCommand { get; }
    public ICommand ResetHotkeysCommand { get; }
    public ICommand BeginRecordHotkeyCommand { get; }
    public ICommand ClearHotkeyCommand { get; }

    public StyleMetadata SelectedStyleInfo => StyleCatalog.GetMetadata(_snapshot.Settings.ActiveStyle);
    public OverlayStyleId ActiveStyle => _snapshot.Settings.ActiveStyle;
    public int ActiveStyleIndex => Math.Max(0, StyleCatalog.All.IndexOf(SelectedStyleInfo));
    public string StyleCounterText => $"{ActiveStyleIndex + 1} / {StyleCatalog.All.Length}";
    public bool IsActiveStylePinned => _snapshot.Settings.PinnedStyles.Contains(ActiveStyle);
    public string ActivePinTooltip => IsActiveStylePinned ? "Unpin from System Tray" : "Pin to System Tray";
    public int PinnedCount => _snapshot.Settings.PinnedStyles.IsDefault ? 0 : _snapshot.Settings.PinnedStyles.Length;
    public string GalleryCountCaption => $"10 styles · {PinnedCount} pinned";

    public StageFlyoutKind ActiveFlyout
    {
        get => _activeFlyout;
        set
        {
            if (SetProperty(ref _activeFlyout, value))
            {
                OnPropertyChanged(nameof(IsAnyFlyoutOpen));
                OnPropertyChanged(nameof(IsSizeFlyoutOpen));
                OnPropertyChanged(nameof(IsFontFlyoutOpen));
                OnPropertyChanged(nameof(IsScreenFlyoutOpen));
                OnPropertyChanged(nameof(IsAestheticsFlyoutOpen));
                OnPropertyChanged(nameof(IsDiagnosticsFlyoutOpen));
                OnPropertyChanged(nameof(IsVisualizerFlyoutOpen));
                OnPropertyChanged(nameof(TopBarOpacity));
                OnPropertyChanged(nameof(IsTopBarInteractive));
            }
        }
    }

    public bool IsAnyFlyoutOpen => ActiveFlyout != StageFlyoutKind.None;
    public bool IsSizeFlyoutOpen => ActiveFlyout == StageFlyoutKind.Size;
    public bool IsFontFlyoutOpen => ActiveFlyout == StageFlyoutKind.Font;
    public bool IsScreenFlyoutOpen => ActiveFlyout == StageFlyoutKind.Screen;
    public bool IsAestheticsFlyoutOpen => ActiveFlyout == StageFlyoutKind.Aesthetics;
    public bool IsDiagnosticsFlyoutOpen => ActiveFlyout == StageFlyoutKind.Diagnostics;
    public bool IsVisualizerFlyoutOpen => ActiveFlyout == StageFlyoutKind.Visualizer;

    public bool IsSettingsDrawerOpen
    {
        get => _isSettingsDrawerOpen;
        set
        {
            if (SetProperty(ref _isSettingsDrawerOpen, value))
            {
                OnPropertyChanged(nameof(TopBarOpacity));
                OnPropertyChanged(nameof(IsTopBarInteractive));
                OnPropertyChanged(nameof(IsDockedDrawerVisible));
                OnPropertyChanged(nameof(IsOverlayDrawerVisible));
            }
        }
    }

    public bool IsCompactDrawerMode
    {
        get => _isCompactDrawerMode;
        set
        {
            if (SetProperty(ref _isCompactDrawerMode, value))
            {
                OnPropertyChanged(nameof(IsDockedDrawerVisible));
                OnPropertyChanged(nameof(IsOverlayDrawerVisible));
            }
        }
    }

    public bool IsDockedDrawerVisible => IsSettingsDrawerOpen && !IsCompactDrawerMode;
    public bool IsOverlayDrawerVisible => IsSettingsDrawerOpen && IsCompactDrawerMode;

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set
        {
            if (SetProperty(ref _isFullscreen, value))
            {
                if (value)
                {
                    ActiveFlyout = StageFlyoutKind.None;
                    IsSettingsDrawerOpen = false;
                }
                else
                {
                    IsChromeVisible = true;
                }

                OnPropertyChanged(nameof(TopBarOpacity));
                OnPropertyChanged(nameof(IsTopBarInteractive));
                OnPropertyChanged(nameof(ShowHeroCarouselChrome));
                OnPropertyChanged(nameof(ShowBottomStyleStrip));
            }
        }
    }

    public bool IsChromeVisible
    {
        get => _isChromeVisible;
        set
        {
            if (SetProperty(ref _isChromeVisible, value))
            {
                OnPropertyChanged(nameof(TopBarOpacity));
                OnPropertyChanged(nameof(IsTopBarInteractive));
                OnPropertyChanged(nameof(ShowHeroCarouselChrome));
                OnPropertyChanged(nameof(ShowBottomStyleStrip));
            }
        }
    }

    public double TopBarOpacity => (!IsFullscreen || IsChromeVisible || IsAnyFlyoutOpen || IsSettingsDrawerOpen) ? 1.0 : 0.0;
    public bool IsTopBarInteractive => TopBarOpacity > 0.5;
    public bool ShowHeroCarouselChrome => !IsFullscreen;
    public bool ShowBottomStyleStrip => !IsFullscreen;

    public bool PreviewsPaused
    {
        get => _previewsPaused;
        set
        {
            if (SetProperty(ref _previewsPaused, value))
            {
                OnPropertyChanged(nameof(EffectivePreviewsPaused));
                OnPropertyChanged(nameof(PausePreviewsLabel));
            }
        }
    }

    public bool EffectivePreviewsPaused => PreviewsPaused || _snapshot.IsEffectiveReducedMotion;
    public bool IsPreviewsToggleEnabled => !_snapshot.IsEffectiveReducedMotion;
    public string PausePreviewsLabel => _snapshot.IsEffectiveReducedMotion
        ? "Paused (Reduced Motion)"
        : EffectivePreviewsPaused ? "Play previews" : "Pause previews";

    public bool IsObsWindowOpen
    {
        get => _isObsWindowOpen;
        set => SetProperty(ref _isObsWindowOpen, value);
    }

    public ObsChromaKeyMode ObsChromaMode
    {
        get => _obsChromaMode;
        set
        {
            if (SetProperty(ref _obsChromaMode, value))
            {
                OnPropertyChanged(nameof(ObsChromaHex));
            }
        }
    }

    public string ObsChromaHex => ObsChromaMode switch
    {
        ObsChromaKeyMode.Blue => "#0000FF",
        ObsChromaKeyMode.Black => "#000000",
        _ => "#00FF00"
    };

    public bool IsResetConfirmArmed
    {
        get => _isResetConfirmArmed;
        set => SetProperty(ref _isResetConfirmArmed, value);
    }

    public string StyleFilter
    {
        get => _styleFilter;
        set
        {
            if (SetProperty(ref _styleFilter, value))
            {
                RebuildFilteredStyleCards();
                OnPropertyChanged(nameof(IsFilterEmpty));
                OnPropertyChanged(nameof(IsFilterAll));
                OnPropertyChanged(nameof(IsFilterPinned));
                OnPropertyChanged(nameof(IsFilterVisuals));
                OnPropertyChanged(nameof(IsFilterMotion));
            }
        }
    }

    public bool IsFilterAll => StyleFilter == "All";
    public bool IsFilterPinned => StyleFilter == "Pinned";
    public bool IsFilterVisuals => StyleFilter == "With Visuals";
    public bool IsFilterMotion => StyleFilter == "Motion";
    public bool IsFilterEmpty => FilteredStyleCards.Count == 0;

    // Settings properties bound directly in UI
    public string FontFamily => _snapshot.Settings.FontFamily;
    public bool IsJostSelected => FontFamily.Equals("Jost", StringComparison.OrdinalIgnoreCase);
    public bool IsBodoniSelected => FontFamily.Contains("Bodoni", StringComparison.OrdinalIgnoreCase);
    public bool IsMulishSelected => FontFamily.Equals("Mulish", StringComparison.OrdinalIgnoreCase);
    public bool IsNunitoSelected => FontFamily.Equals("Nunito", StringComparison.OrdinalIgnoreCase);

    public int FontScalePercent
    {
        get => (int)Math.Round(_snapshot.Settings.FontScale * 100.0);
        set
        {
            var clamped = Math.Clamp(value, 60, 160) / 100.0;
            if (Math.Abs(_snapshot.Settings.FontScale - clamped) > 0.005)
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { FontScale = clamped }));
            }
        }
    }

    public int FullscreenStageScalePercent
    {
        get => (int)Math.Round(_snapshot.Settings.FullscreenStageScale * 100f);
        set
        {
            var clamped = Math.Clamp(value, 120, 260) / 100f;
            if (Math.Abs(_snapshot.Settings.FullscreenStageScale - clamped) > 0.01f)
            {
                _ = SafeRunAsync(() => _runtime.SetFullscreenStageScaleAsync(clamped));
            }
        }
    }

    public int PartyEnergyPercent
    {
        get => (int)Math.Round(_snapshot.Settings.PartyEnergyLevel * 100f);
        set
        {
            var clamped = Math.Clamp(value, 50, 150) / 100f;
            if (Math.Abs(_snapshot.Settings.PartyEnergyLevel - clamped) > 0.01f)
            {
                _ = SafeRunAsync(() => _runtime.SetPartyEnergyLevelAsync(clamped));
            }
        }
    }

    public int OverlayOpacityPercent
    {
        get => (int)Math.Round(_snapshot.Settings.OverlayOpacity * 100.0);
        set
        {
            var clamped = Math.Clamp(value, 25, 100) / 100.0;
            if (Math.Abs(_snapshot.Settings.OverlayOpacity - clamped) > 0.005)
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { OverlayOpacity = clamped }));
            }
        }
    }

    public int SafeMarginPx
    {
        get => _snapshot.Settings.SafeMarginPx;
        set
        {
            var clamped = Math.Clamp(value, AppSettings.MinSafeMarginPx, AppSettings.MaxSafeMarginPx);
            if (_snapshot.Settings.SafeMarginPx != clamped)
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { SafeMarginPx = clamped }));
            }
        }
    }

    public bool AutoSyncColors
    {
        get => _snapshot.Settings.AutoSyncColors;
        set
        {
            if (_snapshot.Settings.AutoSyncColors != value)
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { AutoSyncColors = value }));
            }
        }
    }

    public Color ManualColor1
    {
        get => ToAvaloniaColor(_snapshot.Settings.ManualAccent1);
        set => _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { ManualAccent1 = FromAvaloniaColor(value) }));
    }

    public Color ManualColor2
    {
        get => ToAvaloniaColor(_snapshot.Settings.ManualAccent2);
        set => _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { ManualAccent2 = FromAvaloniaColor(value) }));
    }

    public Color ManualColor3
    {
        get => ToAvaloniaColor(_snapshot.Settings.ManualAccent3);
        set => _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { ManualAccent3 = FromAvaloniaColor(value) }));
    }

    public string ManualHex1 => _snapshot.Settings.ManualAccent1.ToHex();
    public string ManualHex2 => _snapshot.Settings.ManualAccent2.ToHex();
    public string ManualHex3 => _snapshot.Settings.ManualAccent3.ToHex();

    public Color ActivePaletteColor1 => ToAvaloniaColor(_snapshot.ActivePalette.AccentColor1);
    public Color ActivePaletteColor2 => ToAvaloniaColor(_snapshot.ActivePalette.AccentColor2);
    public Color ActivePaletteColor3 => ToAvaloniaColor(_snapshot.ActivePalette.AccentColor3);

    public bool IsAmbientPartyMesh => _snapshot.Settings.StageBackdropMode == StageBackdropMode.AmbientPartyMesh;
    public bool IsPureBlackBackdrop => _snapshot.Settings.StageBackdropMode == StageBackdropMode.PureBlack;

    public bool ShowFollowingDot
    {
        get => _snapshot.Settings.ShowFollowingDot;
        set
        {
            if (_snapshot.Settings.ShowFollowingDot != value)
            {
                _ = SafeRunAsync(() => _runtime.ToggleFollowingDotAsync());
            }
        }
    }

    public bool ShowColorSweepLayer
    {
        get => _snapshot.Settings.ShowColorSweepLayer;
        set
        {
            if (_snapshot.Settings.ShowColorSweepLayer != value)
            {
                _ = SafeRunAsync(() => _runtime.ToggleColorSweepLayerAsync());
            }
        }
    }

    public bool DesktopOverlayEnabled
    {
        get => _snapshot.Settings.DesktopOverlayEnabled;
        set
        {
            if (_snapshot.Settings.DesktopOverlayEnabled != value)
            {
                _ = SafeRunAsync(() => _runtime.ToggleDesktopOverlayAsync());
            }
        }
    }

    public bool LyricsVisible => _snapshot.Settings.LyricsVisible;

    public bool ReducedMotion
    {
        get => _snapshot.Settings.ReducedMotion;
        set
        {
            if (_snapshot.Settings.ReducedMotion != value)
            {
                _ = SafeRunAsync(() => _runtime.ToggleReducedMotionAsync());
            }
        }
    }

    public bool IsOsReducedMotion => _snapshot.IsOsReducedMotion;

    public bool HighContrastScrim
    {
        get => _snapshot.Settings.HighContrastScrim;
        set
        {
            if (_snapshot.Settings.HighContrastScrim != value)
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { HighContrastScrim = value }));
            }
        }
    }

    public bool UniversalPlayerMode
    {
        get => _snapshot.Settings.UniversalPlayerMode;
        set
        {
            if (_snapshot.Settings.UniversalPlayerMode != value)
            {
                _ = SafeRunAsync(() => _runtime.ToggleUniversalPlayerModeAsync());
            }
        }
    }

    public bool HideDuringFullscreen
    {
        get => _snapshot.Settings.HideDuringFullscreen;
        set
        {
            if (_snapshot.Settings.HideDuringFullscreen != value)
            {
                _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with { HideDuringFullscreen = value }));
            }
        }
    }

    public AnchorZone AnchorZone => _snapshot.Settings.AnchorZone;
    public ScrimIntensity ScrimIntensity => _snapshot.Settings.ScrimIntensity;
    public ThemeVariantMode ThemeVariantMode => _snapshot.Settings.ThemeVariantMode;
    public SecondaryLineMode SecondaryLineMode => _snapshot.Settings.SecondaryLineMode;

    public bool IsAnchorTopCenter => AnchorZone == AnchorZone.TopCenter;
    public bool IsAnchorMiddleCenter => AnchorZone == AnchorZone.MiddleCenter;
    public bool IsAnchorBottomThird => AnchorZone == AnchorZone.BottomThird;
    public bool IsAnchorBottomLeft => AnchorZone == AnchorZone.BottomLeft;
    public bool IsAnchorBottomRight => AnchorZone == AnchorZone.BottomRight;

    public bool IsThemeSystem => ThemeVariantMode == ThemeVariantMode.System;
    public bool IsThemeDark => ThemeVariantMode == ThemeVariantMode.Dark;
    public bool IsThemeLight => ThemeVariantMode == ThemeVariantMode.Light;

    public bool IsScrimOff => ScrimIntensity == ScrimIntensity.Off;
    public bool IsScrimSubtle => ScrimIntensity == ScrimIntensity.Subtle;
    public bool IsScrimBalanced => ScrimIntensity == ScrimIntensity.Balanced;
    public bool IsScrimStrong => ScrimIntensity == ScrimIntensity.Strong;

    public bool IsSecondaryOff => SecondaryLineMode == SecondaryLineMode.Off;
    public bool IsSecondaryTranslation => SecondaryLineMode == SecondaryLineMode.Translation;
    public bool IsSecondaryRomanization => SecondaryLineMode == SecondaryLineMode.Romanization;

    public EdgeGlowMode EdgeGlowMode => _snapshot.Settings.EdgeGlowMode;
    public bool IsEdgeGlowOff => EdgeGlowMode == EdgeGlowMode.Off;
    public bool IsEdgeGlowSoftAmbient => EdgeGlowMode == EdgeGlowMode.SoftAmbient;
    public bool IsEdgeGlowBeatPulse => EdgeGlowMode == EdgeGlowMode.BeatPulse;
    public bool IsEdgeGlowNeonRim => EdgeGlowMode == EdgeGlowMode.NeonRim;

    public int EdgeGlowIntensityPercent
    {
        get => (int)Math.Round(_snapshot.Settings.EdgeGlowIntensity * 100f);
        set
        {
            var clamped = Math.Clamp(value, 0, 150) / 100f;
            if (Math.Abs(_snapshot.Settings.EdgeGlowIntensity - clamped) > 0.01f)
            {
                _ = SafeRunAsync(() => _runtime.SetEdgeGlowIntensityAsync(clamped));
            }
        }
    }

    public AudioVisualizerMode AudioVisualizerMode => _snapshot.Settings.AudioVisualizerMode;
    public bool IsVisNone => AudioVisualizerMode == AudioVisualizerMode.None;
    public bool IsVisSpectrumBars => AudioVisualizerMode == AudioVisualizerMode.SpectrumBars;
    public bool IsVisRadialHalo => AudioVisualizerMode == AudioVisualizerMode.RadialHalo;
    public bool IsVisFluidWave => AudioVisualizerMode == AudioVisualizerMode.FluidWave;
    public bool IsVisParticleField => AudioVisualizerMode == AudioVisualizerMode.ParticleField;
    public bool IsVisBassHorizon => AudioVisualizerMode == AudioVisualizerMode.BassHorizon;

    public int AudioVisualizerIntensityPercent
    {
        get => (int)Math.Round(_snapshot.Settings.AudioVisualizerIntensity * 100f);
        set
        {
            var clamped = Math.Clamp(value, 20, 150) / 100f;
            if (Math.Abs(_snapshot.Settings.AudioVisualizerIntensity - clamped) > 0.01f)
            {
                _ = SafeRunAsync(() => _runtime.SetAudioVisualizerIntensityAsync(clamped));
            }
        }
    }

    public bool IsLiveSystemAudioActive => _snapshot.EffectiveAudioMetrics.IsLiveAudioActive;
    public string AudioStatusLabel => IsLiveSystemAudioActive ? "System Audio Live" : "Beat Sync";
    public string DiagnosticAudioLine => IsLiveSystemAudioActive
        ? $"WASAPI Loopback FFT active (Level {(int)Math.Round(_snapshot.EffectiveAudioMetrics.OverallLevel * 100f)}%)"
        : "Using lyric-synced beat pulse (silent or audio fallback)";

    public Bitmap? AlbumArtBitmap => _albumArtBitmap;
    public bool HasAlbumArt => _albumArtBitmap is not null;

    public bool IsReducedMotionToggleEnabled => !_snapshot.IsOsReducedMotion;
    public IBrush MediaHealthDotBrush => MediaHealth switch
    {
        MediaHealthKind.PlayingSynced => new SolidColorBrush(Color.Parse("#22C55E")),
        MediaHealthKind.Warning => new SolidColorBrush(Color.Parse("#F59E0B")),
        MediaHealthKind.Error => new SolidColorBrush(Color.Parse("#DC2626")),
        MediaHealthKind.Loading => new SolidColorBrush(Color.Parse("#818CF8")),
        _ => new SolidColorBrush(Color.Parse("#8490A7"))
    };

    public bool IsDemoMode => _snapshot.IsDemoMode;
    public bool IsLoadingLyrics => !_snapshot.IsDemoMode && _snapshot.LyricsStatus == LyricsStatus.Loading;

    public string MediaTrackTitle
    {
        get
        {
            if (_snapshot.IsDemoMode)
            {
                return "Neon Horizon";
            }

            return _snapshot.Track is { IsValid: true } t ? t.RawTitle : "No music playing";
        }
    }

    public string MediaStatusLabel
    {
        get
        {
            if (_snapshot.IsDemoMode)
            {
                return "Demo Mode · LyricPulse Studio";
            }

            if (_snapshot.Track is not { IsValid: true } t)
            {
                return "Waiting for Spotify or Apple Music";
            }

            var player = _snapshot.PlayerKind switch
            {
                MediaPlayerKind.Spotify => "Spotify",
                MediaPlayerKind.AppleMusic => "Apple Music",
                MediaPlayerKind.DedicatedAudioPlayer => "Audio Player",
                MediaPlayerKind.BrowserOrGeneric => "Media Player",
                _ => "Windows Media"
            };

            var stateWord = _snapshot.PlaybackState == MediaPlaybackState.Paused ? "Paused" : "Playing";
            var artistPart = string.IsNullOrWhiteSpace(t.RawArtist) ? string.Empty : $" · {t.RawArtist}";

            return _snapshot.LyricsStatus switch
            {
                LyricsStatus.Loading => $"Fetching lyrics · {player}{artistPart}",
                LyricsStatus.Synced => $"{stateWord} · {player}{artistPart}",
                LyricsStatus.Instrumental => $"Instrumental · {player}{artistPart}",
                LyricsStatus.Error => $"Lyrics offline · {player}{artistPart}",
                _ => $"No synced lyrics · {player}{artistPart}"
            };
        }
    }

    public MediaHealthKind MediaHealth
    {
        get
        {
            if (_snapshot.IsDemoMode)
            {
                return MediaHealthKind.PlayingSynced;
            }

            if (_snapshot.Track is not { IsValid: true })
            {
                return MediaHealthKind.Idle;
            }

            return _snapshot.LyricsStatus switch
            {
                LyricsStatus.Loading => MediaHealthKind.Loading,
                LyricsStatus.Synced => _snapshot.PlaybackState == MediaPlaybackState.Paused ? MediaHealthKind.Paused : MediaHealthKind.PlayingSynced,
                LyricsStatus.Instrumental => MediaHealthKind.Idle,
                LyricsStatus.Error => MediaHealthKind.Error,
                _ => MediaHealthKind.Warning
            };
        }
    }

    public bool ShowTroubleshootingPill =>
        MediaHealth is MediaHealthKind.Idle or MediaHealthKind.Warning or MediaHealthKind.Error;

    public bool CanAdjustOffset
    {
        get
        {
            var hasSynced = _snapshot.Document is not null && _snapshot.LyricsStatus == LyricsStatus.Synced;
            return _snapshot.IsDemoMode || (_snapshot.Track is { IsValid: true } && hasSynced);
        }
    }

    public bool HasNonZeroOffset => Math.Abs(_snapshot.TimingOffset.Offset.TotalSeconds) >= 0.005;

    public string TimingOffsetDisplay
    {
        get
        {
            var sec = _snapshot.TimingOffset.Offset.TotalSeconds;
            if (Math.Abs(sec) < 0.005)
            {
                return "0.0s";
            }

            var abs = Math.Abs(sec).ToString("0.0", CultureInfo.InvariantCulture);
            return sec < 0 ? $"\u2212{abs}s" : $"+{abs}s";
        }
    }

    public string DiagnosticGsmtcLine => _snapshot.IsDemoMode
        ? "Demo Mode active (looping Neon Horizon)"
        : _snapshot.Track is { IsValid: true }
            ? $"Connected ({_snapshot.SourceAppUserModelId ?? _snapshot.PlayerKind.ToString()})"
            : "No active session detected";

    public string DiagnosticLyricsLine => _snapshot.IsDemoMode
        ? "Synced Enhanced LRC loaded (Built-in Demo)"
        : _snapshot.LyricsStatus switch
        {
            LyricsStatus.Synced => $"Synced lyrics loaded ({_snapshot.Document?.ProviderName ?? "Cached"})",
            LyricsStatus.Loading => "Fetching synced lyrics from LRCLIB / NetEase...",
            LyricsStatus.Instrumental => "Track marked instrumental",
            LyricsStatus.Error => "Network error contacting LRCLIB / NetEase",
            LyricsStatus.PlainOnly => "Only unsynced plain lyrics available",
            _ => "No synced lyrics found for track"
        };

    public string DiagnosticDisplaysLine =>
        !_snapshot.Settings.DesktopOverlayEnabled
            ? "Stage Window primary (Desktop Overlay off)"
            : AllDisplaysDisabled
                ? "Overlay enabled, but 0 monitors active"
                : $"{Displays.Count(d => d.IsEnabled)} of {Math.Max(1, Displays.Count)} displays active";

    public bool AllDisplaysDisabled => Displays.Count > 0 && Displays.All(d => !d.IsEnabled);

    public void ApplySnapshot(OverlayPlaybackSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var prevSettings = _snapshot.Settings;
        _snapshot = snapshot;

        var settings = snapshot.Settings;
        foreach (var card in StyleCards)
        {
            card.IsActive = card.StyleId == settings.ActiveStyle;
            card.IsPinned = !settings.PinnedStyles.IsDefault && settings.PinnedStyles.Contains(card.StyleId);
        }

        if (StyleFilter == "Pinned" && !prevSettings.PinnedStyles.SequenceEqual(settings.PinnedStyles))
        {
            RebuildFilteredStyleCards();
        }
        else if (FilteredStyleCards.Count == 0 && StyleFilter == "All")
        {
            RebuildFilteredStyleCards();
        }

        foreach (var row in HotkeyRows)
        {
            if (settings.GlobalHotkeys.TryGetValue(row.Action, out var gesture))
            {
                row.GestureText = gesture;
            }
        }

        OnPropertyChanged(nameof(SelectedStyleInfo));
        OnPropertyChanged(nameof(ActiveStyle));
        OnPropertyChanged(nameof(ActiveStyleIndex));
        OnPropertyChanged(nameof(StyleCounterText));
        OnPropertyChanged(nameof(IsActiveStylePinned));
        OnPropertyChanged(nameof(ActivePinTooltip));
        OnPropertyChanged(nameof(PinnedCount));
        OnPropertyChanged(nameof(GalleryCountCaption));
        OnPropertyChanged(nameof(FontFamily));
        OnPropertyChanged(nameof(IsJostSelected));
        OnPropertyChanged(nameof(IsBodoniSelected));
        OnPropertyChanged(nameof(IsMulishSelected));
        OnPropertyChanged(nameof(IsNunitoSelected));
        OnPropertyChanged(nameof(FontScalePercent));
        OnPropertyChanged(nameof(FullscreenStageScalePercent));
        OnPropertyChanged(nameof(PartyEnergyPercent));
        OnPropertyChanged(nameof(OverlayOpacityPercent));
        OnPropertyChanged(nameof(SafeMarginPx));
        OnPropertyChanged(nameof(AutoSyncColors));
        OnPropertyChanged(nameof(ManualColor1));
        OnPropertyChanged(nameof(ManualColor2));
        OnPropertyChanged(nameof(ManualColor3));
        OnPropertyChanged(nameof(ManualHex1));
        OnPropertyChanged(nameof(ManualHex2));
        OnPropertyChanged(nameof(ManualHex3));
        OnPropertyChanged(nameof(ActivePaletteColor1));
        OnPropertyChanged(nameof(ActivePaletteColor2));
        OnPropertyChanged(nameof(ActivePaletteColor3));
        OnPropertyChanged(nameof(IsAmbientPartyMesh));
        OnPropertyChanged(nameof(IsPureBlackBackdrop));
        OnPropertyChanged(nameof(ShowFollowingDot));
        OnPropertyChanged(nameof(ShowColorSweepLayer));
        OnPropertyChanged(nameof(DesktopOverlayEnabled));
        OnPropertyChanged(nameof(LyricsVisible));
        OnPropertyChanged(nameof(ReducedMotion));
        OnPropertyChanged(nameof(IsOsReducedMotion));
        OnPropertyChanged(nameof(EffectivePreviewsPaused));
        OnPropertyChanged(nameof(IsPreviewsToggleEnabled));
        OnPropertyChanged(nameof(PausePreviewsLabel));
        OnPropertyChanged(nameof(HighContrastScrim));
        OnPropertyChanged(nameof(UniversalPlayerMode));
        OnPropertyChanged(nameof(HideDuringFullscreen));
        OnPropertyChanged(nameof(AnchorZone));
        OnPropertyChanged(nameof(IsAnchorTopCenter));
        OnPropertyChanged(nameof(IsAnchorMiddleCenter));
        OnPropertyChanged(nameof(IsAnchorBottomThird));
        OnPropertyChanged(nameof(IsAnchorBottomLeft));
        OnPropertyChanged(nameof(IsAnchorBottomRight));
        OnPropertyChanged(nameof(ScrimIntensity));
        OnPropertyChanged(nameof(IsScrimOff));
        OnPropertyChanged(nameof(IsScrimSubtle));
        OnPropertyChanged(nameof(IsScrimBalanced));
        OnPropertyChanged(nameof(IsScrimStrong));
        OnPropertyChanged(nameof(ThemeVariantMode));
        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(SecondaryLineMode));
        OnPropertyChanged(nameof(IsSecondaryOff));
        OnPropertyChanged(nameof(IsSecondaryTranslation));
        OnPropertyChanged(nameof(IsSecondaryRomanization));
        OnPropertyChanged(nameof(EdgeGlowMode));
        OnPropertyChanged(nameof(IsEdgeGlowOff));
        OnPropertyChanged(nameof(IsEdgeGlowSoftAmbient));
        OnPropertyChanged(nameof(IsEdgeGlowBeatPulse));
        OnPropertyChanged(nameof(IsEdgeGlowNeonRim));
        OnPropertyChanged(nameof(EdgeGlowIntensityPercent));
        OnPropertyChanged(nameof(AudioVisualizerMode));
        OnPropertyChanged(nameof(IsVisNone));
        OnPropertyChanged(nameof(IsVisSpectrumBars));
        OnPropertyChanged(nameof(IsVisRadialHalo));
        OnPropertyChanged(nameof(IsVisFluidWave));
        OnPropertyChanged(nameof(IsVisParticleField));
        OnPropertyChanged(nameof(IsVisBassHorizon));
        OnPropertyChanged(nameof(AudioVisualizerIntensityPercent));
        OnPropertyChanged(nameof(IsLiveSystemAudioActive));
        OnPropertyChanged(nameof(AudioStatusLabel));
        OnPropertyChanged(nameof(DiagnosticAudioLine));
        OnPropertyChanged(nameof(IsReducedMotionToggleEnabled));
        OnPropertyChanged(nameof(IsDemoMode));
        OnPropertyChanged(nameof(IsLoadingLyrics));
        OnPropertyChanged(nameof(MediaTrackTitle));
        OnPropertyChanged(nameof(MediaStatusLabel));
        OnPropertyChanged(nameof(MediaHealth));
        OnPropertyChanged(nameof(MediaHealthDotBrush));
        OnPropertyChanged(nameof(ShowTroubleshootingPill));
        OnPropertyChanged(nameof(CanAdjustOffset));
        OnPropertyChanged(nameof(HasNonZeroOffset));
        OnPropertyChanged(nameof(TimingOffsetDisplay));
        OnPropertyChanged(nameof(DiagnosticGsmtcLine));
        OnPropertyChanged(nameof(DiagnosticLyricsLine));
        OnPropertyChanged(nameof(DiagnosticDisplaysLine));

        UpdateAlbumArtBitmap(snapshot.AlbumArt);

        NudgeOffsetCommand.RaiseCanExecuteChanged();
        ResetOffsetCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var prev = _albumArtBitmap;
        _albumArtBitmap = null;
        _albumArtHash = null;
        prev?.Dispose();
    }

    private void UpdateAlbumArtBitmap(AlbumArtPayload? art)
    {
        var incomingHash = art is { ImageBytes.IsEmpty: false } ? art.Sha256Hash : null;
        if (string.Equals(_albumArtHash, incomingHash, StringComparison.Ordinal))
        {
            return;
        }

        Bitmap? nextBitmap = null;
        if (art is { ImageBytes.IsEmpty: false } validArt)
        {
            try
            {
                using var ms = new MemoryStream(validArt.ImageBytes.ToArray(), writable: false);
                nextBitmap = Bitmap.DecodeToWidth(ms, 64);
            }
            catch (Exception ex)
            {
                LogAlbumArtDecodeError(_logger, ex);
                nextBitmap = null;
            }
        }

        var oldBitmap = _albumArtBitmap;
        _albumArtBitmap = nextBitmap;
        _albumArtHash = incomingHash;
        OnPropertyChanged(nameof(AlbumArtBitmap));
        OnPropertyChanged(nameof(HasAlbumArt));
        oldBitmap?.Dispose();
    }

    public void UpdateDisplays(IReadOnlyList<DisplayMonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        Displays.Clear();
        var disabled = _snapshot.Settings.DisabledDisplays;
        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            var enabled = disabled.IsDefault || !disabled.Contains(m.DisplayKey, StringComparer.OrdinalIgnoreCase);
            Displays.Add(new DisplayOptionViewModel(
                i + 1,
                m.DisplayKey,
                m.FriendlyName,
                m.WorkingWidthPx,
                m.WorkingHeightPx,
                m.EffectiveDpiScale,
                m.IsPrimary,
                enabled,
                OnDisplayToggled));
        }

        OnPropertyChanged(nameof(AllDisplaysDisabled));
        OnPropertyChanged(nameof(DiagnosticDisplaysLine));
    }

    public void SetHotkeyConflicts(IReadOnlyDictionary<HotkeyAction, string> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        foreach (var row in HotkeyRows)
        {
            row.ErrorMessage = conflicts.TryGetValue(row.Action, out var err) ? err : null;
        }
    }

    public bool IsRecordingHotkey => _recordingHotkey.HasValue;

    public void BeginHotkeyCapture(HotkeyAction action)
    {
        _recordingHotkey = action;
        foreach (var row in HotkeyRows)
        {
            row.IsRecording = row.Action == action;
            if (row.Action == action)
            {
                row.ErrorMessage = null;
            }
        }
    }

    public void CancelHotkeyCapture()
    {
        _recordingHotkey = null;
        foreach (var row in HotkeyRows)
        {
            row.IsRecording = false;
        }
    }

    public bool ClearCurrentRecordingHotkey()
    {
        if (_recordingHotkey is not { } action)
        {
            return false;
        }

        _recordingHotkey = null;
        foreach (var row in HotkeyRows)
        {
            row.IsRecording = false;
            if (row.Action == action)
            {
                row.ErrorMessage = null;
            }
        }

        ClearHotkeyCommand.Execute(action);
        return true;
    }

    public bool CommitCapturedHotkey(string gesture, bool hasModifier)
    {
        if (_recordingHotkey is not { } action)
        {
            return false;
        }

        var row = HotkeyRows.FirstOrDefault(r => r.Action == action);
        if (!hasModifier || string.IsNullOrWhiteSpace(gesture))
        {
            if (row is not null)
            {
                row.ErrorMessage = "Include Ctrl, Alt, or Shift+Ctrl/Alt.";
                row.IsRecording = false;
            }

            _recordingHotkey = null;
            return false;
        }

        // Check collision with another LyricPulse action
        var duplicate = HotkeyRows.FirstOrDefault(r =>
            r.Action != action &&
            string.Equals(r.GestureText, gesture, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            if (row is not null)
            {
                row.ErrorMessage = $"Already assigned to '{duplicate.Label}'.";
                row.IsRecording = false;
            }

            _recordingHotkey = null;
            return false;
        }

        if (_hotkeyValidator is not null)
        {
            var (ok, err) = _hotkeyValidator(action, gesture);
            if (!ok)
            {
                if (row is not null)
                {
                    row.ErrorMessage = err ?? $"{gesture} is already in use by another app.";
                    row.IsRecording = false;
                }

                _recordingHotkey = null;
                return false;
            }
        }

        if (row is not null)
        {
            row.ErrorMessage = null;
            row.IsRecording = false;
        }

        _recordingHotkey = null;
        _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s => s with
        {
            GlobalHotkeys = s.GlobalHotkeys.SetItem(action, gesture)
        }));
        return true;
    }

    private void OnDisplayToggled(string displayKey, bool enabled)
    {
        _ = SafeRunAsync(() => _runtime.UpdateSettingsAsync(s =>
        {
            var current = s.DisabledDisplays.IsDefault ? ImmutableArray<string>.Empty : s.DisabledDisplays;
            var updated = enabled
                ? current.Where(k => !k.Equals(displayKey, StringComparison.OrdinalIgnoreCase)).ToImmutableArray()
                : current.Contains(displayKey, StringComparer.OrdinalIgnoreCase)
                    ? current
                    : current.Add(displayKey);
            return s with { DisabledDisplays = updated };
        }));
        OnPropertyChanged(nameof(AllDisplaysDisabled));
        OnPropertyChanged(nameof(DiagnosticDisplaysLine));
    }

    private void CycleActiveStyle(int step)
    {
        var all = StyleCatalog.All;
        var nextIdx = (ActiveStyleIndex + step + all.Length) % all.Length;
        _ = SafeRunAsync(() => _runtime.SetActiveStyleAsync(all[nextIdx].StyleId));
    }

    private Task<AppSettings> TogglePinAsync(OverlayStyleId styleId) =>
        _runtime.UpdateSettingsAsync(s =>
        {
            var current = s.PinnedStyles.IsDefault ? ImmutableArray<OverlayStyleId>.Empty : s.PinnedStyles;
            var updated = current.Contains(styleId)
                ? current.Remove(styleId)
                : current.Add(styleId);
            return s with { PinnedStyles = updated };
        });

    private void RebuildFilteredStyleCards()
    {
        var desired = new List<StyleCardViewModel>(StyleCards.Count);
        foreach (var card in StyleCards)
        {
            var matches = StyleFilter switch
            {
                "Pinned" => card.IsPinned,
                "With Visuals" => card.HasVisualIcons,
                "Motion" => !card.HasVisualIcons,
                _ => true
            };

            if (matches)
            {
                desired.Add(card);
            }
        }

        if (FilteredStyleCards.SequenceEqual(desired))
        {
            return;
        }

        FilteredStyleCards.Clear();
        foreach (var card in desired)
        {
            FilteredStyleCards.Add(card);
        }
    }

    private void InitHotkeyRows()
    {
        var labels = new (HotkeyAction Action, string Label)[]
        {
            (HotkeyAction.ToggleLyrics, "Show / Hide Lyrics"),
            (HotkeyAction.CyclePinnedStyles, "Cycle Pinned Styles"),
            (HotkeyAction.OffsetEarlier, "Timing Earlier (\u22120.5s)"),
            (HotkeyAction.OffsetLater, "Timing Later (+0.5s)"),
            (HotkeyAction.OpenGallery, "Open LyricPulse Stage")
        };

        var defaults = _snapshot.Settings.GlobalHotkeys;
        foreach (var (act, lbl) in labels)
        {
            var gesture = defaults.TryGetValue(act, out var g) ? g : string.Empty;
            HotkeyRows.Add(new HotkeyBindingViewModel(act, lbl, gesture, BeginRecordHotkeyCommand, ClearHotkeyCommand));
        }
    }

    private static readonly Action<ILogger, Exception?> LogStageOperationError =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(SafeRunAsync)),
            "Unhandled async error in StageViewModel operation.");

    private static readonly Action<ILogger, Exception?> LogAlbumArtDecodeError =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(2, nameof(UpdateAlbumArtBitmap)),
            "Unable to decode album art thumbnail bitmap.");

    private static Color ToAvaloniaColor(RgbColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);
    private static RgbColor FromAvaloniaColor(Color c) => new(c.R, c.G, c.B, c.A);

    private async Task SafeRunAsync(Func<Task> op)
    {
        try
        {
            await op().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal during shutdown.
        }
        catch (ObjectDisposedException)
        {
            // Normal during shutdown.
        }
        catch (Exception ex)
        {
            LogStageOperationError(_logger, ex);
        }
    }
}
