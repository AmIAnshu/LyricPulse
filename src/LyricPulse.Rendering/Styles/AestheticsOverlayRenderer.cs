using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Shared kinetic aesthetics layer providing:
/// 1) The luminous Color-Changing Highlight Sweep gliding across active words (<c>AccentColor1 -> 2 -> 3</c>),
/// 2) The playful Verci-style tilted inverted sticker cutout pill highlight for visual emphasis words, and
/// 3) The spring-driven glowing Following Pulse Dot hopping cleanly from word to word.
/// Zero per-frame SKPaint/SKImageFilter leaks; automatically suppressed in Reduced Motion.
/// </summary>
public sealed class AestheticsOverlayRenderer : IDisposable
{
    public static readonly SKColor CutoutCardFill = new(0xF8, 0xFA, 0xFC, 0xF5);   // #F5F8FAFC
    public static readonly RgbColor CutoutDarkInk = new(0x0B, 0x0C, 0x16);         // #0B0C16
    public static readonly SKColor CutoutShadowColor = new(0x05, 0x05, 0x10, 0x80);

    private readonly SKPaint _sweepPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _dotCorePaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _dotGlowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _cutoutShadowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _cutoutFillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _cutoutInkPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKImageFilter _dotBlur = SKImageFilter.CreateBlur(7.5f, 7.5f);
    private readonly SKImageFilter _sweepBlur = SKImageFilter.CreateBlur(9.0f, 9.0f);
    private readonly SKImageFilter _cutoutBlur = SKImageFilter.CreateBlur(6.0f, 6.0f);
    private readonly SKColor[] _sweepColors = new SKColor[3];
    private readonly float[] _sweepStops = [0.0f, 0.5f, 1.0f];

    private SpringState _dotX = SpringState.AtRest(0f);
    private SpringState _dotY = SpringState.AtRest(0f);
    private float _hopElapsedSec = 1f;
    private bool _hasDotPosition;
    private bool _disposed;

    public void ResetDot()
    {
        _dotX = SpringState.AtRest(0f);
        _dotY = SpringState.AtRest(0f);
        _hopElapsedSec = 1f;
        _hasDotPosition = false;
    }

    public bool AdvanceDot(float targetX, float targetY, float dt, bool reducedMotion, float wordDurationSec = 0.32f)
    {
        if (reducedMotion || !float.IsFinite(targetX) || !float.IsFinite(targetY))
        {
            _hasDotPosition = false;
            return false;
        }

        if (!_hasDotPosition)
        {
            _dotX = SpringState.AtRest(targetX);
            _dotY = SpringState.AtRest(targetY);
            _hopElapsedSec = 1f;
            _hasDotPosition = true;
            return false;
        }

        var dx = MathF.Abs(targetX - _dotX.Target);
        var dy = MathF.Abs(targetY - _dotY.Target);
        if (dx > 2f || dy > 2f)
        {
            _dotX = _dotX.WithTarget(targetX);
            _dotY = _dotY.WithTarget(targetY);
            _hopElapsedSec = 0f;
        }

        var spring = SpringTokens.WordSnap.WithTempoCompression(wordDurationSec);
        _dotX = _dotX.Step(spring, dt);
        _dotY = _dotY.Step(spring, dt);
        if (_hopElapsedSec < 0.26f)
        {
            _hopElapsedSec = Math.Min(0.26f, _hopElapsedSec + dt);
        }

        return !_dotX.IsAtRest || !_dotY.IsAtRest || _hopElapsedSec < 0.26f;
    }

