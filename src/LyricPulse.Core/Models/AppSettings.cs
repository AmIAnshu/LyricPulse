using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace LyricPulse.Core.Models;

public enum AnchorZone
{
    TopCenter = 0,
    MiddleCenter = 1,
    BottomThird = 2,
    BottomLeft = 3,
    BottomRight = 4
}

public enum ScrimIntensity
{
    Off = 0,
    Subtle = 1,
    Balanced = 2,
    Strong = 3
}

public enum ThemeVariantMode
{
    System = 0,
    Dark = 1,
    Light = 2
}

public enum SecondaryLineMode
{
    Off = 0,
    Translation = 1,
    Romanization = 2
}

public enum HotkeyAction
{
    ToggleLyrics = 0,
    CyclePinnedStyles = 1,
    OffsetEarlier = 2,
    OffsetLater = 3,
    OpenGallery = 4
}

public sealed record OverlayBoundsConfig(
    AnchorZone AnchorZone,
    int SafeMarginPx,
    double AnchorX,
    double AnchorY,
    double WidthRatio,
    double HeightRatio)
{
    public static OverlayBoundsConfig FromAnchorZone(AnchorZone zone, int safeMarginPx)
    {
        var clampedMargin = Math.Clamp(safeMarginPx, AppSettings.MinSafeMarginPx, AppSettings.MaxSafeMarginPx);
        return zone switch
        {
            AnchorZone.TopCenter => new OverlayBoundsConfig(zone, clampedMargin, 0.50, 0.18, 0.80, 0.30),
            AnchorZone.MiddleCenter => new OverlayBoundsConfig(zone, clampedMargin, 0.50, 0.50, 0.80, 0.45),
            AnchorZone.BottomLeft => new OverlayBoundsConfig(zone, clampedMargin, 0.22, 0.80, 0.42, 0.32),
            AnchorZone.BottomRight => new OverlayBoundsConfig(zone, clampedMargin, 0.78, 0.80, 0.42, 0.32),
            _ => new OverlayBoundsConfig(AnchorZone.BottomThird, clampedMargin, 0.50, 0.76, 0.80, 0.35)
        };
    }
}

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;
    public const double MinFontScale = 0.60;
    public const double MaxFontScale = 1.60;
    public const double BaseHeroFontDip = 48.0;
    public const double BodoniModaMinDip = 36.0;
    public const double MinOverlayOpacity = 0.25;
    public const double MaxOverlayOpacity = 1.00;
    public const int MinSafeMarginPx = 16;
    public const int MaxSafeMarginPx = 120;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public OverlayStyleId ActiveStyle { get; init; } = OverlayStyleId.Fisheye;

    /// <summary>
    /// Milestone 1A defaults to pinning the 3 MVP styles; expanded to 4 pins in Phase 4-6.
    /// </summary>
    public ImmutableArray<OverlayStyleId> PinnedStyles { get; init; } =
        [OverlayStyleId.Fisheye, OverlayStyleId.Codestyle, OverlayStyleId.VisualStack];

    public string FontFamily { get; init; } = "Jost";
    public double FontScale { get; init; } = 1.00;
    public bool AutoSyncColors { get; init; } = true;
    public RgbColor ManualAccent1 { get; init; } = AlbumAccentPalette.Default.AccentColor1;
    public RgbColor ManualAccent2 { get; init; } = AlbumAccentPalette.Default.AccentColor2;
    public RgbColor ManualAccent3 { get; init; } = AlbumAccentPalette.Default.AccentColor3;
    public double OverlayOpacity { get; init; } = 0.95;
    public AnchorZone AnchorZone { get; init; } = AnchorZone.BottomThird;
    public int SafeMarginPx { get; init; } = 48;

    public ImmutableDictionary<string, OverlayBoundsConfig> PerDisplayBounds { get; init; } =
        ImmutableDictionary<string, OverlayBoundsConfig>.Empty;

    public ImmutableArray<string> DisabledDisplays { get; init; } = ImmutableArray<string>.Empty;
    public bool UniversalPlayerMode { get; init; }
    public bool IgnoreBrowserVideoSessions { get; init; } = true;

    public ImmutableArray<string> BlockedAppIds { get; init; } =
        ["Discord.exe", "Teams.exe", "Zoom.exe"];

    public bool EnablePerTrackSavedOffsets { get; init; } = true;

    public ImmutableDictionary<string, int> PerTrackOffsetsMs { get; init; } =
        ImmutableDictionary<string, int>.Empty;

    public bool ReducedMotion { get; init; }
    public bool HighContrastScrim { get; init; }
    public ScrimIntensity ScrimIntensity { get; init; } = ScrimIntensity.Balanced;
    public ThemeVariantMode ThemeVariantMode { get; init; } = ThemeVariantMode.System;

    public ImmutableDictionary<HotkeyAction, string> GlobalHotkeys { get; init; } = CreateDefaultHotkeys();

    public bool HideDuringFullscreen { get; init; } = true;
    public bool HideDuringInstrumentals { get; init; }
    public SecondaryLineMode SecondaryLineMode { get; init; } = SecondaryLineMode.Off;
    public bool LyricsVisible { get; init; } = true;
    public VisualPreference VisualPreference { get; init; } = VisualPreference.Balanced;

    [JsonIgnore]
    public MediaSourcePolicy SourcePolicy =>
        UniversalPlayerMode ? MediaSourcePolicy.UniversalFallback : MediaSourcePolicy.SupportedPlayersOnly;

    public static AppSettings Default { get; } = new();

    public static ImmutableDictionary<HotkeyAction, string> CreateDefaultHotkeys() =>
        ImmutableDictionary<HotkeyAction, string>.Empty
            .Add(HotkeyAction.ToggleLyrics, "Ctrl+Shift+L")
            .Add(HotkeyAction.CyclePinnedStyles, "Ctrl+Shift+S")
            .Add(HotkeyAction.OffsetEarlier, "Ctrl+Shift+[")
            .Add(HotkeyAction.OffsetLater, "Ctrl+Shift+]")
            .Add(HotkeyAction.OpenGallery, "Ctrl+Shift+G");

    public double ComputeEffectiveFontSizeDip()
    {
        var scaled = BaseHeroFontDip * Math.Clamp(FontScale, MinFontScale, MaxFontScale);
        var isBodoni = FontFamily.Contains("Bodoni", StringComparison.OrdinalIgnoreCase);
        return isBodoni ? Math.Max(scaled, BodoniModaMinDip) : scaled;
    }

    public AppSettings ValidateAndNormalize()
    {
        var normalizedScale = double.IsFinite(FontScale)
            ? Math.Clamp(FontScale, MinFontScale, MaxFontScale)
            : 1.00;

        var normalizedOpacity = double.IsFinite(OverlayOpacity)
            ? Math.Clamp(OverlayOpacity, MinOverlayOpacity, MaxOverlayOpacity)
            : 0.95;

        var normalizedMargin = Math.Clamp(SafeMarginPx, MinSafeMarginPx, MaxSafeMarginPx);
        var normalizedFont = string.IsNullOrWhiteSpace(FontFamily) ? "Jost" : FontFamily.Trim();
        var pinned = PinnedStyles.IsDefaultOrEmpty
            ? Default.PinnedStyles
            : PinnedStyles.Distinct().ToImmutableArray();

        var disabled = DisabledDisplays.IsDefault ? ImmutableArray<string>.Empty : DisabledDisplays;
        var blocked = BlockedAppIds.IsDefault ? Default.BlockedAppIds : BlockedAppIds;
        var bounds = PerDisplayBounds ?? ImmutableDictionary<string, OverlayBoundsConfig>.Empty;
        var offsets = PerTrackOffsetsMs ?? ImmutableDictionary<string, int>.Empty;
        var hotkeys = GlobalHotkeys is null || GlobalHotkeys.IsEmpty
            ? CreateDefaultHotkeys()
            : GlobalHotkeys;

        return this with
        {
            SchemaVersion = SchemaVersion <= 0 ? CurrentSchemaVersion : SchemaVersion,
            PinnedStyles = pinned,
            FontFamily = normalizedFont,
            FontScale = normalizedScale,
            OverlayOpacity = normalizedOpacity,
            SafeMarginPx = normalizedMargin,
            PerDisplayBounds = bounds,
            DisabledDisplays = disabled,
            BlockedAppIds = blocked,
            PerTrackOffsetsMs = offsets,
            GlobalHotkeys = hotkeys
        };
    }
}
