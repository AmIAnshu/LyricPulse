using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace LyricPulse.Rendering.Fonts;

/// <summary>
/// HarfBuzz-backed multi-script shaping and viewport overflow auto-fit helper
/// adhering strictly to SkiaSharp 3 SKFont APIs.
/// </summary>
public static class TextShaperService
{
    public const float MinAutoFitScale = 0.56f;

    /// <summary>
    /// Computes the MOTION_AND_STYLES.md section 3.2 auto-fit scale factor so long words
    /// never clip outside the viewport safe zone.
    /// </summary>
    public static float ComputeAutoFitScale(
        float measuredWidthDip,
        float viewportWidthDip,
        float maxLineWidthRatio = 0.84f)
    {
        if (!float.IsFinite(measuredWidthDip) || measuredWidthDip <= 0f ||
            !float.IsFinite(viewportWidthDip) || viewportWidthDip <= 0f)
        {
            return 1.0f;
        }

        var clampedRatio = Math.Clamp(maxLineWidthRatio, 0.25f, 0.95f);
        var maxAllowedWidth = viewportWidthDip * clampedRatio;
        return Math.Clamp(maxAllowedWidth / Math.Max(measuredWidthDip, 1f), MinAutoFitScale, 1.0f);
    }

    /// <summary>
    /// Measures shaped text advance width using HarfBuzz when non-ASCII scripts are present,
    /// or fast Skia SKFont glyph metrics for simple ASCII runs.
    /// </summary>
    public static float MeasureWidth(string text, SKFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (string.IsNullOrEmpty(text))
        {
            return 0f;
        }

        if (ContainsNonAscii(text))
        {
            using var shaper = new SKShaper(font.Typeface);
            var shaped = shaper.Shape(text, font);
            var width = shaped.Width;
            return float.IsFinite(width) && width > 0f ? width : font.MeasureText(text);
        }

        return font.MeasureText(text);
    }

    /// <summary>
    /// Shapes complex/multi-script text into an immutable SKTextBlob via HarfBuzz (caller owns disposal).
    /// </summary>
    public static SKTextBlob? ShapeToBlob(string text, SKFont font, out float advanceWidth)
    {
        ArgumentNullException.ThrowIfNull(font);
        advanceWidth = 0f;

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        using var shaper = new SKShaper(font.Typeface);
        var result = shaper.Shape(text, font);
        if (result.Points is null || result.Codepoints is null || result.Codepoints.Length == 0)
        {
            return null;
        }

        advanceWidth = result.Width;
        using var builder = new SKTextBlobBuilder();
        var run = builder.AllocatePositionedRun(font, result.Codepoints.Length);
        var glyphs = run.Glyphs;
        var positions = run.Positions;
        for (var i = 0; i < result.Codepoints.Length; i++)
        {
            glyphs[i] = (ushort)result.Codepoints[i];
            positions[i] = result.Points[i];
        }

        return builder.Build();
    }

    private static bool ContainsNonAscii(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (c > 0x7F)
            {
                return true;
            }
        }

        return false;
    }
}
