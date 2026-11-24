using System.Globalization;

namespace LyricPulse.Core.Models;

public readonly record struct RgbColor(byte R, byte G, byte B, byte A = 255)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public uint ToArgbUInt32() => ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | B;

    public static bool TryParseHex(string? hex, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var span = hex.AsSpan().Trim();
        if (span.StartsWith("#", StringComparison.Ordinal))
        {
            span = span[1..];
        }

        if (span.Length == 6 &&
            byte.TryParse(span[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
            byte.TryParse(span[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
            byte.TryParse(span[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            color = new RgbColor(r, g, b);
            return true;
        }

        if (span.Length == 8 &&
            byte.TryParse(span[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a8) &&
            byte.TryParse(span[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r8) &&
            byte.TryParse(span[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g8) &&
            byte.TryParse(span[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b8))
        {
            color = new RgbColor(r8, g8, b8, a8);
            return true;
        }

        return false;
    }
}

public readonly record struct OklabColor(double L, double A, double B);

public readonly record struct OklchColor(double L, double C, double H);

public sealed record AlbumAccentPalette(
    RgbColor AccentColor1,
    RgbColor AccentColor2,
    RgbColor AccentColor3,
    RgbColor ScrimTint,
    bool IsMonochromeFallback,
    string? SourceAlbumHash)
{
    /// <summary>
    /// Harmonized MASTER.md / MOTION_AND_STYLES.md Studio fallback palette (#F8FAFC / #818CF8 / #94A3B8 / #0F0F23).
    /// </summary>
    public static AlbumAccentPalette Default { get; } = new(
        AccentColor1: new RgbColor(0xF8, 0xFA, 0xFC),
        AccentColor2: new RgbColor(0x81, 0x8C, 0xF8),
        AccentColor3: new RgbColor(0x94, 0xA3, 0xB8),
        ScrimTint: new RgbColor(0x0F, 0x0F, 0x23),
        IsMonochromeFallback: false,
        SourceAlbumHash: null);

    /// <summary>
    /// Achromatic Monochrome Studio fallback palette (#F1F5F9 / #CBD5E1 / #94A3B8 / #0F0F23).
    /// </summary>
    public static AlbumAccentPalette MonochromeStudio { get; } = new(
        AccentColor1: new RgbColor(0xF1, 0xF5, 0xF9),
        AccentColor2: new RgbColor(0xCB, 0xD5, 0xE1),
        AccentColor3: new RgbColor(0x94, 0xA3, 0xB8),
        ScrimTint: new RgbColor(0x0F, 0x0F, 0x23),
        IsMonochromeFallback: true,
        SourceAlbumHash: null);

    /// <summary>
    /// Curated Neon Horizon demo palette (#5EEAD4 / #818CF8 / #94A3B8 / #0F0F23).
    /// </summary>
    public static AlbumAccentPalette DemoNeonHorizon { get; } = new(
        AccentColor1: new RgbColor(0x5E, 0xEA, 0xD4),
        AccentColor2: new RgbColor(0x81, 0x8C, 0xF8),
        AccentColor3: new RgbColor(0x94, 0xA3, 0xB8),
        ScrimTint: new RgbColor(0x0F, 0x0F, 0x23),
        IsMonochromeFallback: false,
        SourceAlbumHash: "demo-neon-horizon");
}
