using LyricPulse.Core.Models;
using SkiaSharp;

namespace LyricPulse.Rendering.Color;

/// <summary>
/// Zero-allocation conversions between Core RgbColor and SkiaSharp SKColor.
/// </summary>
public static class SkColorExtensions
{
    public static SKColor ToSkColor(this RgbColor color) =>
        new(color.R, color.G, color.B, color.A);

    public static SKColor ToSkColorWithAlpha(this RgbColor color, float alphaFraction)
    {
        var clamped = Math.Clamp(float.IsFinite(alphaFraction) ? alphaFraction : 1f, 0f, 1f);
        var a = (byte)Math.Clamp(MathF.Round(color.A * clamped), 0f, 255f);
        return new SKColor(color.R, color.G, color.B, a);
    }

    public static RgbColor ToRgbColor(this SKColor color) =>
        new(color.Red, color.Green, color.Blue, color.Alpha);
}
