using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// High-craft reactive visual illustration engine replacing basic system emojis with
/// animated procedural vector mini-illustrations for signature lyric motifs (<c>fire</c>,
/// <c>heart</c>, <c>heart_broken</c>, <c>water</c>, <c>moon</c>, <c>sun</c>, <c>star</c>,
/// <c>lightning</c>, <c>rain</c>, <c>music</c>, <c>clock</c>, <c>crown</c>, <c>paper_plane</c>,
/// <c>eye</c>, <c>diamond</c>, <c>flower</c>), and luminous duotone vector glyphs for all remaining concepts.
/// Zero per-frame SKPaint/SKPath allocations; respects Reduced Motion cleanly.
/// </summary>
public sealed class ReactiveVisualIconRenderer : IDisposable
{
    private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _glowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKImageFilter _glowBlur = SKImageFilter.CreateBlur(8.0f, 8.0f);
    private readonly SKPath _path = new();
    private readonly SKPath _innerPath = new();
    private readonly SKColor[] _gradColors = new SKColor[2];
    private readonly float[] _gradStops = [0f, 1f];
    private bool _disposed;

    public void DrawVisual(
        in StyleRenderContext context,
        WordVisualMatch visual,
        float centerX,
        float centerY,
        float iconSizeDip,
        float opacity,
        bool isActive) =>
        DrawVisual(context.Canvas, in context, visual, centerX, centerY, iconSizeDip, opacity, isActive);

    public void DrawVisual(
        SKCanvas canvas,
        in StyleRenderContext context,
        WordVisualMatch visual,
        float centerX,
        float centerY,
        float iconSizeDip,
        float opacity,
        bool isActive)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var clampedOpacity = Math.Clamp(opacity, 0f, 1f);
        if (clampedOpacity <= 0.004f || iconSizeDip <= 1f)
        {
            return;
        }

        var packet = context.Packet;
        var audio = packet.EffectiveAudioMetrics;
        var reducedMotion = packet.ReducedMotion;
        var t = reducedMotion ? 0f : (float)packet.RenderTimeSeconds;
        var beatScale = (isActive && !reducedMotion) ? (1.0f + (audio.BeatPulse * 0.14f)) : 1.0f;

        // Check if caller requested explicit EmojiOnly mode
        if (packet.PlaybackSnapshot?.Settings.VisualPreference == VisualPreference.EmojiOnly)
        {
            StyleLayoutHelper.DrawVisualGlyph(
                canvas,
                context.Fonts,
                context.Legibility,
                visual,
                centerX,
                centerY,
                iconSizeDip * beatScale,
                packet.Palette.AccentColor1,
                clampedOpacity);
            return;
        }

        var c1 = packet.Palette.AccentColor1;
        var c2 = packet.Palette.AccentColor2;
        var radius = iconSizeDip * 0.46f * beatScale;

        // Soft glowing aura behind active reactive vectors
        if (isActive && !reducedMotion)
        {
            _glowPaint.Color = c2.ToSkColorWithAlpha(0.32f * clampedOpacity * (0.75f + audio.BeatPulse * 0.45f));
            _glowPaint.ImageFilter = _glowBlur;
            try
            {
                canvas.DrawCircle(centerX, centerY, radius * 1.15f, _glowPaint);
            }
            finally
            {
                _glowPaint.ImageFilter = null;
            }
        }

        if (TryDrawProceduralIllustration(
                canvas,
                visual.ConceptName,
                centerX,
                centerY,
                radius,
                t,
                audio.BeatPulse,
                c1,
                c2,
                clampedOpacity,
                isActive,
                reducedMotion))
        {
            return;
        }

