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

        // Decompression bomb PNG header claiming 10,000 x 10,000 (100 MP > 64 MP cap)
        var bombHeaderPng = BuildSyntheticPngHeader(width: 10_000, height: 10_000);
        extractor.Extract(new AlbumArtPayload("bomb", bombHeaderPng))
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

    private static byte[] BuildSyntheticPngHeader(int width, int height)
    {
        using var ms = new MemoryStream();
        // PNG signature
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        // IHDR chunk (13 bytes payload)
        Span<byte> ihdrData = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdrData[0..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdrData[4..8], height);
        ihdrData[8] = 8; // bit depth
        ihdrData[9] = 6; // RGBA
        ihdrData[10] = 0;
        ihdrData[11] = 0;
        ihdrData[12] = 0;
        WritePngChunk(ms, "IHDR"u8, ihdrData);

        // Minimal IEND chunk
        WritePngChunk(ms, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return ms.ToArray();
    }

    private static void WritePngChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> lenBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lenBytes, data.Length);
        stream.Write(lenBytes);
        stream.Write(type);
        stream.Write(data);

        var crcBuf = new byte[type.Length + data.Length];
        type.CopyTo(crcBuf);
        data.CopyTo(crcBuf.AsSpan(type.Length));

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, ComputeCrc32(crcBuf));
        stream.Write(crcBytes);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFF_FFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB8_8320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
