using LyricPulse.Core.Models;

namespace LyricPulse.Core.Color;

/// <summary>
/// Pure C# (zero SkiaSharp) Björn Ottosson OKLab / OKLCH perceptual color space conversions,
/// 12-step binary-search sRGB gamut mapping, and 650ms OKLab track-change palette morphing.
/// </summary>
public static class OklabColorMath
{
    public const int GamutBinarySearchSteps = 12;
    public static readonly TimeSpan PaletteMorphDuration = TimeSpan.FromMilliseconds(650);

    public static OklabColor SrgbToOklab(RgbColor color)
    {
        var r = SrgbChannelToLinear(color.R / 255.0);
        var g = SrgbChannelToLinear(color.G / 255.0);
        var b = SrgbChannelToLinear(color.B / 255.0);

        var l = (0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b);
        var m = (0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b);
        var s = (0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b);

        var lCbrt = Math.Cbrt(l);
        var mCbrt = Math.Cbrt(m);
        var sCbrt = Math.Cbrt(s);

        return new OklabColor(
            L: (0.2104542553 * lCbrt) + (0.7936177850 * mCbrt) - (0.0040720468 * sCbrt),
            A: (1.9779984951 * lCbrt) - (2.4285922050 * mCbrt) + (0.4505937099 * sCbrt),
            B: (0.0259040371 * lCbrt) + (0.7827717662 * mCbrt) - (0.8086757660 * sCbrt));
    }

    public static OklchColor OklabToOklch(OklabColor lab)
    {
        var c = Math.Sqrt((lab.A * lab.A) + (lab.B * lab.B));
        var h = c < 1e-7 ? 0.0 : NormalizeDegrees(Math.Atan2(lab.B, lab.A) * (180.0 / Math.PI));
        return new OklchColor(lab.L, c, h);
    }

    public static OklabColor OklchToOklab(OklchColor lch)
    {
        var rad = lch.H * (Math.PI / 180.0);
        return new OklabColor(lch.L, lch.C * Math.Cos(rad), lch.C * Math.Sin(rad));
    }

    public static double DeltaE(OklabColor a, OklabColor b) => Math.Sqrt(SquaredDistance(a, b));

    public static double SquaredDistance(OklabColor a, OklabColor b)
    {
        var dl = a.L - b.L;
        var da = a.A - b.A;
        var db = a.B - b.B;
        return (dl * dl) + (da * da) + (db * db);
    }

    public static double AngularDistanceDegrees(double h1, double h2)
    {
        var diff = Math.Abs(NormalizeDegrees(h1) - NormalizeDegrees(h2));
        return diff > 180.0 ? 360.0 - diff : diff;
    }

    public static double NormalizeDegrees(double deg)
    {
        var r = deg % 360.0;
        return r < 0 ? r + 360.0 : r;
    }

