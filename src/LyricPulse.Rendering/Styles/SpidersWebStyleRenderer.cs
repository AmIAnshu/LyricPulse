using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #7 — Spider's Web: radial geometric web spun outward from viewport center with
/// 3 softly sagging catenary concentric silk rings, plucked-strand transverse damped vibration
/// on word onset, and strictly upright (<c>0°</c>) anchored dew-node labels.
/// </summary>
public sealed class SpidersWebStyleRenderer : IKineticStyleRenderer
{
    private static readonly float[] RingRatios = [0.36f, 0.66f, 0.94f];

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _ringPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.05f };
    private readonly SKPaint _spokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _spokeGlowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _dewPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath _silkPath = new();
    private readonly SKImageFilter _glowBlur = SKImageFilter.CreateBlur(5.0f, 5.0f);

    private SpringState _wordScale = SpringState.AtRest(1.05f);
    private float _twangElapsedSec = 0.40f;
    private float _highlightFade = 1.0f;
    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private float _lastWordDurationSec = 0.32f;
    private bool _disposed;

    public OverlayStyleId StyleId => OverlayStyleId.SpidersWeb;

    public void ResetState()
    {
        _wordScale = SpringState.AtRest(1.05f);
        _twangElapsedSec = 0.40f;
        _highlightFade = 1.0f;
        _lastWordKey = (-1, -1);
        _lastWordDurationSec = 0.32f;
        _aesthetics.ResetDot();
    }

    public bool Advance(in FrameRenderPacket packet, in LifecycleFrameState lifecycle)
    {
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        if (slice.CurrentLine is null)
        {
            return false;
        }

        var dt = Math.Clamp(packet.DeltaSeconds, 0f, SpringState.MaxDeltaSeconds);
        var activeIdx = slice.CurrentLine.Words.IsEmpty
            ? 0
            : Math.Clamp(slice.ActiveWordIndex, 0, slice.CurrentLine.Words.Length - 1);

        if (!slice.CurrentLine.Words.IsEmpty)
        {
            var word = slice.CurrentLine.Words[activeIdx];
            _lastWordDurationSec = Math.Clamp((float)(word.EndTime - word.StartTime).TotalSeconds, 0.08f, 2.5f);
        }

        var key = (slice.ActiveLineIndex, activeIdx);
        if (key != _lastWordKey)
        {
            _lastWordKey = key;
            if (packet.ReducedMotion)
            {
                _wordScale = SpringState.AtRest(1.00f);
                _twangElapsedSec = 0.40f;
                _highlightFade = 0f;
            }
            else
            {
                _wordScale = new SpringState(Value: 0.85f, Velocity: 0f, Target: 1.05f + ((packet.SafePartyEnergy - 1f) * 0.08f));
                _twangElapsedSec = 0f;
                _highlightFade = 0f;
            }
        }

        var fadeRate = packet.ReducedMotion ? (1f / 0.14f) : (1f / 0.16f);
        if (_highlightFade < 1f)
        {
            _highlightFade = Math.Min(1f, _highlightFade + (dt * fadeRate));
        }

        if (packet.ReducedMotion)
        {
            _wordScale = SpringState.AtRest(1.00f);
            _twangElapsedSec = 0.40f;
            _aesthetics.ResetDot();
            return _highlightFade < 0.999f;
        }

        var spring = SpringTokens.WordSnap.WithTempoCompression(_lastWordDurationSec);
        _wordScale = _wordScale.Step(spring, dt);
        if (_twangElapsedSec < 0.34f)
        {
            _twangElapsedSec = Math.Min(0.34f, _twangElapsedSec + dt);
        }

        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        return !_wordScale.IsAtRest || _twangElapsedSec < 0.34f || _highlightFade < 0.999f || isPlaying;
    }

    public void Render(in StyleRenderContext context, in LifecycleFrameState lifecycle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var packet = context.Packet;
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        if (slice.CurrentLine is null)
        {
            return;
        }

        var fontId = packet.ActiveFontId;
        var fontSize = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 0.64f);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);

        var (cx, cy) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.50f);
        var radius = MathF.Min(packet.ViewportWidthDip * (packet.IsCompactCard ? 0.40f : 0.33f), packet.ViewportHeightDip * (packet.IsCompactCard ? 0.42f : 0.36f));
        radius = Math.Max(radius, 42f);

        var scrimRect = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        context.Legibility.DrawAmbientScrim(
            context.Canvas,
            scrimRect,
            fontSize,
            packet.Palette.ScrimTint,
            packet.ScrimIntensity,
            packet.HighContrastScrim);

        var words = slice.CurrentLine.Words;
        var wordCount = words.IsEmpty ? (string.IsNullOrWhiteSpace(slice.CurrentLine.RawText) ? 0 : 1) : words.Length;
        if (wordCount == 0)
        {
            return;
        }

        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, wordCount - 1);
        var spokeCount = Math.Max(wordCount, 6);
        var seed = StyleLayoutHelper.DeterministicHash(slice.CurrentLine.Index, slice.CurrentLine.RawText);

        Span<float> angles = spokeCount <= 32 ? stackalloc float[spokeCount] : new float[spokeCount];
        const float startAngleRad = -115f * (MathF.PI / 180f);
        var stepRad = (2f * MathF.PI) / spokeCount;
        for (var i = 0; i < spokeCount; i++)
        {
            var jitterDeg = ((((seed >> (i % 16)) & 0xF) / 15f) - 0.5f) * 8f;
            angles[i] = startAngleRad + (i * stepRad) + (jitterDeg * (MathF.PI / 180f));
        }

        // 1. Concentric sagging catenary silk rings
        DrawCatenaryRings(context.Canvas, cx, cy, radius, angles, packet);

        // 2. Radial spokes with plucked-strand transverse vibration on active spoke
        var spinFraction = packet.ReducedMotion ? 1.0f : (float)Math.Clamp(slice.WordProgress / 0.45, 0.15, 1.0);
        var twangAmp = packet.ReducedMotion
            ? 0f
            : 4.5f * MathF.Exp(-9f * _twangElapsedSec) * MathF.Sin(42f * _twangElapsedSec) * packet.SafePartyEnergy;

        for (var i = 0; i < spokeCount; i++)
        {
            var isWordSpoke = i < wordCount;
            var isActive = isWordSpoke && i == activeIdx;
            var isPast = isWordSpoke && i < activeIdx;

            var angle = angles[i];
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);
            var reach = isActive ? (radius * spinFraction) : radius;
            var ex = cx + (cos * reach);
            var ey = cy + (sin * reach);

            if (isActive && MathF.Abs(twangAmp) > 0.08f)
            {
                var mx = cx + (cos * reach * 0.5f) - (sin * twangAmp);
                var my = cy + (sin * reach * 0.5f) + (cos * twangAmp);
                _silkPath.Rewind();
                _silkPath.MoveTo(cx, cy);
                _silkPath.QuadTo(mx, my, ex, ey);

                _spokeGlowPaint.StrokeWidth = 5.0f;
                _spokeGlowPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.45f);
                _spokeGlowPaint.ImageFilter = _glowBlur;
                try
                {
                    context.Canvas.DrawPath(_silkPath, _spokeGlowPaint);
                }
                finally
                {
                    _spokeGlowPaint.ImageFilter = null;
                }

                _spokePaint.StrokeWidth = 1.9f;
                _spokePaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(0.95f);
                context.Canvas.DrawPath(_silkPath, _spokePaint);
            }
            else
            {
                _spokePaint.StrokeWidth = isActive ? 1.9f : (isPast ? 1.4f : 1.0f);
                _spokePaint.Color = isActive
                    ? packet.Palette.AccentColor1.ToSkColorWithAlpha(0.95f)
                    : isPast
                        ? packet.Palette.AccentColor2.ToSkColorWithAlpha(0.58f)
                        : packet.Palette.AccentColor3.ToSkColorWithAlpha(0.24f);
                context.Canvas.DrawLine(cx, cy, ex, ey, _spokePaint);
            }
        }

        // Hub anchor glow
        _dewPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.75f);
        context.Canvas.DrawCircle(cx, cy, packet.IsCompactCard ? 2.5f : 4.0f, _dewPaint);

        // 3. Strictly upright (0° horizontal) word labels anchored on alternating radial tiers
        var highlightEased = Ease.Highlight(_highlightFade);
        var activeNodeX = cx;
        var activeNodeY = cy;

        for (var i = 0; i < wordCount; i++)
        {
            var text = words.IsEmpty ? slice.CurrentLine.RawText : words[i].Text;
            var angle = angles[i];
            var tierRatio = (i % 2 == 0) ? 0.54f : 0.80f;
            var nx = cx + (MathF.Cos(angle) * radius * tierRatio);
            var ny = cy + (MathF.Sin(angle) * radius * tierRatio);
            var isActive = i == activeIdx;
            var isPast = i < activeIdx;

            if (isActive)
            {
                activeNodeX = nx;
                activeNodeY = ny;
            }

            _dewPaint.Color = isActive
                ? packet.Palette.AccentColor1.ToSkColorWithAlpha(1.0f)
                : isPast
                    ? packet.Palette.AccentColor2.ToSkColorWithAlpha(0.80f)
                    : packet.Palette.AccentColor3.ToSkColorWithAlpha(0.42f);
            context.Canvas.DrawCircle(nx, ny, isActive ? (packet.IsCompactCard ? 3.5f : 5.2f) : (packet.IsCompactCard ? 2.0f : 3.0f), _dewPaint);

            var labelOpacity = isActive
                ? (packet.ReducedMotion ? (0.90f + (0.10f * highlightEased)) : 1.0f)
                : isPast ? 0.72f : 0.36f;
            var labelColor = isActive
                ? StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor3, packet.Palette.AccentColor1, highlightEased)
                : packet.Palette.AccentColor3;
            var labelScale = isActive ? (packet.ReducedMotion ? 1.0f : _wordScale.Value) : 0.90f;

            var measuredW = TextShaperService.MeasureWidth(text, font, context.Fonts) * labelScale;
            var clampedX = Math.Clamp(nx, (measuredW * 0.5f) + 10f, Math.Max((measuredW * 0.5f) + 10f, packet.ViewportWidthDip - (measuredW * 0.5f) - 10f));
            var clampedY = Math.Clamp(ny - (font.Size * 0.45f), font.Size + 8f, Math.Max(font.Size + 8f, packet.ViewportHeightDip - 12f));

            if (isActive && packet.ShowColorSweepLayer)
            {
                var sweepRect = new SKRect(clampedX - (measuredW * 0.5f), clampedY - (font.Size * 0.85f * labelScale), clampedX + (measuredW * 0.5f), clampedY + (font.Size * 0.16f * labelScale));
                _aesthetics.DrawWordColorSweep(context.Canvas, sweepRect, slice.WordProgress, packet, labelOpacity);
            }

            var save = context.Canvas.Save();
            try
            {
                context.Canvas.Translate(clampedX, clampedY);
                context.Canvas.Scale(labelScale, labelScale);
                context.Legibility.DrawLegibleText(
                    context.Canvas,
                    text,
                    0f,
                    0f,
                    SKTextAlign.Center,
                    font,
                    fontId,
                    labelColor,
                    opacity: labelOpacity,
                    activeGlowColor: isActive ? packet.Palette.AccentColor2 : null,
                    fonts: context.Fonts);
            }
            finally
            {
                context.Canvas.RestoreToCount(save);
            }
        }

        _aesthetics.DrawFollowingDot(context.Canvas, packet, activeNodeX, activeNodeY, Math.Clamp(fontSize * 0.14f, 3.0f, 6.5f));

        StyleLayoutHelper.DrawSecondarySubline(
            in context,
            slice.CurrentLine,
            cx,
            cy + radius + Math.Max(16f, fontSize * 0.65f),
            fontSize * 1.15f);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _aesthetics.Dispose();
        _ringPaint.Dispose();
        _spokePaint.Dispose();
        _spokeGlowPaint.Dispose();
        _dewPaint.Dispose();
        _silkPath.Dispose();
        _glowBlur.Dispose();
    }

    private void DrawCatenaryRings(SKCanvas canvas, float cx, float cy, float radius, ReadOnlySpan<float> angles, FrameRenderPacket packet)
    {
        _ringPaint.Color = packet.Palette.AccentColor3.ToSkColorWithAlpha(0.28f);

        foreach (var ringRatio in RingRatios)
        {
            var r = radius * ringRatio;
            var sagR = r * 0.94f; // 6% inward catenary sag between spokes
            _silkPath.Rewind();

            for (var i = 0; i < angles.Length; i++)
            {
                var a0 = angles[i];
                var a1 = angles[(i + 1) % angles.Length];
                var x0 = cx + (MathF.Cos(a0) * r);
                var y0 = cy + (MathF.Sin(a0) * r);
                var x1 = cx + (MathF.Cos(a1) * r);
                var y1 = cy + (MathF.Sin(a1) * r);

                var midX = ((x0 + x1) * 0.5f) * 0.96f + (cx * 0.04f);
                var midY = ((y0 + y1) * 0.5f) * 0.96f + (cy * 0.04f);
                var midAngle = MathF.Atan2(midY - cy, midX - cx);
                var ctrlX = cx + (MathF.Cos(midAngle) * sagR);
                var ctrlY = cy + (MathF.Sin(midAngle) * sagR);

                if (i == 0)
                {
                    _silkPath.MoveTo(x0, y0);
                }

                _silkPath.QuadTo(ctrlX, ctrlY, x1, y1);
            }

            _silkPath.Close();
            canvas.DrawPath(_silkPath, _ringPaint);
        }
    }
}
