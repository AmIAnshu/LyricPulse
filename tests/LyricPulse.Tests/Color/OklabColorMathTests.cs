using AwesomeAssertions;
using LyricPulse.Core.Color;
using LyricPulse.Core.Models;
using Xunit;

namespace LyricPulse.Tests.Color;

public sealed class OklabColorMathTests
{
    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    [InlineData(248, 250, 252)]
    [InlineData(129, 140, 248)]
    [InlineData(15, 15, 35)]
    public void SrgbToOklabAndBack_RoundTripsWithinOneLsb(byte r, byte g, byte b)
    {
        var original = new RgbColor(r, g, b);
        var lab = OklabColorMath.SrgbToOklab(original);
        var lch = OklabColorMath.OklabToOklch(lab);
        var roundTripped = OklabColorMath.OklchToSrgbGamutMapped(lch);

        Math.Abs(roundTripped.R - original.R).Should().BeLessThanOrEqualTo(1);
        Math.Abs(roundTripped.G - original.G).Should().BeLessThanOrEqualTo(1);
        Math.Abs(roundTripped.B - original.B).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void ExtractPaletteFromRgba_DetectsMonochromeCoversAndReturnsMonochromeStudioFallback()
    {
        // Pure B&W checkerboard
        var bw = CreateCheckerboardRgba(32, 32, new RgbColor(20, 20, 20), new RgbColor(235, 235, 235));
        var seed = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var palette = OklabColorMath.ExtractPaletteFromRgba(bw, 32, 32, seed, "bw-hash");

        palette.IsMonochromeFallback.Should().BeTrue();
        palette.AccentColor1.ToHex().Should().Be("#F1F5F9");
        palette.AccentColor2.ToHex().Should().Be("#CBD5E1");
        palette.AccentColor3.ToHex().Should().Be("#94A3B8");
        palette.ScrimTint.ToHex().Should().Be("#0F0F23");
        palette.SourceAlbumHash.Should().Be("bw-hash");
    }

    [Fact]
    public void ExtractPaletteFromRgba_SatisfiesOklchContrastBoundsAcrossStressCoversAndIsDeterministic()
    {
        var covers = new (string Name, byte[] Pixels)[]
        {
            ("NeonGreen", CreateSolidRgba(32, 32, new RgbColor(0, 255, 0))),
            ("MuddyBrown", CreateSolidRgba(32, 32, new RgbColor(85, 60, 38))),
            ("VibrantSplit", CreateSplitRgba(32, 32, new RgbColor(230, 35, 110), new RgbColor(25, 165, 245), new RgbColor(240, 190, 40))),
            ("DeepIndigo", CreateSolidRgba(32, 32, new RgbColor(25, 20, 90)))
        };

        var seed = new byte[] { 42, 99, 12, 7, 18, 221, 5, 88 };

        foreach (var (name, pixels) in covers)
        {
            var palette1 = OklabColorMath.ExtractPaletteFromRgba(pixels, 32, 32, seed, name);
            var palette2 = OklabColorMath.ExtractPaletteFromRgba(pixels, 32, 32, seed, name);

            // Bit-identical determinism
            palette1.Should().Be(palette2, $"cover '{name}' must produce deterministic results");
            palette1.IsMonochromeFallback.Should().BeFalse($"cover '{name}' is chromatic");

            var lch1 = OklabColorMath.OklabToOklch(OklabColorMath.SrgbToOklab(palette1.AccentColor1));
            var lch2 = OklabColorMath.OklabToOklch(OklabColorMath.SrgbToOklab(palette1.AccentColor2));
            var lch3 = OklabColorMath.OklabToOklch(OklabColorMath.SrgbToOklab(palette1.AccentColor3));
            var lchScrim = OklabColorMath.OklabToOklch(OklabColorMath.SrgbToOklab(palette1.ScrimTint));

            lch1.L.Should().BeInRange(0.76, 0.94, $"{name} Accent1 L");
            lch2.L.Should().BeInRange(0.64, 0.84, $"{name} Accent2 L");
            lch3.L.Should().BeInRange(0.60, 0.78, $"{name} Accent3 L");
            lchScrim.L.Should().BeInRange(0.12, 0.24, $"{name} ScrimTint L");

            OklabColorMath.AngularDistanceDegrees(lch1.H, lch2.H)
                .Should().BeGreaterThanOrEqualTo(34.0, $"{name} Accent1/2 hue separation");

            OklabColorMath.DeltaE(
                OklabColorMath.SrgbToOklab(palette1.AccentColor1),
                OklabColorMath.SrgbToOklab(palette1.AccentColor2))
                .Should().BeGreaterThanOrEqualTo(0.14, $"{name} Accent1/2 OKLab distance");
        }
    }

    [Fact]
    public void ExtractPaletteFromRgba_HandlesPureBlackAndPureWhiteGracefully()
    {
        var pureBlack = CreateSolidRgba(16, 16, new RgbColor(0, 0, 0));
        var pureWhite = CreateSolidRgba(16, 16, new RgbColor(255, 255, 255));
        var seed = new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 };

        OklabColorMath.ExtractPaletteFromRgba(pureBlack, 16, 16, seed).IsMonochromeFallback.Should().BeTrue();
        OklabColorMath.ExtractPaletteFromRgba(pureWhite, 16, 16, seed).IsMonochromeFallback.Should().BeTrue();
    }

    [Fact]
    public void MorphPalette_InterpolatesSmoothlyInOklabSpaceOver650ms()
    {
        var from = AlbumAccentPalette.Default;
        var to = AlbumAccentPalette.DemoNeonHorizon;

        OklabColorMath.PaletteMorphDuration.Should().Be(TimeSpan.FromMilliseconds(650));
        OklabColorMath.MorphPalette(from, to, 0.0).AccentColor1.Should().Be(from.AccentColor1);
        OklabColorMath.MorphPalette(from, to, 1.0).AccentColor1.Should().Be(to.AccentColor1);

        var mid = OklabColorMath.MorphPalette(from, to, 0.5);
        mid.AccentColor1.Should().NotBe(from.AccentColor1);
        mid.AccentColor1.Should().NotBe(to.AccentColor1);
    }

    private static byte[] CreateSolidRgba(int w, int h, RgbColor c)
    {
        var data = new byte[w * h * 4];
        for (var i = 0; i < data.Length; i += 4)
        {
            data[i] = c.R;
            data[i + 1] = c.G;
            data[i + 2] = c.B;
            data[i + 3] = 255;
        }

        return data;
    }

    private static byte[] CreateCheckerboardRgba(int w, int h, RgbColor a, RgbColor b)
    {
        var data = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = ((x + y) & 1) == 0 ? a : b;
                var idx = (y * w + x) * 4;
                data[idx] = c.R;
                data[idx + 1] = c.G;
                data[idx + 2] = c.B;
                data[idx + 3] = 255;
            }
        }

        return data;
    }

    private static byte[] CreateSplitRgba(int w, int h, RgbColor c1, RgbColor c2, RgbColor c3)
    {
        var data = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = x < w / 3 ? c1 : x < (2 * w) / 3 ? c2 : c3;
                var idx = (y * w + x) * 4;
                data[idx] = c.R;
                data[idx + 1] = c.G;
                data[idx + 2] = c.B;
                data[idx + 3] = 255;
            }
        }

        return data;
    }
}
