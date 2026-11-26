using Avalonia.Media;
using LyricPulse.Core.Models;

namespace LyricPulse.UI.Theme;

/// <summary>
/// Derives accessible, high-contrast Avalonia UI accent tokens from the active album palette
/// so controls, pills, selected style cards, sliders, and drawer highlights harmonize live
/// with the playing song without violating text contrast in Dark or Light themes.
/// </summary>
public readonly record struct AlbumUiAccentColors(
    Color Primary,
    Color PrimaryHover,
    Color PrimaryPressed,
    Color Text,
    Color Border,
    Color SoftBg,
    Color FocusRing);

public static class AlbumEnvironmentPalette
{
    public static AlbumUiAccentColors ComputeDynamicAccentColors(
        Color accent1,
        Color accent2,
        Color accent3,
        bool isLightTheme)
    {
        if (isLightTheme)
        {
            var richLightPrimary = CapRelativeLuminance(accent2, maxLuminance: 0.16);
            var lightHover = AdjustChannelMultiplier(richLightPrimary, 0.86);
            var lightPressed = AdjustChannelMultiplier(richLightPrimary, 0.74);
            var lightText = CapRelativeLuminance(Blend(accent2, accent3, 0.25), maxLuminance: 0.14);
            var lightSoftBg = Blend(Color.FromRgb(0xFF, 0xFF, 0xFF), accent2, 0.13);
            return new AlbumUiAccentColors(
                Primary: richLightPrimary,
                PrimaryHover: lightHover,
                PrimaryPressed: lightPressed,
                Text: lightText,
                Border: richLightPrimary,
                SoftBg: lightSoftBg,
                FocusRing: lightText);
        }

        // Dark Studio Mode: keep White OnAccentPrimary readable (lum <= 0.24) and AccentText bright (lum >= 0.42)
        var primary = CapRelativeLuminance(accent2, maxLuminance: 0.23);
        var hover = BoostBrightnessForHover(primary, 1.12);
        var pressed = AdjustChannelMultiplier(primary, 0.88);
        var text = EnsureMinRelativeLuminance(Blend(accent1, accent2, 0.20), minLuminance: 0.44);
        var border = EnsureMinRelativeLuminance(Blend(accent2, accent1, 0.35), minLuminance: 0.28);
        var softBg = Blend(Color.FromRgb(0x11, 0x13, 0x1E), accent2, 0.20);
        return new AlbumUiAccentColors(
            Primary: primary,
            PrimaryHover: hover,
            PrimaryPressed: pressed,
            Text: text,
            Border: border,
            SoftBg: softBg,
            FocusRing: text);
    }

    public static AlbumUiAccentColors FromPalette(AlbumAccentPalette palette, bool isLightTheme)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return ComputeDynamicAccentColors(
            ToAvaloniaColor(palette.AccentColor1),
            ToAvaloniaColor(palette.AccentColor2),
            ToAvaloniaColor(palette.AccentColor3),
            isLightTheme);
    }

    private static Color ToAvaloniaColor(RgbColor c) => Color.FromRgb(c.R, c.G, c.B);

    private static Color Blend(Color a, Color b, double t)
    {
        var ct = Math.Clamp(t, 0.0, 1.0);
        return Color.FromRgb(
            (byte)Math.Clamp(Math.Round(a.R + ((b.R - a.R) * ct)), 0, 255),
            (byte)Math.Clamp(Math.Round(a.G + ((b.G - a.G) * ct)), 0, 255),
            (byte)Math.Clamp(Math.Round(a.B + ((b.B - a.B) * ct)), 0, 255));
    }

    private static Color CapRelativeLuminance(Color c, double maxLuminance)
    {
        var lum = ComputeRelativeLuminance(c);
        if (lum <= maxLuminance || lum <= 0.001)
        {
            return c;
        }

        // Binary search blend toward black in sRGB so hue character stays crisp
        var low = 0.0;
        var high = 1.0;
        for (var i = 0; i < 10; i++)
        {
            var mid = (low + high) * 0.5;
            var candidate = Blend(Color.FromRgb(0x0D, 0x0F, 0x18), c, mid);
            if (ComputeRelativeLuminance(candidate) <= maxLuminance)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return Blend(Color.FromRgb(0x0D, 0x0F, 0x18), c, low);
    }

    private static Color EnsureMinRelativeLuminance(Color c, double minLuminance)
    {
        if (ComputeRelativeLuminance(c) >= minLuminance)
        {
            return c;
        }

        var low = 0.0;
        var high = 1.0;
        for (var i = 0; i < 10; i++)
        {
            var mid = (low + high) * 0.5;
            var candidate = Blend(c, Color.FromRgb(0xF8, 0xFA, 0xFC), mid);
            if (ComputeRelativeLuminance(candidate) >= minLuminance)
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return Blend(c, Color.FromRgb(0xF8, 0xFA, 0xFC), high);
    }

    private static Color AdjustChannelMultiplier(Color c, double scale) =>
        Color.FromRgb(
            (byte)Math.Clamp(Math.Round(c.R * scale), 0, 255),
            (byte)Math.Clamp(Math.Round(c.G * scale), 0, 255),
            (byte)Math.Clamp(Math.Round(c.B * scale), 0, 255));

    private static Color BoostBrightnessForHover(Color c, double scale) =>
        CapRelativeLuminance(
            Color.FromRgb(
                (byte)Math.Clamp(Math.Round(c.R * scale), 0, 255),
                (byte)Math.Clamp(Math.Round(c.G * scale), 0, 255),
                (byte)Math.Clamp(Math.Round(c.B * scale), 0, 255)),
            maxLuminance: 0.27);

    public static double ComputeRelativeLuminance(Color c)
    {
        static double Lin(byte ch)
        {
            var v = ch / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Lin(c.R)) + (0.7152 * Lin(c.G)) + (0.0722 * Lin(c.B));
    }
}
