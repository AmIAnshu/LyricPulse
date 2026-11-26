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

    public static SKImage? DecodeBoundedCoverImage(ReadOnlySpan<byte> imageBytes, int targetDimension = 288)
    {
        if (imageBytes.IsEmpty || imageBytes.Length > MaxPayloadBytes)
        {
            return null;
        }

        try
        {
            using var encodedData = SKData.CreateCopy(imageBytes);
            using var codec = SKCodec.Create(encodedData);
            SKBitmap? decoded = null;

            if (codec is not null && codec.Info.Width > 0 && codec.Info.Height > 0)
            {
                var codecInfo = codec.Info;
                if ((long)codecInfo.Width * codecInfo.Height > MaxDecodedPixels)
                {
                    return null;
                }

                var maxSide = Math.Max(codecInfo.Width, codecInfo.Height);
                var desiredScale = maxSide > targetDimension * 2
                    ? Math.Clamp((float)(targetDimension * 2) / maxSide, 0.05f, 1.0f)
                    : 1.0f;

                var scaledSize = desiredScale < 1.0f
                    ? codec.GetScaledDimensions(desiredScale)
                    : new SKSizeI(codecInfo.Width, codecInfo.Height);

                var decodeW = Math.Max(1, scaledSize.Width);
                var decodeH = Math.Max(1, scaledSize.Height);
                var decodeInfo = new SKImageInfo(decodeW, decodeH, SKColorType.Bgra8888, SKAlphaType.Premul);

                var candidate = new SKBitmap(decodeInfo);
                if (codec.GetPixels(decodeInfo, candidate.GetPixels()) is SKCodecResult.Success or SKCodecResult.IncompleteInput)
                {
                    decoded = candidate;
                }
                else
                {
                    candidate.Dispose();
                }
            }

            decoded ??= SKBitmap.Decode(encodedData);
            if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0)
            {
                decoded?.Dispose();
                return null;
            }

            using (decoded)
            {
                var w = decoded.Width;
                var h = decoded.Height;
                var cropSide = Math.Min(w, h);
                var srcX = (w - cropSide) / 2;
                var srcY = (h - cropSide) / 2;
                var outSide = Math.Clamp(Math.Min(cropSide, targetDimension), 48, targetDimension);
                var outInfo = new SKImageInfo(outSide, outSide, SKColorType.Bgra8888, SKAlphaType.Premul);

                using var surface = SKSurface.Create(outInfo);
                if (surface is null)
                {
                    return SKImage.FromBitmap(decoded);
                }

                using var sourceImg = SKImage.FromBitmap(decoded);
                var srcRect = new SKRect(srcX, srcY, srcX + cropSide, srcY + cropSide);
                var dstRect = new SKRect(0f, 0f, outSide, outSide);
                using var paint = new SKPaint { IsAntialias = true };
                surface.Canvas.DrawImage(sourceImg, srcRect, dstRect, LinearSampling, paint);
                return surface.Snapshot();
            }
        }
        catch
        {
            return null;
        }
    }

    public static (SKImage? CircularCover, SKImage? AmbientBlur) DecodeVinylAlbumTextures(
        ReadOnlySpan<byte> imageBytes,
        int coverDimension = 288,
        int ambientDimension = 40)
    {
        using var squareCover = DecodeBoundedCoverImage(imageBytes, coverDimension);
        if (squareCover is null)
        {
            return (null, null);
        }

        SKImage? circularCover = null;
        SKImage? ambientBlur = null;
        var side = Math.Clamp(Math.Min(squareCover.Width, squareCover.Height), 48, coverDimension);
        var half = side * 0.5f;

        // 1. Pre-bake a pixel-true circular alpha cutout + punched spindle cutout so GPU rotation NEVER shows a square box
        var circleInfo = new SKImageInfo(side, side, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var circleSurface = SKSurface.Create(circleInfo))
        {
            if (circleSurface is not null)
            {
                var canvas = circleSurface.Canvas;
                canvas.Clear(SKColors.Transparent);

                using var imgShader = squareCover.ToShader(
                    SKShaderTileMode.Clamp,
                    SKShaderTileMode.Clamp,
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
                    SKMatrix.CreateScale(side / (float)squareCover.Width, side / (float)squareCover.Height));
                using var fillPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Shader = imgShader
                };
                canvas.DrawCircle(half, half, half - 1.5f, fillPaint);

                using var holeClearPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    BlendMode = SKBlendMode.Clear
                };
                canvas.DrawCircle(half, half, Math.Max(3f, side * 0.082f), holeClearPaint);

                circularCover = circleSurface.Snapshot();
            }
        }

        // 2. Bake a velvety 40x40 blurred ambient backdrop texture ONCE on track switch (O(1) cost per render frame)
        var blurSide = Math.Clamp(ambientDimension, 24, 64);
        var blurInfo = new SKImageInfo(blurSide, blurSide, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var blurSurface = SKSurface.Create(blurInfo))
        {
            if (blurSurface is not null)
            {
                var canvas = blurSurface.Canvas;
                canvas.Clear(SKColors.Black);
                using var blurFilter = SKImageFilter.CreateBlur(3.4f, 3.4f);
                using var blurPaint = new SKPaint
                {
                    IsAntialias = true,
                    ImageFilter = blurFilter
                };
                var bleed = blurSide * 0.14f;
                var dst = new SKRect(-bleed, -bleed, blurSide + bleed, blurSide + bleed);
                canvas.DrawImage(squareCover, new SKRect(0, 0, squareCover.Width, squareCover.Height), dst, LinearSampling, blurPaint);
                ambientBlur = blurSurface.Snapshot();
            }
        }

        return (circularCover, ambientBlur);
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
