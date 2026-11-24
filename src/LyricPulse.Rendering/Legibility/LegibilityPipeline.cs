using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using SkiaSharp;

namespace LyricPulse.Rendering.Legibility;

/// <summary>
/// 4-Layer Desktop Overlay Legibility Pipeline:
/// Layer 0 (Adaptive Radial Ambient Scrim or High-Contrast Scrim),
/// Layer 1 (Cached Dual-Pass Contact + Ambient Drop Shadow),
/// Layer 2 (Optical Round-Join Stroke Halo drawn strictly under fill with dark-fill inversion),
/// Layer 3 (Antialiased Premultiplied Fill + optional active glow).
/// </summary>
public sealed class LegibilityPipeline : IDisposable
{
    public const int MaxCachedShadowBuckets = 64;

    private static readonly float[] RadialStopPositions = [0.00f, 0.52f, 0.82f, 1.00f];

    private readonly Dictionary<ShadowBucketKey, ShadowFilterPair> _shadowCache = new(MaxCachedShadowBuckets);
    private readonly SKPaint _scrimPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _shadowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _haloPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeJoin = SKStrokeJoin.Round,
        StrokeCap = SKStrokeCap.Round,
        StrokeMiter = 2.0f
    };
    private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _glowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _layerOpacityPaint = new() { IsAntialias = true };
    private readonly SKColor[] _radialStopColors = new SKColor[4];

    private bool _disposed;

    public int CachedShadowBucketCount => _shadowCache.Count;

    /// <summary>
    /// Applies the user's global overlay opacity once via an outer SaveLayer so semi-transparent
    /// opacity never exposes Layer 2 stroke halos through Layer 3 glyph fills.
    /// </summary>
    public int BeginGlobalOpacityLayer(SKCanvas canvas, float globalOpacity)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var clamped = Math.Clamp(float.IsFinite(globalOpacity) ? globalOpacity : 1f, 0f, 1f);
        if (clamped >= 0.999f)
        {
            return canvas.Save();
        }

        var alphaByte = (byte)Math.Clamp(MathF.Round(clamped * 255f), 0f, 255f);
        _layerOpacityPaint.Color = SKColors.White.WithAlpha(alphaByte);
        return canvas.SaveLayer(_layerOpacityPaint);
    }

    public static void EndGlobalOpacityLayer(SKCanvas canvas, int saveCount)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        canvas.RestoreToCount(saveCount);
    }

    /// <summary>
    /// Layer 0: Draws the feathered elliptical radial ambient scrim (or High-Contrast #CC08090D pill backdrop).
    /// </summary>
    public void DrawAmbientScrim(
        SKCanvas canvas,
        SKRect clusterBounds,
        float fontSizeDip,
        RgbColor albumScrimTint,
        ScrimIntensity intensity,
        bool highContrastScrim,
        float opacity = 1f)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var alpha = LegibilityStyleSpec.GetScrimAlpha(intensity, highContrastScrim) * Math.Clamp(opacity, 0f, 1f);
        if (alpha <= 0.001f || clusterBounds.Width <= 0f || clusterBounds.Height <= 0f)
        {
            return;
        }

        if (highContrastScrim)
        {
            var padX = Math.Max(fontSizeDip * 0.55f, 18f);
            var padY = Math.Max(fontSizeDip * 0.35f, 12f);
            var pillRect = SKRect.Inflate(clusterBounds, padX, padY);
            var cornerRadius = Math.Clamp(fontSizeDip * 0.35f, 10f, 24f);

            _scrimPaint.Shader = null;
            _scrimPaint.Color = LegibilityStyleSpec.ScaleAlpha(LegibilityStyleSpec.HighContrastScrimColor, opacity);
            canvas.DrawRoundRect(pillRect, cornerRadius, cornerRadius, _scrimPaint);
            return;
        }

        var (rx, ry) = LegibilityStyleSpec.ComputeScrimRadii(clusterBounds, fontSizeDip);
        if (rx <= 1f || ry <= 1f)
        {
            return;
        }

        var baseColor = new SKColor(albumScrimTint.R, albumScrimTint.G, albumScrimTint.B, 255);
        _radialStopColors[0] = LegibilityStyleSpec.ScaleAlpha(baseColor, alpha);
        _radialStopColors[1] = LegibilityStyleSpec.ScaleAlpha(baseColor, alpha * 0.58f);
        _radialStopColors[2] = LegibilityStyleSpec.ScaleAlpha(baseColor, alpha * 0.14f);
        _radialStopColors[3] = baseColor.WithAlpha(0);

        var ellipseMatrix = SKMatrix.CreateScale(1f, ry / rx);
        var center = new SKPoint(clusterBounds.MidX, clusterBounds.MidY);
        var localTransform = SKMatrix.Concat(
            SKMatrix.CreateTranslation(center.X, center.Y),
            ellipseMatrix);

        using var shader = SKShader.CreateRadialGradient(
            SKPoint.Empty,
            rx,
            _radialStopColors,
            RadialStopPositions,
            SKShaderTileMode.Clamp,
            localTransform);

        _scrimPaint.Shader = shader;
        _scrimPaint.Color = SKColors.White;

        try
        {
            canvas.DrawOval(center.X, center.Y, rx, ry, _scrimPaint);
        }
        finally
        {
            _scrimPaint.Shader = null;
        }
    }

    /// <summary>
    /// Layers 1 -> 2 -> 3: Renders crisp legible text with dual-pass cached shadows,
    /// round-join optical stroke halo under fill (with dark-fill auto-inversion), and antialiased fill.
    /// </summary>
    public void DrawLegibleText(
        SKCanvas canvas,
        string text,
        float x,
        float y,
        SKTextAlign align,
        SKFont font,
        LyricFontId fontId,
        RgbColor fillColor,
        float opacity = 1f,
        RgbColor? activeGlowColor = null,
        bool includeStrokeHalo = true)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(font);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var clampedOpacity = Math.Clamp(float.IsFinite(opacity) ? opacity : 1f, 0f, 1f);
        if (string.IsNullOrEmpty(text) || clampedOpacity <= 0.002f)
        {
            return;
        }

        // Layer 1: Dual-pass cached Contact + Ambient Drop Shadow.
        var shadows = GetOrCreateShadowFilters(font.Size, clampedOpacity);
        DrawShadowPass(canvas, text, x, y, align, font, shadows.AmbientShadow);
        DrawShadowPass(canvas, text, x, y, align, font, shadows.ContactShadow);

        // Optional subtle active bloom glow (Layer 2.5 behind halo/fill).
        if (activeGlowColor is { } glowColor)
        {
            using var blurFilter = SKImageFilter.CreateBlur(8.0f, 8.0f);
            _glowPaint.Color = glowColor.ToSkColorWithAlpha(0.28f * clampedOpacity);
            _glowPaint.ImageFilter = blurFilter;
            try
            {
                canvas.DrawText(text, x, y, align, font, _glowPaint);
            }
            finally
            {
                _glowPaint.ImageFilter = null;
            }
        }

        // Layer 2: Optical Round-Join Stroke Halo (strictly UNDER fill).
        if (includeStrokeHalo)
        {
            var strokeWidth = LegibilityStyleSpec.ComputeOpticalStrokeWidth(font.Size, fontId);
            if (strokeWidth > 0f)
            {
                _haloPaint.StrokeWidth = strokeWidth;
                _haloPaint.Color = LegibilityStyleSpec.ResolveOpticalHaloColor(fillColor, clampedOpacity);
                canvas.DrawText(text, x, y, align, font, _haloPaint);
            }
        }

        // Layer 3: Contrast-Guarded Premultiplied Antialias Fill.
        _fillPaint.Color = fillColor.ToSkColorWithAlpha(clampedOpacity);
        canvas.DrawText(text, x, y, align, font, _fillPaint);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ClearShadowCache();
        _scrimPaint.Dispose();
        _shadowPaint.Dispose();
        _haloPaint.Dispose();
        _fillPaint.Dispose();
        _glowPaint.Dispose();
        _layerOpacityPaint.Dispose();
    }

    private void DrawShadowPass(
        SKCanvas canvas,
        string text,
        float x,
        float y,
        SKTextAlign align,
        SKFont font,
        SKImageFilter filter)
    {
        _shadowPaint.Color = SKColors.Black;
        _shadowPaint.ImageFilter = filter;
        try
        {
            canvas.DrawText(text, x, y, align, font, _shadowPaint);
        }
        finally
        {
            _shadowPaint.ImageFilter = null;
        }
    }

    private ShadowFilterPair GetOrCreateShadowFilters(float fontSizeDip, float opacity)
    {
        var fontSizeBucket = Math.Clamp((int)MathF.Round(fontSizeDip / 8f) * 8, 16, 160);
        var opacityBucket = Math.Clamp((int)MathF.Round(opacity * 20f), 1, 20);
        var key = new ShadowBucketKey(fontSizeBucket, opacityBucket);

        if (_shadowCache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        if (_shadowCache.Count >= MaxCachedShadowBuckets)
        {
            ClearShadowCache();
        }

        var bucketOpacity = opacityBucket / 20f;
        var sizeScale = Math.Clamp(fontSizeBucket / 48f, 0.70f, 1.45f);

        var contactColor = LegibilityStyleSpec.ScaleAlpha(LegibilityStyleSpec.ContactShadowBaseColor, bucketOpacity);
        var ambientColor = LegibilityStyleSpec.ScaleAlpha(LegibilityStyleSpec.AmbientShadowBaseColor, bucketOpacity);

        var contactFilter = SKImageFilter.CreateDropShadowOnly(
            dx: 0f,
            dy: LegibilityStyleSpec.ContactShadowDyDip * sizeScale,
            sigmaX: LegibilityStyleSpec.ContactShadowSigmaDip * sizeScale,
            sigmaY: LegibilityStyleSpec.ContactShadowSigmaDip * sizeScale,
            color: contactColor);

        var ambientFilter = SKImageFilter.CreateDropShadowOnly(
            dx: 0f,
            dy: LegibilityStyleSpec.AmbientShadowDyDip * sizeScale,
            sigmaX: LegibilityStyleSpec.AmbientShadowSigmaDip * sizeScale,
            sigmaY: LegibilityStyleSpec.AmbientShadowSigmaDip * sizeScale,
            color: ambientColor);

        var pair = new ShadowFilterPair(contactFilter, ambientFilter);
        _shadowCache[key] = pair;
        return pair;
    }

    private void ClearShadowCache()
    {
        foreach (var pair in _shadowCache.Values)
        {
            pair.Dispose();
        }

        _shadowCache.Clear();
    }

    private readonly record struct ShadowBucketKey(int FontSizeBucket, int OpacityBucket);

    private sealed record ShadowFilterPair(SKImageFilter ContactShadow, SKImageFilter AmbientShadow) : IDisposable
    {
        public void Dispose()
        {
            ContactShadow.Dispose();
            AmbientShadow.Dispose();
        }
    }
}
