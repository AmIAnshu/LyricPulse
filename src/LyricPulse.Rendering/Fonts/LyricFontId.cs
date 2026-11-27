namespace LyricPulse.Rendering.Fonts;

/// <summary>
/// Canonical font identifiers for bundled SIL OFL typefaces, MIT Fluent Icons, and OS system fonts.
/// </summary>
public enum LyricFontId
{
    BodoniModa = 0,
    Jost = 1,
    Mulish = 2,
    Nunito = 3,
    FluentIcons = 4,
    SystemMono = 5,
    SystemUi = 6,
    Emoji = 7,
    Bahnschrift = 8,
    Impact = 9,
    Georgia = 10,
    SitkaDisplay = 11,
    InkFree = 12
}

/// <summary>
/// Optical calibration metrics per typeface from MOTION_AND_STYLES.md section 3.1.
/// </summary>
public sealed record FontOpticalMetrics(
    LyricFontId FontId,
    string DisplayName,
    float TrackingEm,
    float LineHeightMultiplier,
    float StrokeHaloRatio,
    float MinOverlaySizeDip,
    string? BundledAssetUri);
