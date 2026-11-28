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

        var cropRect = ComputeSmartArtworkCropRect(decodedBitmap);
        var cropI = new SKRectI(
            Math.Clamp((int)MathF.Floor(cropRect.Left), 0, Math.Max(0, decodedBitmap.Width - 1)),
            Math.Clamp((int)MathF.Floor(cropRect.Top), 0, Math.Max(0, decodedBitmap.Height - 1)),
            Math.Clamp((int)MathF.Ceiling(cropRect.Right), 1, decodedBitmap.Width),
            Math.Clamp((int)MathF.Ceiling(cropRect.Bottom), 1, decodedBitmap.Height));

        using var croppedBitmap = (cropI.Left > 0 || cropI.Top > 0 || cropI.Right < decodedBitmap.Width || cropI.Bottom < decodedBitmap.Height) &&
                                   cropI.Width >= 16 && cropI.Height >= 16
            ? ExtractSubsetCopy(decodedBitmap, cropI)
            : null;
        var sourceForPalette = croppedBitmap ?? decodedBitmap;

        var targetInfo = new SKImageInfo(
            DownsampleDimension,
            DownsampleDimension,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);

        using var resized = sourceForPalette.Width == DownsampleDimension &&
                            sourceForPalette.Height == DownsampleDimension &&
                            sourceForPalette.ColorType == SKColorType.Rgba8888 &&
                            sourceForPalette.AlphaType == SKAlphaType.Unpremul
            ? sourceForPalette.Copy()
            : sourceForPalette.Resize(targetInfo, LinearSampling);

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
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            {
                return null;
            }

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
            if ((long)decodeW * decodeH > MaxDecodedPixels)
            {
                return null;
            }

            var decodeInfo = new SKImageInfo(decodeW, decodeH, SKColorType.Bgra8888, SKAlphaType.Premul);

            var decoded = new SKBitmap(decodeInfo);
            if (codec.GetPixels(decodeInfo, decoded.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                decoded.Dispose();
                return null;
            }

            if (decoded.Width <= 0 || decoded.Height <= 0)
            {
                decoded.Dispose();
                return null;
            }

            using (decoded)
            {
                var srcRect = ComputeSmartArtworkCropRect(decoded);
                var outSide = Math.Clamp((int)MathF.Round(MathF.Min(srcRect.Width, srcRect.Height)), 48, targetDimension);
                var outInfo = new SKImageInfo(outSide, outSide, SKColorType.Bgra8888, SKAlphaType.Premul);

                using var surface = SKSurface.Create(outInfo);
                if (surface is null)
                {
                    return SKImage.FromBitmap(decoded);
                }

                surface.Canvas.Clear(SKColors.Black);
                using var sourceImg = SKImage.FromBitmap(decoded);
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

    /// <summary>
    /// Decodes album artwork once per track into (1) a crisp square cover texture and
    /// (2) a full-bleed lightly-blurred backdrop texture (320x320 with gentle blur so cover art
    /// details and color vibe stay recognizable across the Glass Stage room).
    /// </summary>
    public static (SKImage? CoverArt, SKImage? LightBlurBackdrop) DecodeGlassStageTextures(
        ReadOnlySpan<byte> imageBytes,
        int coverDimension = 360,
        int backdropDimension = 320)
    {
        var coverSide = Math.Clamp(coverDimension, 96, 512);
        var coverArt = DecodeBoundedCoverImage(imageBytes, coverSide);
        if (coverArt is null)
        {
            return (null, null);
        }

        SKImage? lightBlurBackdrop = null;
        var blurSide = Math.Clamp(backdropDimension, 128, 420);
        var blurInfo = new SKImageInfo(blurSide, blurSide, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var blurSurface = SKSurface.Create(blurInfo))
        {
            if (blurSurface is not null)
            {
                var canvas = blurSurface.Canvas;
                canvas.Clear(SKColors.Black);
                using var blurFilter = SKImageFilter.CreateBlur(7.5f, 7.5f);
                using var blurPaint = new SKPaint
                {
                    IsAntialias = true,
                    ImageFilter = blurFilter
                };
                var bleed = blurSide * 0.08f;
                var dst = new SKRect(-bleed, -bleed, blurSide + bleed, blurSide + bleed);
                canvas.DrawImage(
                    coverArt,
                    new SKRect(0f, 0f, coverArt.Width, coverArt.Height),
                    dst,
                    LinearSampling,
                    blurPaint);
                lightBlurBackdrop = blurSurface.Snapshot();
            }
        }

        return (coverArt, lightBlurBackdrop);
    }

    internal static SKRect ComputeSmartArtworkCropRect(SKBitmap bitmap)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;
        if (w < 32 || h < 32)
        {
            var side = MathF.Min(w, h);
            return new SKRect((w - side) * 0.5f, (h - side) * 0.5f, ((w - side) * 0.5f) + side, ((h - side) * 0.5f) + side);
        }

        if (TryDetectTransparentPaddedArtwork(bitmap, w, h, out var alphaTrim))
        {
            return CenterSquare(alphaTrim);
        }

        if (TryDetectMattePaddedArtwork(bitmap, w, h, out var matteTrim))
        {
            return CenterSquare(matteTrim);
        }

        var cropSide = MathF.Min(w, h);
        var srcX = (w - cropSide) * 0.5f;
        var srcY = (h - cropSide) * 0.5f;
        return new SKRect(srcX, srcY, srcX + cropSide, srcY + cropSide);
    }

    private static SKRect CenterSquare(SKRect rect)
    {
        var side = MathF.Max(16f, MathF.Min(rect.Width, rect.Height));
        var cx = rect.MidX;
        var cy = rect.MidY;
        return new SKRect(cx - (side * 0.5f), cy - (side * 0.5f), cx + (side * 0.5f), cy + (side * 0.5f));
    }

    private static bool TryDetectTransparentPaddedArtwork(SKBitmap bitmap, int w, int h, out SKRect trimmed)
    {
        trimmed = default;
        var stepX = w > 256 ? 2 : 1;
        var stepY = h > 256 ? 2 : 1;
        var yScanStart = (int)(h * 0.08f);
        var yScanEnd = (int)(h * 0.66f);

        var leftCol = -1;
        var rightCol = -1;
        for (var x = 0; x < w; x += stepX)
        {
            var total = 0;
            var opaque = 0;
            for (var y = yScanStart; y <= yScanEnd; y += stepY)
            {
                total++;
                if (bitmap.GetPixel(x, y).Alpha >= 24)
                {
                    opaque++;
                }
            }

            if (total > 0 && (opaque / (float)total) >= 0.32f)
            {
                if (leftCol < 0)
                {
                    leftCol = x;
                }

                rightCol = Math.Min(w - 1, x + stepX - 1);
            }
        }

        if (leftCol < 0 || rightCol <= leftCol)
        {
            return false;
        }

        var boxW = rightCol - leftCol + 1;
        if (boxW < Math.Max(24, w * 0.35f))
        {
            return false;
        }

        var innerL = Math.Min(rightCol, leftCol + Math.Max(2, (int)(boxW * 0.06f)));
        var innerR = Math.Max(innerL, rightCol - Math.Max(2, (int)(boxW * 0.06f)));

        var topRow = -1;
        var solidEndRow = -1;
        var emptyStreak = 0;

        for (var y = 0; y < h; y += stepY)
        {
            var total = 0;
            var opaque = 0;
            for (var x = innerL; x <= innerR; x += stepX)
            {
                total++;
                if (bitmap.GetPixel(x, y).Alpha >= 24)
                {
                    opaque++;
                }
            }

            var frac = total > 0 ? opaque / (float)total : 0f;
            if (topRow < 0)
            {
                if (frac >= 0.55f)
                {
                    topRow = y;
                    solidEndRow = y;
                }
                continue;
            }

            if (frac >= 0.52f)
            {
                emptyStreak = 0;
                solidEndRow = Math.Min(h - 1, y + stepY - 1);
            }
            else if (y - topRow >= h * 0.30f)
            {
                emptyStreak++;
                if (frac < 0.24f || emptyStreak >= 2)
                {
                    break;
                }
            }
        }

        if (topRow < 0 || solidEndRow <= topRow)
        {
            return false;
        }

        var boxH = solidEndRow - topRow + 1;
        if (boxH < Math.Max(24, h * 0.35f))
        {
            return false;
        }

        // Only apply when transparent margins / detached watermark removed meaningful border (>= 3.5%)
        var significantTrim = boxW <= w - Math.Max(4, (int)(w * 0.035f)) ||
                              boxH <= h - Math.Max(4, (int)(h * 0.035f));
        if (!significantTrim)
        {
            return false;
        }

        var inset = Math.Clamp(Math.Min(boxW, boxH) / 96f, 1f, 3f);
        trimmed = new SKRect(leftCol + inset, topRow + inset, rightCol + 1 - inset, solidEndRow + 1 - inset);
        return trimmed.Width >= 24f && trimmed.Height >= 24f;
    }

    private static bool TryDetectMattePaddedArtwork(SKBitmap bitmap, int w, int h, out SKRect trimmed)
    {
        trimmed = default;
        var stepX = w > 256 ? 2 : 1;
        var stepY = h > 256 ? 2 : 1;

        var corner = bitmap.GetPixel(2, 2);
        var bLeft = bitmap.GetPixel(2, h / 2);
        var bRight = bitmap.GetPixel(w - 3, h / 2);
        var bBot = bitmap.GetPixel(w / 2, h - 3);
        if (corner.Alpha < 220 || bLeft.Alpha < 220 || bRight.Alpha < 220 || bBot.Alpha < 220)
        {
            return false;
        }

        var bgR = (byte)((bLeft.Red + bRight.Red + bBot.Red) / 3);
        var bgG = (byte)((bLeft.Green + bRight.Green + bBot.Green) / 3);
        var bgB = (byte)((bLeft.Blue + bRight.Blue + bBot.Blue) / 3);

        static int ColorDist(SKColor c, byte r, byte g, byte b) =>
            Math.Abs(c.Red - r) + Math.Abs(c.Green - g) + Math.Abs(c.Blue - b);

        // Confirm uniform perimeter matte across left edge, right edge, and bottom strip
        var perimTotal = 0;
        var perimMatch = 0;
        for (var y = (int)(h * 0.08f); y <= (int)(h * 0.72f); y += stepY * 2)
        {
            perimTotal += 2;
            if (ColorDist(bitmap.GetPixel(2, y), bgR, bgG, bgB) <= 26) perimMatch++;
            if (ColorDist(bitmap.GetPixel(w - 3, y), bgR, bgG, bgB) <= 26) perimMatch++;
        }
        for (var x = (int)(w * 0.06f); x <= (int)(w * 0.94f); x += stepX * 2)
        {
            perimTotal++;
            if (ColorDist(bitmap.GetPixel(x, h - 3), bgR, bgG, bgB) <= 26) perimMatch++;
        }

        if (perimTotal == 0 || (perimMatch / (float)perimTotal) < 0.92f)
        {
            return false;
        }

        // Confirm real contrasting artwork exists inside center (never crop solid single-color covers)
        var centerTotal = 0;
        var centerContrast = 0;
        for (var y = (int)(h * 0.14f); y <= (int)(h * 0.58f); y += stepY * 2)
        {
            for (var x = (int)(w * 0.22f); x <= (int)(w * 0.78f); x += stepX * 2)
            {
                centerTotal++;
                if (ColorDist(bitmap.GetPixel(x, y), bgR, bgG, bgB) > 42)
                {
                    centerContrast++;
                }
            }
        }

        if (centerTotal == 0 || (centerContrast / (float)centerTotal) < 0.18f)
        {
            return false;
        }

        var yStart = (int)(h * 0.08f);
        var yEnd = (int)(h * 0.68f);
        var leftCol = -1;
        var rightCol = -1;
        for (var x = 0; x < w; x += stepX)
        {
            var tot = 0;
            var fg = 0;
            for (var y = yStart; y <= yEnd; y += stepY)
            {
                tot++;
                if (ColorDist(bitmap.GetPixel(x, y), bgR, bgG, bgB) > 28) fg++;
            }

            if (tot > 0 && (fg / (float)tot) >= 0.15f)
            {
                if (leftCol < 0) leftCol = x;
                rightCol = Math.Min(w - 1, x + stepX - 1);
            }
        }

        if (leftCol < 0 || rightCol <= leftCol)
        {
            return false;
        }

        var boxW = rightCol - leftCol + 1;
        var hasSidePadding = leftCol >= Math.Max(4, (int)(w * 0.045f)) &&
                             (w - 1 - rightCol) >= Math.Max(4, (int)(w * 0.045f));
        if (!hasSidePadding || boxW < Math.Max(24, w * 0.35f))
        {
            return false;
        }

        // Locate separator gap row above the bottom badge (e.g., Spotify wordmark bar)
        var innerL = Math.Min(rightCol, leftCol + Math.Max(2, (int)(boxW * 0.08f)));
        var innerR = Math.Max(innerL, rightCol - Math.Max(2, (int)(boxW * 0.08f)));
        var topRow = -1;
        var gapRow = -1;
        for (var y = 0; y < h; y += stepY)
        {
            var tot = 0;
            var fg = 0;
            for (var x = innerL; x <= innerR; x += stepX)
            {
                tot++;
                if (ColorDist(bitmap.GetPixel(x, y), bgR, bgG, bgB) > 28) fg++;
            }

            var frac = tot > 0 ? fg / (float)tot : 0f;
            if (topRow < 0)
            {
                if (frac >= 0.12f) topRow = y;
                continue;
            }

            if (y - topRow >= h * 0.34f && frac < 0.08f)
            {
                gapRow = y;
                break;
            }
        }

        var bottomLimit = gapRow > 0 ? gapRow : h;
        var squareTop = gapRow > 0 ? Math.Max(0, Math.Min(topRow, gapRow - boxW)) : Math.Max(0, topRow);
        var boxH = bottomLimit - squareTop;
        if (boxH < Math.Max(24, h * 0.35f))
        {
            return false;
        }

        var inset = Math.Clamp(Math.Min(boxW, boxH) / 96f, 1f, 3f);
        trimmed = new SKRect(leftCol + inset, squareTop + inset, rightCol + 1 - inset, bottomLimit - inset);
        return trimmed.Width >= 24f && trimmed.Height >= 24f;
    }

    private static SKBitmap? ExtractSubsetCopy(SKBitmap source, SKRectI rect)
    {
        using var subset = new SKBitmap();
        if (!source.ExtractSubset(subset, rect))
        {
            return null;
        }

        var copy = subset.Copy(source.ColorType);
        return copy;
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
