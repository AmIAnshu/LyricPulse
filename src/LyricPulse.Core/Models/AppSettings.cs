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

public enum StageBackdropMode
{
    AmbientPartyMesh = 0,
    PureBlack = 1,
    TransparentOverlay = 2
}

public enum EdgeGlowMode
{
    Off = 0,
    SoftAmbient = 1,
    BeatPulse = 2,
    NeonRim = 3,
    NeonRush = 4
}

public enum EdgeGlowAnimationMode
{
    MusicSync = 0,
    AmbientDrift = 1,
    Still = 2
}

public enum AudioVisualizerMode
{
    None = 0,
    SpectrumBars = 1,
    RadialHalo = 2,
    FluidWave = 3,
    ParticleField = 4,
    BassHorizon = 5
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
    public const float MinPartyEnergyLevel = 0.5f;
    public const float MaxPartyEnergyLevel = 1.5f;
    public const float DefaultPartyEnergyLevel = 1.0f;
    public const float MinFullscreenStageScale = 1.2f;
    public const float MaxFullscreenStageScale = 2.6f;
    public const float DefaultFullscreenStageScale = 1.85f;
    public const float MinGlowIntensity = 0.2f;
    public const float MaxGlowIntensity = 1.5f;
    public const float DefaultEdgeGlowIntensity = 0.90f;
    public const float MinEdgeGlowSpeed = 0.20f;
    public const float MaxEdgeGlowSpeed = 2.50f;
    public const float DefaultEdgeGlowSpeed = 0.70f;
    public const float MinEdgeGlowThickness = 0.40f;
    public const float MaxEdgeGlowThickness = 2.50f;
    public const float DefaultEdgeGlowThickness = 1.35f;
    public const float MinEdgeGlowSpill = 0.25f;
    public const float MaxEdgeGlowSpill = 2.00f;
    public const float DefaultEdgeGlowSpill = 0.85f;
    public const float MinVisualizerIntensity = 0.2f;
    public const float MaxVisualizerIntensity = 1.5f;
    public const float DefaultVisualizerIntensity = 0.75f;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public OverlayStyleId ActiveStyle { get; init; } = OverlayStyleId.Fisheye;