        // Luminous duotone vector glyph from FluentSystemIcons-Filled.ttf (never a flat grey box!)
        var glyphTint = isActive ? c1 : StyleLayoutHelper.BlendSrgb(c1, packet.Palette.AccentColor3, 0.45f);
        StyleLayoutHelper.DrawVisualGlyph(
            canvas,
            context.Fonts,
            context.Legibility,
            visual,
            centerX,
            centerY,
            iconSizeDip * beatScale,
            glyphTint,
            clampedOpacity);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fillPaint.Dispose();
        _strokePaint.Dispose();
        _glowPaint.Dispose();
        _glowBlur.Dispose();
        _path.Dispose();
        _innerPath.Dispose();
    }

    private bool TryDrawProceduralIllustration(
        SKCanvas canvas,
        string conceptName,
        float cx,
        float cy,
        float r,
        float t,
        float beatPulse,
        RgbColor c1,
        RgbColor c2,
        float opacity,
        bool isActive,
        bool reducedMotion)
    {
        var key = (conceptName ?? string.Empty).Trim().ToLowerInvariant();
        switch (key)
        {
            case "fire" or "flame" or "burn" or "hot" or "ember":
                DrawFlame(canvas, cx, cy, r, t, beatPulse, opacity, reducedMotion);
                return true;
            case "heart" or "love" or "lover" or "kiss" or "crush" or "soul":
                DrawHeart(canvas, cx, cy, r, t, beatPulse, c2, opacity, isActive);
                return true;
            case "heart_broken" or "broken" or "heartbreak" or "hurt":
                DrawBrokenHeart(canvas, cx, cy, r, t, c2, opacity, reducedMotion);
                return true;
            case "water" or "wave" or "ocean" or "sea" or "river" or "swim":
                DrawWaves(canvas, cx, cy, r, t, c1, c2, opacity, reducedMotion);
                return true;
            case "moon" or "night" or "midnight" or "luna" or "dream":
                DrawCrescentMoon(canvas, cx, cy, r, t, c1, opacity, reducedMotion);
                return true;
            case "sun" or "light" or "dawn" or "sunrise" or "gold" or "bright":
                DrawSun(canvas, cx, cy, r, t, opacity, reducedMotion);
                return true;
            case "star" or "stars" or "sparkle" or "shine" or "magic" or "sky":
                DrawSparkleStar(canvas, cx, cy, r, t, beatPulse, c1, c2, opacity, reducedMotion);
                return true;
            case "lightning" or "thunder" or "storm" or "electric" or "flash" or "shock":
                DrawLightning(canvas, cx, cy, r, beatPulse, opacity);
                return true;
            case "rain" or "tears" or "cry" or "cloud":
                DrawRain(canvas, cx, cy, r, t, c1, opacity, reducedMotion);
                return true;
            case "music" or "song" or "melody" or "dance" or "rhythm" or "beat" or "sing":
                DrawMusicNote(canvas, cx, cy, r, t, beatPulse, c1, c2, opacity, reducedMotion);
                return true;
            case "clock" or "time" or "hour" or "wait" or "minute":
                DrawClock(canvas, cx, cy, r, t, c1, c2, opacity, reducedMotion);
                return true;
            case "crown" or "king" or "queen" or "royal" or "throne":
                DrawCrown(canvas, cx, cy, r, opacity);
                return true;
            case "paper_plane" or "plane" or "fly" or "wings" or "wind" or "ship" or "paper":
                DrawPaperPlane(canvas, cx, cy, r, t, c1, c2, opacity, reducedMotion);
                return true;
            case "eye" or "eyes" or "look" or "see" or "watch":
                DrawEye(canvas, cx, cy, r, t, c1, c2, opacity, reducedMotion);
                return true;
            case "diamond" or "gem" or "coin" or "money" or "ring" or "crystal":
                DrawDiamond(canvas, cx, cy, r, t, c1, c2, opacity, reducedMotion);
                return true;
            case "flower" or "rose" or "bloom" or "petal":
                DrawFlower(canvas, cx, cy, r, t, c2, opacity, reducedMotion);
                return true;
            case "paper" or "newspaper" or "letter" or "page" or "book" or "note" or "write":
                DrawPaperSheet(canvas, cx, cy, r, t, c1, c2, opacity, reducedMotion);
                return true;
            case "butterfly" or "wings" or "angel" or "bird" or "feather":
                DrawButterfly(canvas, cx, cy, r, t, beatPulse, c1, c2, opacity, reducedMotion);
                return true;
            case "planet" or "world" or "earth" or "universe" or "space" or "galaxy" or "orbit":
                DrawPlanet(canvas, cx, cy, r, t, c1, c2, opacity, reducedMotion);
                return true;
            case "car" or "drive" or "road" or "ride" or "speed" or "wheel" or "street":
                DrawCar(canvas, cx, cy, r, t, beatPulse, c1, c2, opacity, reducedMotion);
                return true;
            case "ghost" or "shadow" or "phantom" or "spirit" or "haunt":
                DrawGhost(canvas, cx, cy, r, t, c1, c2, opacity, reducedMotion);
                return true;
            case "wine" or "glass" or "drink" or "cup" or "champagne" or "bottle":
                DrawGlass(canvas, cx, cy, r, t, c1, c2, opacity, reducedMotion);
                return true;
            case "key" or "lock" or "unlock" or "door" or "secret":
                DrawKey(canvas, cx, cy, r, t, c1, c2, opacity, reducedMotion);
                return true;
            case "sword" or "knife" or "blade" or "cut" or "fight":
                DrawSword(canvas, cx, cy, r, t, c1, c2, opacity, reducedMotion);
                return true;
            default:
                return false;
        }
    }

    private void DrawFlame(SKCanvas canvas, float cx, float cy, float r, float t, float beatPulse, float opacity, bool reducedMotion)
    {
        var sway = reducedMotion ? 0f : MathF.Sin(t * 7.2f) * r * 0.14f;
        var tipH = r * (1.18f + (beatPulse * 0.18f));
        _path.Rewind();
        _path.MoveTo(cx + sway, cy - tipH);
        _path.CubicTo(cx + r * 0.95f, cy - r * 0.25f, cx + r * 0.92f, cy + r * 0.85f, cx, cy + r * 0.95f);
        _path.CubicTo(cx - r * 0.92f, cy + r * 0.85f, cx - r * 0.95f, cy - r * 0.25f, cx + sway, cy - tipH);
        _path.Close();

        _gradColors[0] = new SKColor(0xFF, 0xE1, 0x4D, (byte)(250 * opacity));
        _gradColors[1] = new SKColor(0xF9, 0x3B, 0x1D, (byte)(250 * opacity));
        using var grad = SKShader.CreateLinearGradient(new SKPoint(cx, cy - tipH), new SKPoint(cx, cy + r), _gradColors, _gradStops, SKShaderTileMode.Clamp);
        _fillPaint.Shader = grad;
        _fillPaint.Color = SKColors.White;
        try
        {
            canvas.DrawPath(_path, _fillPaint);
        }
        finally
        {
            _fillPaint.Shader = null;
        }

        _innerPath.Rewind();
        _innerPath.MoveTo(cx - (sway * 0.5f), cy - (tipH * 0.35f));
        _innerPath.CubicTo(cx + r * 0.45f, cy + r * 0.10f, cx + r * 0.42f, cy + r * 0.72f, cx, cy + r * 0.82f);
        _innerPath.CubicTo(cx - r * 0.42f, cy + r * 0.72f, cx - r * 0.45f, cy + r * 0.10f, cx - (sway * 0.5f), cy - (tipH * 0.35f));
        _innerPath.Close();
        _fillPaint.Color = new SKColor(0xFF, 0xFA, 0xC2, (byte)(235 * opacity));
        canvas.DrawPath(_innerPath, _fillPaint);
    }

    private void DrawHeart(SKCanvas canvas, float cx, float cy, float r, float t, float beatPulse, RgbColor accent, float opacity, bool isActive)
    {
        var lubDub = isActive ? (1f + MathF.Max(0f, MathF.Sin(t * 6.5f)) * 0.08f + (beatPulse * 0.12f)) : 1f;
        var s = r * lubDub;
        _path.Rewind();
        _path.MoveTo(cx, cy + (s * 0.88f));
        _path.CubicTo(cx - s * 1.25f, cy + s * 0.12f, cx - s * 0.98f, cy - s * 0.92f, cx, cy - s * 0.28f);
        _path.CubicTo(cx + s * 0.98f, cy - s * 0.92f, cx + s * 1.25f, cy + s * 0.12f, cx, cy + (s * 0.88f));
        _path.Close();

        _gradColors[0] = new SKColor(0xFF, 0x5E, 0x8E, (byte)(255 * opacity));
        _gradColors[1] = StyleLayoutHelper.BlendSrgb(new RgbColor(0xE1, 0x1D, 0x48), accent, 0.35f).ToSkColorWithAlpha(opacity);
        using var grad = SKShader.CreateLinearGradient(new SKPoint(cx, cy - s), new SKPoint(cx, cy + s), _gradColors, _gradStops, SKShaderTileMode.Clamp);
        _fillPaint.Shader = grad;
        _fillPaint.Color = SKColors.White;
        try
        {
            canvas.DrawPath(_path, _fillPaint);
        }
        finally
        {
            _fillPaint.Shader = null;
        }
    }

    private void DrawBrokenHeart(SKCanvas canvas, float cx, float cy, float r, float t, RgbColor accent, float opacity, bool reducedMotion)
    {
        var jitter = reducedMotion ? 0f : MathF.Sin(t * 9f) * r * 0.05f;
        DrawHeart(canvas, cx - jitter, cy, r * 0.95f, t, 0f, accent, opacity * 0.92f, false);
        _strokePaint.StrokeWidth = Math.Max(1.6f, r * 0.16f);
        _strokePaint.Color = new SKColor(0x0B, 0x0C, 0x16, (byte)(240 * opacity));
        _path.Rewind();
        _path.MoveTo(cx, cy - r * 0.30f);
        _path.LineTo(cx - r * 0.18f, cy + r * 0.05f);
        _path.LineTo(cx + r * 0.14f, cy + r * 0.35f);
        _path.LineTo(cx - r * 0.04f, cy + r * 0.88f);
        canvas.DrawPath(_path, _strokePaint);
    }

    private void DrawWaves(SKCanvas canvas, float cx, float cy, float r, float t, RgbColor c1, RgbColor c2, float opacity, bool reducedMotion)
    {
        var cWave = StyleLayoutHelper.BlendSrgb(new RgbColor(0x38, 0xBD, 0xF8), c1, 0.35f);
        _strokePaint.StrokeWidth = Math.Max(1.8f, r * 0.22f);
        for (var row = -1; row <= 1; row++)
        {
            var yBase = cy + (row * r * 0.44f);
            var phase = reducedMotion ? row : (t * 4.5f) + (row * 1.1f);
            _path.Rewind();
            _path.MoveTo(cx - r * 0.85f, yBase + MathF.Sin(phase) * r * 0.14f);
            _path.CubicTo(
                cx - r * 0.28f, yBase - r * 0.26f + MathF.Cos(phase) * r * 0.12f,
                cx + r * 0.28f, yBase + r * 0.26f - MathF.Sin(phase) * r * 0.12f,
                cx + r * 0.85f, yBase - MathF.Sin(phase) * r * 0.14f);
            _strokePaint.Color = (row == 0 ? cWave : c2).ToSkColorWithAlpha(opacity * (row == 0 ? 0.98f : 0.78f));
            canvas.DrawPath(_path, _strokePaint);
        }
    }

    private void DrawCrescentMoon(SKCanvas canvas, float cx, float cy, float r, float t, RgbColor c1, float opacity, bool reducedMotion)
    {
        var glowColor = StyleLayoutHelper.BlendSrgb(new RgbColor(0xFD, 0xE6, 0x8A), c1, 0.35f);
        _fillPaint.Shader = null;
        _fillPaint.Color = glowColor.ToSkColorWithAlpha(opacity);
        _path.Rewind();
        _path.AddCircle(cx, cy, r * 0.84f);
        _innerPath.Rewind();
        _innerPath.AddCircle(cx + r * 0.38f, cy - r * 0.22f, r * 0.68f);
        using var crescent = _path.Op(_innerPath, SKPathOp.Difference);
        if (crescent is not null)
        {
            canvas.DrawPath(crescent, _fillPaint);
        }

        // Tiny twinkling companion star
        var starScale = reducedMotion ? 0.30f : (0.24f + (0.10f * MathF.Sin(t * 5.2f)));
        DrawSparkleStar(canvas, cx + r * 0.56f, cy - r * 0.46f, r * starScale, t, 0f, c1, c1, opacity, reducedMotion);
    }

    private void DrawSun(SKCanvas canvas, float cx, float cy, float r, float t, float opacity, bool reducedMotion)
    {
        var raysRot = reducedMotion ? 0f : t * 0.65f;
        _strokePaint.StrokeWidth = Math.Max(1.6f, r * 0.16f);
        _strokePaint.Color = new SKColor(0xFD, 0xBA, 0x45, (byte)(240 * opacity));
        for (var i = 0; i < 8; i++)
        {
            var a = raysRot + ((i / 8f) * MathF.Tau);
            canvas.DrawLine(
                cx + MathF.Cos(a) * r * 0.58f,
                cy + MathF.Sin(a) * r * 0.58f,
                cx + MathF.Cos(a) * r * 0.94f,
                cy + MathF.Sin(a) * r * 0.94f,
                _strokePaint);
        }

        _fillPaint.Shader = null;
        _fillPaint.Color = new SKColor(0xFE, 0xE1, 0x5A, (byte)(255 * opacity));
        canvas.DrawCircle(cx, cy, r * 0.42f, _fillPaint);
    }

    private void DrawSparkleStar(SKCanvas canvas, float cx, float cy, float r, float t, float beatPulse, RgbColor c1, RgbColor c2, float opacity, bool reducedMotion)
    {
        var s = r * (1f + (reducedMotion ? 0f : beatPulse * 0.18f));
        var inner = s * 0.24f;
        _path.Rewind();
        _path.MoveTo(cx, cy - s);
        _path.QuadTo(cx + inner, cy - inner, cx + s, cy);
        _path.QuadTo(cx + inner, cy + inner, cx, cy + s);
        _path.QuadTo(cx - inner, cy + inner, cx - s, cy);
        _path.QuadTo(cx - inner, cy - inner, cx, cy - s);
        _path.Close();

        _gradColors[0] = SKColors.White.WithAlpha((byte)(255 * opacity));
        _gradColors[1] = StyleLayoutHelper.BlendSrgb(c1, c2, 0.5f).ToSkColorWithAlpha(opacity);
        using var grad = SKShader.CreateRadialGradient(new SKPoint(cx, cy), Math.Max(s, 1f), _gradColors, _gradStops, SKShaderTileMode.Clamp);
        _fillPaint.Shader = grad;
        _fillPaint.Color = SKColors.White;
        try
        {
            canvas.DrawPath(_path, _fillPaint);
        }
        finally
        {
            _fillPaint.Shader = null;
        }
    }

    private void DrawLightning(SKCanvas canvas, float cx, float cy, float r, float beatPulse, float opacity)
    {
        _path.Rewind();
        _path.MoveTo(cx + r * 0.14f, cy - r * 0.98f);
        _path.LineTo(cx - r * 0.48f, cy + r * 0.08f);
        _path.LineTo(cx - r * 0.04f, cy + r * 0.08f);
        _path.LineTo(cx - r * 0.20f, cy + r * 0.98f);
        _path.LineTo(cx + r * 0.52f, cy - r * 0.12f);
        _path.LineTo(cx + r * 0.06f, cy - r * 0.12f);
        _path.Close();

        _fillPaint.Shader = null;
        var alpha = Math.Clamp((0.88f + beatPulse * 0.12f) * opacity, 0f, 1f);
        _fillPaint.Color = new SKColor(0xFD, 0xE0, 0x47, (byte)(255 * alpha));
        canvas.DrawPath(_path, _fillPaint);
    }

    private void DrawRain(SKCanvas canvas, float cx, float cy, float r, float t, RgbColor c1, float opacity, bool reducedMotion)
    {
        var dropColor = StyleLayoutHelper.BlendSrgb(new RgbColor(0x60, 0xA5, 0xFA), c1, 0.30f);
        _fillPaint.Shader = null;
        _fillPaint.Color = new SKColor(0xE2, 0xE8, 0xF0, (byte)(220 * opacity));
        canvas.DrawCircle(cx - r * 0.25f, cy - r * 0.20f, r * 0.34f, _fillPaint);
        canvas.DrawCircle(cx + r * 0.16f, cy - r * 0.28f, r * 0.40f, _fillPaint);
        canvas.DrawRoundRect(cx - r * 0.58f, cy - r * 0.22f, r * 1.16f, r * 0.36f, r * 0.18f, r * 0.18f, _fillPaint);

        _strokePaint.StrokeWidth = Math.Max(1.4f, r * 0.15f);
        _strokePaint.Color = dropColor.ToSkColorWithAlpha(opacity);
        for (var i = -1; i <= 1; i++)
        {
            var offset = reducedMotion ? 0f : ((t * 2.2f + (i * 0.3f)) % 1.0f) * r * 0.28f;
            var dx = cx + (i * r * 0.36f);
            var dy = cy + r * 0.28f + offset;
            canvas.DrawLine(dx, dy, dx - r * 0.14f, dy + r * 0.34f, _strokePaint);
        }
    }

    private void DrawMusicNote(SKCanvas canvas, float cx, float cy, float r, float t, float beatPulse, RgbColor c1, RgbColor c2, float opacity, bool reducedMotion)
    {
        var bob = reducedMotion ? 0f : MathF.Sin(t * 5.8f) * r * 0.08f;
        var yShift = cy - bob - (beatPulse * r * 0.08f);
        _fillPaint.Shader = null;
        _fillPaint.Color = StyleLayoutHelper.BlendSrgb(c1, c2, 0.35f).ToSkColorWithAlpha(opacity);
        canvas.DrawOval(cx - r * 0.42f, yShift + r * 0.42f, r * 0.24f, r * 0.18f, _fillPaint);
        canvas.DrawOval(cx + r * 0.36f, yShift + r * 0.28f, r * 0.24f, r * 0.18f, _fillPaint);

        _strokePaint.StrokeWidth = Math.Max(1.6f, r * 0.16f);
        _strokePaint.Color = _fillPaint.Color;
        canvas.DrawLine(cx - r * 0.22f, yShift + r * 0.40f, cx - r * 0.22f, yShift - r * 0.56f, _strokePaint);
        canvas.DrawLine(cx + r * 0.56f, yShift + r * 0.26f, cx + r * 0.56f, yShift - r * 0.70f, _strokePaint);
        canvas.DrawLine(cx - r * 0.22f, yShift - r * 0.56f, cx + r * 0.56f, yShift - r * 0.70f, _strokePaint);
    }

    private void DrawClock(SKCanvas canvas, float cx, float cy, float r, float t, RgbColor c1, RgbColor c2, float opacity, bool reducedMotion)
    {
        _strokePaint.StrokeWidth = Math.Max(1.5f, r * 0.15f);
        _strokePaint.Color = c1.ToSkColorWithAlpha(opacity * 0.92f);
        canvas.DrawCircle(cx, cy, r * 0.78f, _strokePaint);

        var minuteAng = (reducedMotion ? 0f : t * 2.2f) - (MathF.PI * 0.5f);
        var hourAng = (reducedMotion ? 0.5f : t * 0.3f) - (MathF.PI * 0.2f);
        _strokePaint.Color = c2.ToSkColorWithAlpha(opacity);
        canvas.DrawLine(cx, cy, cx + MathF.Cos(minuteAng) * r * 0.50f, cy + MathF.Sin(minuteAng) * r * 0.50f, _strokePaint);
        canvas.DrawLine(cx, cy, cx + MathF.Cos(hourAng) * r * 0.32f, cy + MathF.Sin(hourAng) * r * 0.32f, _strokePaint);
    }

    private void DrawCrown(SKCanvas canvas, float cx, float cy, float r, float opacity)
    {
        _path.Rewind();
        _path.MoveTo(cx - r * 0.78f, cy + r * 0.46f);
        _path.LineTo(cx - r * 0.86f, cy - r * 0.38f);
        _path.LineTo(cx - r * 0.34f, cy + r * 0.04f);
        _path.LineTo(cx, cy - r * 0.60f);
        _path.LineTo(cx + r * 0.34f, cy + r * 0.04f);
        _path.LineTo(cx + r * 0.86f, cy - r * 0.38f);
        _path.LineTo(cx + r * 0.78f, cy + r * 0.46f);
        _path.Close();

        _fillPaint.Shader = null;
        _fillPaint.Color = new SKColor(0xFB, 0xBF, 0x24, (byte)(248 * opacity));
        canvas.DrawPath(_path, _fillPaint);
    }

    private void DrawPaperPlane(SKCanvas canvas, float cx, float cy, float r, float t, RgbColor c1, RgbColor c2, float opacity, bool reducedMotion)
    {
        var bob = reducedMotion ? 0f : MathF.Sin(t * 4.5f) * r * 0.08f;
        _path.Rewind();
        _path.MoveTo(cx + r * 0.88f, cy - r * 0.62f + bob);
        _path.LineTo(cx - r * 0.84f, cy - r * 0.06f + bob);
        _path.LineTo(cx - r * 0.14f, cy + r * 0.22f + bob);
        _path.LineTo(cx + r * 0.18f, cy + r * 0.78f + bob);
        _path.Close();

        _fillPaint.Shader = null;
        _fillPaint.Color = StyleLayoutHelper.BlendSrgb(SKColors.White.ToRgb(), c1, 0.25f).ToSkColorWithAlpha(opacity * 0.96f);
        canvas.DrawPath(_path, _fillPaint);
        _strokePaint.StrokeWidth = Math.Max(1.2f, r * 0.10f);
        _strokePaint.Color = c2.ToSkColorWithAlpha(opacity * 0.85f);
        canvas.DrawLine(cx - r * 0.14f, cy + r * 0.22f + bob, cx + r * 0.88f, cy - r * 0.62f + bob, _strokePaint);
    }

    private void DrawEye(SKCanvas canvas, float cx, float cy, float r, float t, RgbColor c1, RgbColor c2, float opacity, bool reducedMotion)
    {
        _path.Rewind();
        _path.MoveTo(cx - r * 0.88f, cy);
        _path.QuadTo(cx, cy - r * 0.72f, cx + r * 0.88f, cy);
        _path.QuadTo(cx, cy + r * 0.72f, cx - r * 0.88f, cy);
        _path.Close();

        _strokePaint.StrokeWidth = Math.Max(1.6f, r * 0.15f);
        _strokePaint.Color = c1.ToSkColorWithAlpha(opacity);
        canvas.DrawPath(_path, _strokePaint);

        var gazeX = reducedMotion ? 0f : MathF.Sin(t * 2.1f) * r * 0.14f;
        _fillPaint.Shader = null;
        _fillPaint.Color = c2.ToSkColorWithAlpha(opacity);
        canvas.DrawCircle(cx + gazeX, cy, r * 0.30f, _fillPaint);
        _fillPaint.Color = SKColors.White.WithAlpha((byte)(235 * opacity));
        canvas.DrawCircle(cx + gazeX - r * 0.09f, cy - r * 0.09f, r * 0.10f, _fillPaint);
    }

    private void DrawDiamond(SKCanvas canvas, float cx, float cy, float r, float t, RgbColor c1, RgbColor c2, float opacity, bool reducedMotion)
    {
        _path.Rewind();
        _path.MoveTo(cx - r * 0.56f, cy - r * 0.50f);
        _path.LineTo(cx + r * 0.56f, cy - r * 0.50f);
        _path.LineTo(cx + r * 0.88f, cy - r * 0.08f);
        _path.LineTo(cx, cy + r * 0.82f);
        _path.LineTo(cx - r * 0.88f, cy - r * 0.08f);
        _path.Close();

        var shimmer = reducedMotion ? 0.5f : 0.5f + 0.5f * MathF.Sin(t * 4.5f);
        _gradColors[0] = StyleLayoutHelper.BlendSrgb(new RgbColor(0x67, 0xE8, 0xF9), c1, shimmer * 0.45f).ToSkColorWithAlpha(opacity);
        _gradColors[1] = StyleLayoutHelper.BlendSrgb(new RgbColor(0x3B, 0x82, 0xF6), c2, 0.45f).ToSkColorWithAlpha(opacity);
        using var grad = SKShader.CreateLinearGradient(new SKPoint(cx - r, cy - r), new SKPoint(cx + r, cy + r), _gradColors, _gradStops, SKShaderTileMode.Clamp);
        _fillPaint.Shader = grad;
        _fillPaint.Color = SKColors.White;
        try
        {
            canvas.DrawPath(_path, _fillPaint);
        }
        finally
        {
            _fillPaint.Shader = null;
        }
    }

    private void DrawFlower(SKCanvas canvas, float cx, float cy, float r, float t, RgbColor c2, float opacity, bool reducedMotion)
    {
        var rot = reducedMotion ? 0f : t * 0.45f;
        var petalColor = StyleLayoutHelper.BlendSrgb(new RgbColor(0xFB, 0x71, 0x85), c2, 0.35f).ToSkColorWithAlpha(opacity * 0.92f);
        _fillPaint.Shader = null;
        _fillPaint.Color = petalColor;
        for (var i = 0; i < 6; i++)
        {
            var a = rot + ((i / 6f) * MathF.Tau);
            canvas.DrawCircle(cx + MathF.Cos(a) * r * 0.44f, cy + MathF.Sin(a) * r * 0.44f, r * 0.34f, _fillPaint);
        }

        _fillPaint.Color = new SKColor(0xFD, 0xE0, 0x47, (byte)(245 * opacity));
        canvas.DrawCircle(cx, cy, r * 0.28f, _fillPaint);
    }

    private void DrawPaperSheet(SKCanvas canvas, float cx, float cy, float r, float t, RgbColor c1, RgbColor c2, float opacity, bool reducedMotion)
    {
        var tilt = reducedMotion ? -5f : (-5f + (MathF.Sin(t * 3.1f) * 3.5f));
        var save = canvas.Save();
        try
        {
            canvas.Translate(cx, cy);
            canvas.RotateDegrees(tilt);

            var rect = new SKRect(-r * 0.68f, -r * 0.84f, r * 0.68f, r * 0.84f);
            _fillPaint.Shader = null;
            _fillPaint.Color = new SKColor(0xF8, 0xFA, 0xFC, (byte)(242 * opacity));
            canvas.DrawRoundRect(rect, r * 0.14f, r * 0.14f, _fillPaint);

            _strokePaint.StrokeWidth = Math.Max(1.2f, r * 0.10f);
            _strokePaint.Color = StyleLayoutHelper.BlendSrgb(c1, c2, 0.5f).ToSkColorWithAlpha(opacity * 0.85f);
            canvas.DrawLine(-r * 0.42f, -r * 0.36f, r * 0.42f, -r * 0.36f, _strokePaint);
            canvas.DrawLine(-r * 0.42f, 0f, r * 0.38f, 0f, _strokePaint);
            canvas.DrawLine(-r * 0.42f, r * 0.36f, r * 0.22f, r * 0.36f, _strokePaint);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    private void DrawButterfly(SKCanvas canvas, float cx, float cy, float r, float t, float beatPulse, RgbColor c1, RgbColor c2, float opacity, bool reducedMotion)
    {
        var flap = reducedMotion ? 1.0f : (0.68f + (0.32f * MathF.Abs(MathF.Cos(t * 6.5f))));
        var span = r * flap * (1f + (beatPulse * 0.12f));

        _fillPaint.Shader = null;
        _fillPaint.Color = StyleLayoutHelper.BlendSrgb(c1, c2, 0.3f).ToSkColorWithAlpha(opacity * 0.92f);
        canvas.DrawOval(cx - (span * 0.50f), cy - (r * 0.22f), span * 0.48f, r * 0.36f, _fillPaint);
        canvas.DrawOval(cx + (span * 0.50f), cy - (r * 0.22f), span * 0.48f, r * 0.36f, _fillPaint);

        _fillPaint.Color = StyleLayoutHelper.BlendSrgb(c2, new RgbColor(0xFF, 0xFF, 0xFF), 0.25f).ToSkColorWithAlpha(opacity * 0.84f);
        canvas.DrawOval(cx - (span * 0.38f), cy + (r * 0.28f), span * 0.34f, r * 0.28f, _fillPaint);
        canvas.DrawOval(cx + (span * 0.38f), cy + (r * 0.28f), span * 0.34f, r * 0.28f, _fillPaint);

        _strokePaint.StrokeWidth = Math.Max(1.4f, r * 0.14f);
        _strokePaint.Color = SKColors.White.WithAlpha((byte)(240 * opacity));
        canvas.DrawLine(cx, cy - (r * 0.50f), cx, cy + (r * 0.56f), _strokePaint);
    }

    private void DrawPlanet(SKCanvas canvas, float cx, float cy, float r, float t, RgbColor c1, RgbColor c2, float opacity, bool reducedMotion)
    {
        _gradColors[0] = c1.ToSkColorWithAlpha(opacity);
        _gradColors[1] = c2.ToSkColorWithAlpha(opacity);
        using var grad = SKShader.CreateLinearGradient(new SKPoint(cx - r, cy - r), new SKPoint(cx + r, cy + r), _gradColors, _gradStops, SKShaderTileMode.Clamp);
        _fillPaint.Shader = grad;
        _fillPaint.Color = SKColors.White;
        try
        {
            canvas.DrawCircle(cx, cy, r * 0.54f, _fillPaint);
        }
        finally
        {
            _fillPaint.Shader = null;
        }

        var save = canvas.Save();
        try
        {
            canvas.Translate(cx, cy);
            canvas.RotateDegrees(reducedMotion ? -22f : (-22f + (MathF.Sin(t * 1.4f) * 6f)));
            _strokePaint.StrokeWidth = Math.Max(1.4f, r * 0.12f);
            _strokePaint.Color = SKColors.White.WithAlpha((byte)(220 * opacity));
            canvas.DrawOval(0f, 0f, r * 0.96f, r * 0.28f, _strokePaint);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    private void DrawCar(SKCanvas canvas, float cx, float cy, float r, float t, float beatPulse, RgbColor c1, RgbColor c2, float opacity, bool reducedMotion)
    {
        var bob = reducedMotion ? 0f : (MathF.Sin(t * 10f) * r * 0.04f) - (beatPulse * r * 0.06f);
        _path.Rewind();
        _path.MoveTo(cx - r * 0.86f, cy + r * 0.12f + bob);
        _path.LineTo(cx - r * 0.62f, cy - r * 0.26f + bob);
        _path.LineTo(cx + r * 0.28f, cy - r * 0.26f + bob);
        _path.LineTo(cx + r * 0.72f, cy + r * 0.08f + bob);
        _path.LineTo(cx + r * 0.88f, cy + r * 0.16f + bob);
        _path.LineTo(cx + r * 0.88f, cy + r * 0.36f + bob);
        _path.LineTo(cx - r * 0.86f, cy + r * 0.36f + bob);
        _path.Close();

        _fillPaint.Shader = null;
        _fillPaint.Color = StyleLayoutHelper.BlendSrgb(c1, c2, 0.35f).ToSkColorWithAlpha(opacity * 0.94f);
        canvas.DrawPath(_path, _fillPaint);

        _fillPaint.Color = new SKColor(0x0B, 0x0C, 0x16, (byte)(245 * opacity));
        canvas.DrawCircle(cx - r * 0.46f, cy + r * 0.40f + bob, r * 0.22f, _fillPaint);
        canvas.DrawCircle(cx + r * 0.46f, cy + r * 0.40f + bob, r * 0.22f, _fillPaint);
    }

    private void DrawGhost(SKCanvas canvas, float cx, float cy, float r, float t, RgbColor c1, RgbColor c2, float opacity, bool reducedMotion)
    {
        var bob = reducedMotion ? 0f : MathF.Sin(t * 3.8f) * r * 0.09f;
        _path.Rewind();
        _path.MoveTo(cx - r * 0.64f, cy + r * 0.72f + bob);
        _path.LineTo(cx - r * 0.64f, cy - r * 0.12f + bob);
        _path.CubicTo(cx - r * 0.64f, cy - r * 0.92f + bob, cx + r * 0.64f, cy - r * 0.92f + bob, cx + r * 0.64f, cy - r * 0.12f + bob);
        _path.LineTo(cx + r * 0.64f, cy + r * 0.72f + bob);
        _path.LineTo(cx + r * 0.28f, cy + r * 0.48f + bob);
        _path.LineTo(cx, cy + r * 0.72f + bob);
        _path.LineTo(cx - r * 0.28f, cy + r * 0.48f + bob);
        _path.Close();

        _fillPaint.Shader = null;
        _fillPaint.Color = StyleLayoutHelper.BlendSrgb(SKColors.White.ToRgb(), c2, 0.20f).ToSkColorWithAlpha(opacity * 0.92f);
        canvas.DrawPath(_path, _fillPaint);

        _fillPaint.Color = new SKColor(0x0B, 0x0C, 0x16, (byte)(235 * opacity));
        canvas.DrawCircle(cx - r * 0.22f, cy - r * 0.16f + bob, r * 0.12f, _fillPaint);
        canvas.DrawCircle(cx + r * 0.22f, cy - r * 0.16f + bob, r * 0.12f, _fillPaint);
    }

    private void DrawGlass(SKCanvas canvas, float cx, float cy, float r, float t, RgbColor c1, RgbColor c2, float opacity, bool reducedMotion)
    {
        var sway = reducedMotion ? 0f : MathF.Sin(t * 4.2f) * r * 0.08f;
        _strokePaint.StrokeWidth = Math.Max(1.4f, r * 0.13f);
        _strokePaint.Color = SKColors.White.WithAlpha((byte)(230 * opacity));
        canvas.DrawLine(cx - r * 0.56f, cy - r * 0.72f, cx + r * 0.56f, cy - r * 0.72f, _strokePaint);

        _path.Rewind();
        _path.MoveTo(cx - r * 0.52f, cy - r * 0.70f);
        _path.CubicTo(cx - r * 0.54f, cy + r * 0.06f, cx + r * 0.54f, cy + r * 0.06f, cx + r * 0.52f, cy - r * 0.70f);
        _path.Close();

        _fillPaint.Shader = null;
        _fillPaint.Color = StyleLayoutHelper.BlendSrgb(c1, c2, 0.45f).ToSkColorWithAlpha(opacity * 0.86f);
        canvas.DrawPath(_path, _fillPaint);

        canvas.DrawLine(cx, cy + r * 0.05f, cx, cy + r * 0.76f, _strokePaint);
        canvas.DrawLine(cx - r * 0.36f + (sway * 0.2f), cy + r * 0.76f, cx + r * 0.36f + (sway * 0.2f), cy + r * 0.76f, _strokePaint);
    }

    private void DrawKey(SKCanvas canvas, float cx, float cy, float r, float t, RgbColor c1, RgbColor c2, float opacity, bool reducedMotion)
    {
        var rot = reducedMotion ? -20f : (-20f + (MathF.Sin(t * 2.8f) * 8f));
        var save = canvas.Save();
        try
        {
            canvas.Translate(cx, cy);
            canvas.RotateDegrees(rot);

            _strokePaint.StrokeWidth = Math.Max(1.6f, r * 0.16f);
            _strokePaint.Color = StyleLayoutHelper.BlendSrgb(new RgbColor(0xFB, 0xBF, 0x24), c1, 0.30f).ToSkColorWithAlpha(opacity);
            canvas.DrawCircle(-r * 0.42f, 0f, r * 0.32f, _strokePaint);
            canvas.DrawLine(-r * 0.10f, 0f, r * 0.76f, 0f, _strokePaint);
            canvas.DrawLine(r * 0.42f, 0f, r * 0.42f, r * 0.30f, _strokePaint);
            canvas.DrawLine(r * 0.68f, 0f, r * 0.68f, r * 0.24f, _strokePaint);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    private void DrawSword(SKCanvas canvas, float cx, float cy, float r, float t, RgbColor c1, RgbColor c2, float opacity, bool reducedMotion)
    {
        var tilt = reducedMotion ? 35f : (35f + (MathF.Sin(t * 3.6f) * 6f));
        var save = canvas.Save();
        try
        {
            canvas.Translate(cx, cy);
            canvas.RotateDegrees(tilt);

            _path.Rewind();
            _path.MoveTo(0f, -r * 0.90f);
            _path.LineTo(r * 0.18f, -r * 0.56f);
            _path.LineTo(r * 0.14f, r * 0.32f);
            _path.LineTo(-r * 0.14f, r * 0.32f);
            _path.LineTo(-r * 0.18f, -r * 0.56f);
            _path.Close();

            _fillPaint.Shader = null;
            _fillPaint.Color = SKColors.White.WithAlpha((byte)(238 * opacity));
            canvas.DrawPath(_path, _fillPaint);

            _strokePaint.StrokeWidth = Math.Max(1.6f, r * 0.15f);
            _strokePaint.Color = StyleLayoutHelper.BlendSrgb(c1, c2, 0.5f).ToSkColorWithAlpha(opacity);
            canvas.DrawLine(-r * 0.42f, r * 0.32f, r * 0.42f, r * 0.32f, _strokePaint);
            canvas.DrawLine(0f, r * 0.32f, 0f, r * 0.80f, _strokePaint);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }
}

internal static class RgbColorExtensions
{
    public static RgbColor ToRgb(this SKColor c) => new(c.Red, c.Green, c.Blue, c.Alpha);
}
