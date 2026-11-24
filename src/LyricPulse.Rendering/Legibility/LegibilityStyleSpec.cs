using LyricPulse.Core.Color;
using LyricPulse.Core.Models;
using LyricPulse.Rendering.Fonts;
using SkiaSharp;

namespace LyricPulse.Rendering.Legibility;

/// <summary>
/// Pure, deterministic parameter calculations for the 4-layer desktop overlay legibility pipeline
/// (MOTION_AND_STYLES.md section 1).
/// </summary>
public static class LegibilityStyleSpec
{
    public const double DarkFillInversionLightnessThreshold = 0.38;

    public const float MinHaloStrokeWidthDip = 1.8f;
    public const float MaxHaloStrokeWidthDip = 5.0f;

    public const float ContactShadowDyDip = 1.5f;
    public const float ContactShadowSigmaDip = 2.5f;
    public const float AmbientShadowDyDip = 4.0f;
    public const float AmbientShadowSigmaDip = 14.0f;

    public static readonly SKColor DarkHaloColor = new(0x07, 0x07, 0x12, 0xC2);  // #070712 @ 76%
    public static readonly SKColor LightHaloColor = new(0xF8, 0xFA, 0xFC, 0xAD); // #F8FAFC @ 68%
    public static readonly SKColor HighContrastScrimColor = new(0x08, 0x09, 0x0D, 0xCC); // #CC08090D
    public static readonly SKColor ContactShadowBaseColor = new(0x05, 0x05, 0x10, 0xB8); // #050510 @ 72%
    public static readonly SKColor AmbientShadowBaseColor = new(0x05, 0x05, 0x10, 0x7A); // #050510 @ 48%

    public static float GetScrimAlpha(ScrimIntensity intensity, bool highContrastScrim)
    {
        if (highContrastScrim)
        {
            return HighContrastScrimColor.Alpha / 255f;
        }

        return intensity switch
        {
            ScrimIntensity.Off => 0.00f,
            ScrimIntensity.Subtle => 0.24f,
            ScrimIntensity.Balanced => 0.42f,
            ScrimIntensity.Strong => 0.62f,
            _ => 0.42f
        };
    }

    public static SKColor ResolveScrimBaseColor(RgbColor albumScrimTint, ScrimIntensity intensity, bool highContrastScrim)
    {
        if (highContrastScrim)
        {
            return HighContrastScrimColor;
        }

        var alpha = GetScrimAlpha(intensity, highContrastScrim: false);
        var alphaByte = (byte)Math.Clamp(MathF.Round(alpha * 255f), 0f, 255f);
        return new SKColor(albumScrimTint.R, albumScrimTint.G, albumScrimTint.B, alphaByte);
    }

    public static (float RadiusXDip, float RadiusYDip) ComputeScrimRadii(SKRect clusterBounds, float fontSizeDip)
    {
        var safeFont = Math.Max(float.IsFinite(fontSizeDip) ? fontSizeDip : 48f, 12f);
        var width = Math.Max(clusterBounds.Width, safeFont);
        var height = Math.Max(clusterBounds.Height, safeFont);

        var rx = (width * 0.62f) + (1.4f * safeFont);
        var ry = (height * 0.68f) + (1.1f * safeFont);
        return (rx, ry);
    }

    public static float ComputeOpticalStrokeWidth(float fontSizeDip, LyricFontId fontId)
    {
        var metrics = FontAssetRegistry.GetOpticalMetrics(fontId);
        if (metrics.StrokeHaloRatio <= 0f)
        {
            return 0f;
        }

        var safeFont = float.IsFinite(fontSizeDip) && fontSizeDip > 0f ? fontSizeDip : 48f;
        return Math.Clamp(safeFont * metrics.StrokeHaloRatio, MinHaloStrokeWidthDip, MaxHaloStrokeWidthDip);
    }

    public static bool ShouldInvertHaloForDarkFill(RgbColor fillColor) =>
        OklabColorMath.SrgbToOklab(fillColor).L < DarkFillInversionLightnessThreshold;

    public static SKColor ResolveOpticalHaloColor(RgbColor fillColor, float opacity = 1f)
    {
        var baseColor = ShouldInvertHaloForDarkFill(fillColor) ? LightHaloColor : DarkHaloColor;
        return ScaleAlpha(baseColor, opacity);
    }

    public static SKColor ScaleAlpha(SKColor color, float factor)
    {
        var clamped = Math.Clamp(float.IsFinite(factor) ? factor : 1f, 0f, 1f);
        var alpha = (byte)Math.Clamp(MathF.Round(color.Alpha * clamped), 0f, 255f);
        return color.WithAlpha(alpha);
    }
}