    /// <summary>
    /// Canonical v1.0 4-style pinned set per ROADMAP.md; user-saved pins are preserved cleanly on upgrade.
    /// </summary>
    public ImmutableArray<OverlayStyleId> PinnedStyles { get; init; } =
        [OverlayStyleId.FisheyeVisual, OverlayStyleId.Codestyle, OverlayStyleId.Constellation, OverlayStyleId.KineticWall];

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
    public bool UniversalPlayerMode { get; init; } = true;
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
    public StageBackdropMode StageBackdropMode { get; init; } = StageBackdropMode.AmbientPartyMesh;
    public bool ShowFollowingDot { get; init; } = true;
    public bool ShowColorSweepLayer { get; init; } = true;
    public float PartyEnergyLevel { get; init; } = DefaultPartyEnergyLevel;
    public float FullscreenStageScale { get; init; } = DefaultFullscreenStageScale;
    public bool DesktopOverlayEnabled { get; init; }
    public EdgeGlowMode EdgeGlowMode { get; init; } = EdgeGlowMode.NeonRush;
    public float EdgeGlowIntensity { get; init; } = DefaultEdgeGlowIntensity;
    public float EdgeGlowSpeed { get; init; } = DefaultEdgeGlowSpeed;
    public float EdgeGlowThickness { get; init; } = DefaultEdgeGlowThickness;
    public bool EdgeGlowDesktopEnabled { get; init; } = true;
    public EdgeGlowAnimationMode EdgeGlowAnimation { get; init; } = EdgeGlowAnimationMode.MusicSync;
    public bool EdgeGlowTwoColorGradient { get; init; } = true;
    public float EdgeGlowSpill { get; init; } = DefaultEdgeGlowSpill;
    public bool EdgeGlowOverrideAlbumColors { get; init; }
    public RgbColor EdgeGlowPrimaryColor { get; init; } = new(0x38, 0xBD, 0xF8);
    public RgbColor EdgeGlowSecondaryColor { get; init; } = new(0xEC, 0x48, 0x99);
    public AudioVisualizerMode AudioVisualizerMode { get; init; } = AudioVisualizerMode.RadialHalo;
    public float AudioVisualizerIntensity { get; init; } = DefaultVisualizerIntensity;

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
        var normalizedEnergy = float.IsFinite(PartyEnergyLevel)
            ? Math.Clamp(PartyEnergyLevel, MinPartyEnergyLevel, MaxPartyEnergyLevel)
            : DefaultPartyEnergyLevel;
        var normalizedStageScale = float.IsFinite(FullscreenStageScale)
            ? Math.Clamp(FullscreenStageScale, MinFullscreenStageScale, MaxFullscreenStageScale)
            : DefaultFullscreenStageScale;
        var normalizedBackdrop = Enum.IsDefined(StageBackdropMode)
            ? StageBackdropMode
            : StageBackdropMode.AmbientPartyMesh;
        var normalizedEdgeGlowMode = Enum.IsDefined(EdgeGlowMode)
            ? EdgeGlowMode
            : EdgeGlowMode.NeonRush;
        var normalizedEdgeGlowIntensity = float.IsFinite(EdgeGlowIntensity)
            ? Math.Clamp(EdgeGlowIntensity, MinGlowIntensity, MaxGlowIntensity)
            : DefaultEdgeGlowIntensity;
        var normalizedEdgeGlowSpeed = !float.IsFinite(EdgeGlowSpeed) || EdgeGlowSpeed <= 0f
            ? DefaultEdgeGlowSpeed
            : Math.Clamp(EdgeGlowSpeed, MinEdgeGlowSpeed, MaxEdgeGlowSpeed);
        var normalizedEdgeGlowThickness = !float.IsFinite(EdgeGlowThickness) || EdgeGlowThickness <= 0f
            ? DefaultEdgeGlowThickness
            : Math.Clamp(EdgeGlowThickness, MinEdgeGlowThickness, MaxEdgeGlowThickness);
        var normalizedEdgeGlowAnimation = Enum.IsDefined(EdgeGlowAnimation)
            ? EdgeGlowAnimation
            : EdgeGlowAnimationMode.MusicSync;
        var normalizedEdgeGlowSpill = !float.IsFinite(EdgeGlowSpill) || EdgeGlowSpill <= 0f
            ? DefaultEdgeGlowSpill
            : Math.Clamp(EdgeGlowSpill, MinEdgeGlowSpill, MaxEdgeGlowSpill);
        var normalizedVisualizerMode = Enum.IsDefined(AudioVisualizerMode)
            ? AudioVisualizerMode
            : AudioVisualizerMode.RadialHalo;
        var normalizedVisualizerIntensity = float.IsFinite(AudioVisualizerIntensity)
            ? Math.Clamp(AudioVisualizerIntensity, MinVisualizerIntensity, MaxVisualizerIntensity)
            : DefaultVisualizerIntensity;
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
            StageBackdropMode = normalizedBackdrop,
            PartyEnergyLevel = normalizedEnergy,
            FullscreenStageScale = normalizedStageScale,
            EdgeGlowMode = normalizedEdgeGlowMode,
            EdgeGlowIntensity = normalizedEdgeGlowIntensity,
            EdgeGlowSpeed = normalizedEdgeGlowSpeed,
            EdgeGlowThickness = normalizedEdgeGlowThickness,
            EdgeGlowAnimation = normalizedEdgeGlowAnimation,
            EdgeGlowSpill = normalizedEdgeGlowSpill,
            AudioVisualizerMode = normalizedVisualizerMode,
            AudioVisualizerIntensity = normalizedVisualizerIntensity,
            PerDisplayBounds = bounds,
            DisabledDisplays = disabled,
            BlockedAppIds = blocked,
            PerTrackOffsetsMs = offsets,
            GlobalHotkeys = hotkeys
        };
    }
}
