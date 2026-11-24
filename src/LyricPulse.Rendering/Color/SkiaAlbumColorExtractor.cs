using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Color;
using LyricPulse.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace LyricPulse.Rendering.Color;

/// <summary>
/// SkiaSharp 3 implementation of <see cref="IAlbumColorExtractor"/> enforcing decompression-bomb guards
/// (&lt;= 10 MB payload, &lt;= 64 MP decoded area), linear 64x64 downsampling via <see cref="SKSamplingOptions"/>,
/// and deterministic delegation to <see cref="OklabPaletteExtractor"/>.
/// </summary>
public sealed class SkiaAlbumColorExtractor : IAlbumColorExtractor
{
    public const int MaxPayloadBytes = 10 * 1024 * 1024;
    public const long MaxDecodedPixels = 64_000_000L;
    public const int DownsampleDimension = 64;

    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private readonly ILogger<SkiaAlbumColorExtractor> _logger;

    public SkiaAlbumColorExtractor(ILogger<SkiaAlbumColorExtractor>? logger = null)
    {
        _logger = logger ?? NullLogger<SkiaAlbumColorExtractor>.Instance;
    }

    public AlbumAccentPalette Extract(AlbumArtPayload? artPayload)
    {
        if (artPayload is null || artPayload.ImageBytes.IsEmpty)
        {
            return AlbumAccentPalette.Default;
        }

        var hash = artPayload.Sha256Hash;
        var span = artPayload.ImageBytes.Span;

        if (span.Length > MaxPayloadBytes)
        {
            _logger.LogAlbumArtOversizedBytes(span.Length, MaxPayloadBytes);
            return AlbumAccentPalette.Default with { SourceAlbumHash = hash };
        }

        using var encodedData = SKData.CreateCopy(span);
        using var codec = SKCodec.Create(encodedData);
        if (codec is null)
        {
            _logger.LogAlbumArtDecodeFailed();
            return AlbumAccentPalette.Default with { SourceAlbumHash = hash };
        }

        var codecInfo = codec.Info;
        if (codecInfo.Width <= 0 || codecInfo.Height <= 0)
        {
            _logger.LogAlbumArtDecodeFailed();
            return AlbumAccentPalette.Default with { SourceAlbumHash = hash };
        }

        var pixelCount = (long)codecInfo.Width * codecInfo.Height;
        if (pixelCount > MaxDecodedPixels)
        {
            _logger.LogAlbumArtOversizedPixels(codecInfo.Width, codecInfo.Height, MaxDecodedPixels);
            return AlbumAccentPalette.Default with { SourceAlbumHash = hash };
        }

        using var decodedBitmap = DecodeSafeBitmap(codec, codecInfo);
        if (decodedBitmap is null)
        {
            _logger.LogAlbumArtDecodeFailed();
            return AlbumAccentPalette.Default with { SourceAlbumHash = hash };
        }

        var targetInfo = new SKImageInfo(
            DownsampleDimension,
            DownsampleDimension,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);

        using var resized = decodedBitmap.Width == DownsampleDimension &&
                            decodedBitmap.Height == DownsampleDimension &&
                            decodedBitmap.ColorType == SKColorType.Rgba8888 &&
                            decodedBitmap.AlphaType == SKAlphaType.Unpremul
            ? decodedBitmap.Copy()
            : decodedBitmap.Resize(targetInfo, LinearSampling);

        if (resized is null)
        {
            _logger.LogAlbumArtDecodeFailed();
            return AlbumAccentPalette.Default with { SourceAlbumHash = hash };
        }

        var pixelSpan = resized.GetPixelSpan();
        return OklabPaletteExtractor.ExtractFromRgba(
            pixelSpan,
            DownsampleDimension,
            DownsampleDimension,
            span,
            hash);
    }

    private static SKBitmap? DecodeSafeBitmap(SKCodec codec, SKImageInfo codecInfo)
    {
        // Request decoder-level downscaling for large covers (e.g. 4000x4000 JPEG) to avoid giant allocations.
        var maxSide = Math.Max(codecInfo.Width, codecInfo.Height);
        var desiredScale = maxSide > DownsampleDimension * 4
            ? Math.Clamp((float)(DownsampleDimension * 2) / maxSide, 0.05f, 1.0f)
            : 1.0f;

        var scaledSize = desiredScale < 1.0f
            ? codec.GetScaledDimensions(desiredScale)
            : new SKSizeI(codecInfo.Width, codecInfo.Height);

        var decodeW = Math.Max(1, scaledSize.Width);
        var decodeH = Math.Max(1, scaledSize.Height);
        var decodeInfo = new SKImageInfo(decodeW, decodeH, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        var bitmap = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, bitmap.GetPixels());
        if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
        {
            return bitmap;
        }

        bitmap.Dispose();
        return null;
    }
}
