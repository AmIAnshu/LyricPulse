using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #7 — Spider's Web (Verci-inspired): delicate sagging concentric orb-weaver silk rings,
/// plucked-strand transverse vibration on word onset, stylized white translucent spider weaving smoothly
/// along the active silk thread, and upright word labels anchored on radial tiers.
/// </summary>
public sealed class SpidersWebStyleRenderer : IKineticStyleRenderer
{
    private static readonly float[] RingRatios = [0.24f, 0.44f, 0.66f, 0.88f];

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _ringPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.95f };
    private readonly SKPaint _spokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _spokeGlowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _dewPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _spiderBodyPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _spiderLegPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPath _silkPath = new();
    private readonly SKPath _legPath = new();
    private readonly SKImageFilter _glowBlur = SKImageFilter.CreateBlur(5.0f, 5.0f);

    private SpringState _wordScale = SpringState.AtRest(1.05f);
    private SpringState _spiderX = SpringState.AtRest(0f);
    private SpringState _spiderY = SpringState.AtRest(0f);
    private float _twangElapsedSec = 0.40f;
    private float _highlightFade = 1.0f;
    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private float _lastWordDurationSec = 0.32f;
    private bool _hasSpiderPos;
    private bool _disposed;

    public OverlayStyleId StyleId => OverlayStyleId.SpidersWeb;

    public void ResetState()
    {
        _wordScale = SpringState.AtRest(1.05f);
        _spiderX = SpringState.AtRest(0f);
        _spiderY = SpringState.AtRest(0f);
        _hasSpiderPos = false;
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
        var words = slice.CurrentLine.Words;
        var wordCount = words.IsEmpty ? 1 : words.Length;
        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, wordCount - 1);

        if (!words.IsEmpty)
        {
            var word = words[activeIdx];
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
                _wordScale = new SpringState(Value: 0.85f, Velocity: 0f, Target: 1.06f + ((packet.SafePartyEnergy - 1f) * 0.08f));
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
            _hasSpiderPos = false;
            _aesthetics.ResetDot();
            return _highlightFade < 0.999f;
        }

        var (cx, cy, radius) = ComputeWebMetrics(packet);
        var spokeCount = Math.Max(wordCount, 7);
        var seed = StyleLayoutHelper.DeterministicHash(slice.CurrentLine.Index, slice.CurrentLine.RawText);
        var angle = ComputeSpokeAngle(activeIdx, spokeCount, seed);
        var tierRatio = (activeIdx % 2 == 0) ? 0.56f : 0.82f;
        var nx = cx + (MathF.Cos(angle) * radius * tierRatio);
        var ny = cy + (MathF.Sin(angle) * radius * tierRatio);

        // Spider hovers gracefully between center hub and active node
        var spiderTargetX = cx + ((nx - cx) * 0.44f);
        var spiderTargetY = cy + ((ny - cy) * 0.44f);
        if (!_hasSpiderPos)
        {
            _spiderX = SpringState.AtRest(spiderTargetX);
            _spiderY = SpringState.AtRest(spiderTargetY);
            _hasSpiderPos = true;
        }
        else
        {
            _spiderX = _spiderX.WithTarget(spiderTargetX).Step(SpringTokens.WordEnter.WithTempoCompression(_lastWordDurationSec), dt);
            _spiderY = _spiderY.WithTarget(spiderTargetY).Step(SpringTokens.WordEnter.WithTempoCompression(_lastWordDurationSec), dt);
        }

        var spring = SpringTokens.WordSnap.WithTempoCompression(_lastWordDurationSec);
        _wordScale = _wordScale.Step(spring, dt);
        var dotActive = packet.ShowFollowingDot && _aesthetics.AdvanceDot(key, nx, ny, dt, reducedMotion: false, _lastWordDurationSec);

        if (_twangElapsedSec < 0.34f)
        {
            _twangElapsedSec = Math.Min(0.34f, _twangElapsedSec + dt);
        }

        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        return !_wordScale.IsAtRest || !_spiderX.IsAtRest || dotActive || _twangElapsedSec < 0.34f || _highlightFade < 0.999f || isPlaying;
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
        var fontSize = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 0.62f);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);

        var (cx, cy, radius) = ComputeWebMetrics(packet);
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
        var spokeCount = Math.Max(wordCount, 7);
        var seed = StyleLayoutHelper.DeterministicHash(slice.CurrentLine.Index, slice.CurrentLine.RawText);

        Span<float> angles = spokeCount <= 32 ? stackalloc float[spokeCount] : new float[spokeCount];
        for (var i = 0; i < spokeCount; i++)
        {
            angles[i] = ComputeSpokeAngle(i, spokeCount, seed);
        }

        DrawCatenaryRings(context.Canvas, cx, cy, radius, angles, packet);

        var spinFraction = packet.ReducedMotion ? 1.0f : (float)Math.Clamp(slice.WordProgress / 0.45, 0.25, 1.0);
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

                _spokeGlowPaint.StrokeWidth = 4.8f;
                _spokeGlowPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.42f);
                _spokeGlowPaint.ImageFilter = _glowBlur;
                try
                {
                    context.Canvas.DrawPath(_silkPath, _spokeGlowPaint);
                }
                finally
                {
                    _spokeGlowPaint.ImageFilter = null;
                }

                _spokePaint.StrokeWidth = 1.8f;
                _spokePaint.Color = SKColors.White.WithAlpha(235);
                context.Canvas.DrawPath(_silkPath, _spokePaint);
            }
            else
            {
                _spokePaint.StrokeWidth = isActive ? 1.8f : (isPast ? 1.2f : 0.85f);
                _spokePaint.Color = isActive
                    ? SKColors.White.WithAlpha(235)
                    : isPast
                        ? packet.Palette.AccentColor1.ToSkColorWithAlpha(0.55f)
                        : packet.Palette.AccentColor3.ToSkColorWithAlpha(0.22f);
                context.Canvas.DrawLine(cx, cy, ex, ey, _spokePaint);
            }
        }

        // Stylized translucent Verci white spider perched near center facing active word
        var sx = !packet.ReducedMotion && _hasSpiderPos ? _spiderX.Value : cx;
        var sy = !packet.ReducedMotion && _hasSpiderPos ? _spiderY.Value : cy;
        DrawStylizedSpider(context.Canvas, sx, sy, angles[activeIdx], packet.IsCompactCard ? 0.55f : 1.0f, (float)packet.RenderTimeSeconds, packet.ReducedMotion);

        // Upright dew-node word labels
        var highlightEased = Ease.Highlight(_highlightFade);
        var activeNodeX = cx;
        var activeNodeY = cy;

        for (var i = 0; i < wordCount; i++)
        {
            var text = words.IsEmpty ? slice.CurrentLine.RawText : words[i].Text;
            var angle = angles[i];
            var tierRatio = (i % 2 == 0) ? 0.56f : 0.82f;
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
                ? SKColors.White
                : isPast
                    ? packet.Palette.AccentColor1.ToSkColorWithAlpha(0.82f)
                    : packet.Palette.AccentColor3.ToSkColorWithAlpha(0.38f);
            context.Canvas.DrawCircle(nx, ny, isActive ? (packet.IsCompactCard ? 3.2f : 4.8f) : (packet.IsCompactCard ? 1.8f : 2.6f), _dewPaint);

            var labelOpacity = isActive
                ? (packet.ReducedMotion ? (0.90f + (0.10f * highlightEased)) : 1.0f)
                : isPast ? 0.74f : 0.34f;
            var labelColor = isActive
                ? StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor1, SKColors.White.ToRgb(), 0.45f * highlightEased)
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

        _aesthetics.DrawFollowingDot(context.Canvas, packet, activeNodeX, activeNodeY, Math.Clamp(fontSize * 0.14f, 3.0f, 6.2f));

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
        _spiderBodyPaint.Dispose();
        _spiderLegPaint.Dispose();
        _silkPath.Dispose();
        _legPath.Dispose();
        _glowBlur.Dispose();
    }

    private void DrawStylizedSpider(SKCanvas canvas, float sx, float sy, float facingRad, float scale, float timeSec, bool reducedMotion)
    {
        var save = canvas.Save();
        try
        {
            canvas.Translate(sx, sy);
            canvas.RotateDegrees((facingRad * (180f / MathF.PI)) + 90f);
            canvas.Scale(scale, scale);

            var legJitter = reducedMotion ? 0f : MathF.Sin(timeSec * 14f) * 1.6f;

            // 8 graceful orb-weaver articulated silk legs
            _spiderLegPaint.StrokeWidth = 1.6f;
            _spiderLegPaint.Color = new SKColor(0xF8, 0xFA, 0xFC, 215);
            for (var side = -1; side <= 1; side += 2)
            {
                for (var leg = 0; leg < 4; leg++)
                {
                    var yAnchor = -4f + (leg * 3.5f);
                    var spreadY = -18f + (leg * 12f) + ((leg % 2 == 0 ? 1 : -1) * legJitter);
                    var kneeX = side * (16f + (leg == 1 || leg == 2 ? 4f : 0f));
                    var tipX = side * (28f + (leg == 1 ? 6f : 0f));

                    _legPath.Rewind();
                    _legPath.MoveTo(side * 3f, yAnchor);
                    _legPath.LineTo(kneeX, spreadY * 0.6f);
                    _legPath.LineTo(tipX, spreadY);
                    canvas.DrawPath(_legPath, _spiderLegPaint);
                }
            }

            // Sleek white cephalothorax & tapered abdomen
            _spiderBodyPaint.Color = new SKColor(0xFF, 0xFF, 0xFF, 238);
            canvas.DrawOval(0f, -4f, 4.2f, 5.2f, _spiderBodyPaint);
            canvas.DrawOval(0f, 6.5f, 6.0f, 8.2f, _spiderBodyPaint);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    private void DrawCatenaryRings(SKCanvas canvas, float cx, float cy, float radius, ReadOnlySpan<float> angles, FrameRenderPacket packet)
    {
        _ringPaint.Color = new SKColor(0xFF, 0xFF, 0xFF, 48);

        foreach (var ringRatio in RingRatios)
        {
            var r = radius * ringRatio;
            _silkPath.Rewind();
            for (var i = 0; i < angles.Length; i++)
            {
                var a1 = angles[i];
                var a2 = angles[(i + 1) % angles.Length];
                var x1 = cx + (MathF.Cos(a1) * r);
                var y1 = cy + (MathF.Sin(a1) * r);
                var x2 = cx + (MathF.Cos(a2) * r);
                var y2 = cy + (MathF.Sin(a2) * r);

                var midX = ((x1 + x2) * 0.5f) * 0.93f + (cx * 0.07f);
                var midY = ((y1 + y2) * 0.5f) * 0.93f + (cy * 0.07f) + 2.5f;

                if (i == 0)
                {
                    _silkPath.MoveTo(x1, y1);
                }

                _silkPath.QuadTo(midX, midY, x2, y2);
            }

            _silkPath.Close();
            canvas.DrawPath(_silkPath, _ringPaint);
        }
    }

    private static (float Cx, float Cy, float Radius) ComputeWebMetrics(FrameRenderPacket packet)
    {
        var (cx, cy) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.50f);
        var radius = MathF.Min(
            packet.ViewportWidthDip * (packet.IsCompactCard ? 0.42f : 0.36f),
            packet.ViewportHeightDip * (packet.IsCompactCard ? 0.44f : 0.38f));
        return (cx, cy, Math.Max(radius, 42f));
    }

    private static float ComputeSpokeAngle(int index, int spokeCount, uint seed)
    {
        const float startAngleRad = -115f * (MathF.PI / 180f);
        var stepRad = (2f * MathF.PI) / spokeCount;
        var jitterDeg = ((((seed >> (index % 16)) & 0xF) / 15f) - 0.5f) * 8f;
        return startAngleRad + (index * stepRad) + (jitterDeg * (MathF.PI / 180f));
    }
}