    public static RgbColor OklchToSrgbGamutMapped(OklchColor lch, byte alpha = 255)
    {
        var safeL = double.IsFinite(lch.L) ? Math.Clamp(lch.L, 0.0, 1.0) : 0.5;
        var safeC = double.IsFinite(lch.C) ? Math.Max(0.0, lch.C) : 0.0;
        var safeH = double.IsFinite(lch.H) ? NormalizeDegrees(lch.H) : 0.0;

        if (TryOklabToLinearSrgb(OklchToOklab(new OklchColor(safeL, safeC, safeH)), out var lr, out var lg, out var lb))
        {
            return LinearSrgbToRgbColor(lr, lg, lb, alpha);
        }

        // 12-step binary search on chroma keeping L and H strictly invariant.
        var low = 0.0;
        var high = safeC;
        TryOklabToLinearSrgb(new OklabColor(safeL, 0.0, 0.0), out var bestR, out var bestG, out var bestB);

        for (var i = 0; i < GamutBinarySearchSteps; i++)
        {
            var mid = (low + high) * 0.5;
            if (TryOklabToLinearSrgb(OklchToOklab(new OklchColor(safeL, mid, safeH)), out var mr, out var mg, out var mb))
            {
                bestR = mr;
                bestG = mg;
                bestB = mb;
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return LinearSrgbToRgbColor(bestR, bestG, bestB, alpha);
    }

    public static AlbumAccentPalette ExtractPaletteFromRgba(
        ReadOnlySpan<byte> rgbaPixels,
        int width,
        int height,
        ReadOnlySpan<byte> seedBytes,
        string? sourceAlbumHash = null) =>
        OklabPaletteExtractor.ExtractFromRgba(rgbaPixels, width, height, seedBytes, sourceAlbumHash);

    public static AlbumAccentPalette MorphPalette(AlbumAccentPalette from, AlbumAccentPalette to, double progress)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        var t = EaseColorMorph(Math.Clamp(progress, 0.0, 1.0));
        if (t <= 0.0)
        {
            return from;
        }

        if (t >= 1.0)
        {
            return to;
        }

        return new AlbumAccentPalette(
            AccentColor1: InterpolateSrgbInOklab(from.AccentColor1, to.AccentColor1, t),
            AccentColor2: InterpolateSrgbInOklab(from.AccentColor2, to.AccentColor2, t),
            AccentColor3: InterpolateSrgbInOklab(from.AccentColor3, to.AccentColor3, t),
            ScrimTint: InterpolateSrgbInOklab(from.ScrimTint, to.ScrimTint, t),
            IsMonochromeFallback: to.IsMonochromeFallback,
            SourceAlbumHash: to.SourceAlbumHash);
    }

    /// <summary>
    /// Evaluates cubic-bezier(0.25, 0.10, 0.25, 1.00) (Ease.ColorMorph).
    /// </summary>
    public static double EaseColorMorph(double t)
    {
        var clamped = Math.Clamp(t, 0.0, 1.0);
        var u = clamped;
        for (var i = 0; i < 6; i++)
        {
            var x = EvaluateCubicBezierAxis(u, 0.25, 0.25) - clamped;
            var dx = EvaluateCubicBezierDerivative(u, 0.25, 0.25);
            if (Math.Abs(dx) < 1e-6)
            {
                break;
            }

            u = Math.Clamp(u - (x / dx), 0.0, 1.0);
        }

        return Math.Clamp(EvaluateCubicBezierAxis(u, 0.10, 1.00), 0.0, 1.0);
    }

    private static RgbColor InterpolateSrgbInOklab(RgbColor from, RgbColor to, double t)
    {
        var labA = SrgbToOklab(from);
        var labB = SrgbToOklab(to);
        var mixed = new OklabColor(
            labA.L + ((labB.L - labA.L) * t),
            labA.A + ((labB.A - labA.A) * t),
            labA.B + ((labB.B - labA.B) * t));
        var alpha = (byte)Math.Clamp(Math.Round(from.A + ((to.A - from.A) * t)), 0, 255);
        return OklchToSrgbGamutMapped(OklabToOklch(mixed), alpha);
    }

    private static bool TryOklabToLinearSrgb(OklabColor lab, out double r, out double g, out double b)
    {
        var lCbrt = lab.L + (0.3963377774 * lab.A) + (0.2158037573 * lab.B);
        var mCbrt = lab.L - (0.1055613458 * lab.A) - (0.0638541728 * lab.B);
        var sCbrt = lab.L - (0.0894841775 * lab.A) - (1.2914855480 * lab.B);

        var l = lCbrt * lCbrt * lCbrt;
        var m = mCbrt * mCbrt * mCbrt;
        var s = sCbrt * sCbrt * sCbrt;

        r = (+4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s);
        g = (-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s);
        b = (-0.0041960863 * l) - (0.7034186147 * m) + (1.7076147010 * s);

        return r is >= -1e-4 and <= 1.0001 &&
               g is >= -1e-4 and <= 1.0001 &&
               b is >= -1e-4 and <= 1.0001;
    }

    private static RgbColor LinearSrgbToRgbColor(double r, double g, double b, byte alpha) =>
        new(LinearChannelToSrgbByte(r), LinearChannelToSrgbByte(g), LinearChannelToSrgbByte(b), alpha);

    private static double SrgbChannelToLinear(double c) =>
        c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static byte LinearChannelToSrgbByte(double linear)
    {
        var clamped = Math.Clamp(linear, 0.0, 1.0);
        var srgb = clamped <= 0.0031308
            ? 12.92 * clamped
            : (1.055 * Math.Pow(clamped, 1.0 / 2.4)) - 0.055;
        return (byte)Math.Clamp(Math.Round(srgb * 255.0), 0, 255);
    }

    private static double EvaluateCubicBezierAxis(double u, double p1, double p2)
    {
        var inv = 1.0 - u;
        return (3.0 * inv * inv * u * p1) + (3.0 * inv * u * u * p2) + (u * u * u);
    }

    private static double EvaluateCubicBezierDerivative(double u, double p1, double p2)
    {
        var inv = 1.0 - u;
        return (3.0 * inv * inv * p1) + (6.0 * inv * u * (p2 - p1)) + (3.0 * u * u * (1.0 - p2));
    }
}