    /// <summary>
    /// Draws a luminous color-changing highlight sweep ribbon & soft aura gliding left-to-right across
    /// the active word bounds (<paramref name="wordBounds"/>) driven by <paramref name="wordProgress"/> [0..1].
    /// </summary>
    public void DrawWordColorSweep(
        SKCanvas canvas,
        SKRect wordBounds,
        double wordProgress,
        FrameRenderPacket packet,
        float opacity = 1.0f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var clampedOpacity = Math.Clamp(float.IsFinite(opacity) ? opacity : 1f, 0f, 1f);
        if (!packet.ShowColorSweepLayer || clampedOpacity <= 0.004f || wordBounds.Width <= 1f || wordBounds.Height <= 1f)
        {
            return;
        }

        var progress = (float)Math.Clamp(double.IsFinite(wordProgress) ? wordProgress : 0.5, 0.0, 1.0);
        var energy = packet.SafePartyEnergy;

        var sweepX = wordBounds.Left + (wordBounds.Width * progress);
        var auraHalfW = Math.Max(wordBounds.Width * 0.34f, wordBounds.Height * 0.65f);
        var auraRect = new SKRect(
            Math.Max(wordBounds.Left - 8f, sweepX - auraHalfW),
            wordBounds.Top - 3f,
            Math.Min(wordBounds.Right + 8f, sweepX + auraHalfW),
            wordBounds.Bottom + 3f);

        var cStart = StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor1, packet.Palette.AccentColor2, progress);
        var cMid = StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor2, packet.Palette.AccentColor3, progress * 0.7f);
        var cEnd = StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor3, packet.Palette.AccentColor1, progress);

        var auraAlpha = Math.Clamp((0.18f + (0.08f * energy)) * clampedOpacity, 0f, 0.42f);
        _sweepColors[0] = cStart.ToSkColorWithAlpha(auraAlpha * 0.25f);
        _sweepColors[1] = cMid.ToSkColorWithAlpha(auraAlpha);
        _sweepColors[2] = cEnd.ToSkColorWithAlpha(auraAlpha * 0.25f);

        using var auraShader = SKShader.CreateLinearGradient(
            new SKPoint(wordBounds.Left, wordBounds.MidY),
            new SKPoint(wordBounds.Right, wordBounds.MidY),
            _sweepColors,
            _sweepStops,
            SKShaderTileMode.Clamp);

        _sweepPaint.Shader = auraShader;
        _sweepPaint.Color = SKColors.White;
        _sweepPaint.ImageFilter = _sweepBlur;
        try
        {
            canvas.DrawRoundRect(auraRect, auraRect.Height * 0.45f, auraRect.Height * 0.45f, _sweepPaint);
        }
        finally
        {
            _sweepPaint.ImageFilter = null;
            _sweepPaint.Shader = null;
        }

        // Crisp luminous sweep underline bar gliding across active word
        var filledW = Math.Max(4f, wordBounds.Width * Math.Max(progress, 0.14f));
        var barH = Math.Clamp(wordBounds.Height * 0.085f, 2.0f, 5.0f);
        var barRect = new SKRect(wordBounds.Left, wordBounds.Bottom + 3f, wordBounds.Left + filledW, wordBounds.Bottom + 3f + barH);

        _sweepColors[0] = packet.Palette.AccentColor1.ToSkColorWithAlpha(0.90f * clampedOpacity);
        _sweepColors[1] = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.95f * clampedOpacity);
        _sweepColors[2] = packet.Palette.AccentColor3.ToSkColorWithAlpha(0.70f * clampedOpacity);

        using var barShader = SKShader.CreateLinearGradient(
            new SKPoint(barRect.Left, barRect.MidY),
            new SKPoint(Math.Max(barRect.Right, barRect.Left + 2f), barRect.MidY),
            _sweepColors,
            _sweepStops,
            SKShaderTileMode.Clamp);

        _sweepPaint.Shader = barShader;
        _sweepPaint.Color = SKColors.White;
        try
        {
            canvas.DrawRoundRect(barRect, barH * 0.5f, barH * 0.5f, _sweepPaint);
        }
        finally
        {
            _sweepPaint.Shader = null;
        }
    }

    /// <summary>
    /// Draws a crisp Verci-inspired playful inverted sticker cutout pill behind a focal visual word
    /// (luminous <c>#F8FAFC</c> slightly tilted rounded card + deep <c>#0B0C16</c> text).
    /// </summary>
    public void DrawInvertedCutoutWord(
        SKCanvas canvas,
        in StyleRenderContext context,
        SKFont font,
        string text,
        float centerX,
        float centerY,
        float measuredWidth,
        float tiltDegrees,
        float opacity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var clampedOpacity = Math.Clamp(opacity, 0f, 1f);
        if (string.IsNullOrEmpty(text) || clampedOpacity <= 0.004f)
        {
            return;
        }

        var padX = Math.Max(font.Size * 0.30f, 10f);
        var padY = Math.Max(font.Size * 0.14f, 5f);
        var cardW = measuredWidth + (padX * 2f);
        var cardH = (font.Size * 1.12f) + (padY * 2f);
        var corner = Math.Clamp(font.Size * 0.24f, 6f, 18f);
        var cardRect = new SKRect(-cardW * 0.5f, -cardH * 0.5f, cardW * 0.5f, cardH * 0.5f);

        var save = canvas.Save();
        try
        {
            canvas.Translate(centerX, centerY);
            if (MathF.Abs(tiltDegrees) > 0.05f && !context.Packet.ReducedMotion)
            {
                canvas.RotateDegrees(tiltDegrees);
            }

            // Soft elevation shadow behind sticker cutout
            _cutoutShadowPaint.Color = LegibilityStyleSpec.ScaleAlpha(CutoutShadowColor, clampedOpacity);
            _cutoutShadowPaint.ImageFilter = _cutoutBlur;
            try
            {
                var shadowRect = cardRect;
                shadowRect.Offset(0f, 3f);
                canvas.DrawRoundRect(shadowRect, corner, corner, _cutoutShadowPaint);
            }
            finally
            {
                _cutoutShadowPaint.ImageFilter = null;
            }

            _cutoutFillPaint.Color = LegibilityStyleSpec.ScaleAlpha(CutoutCardFill, clampedOpacity);
            canvas.DrawRoundRect(cardRect, corner, corner, _cutoutFillPaint);

            font.GetFontMetrics(out var metrics);
            var baselineY = -((metrics.Ascent + metrics.Descent) * 0.5f);

            _cutoutInkPaint.Color = CutoutDarkInk.ToSkColorWithAlpha(clampedOpacity);
            canvas.DrawText(text, 0f, baselineY, SKTextAlign.Center, font, _cutoutInkPaint);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    /// <summary>
    /// Draws the spring-driven glowing Following Pulse Dot at the current interpolated or explicit
    /// word anchor point. Suppressed automatically when Reduced Motion is active or disabled in settings.
    /// </summary>
    public void DrawFollowingDot(
        SKCanvas canvas,
        FrameRenderPacket packet,
        float fallbackX,
        float fallbackY,
        float baseRadiusDip,
        float opacity = 1.0f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var clampedOpacity = Math.Clamp(opacity, 0f, 1f);
        if (packet.ReducedMotion || !packet.ShowFollowingDot || clampedOpacity <= 0.004f)
        {
            return;
        }

        var x = _hasDotPosition ? _dotX.Value : fallbackX;
        var y = _hasDotPosition ? _dotY.Value : fallbackY;

        // Subtle parabolic arc hop on word onset
        var hopT = Math.Clamp(_hopElapsedSec / 0.26f, 0f, 1f);
        var hopArc = MathF.Sin(hopT * MathF.PI) * baseRadiusDip * 2.2f * packet.SafePartyEnergy;
        var drawY = y - hopArc;

        var pulseScale = 1.0f + (0.28f * MathF.Sin(hopT * MathF.PI) * packet.SafePartyEnergy);
        var radius = Math.Clamp(baseRadiusDip * pulseScale, 2.5f, 12f);

        // Soft outer bloom halo
        _dotGlowPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.72f * clampedOpacity);
        _dotGlowPaint.ImageFilter = _dotBlur;
        try
        {
            canvas.DrawCircle(x, drawY, radius * 2.15f, _dotGlowPaint);
        }
        finally
        {
            _dotGlowPaint.ImageFilter = null;
        }

        // Mid accent ring
        _dotCorePaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(0.95f * clampedOpacity);
        canvas.DrawCircle(x, drawY, radius, _dotCorePaint);

        // Crisp white-hot center specular
        _dotCorePaint.Color = new SKColor(0xFF, 0xFF, 0xFF, (byte)Math.Clamp(MathF.Round(235f * clampedOpacity), 0f, 255f));
        canvas.DrawCircle(x, drawY, radius * 0.48f, _dotCorePaint);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dotBlur.Dispose();
        _sweepBlur.Dispose();
        _cutoutBlur.Dispose();
        _sweepPaint.Dispose();
        _dotCorePaint.Dispose();
        _dotGlowPaint.Dispose();
        _cutoutShadowPaint.Dispose();
        _cutoutFillPaint.Dispose();
        _cutoutInkPaint.Dispose();
    }
}
