using System.Buffers.Binary;
using AwesomeAssertions;
using LyricPulse.Core.Color;
using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using SkiaSharp;
using Xunit;

namespace LyricPulse.Tests.Rendering;

public sealed class SkiaAlbumColorExtractorTests
{
    [Fact]
    public void Extract_VibrantCoverProducesClampedOklchPaletteDeterministically()
    {
        var pngBytes = EncodeSyntheticCoverPng(128, 128, (x, _) => x < 42
            ? new SKColor(235, 38, 120)
            : x < 86
                ? new SKColor(28, 170, 248)
                : new SKColor(245, 195, 42));

        var extractor = new SkiaAlbumColorExtractor();
        var payload = new AlbumArtPayload("vibrant-hash", pngBytes);

        var p1 = extractor.Extract(payload);
        var p2 = extractor.Extract(payload);

        p1.Should().Be(p2);
        p1.IsMonochromeFallback.Should().BeFalse();
        p1.SourceAlbumHash.Should().Be("vibrant-hash");

        var lch1 = OklabColorMath.OklabToOklch(OklabColorMath.SrgbToOklab(p1.AccentColor1));
        var lch2 = OklabColorMath.OklabToOklch(OklabColorMath.SrgbToOklab(p1.AccentColor2));
        lch1.L.Should().BeInRange(0.76, 0.94);
        lch2.L.Should().BeInRange(0.64, 0.84);
    }

    [Fact]
    public void Extract_MonochromeCoverReturnsMonochromeStudioPalette()
    {
        var pngBytes = EncodeSyntheticCoverPng(96, 96, (x, y) => ((x + y) & 1) == 0
            ? new SKColor(24, 24, 24)
            : new SKColor(232, 232, 232));

        var extractor = new SkiaAlbumColorExtractor();
        var palette = extractor.Extract(new AlbumArtPayload("mono-hash", pngBytes));

        palette.IsMonochromeFallback.Should().BeTrue();
        palette.AccentColor1.ToHex().Should().Be("#F1F5F9");
        palette.SourceAlbumHash.Should().Be("mono-hash");
    }

    [Fact]
    public void Extract_LargeCoverDownsamplesCleanlyViaCodecScaling()
    {
        var jpegBytes = EncodeSyntheticCoverJpeg(512, 512, new SKColor(32, 190, 145));
        var extractor = new SkiaAlbumColorExtractor();
        var palette = extractor.Extract(new AlbumArtPayload("large-jpeg", jpegBytes));

        palette.IsMonochromeFallback.Should().BeFalse();
        palette.SourceAlbumHash.Should().Be("large-jpeg");
    }

    [Fact]
    public void Extract_RejectsNullCorruptOversizedAndDecompressionBombPayloadsGracefully()
    {
        var extractor = new SkiaAlbumColorExtractor();

        extractor.Extract(null).Should().Be(AlbumAccentPalette.Default);
        extractor.Extract(new AlbumArtPayload("empty", ReadOnlyMemory<byte>.Empty))
            .Should().Be(AlbumAccentPalette.Default);

        // Corrupt random bytes
        var corrupt = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03 };
        extractor.Extract(new AlbumArtPayload("corrupt", corrupt))
            .Should().Be(AlbumAccentPalette.Default with { SourceAlbumHash = "corrupt" });

        // Oversized payload > 10 MB
        var oversized = new byte[SkiaAlbumColorExtractor.MaxPayloadBytes + 1];
        extractor.Extract(new AlbumArtPayload("oversized", oversized))
            .Should().Be(AlbumAccentPalette.Default with { SourceAlbumHash = "oversized" });

        // Decompression bomb BMP header claiming 9,000 x 9,000 (81 MP > 64 MP cap)
        // Verify SKCodec ACTUALLY parses it so this test cannot be false-green!
        var bombHeaderBmp = BuildSyntheticBmpHeader(width: 9_000, height: 9_000);
        using (var probeData = SKData.CreateCopy(bombHeaderBmp))
        using (var probeCodec = SKCodec.Create(probeData))
        {
            probeCodec.Should().NotBeNull("bomb fixture must be accepted by SKCodec.Create so the >64MP guard executes");
            probeCodec!.Info.Width.Should().Be(9_000);
            probeCodec.Info.Height.Should().Be(9_000);
        }

        extractor.Extract(new AlbumArtPayload("bomb", bombHeaderBmp))
            .Should().Be(AlbumAccentPalette.Default with { SourceAlbumHash = "bomb" });
    }

    [Fact]
    public void SkColorExtensions_RoundTripCleanlyWithAlphaScaling()
    {
        var original = new RgbColor(0x5E, 0xEA, 0xD4, 255);
        var sk = original.ToSkColor();
        sk.ToRgbColor().Should().Be(original);

        var half = original.ToSkColorWithAlpha(0.5f);
        half.Alpha.Should().BeInRange(127, 128);
    }

    private static byte[] EncodeSyntheticCoverPng(int width, int height, Func<int, int, SKColor> pixelFunc)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, pixelFunc(x, y));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] EncodeSyntheticCoverJpeg(int width, int height, SKColor fill)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        bitmap.Erase(fill);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }

    private static byte[] BuildSyntheticBmpHeader(int width, int height)
    {
        var bytes = new byte[54 + 4];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2, 4), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10, 4), 54); // pixel data offset
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14, 4), 40); // BITMAPINFOHEADER size
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22, 4), height);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26, 2), 1);  // planes
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28, 2), 24); // 24bpp
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(30, 4), 0);  // BI_RGB
        return bytes;
    }
}
