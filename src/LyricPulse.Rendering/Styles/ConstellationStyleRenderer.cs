using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #6 — Constellation (Verci-inspired): wide airy celestial starfield with a faint
/// ghosted previous-line constellation drifting in depth, pinched 4-point diamond stars,
/// luminous gossamer connecting threads, upright word labels, and word-locked pulse dot hopping.
/// </summary>
public sealed class ConstellationStyleRenderer : IKineticStyleRenderer
{
    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _threadPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _threadGlowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _starPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath _diamondStarPath = new();
    private readonly SKImageFilter _glowBlur = SKImageFilter.CreateBlur(7.0f, 7.0f);
    private readonly List<StarNode> _nodes = new(16);
    private readonly List<StarNode> _ghostNodes = new(16);

    private SpringState _flareScale = SpringState.AtRest(1.0f);
    private SpringState _labelLift = SpringState.AtRest(0f);
    private float _highlightFade = 1.0f;
    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private float _lastWordDurationSec = 0.32f;
    private bool _disposed;

    public OverlayStyleId StyleId => OverlayStyleId.Constellation;

    public void ResetState()
    {
        _flareScale = SpringState.AtRest(1.0f);
        _labelLift = SpringState.AtRest(0f);
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
        var activeWordIdx = slice.CurrentLine.Words.IsEmpty
            ? 0
            : Math.Clamp(slice.ActiveWordIndex, 0, slice.CurrentLine.Words.Length - 1);

        if (!slice.CurrentLine.Words.IsEmpty)
        {
            var word = slice.CurrentLine.Words[activeWordIdx];
            _lastWordDurationSec = Math.Clamp((float)(word.EndTime - word.StartTime).TotalSeconds, 0.08f, 2.5f);
        }

        var key = (slice.ActiveLineIndex, activeWordIdx);
        if (key != _lastWordKey)
        {
            _lastWordKey = key;
            if (packet.ReducedMotion)
            {
                _flareScale = SpringState.AtRest(1.0f);
                _labelLift = SpringState.AtRest(0f);
                _highlightFade = 0f;
            }
            else
            {
                _flareScale = new SpringState(Value: 0.35f, Velocity: 0f, Target: 1.08f + ((packet.SafePartyEnergy - 1.0f) * 0.25f));
                _labelLift = new SpringState(Value: 7.5f, Velocity: 0f, Target: 0f);
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
            _flareScale = SpringState.AtRest(1.0f);
            _labelLift = SpringState.AtRest(0f);
            _aesthetics.ResetDot();
            return _highlightFade < 0.999f;
        }

        var iconSpring = SpringTokens.IconPop.WithTempoCompression(_lastWordDurationSec);
        var wordSpring = SpringTokens.WordEnter.WithTempoCompression(_lastWordDurationSec);
        _flareScale = _flareScale.Step(iconSpring, dt);
        _labelLift = _labelLift.Step(wordSpring, dt);

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.48f);
        var regionW = Math.Clamp(packet.ViewportWidthDip * (packet.IsCompactCard ? 0.88f : 0.78f), 140f, packet.ViewportWidthDip - 24f);
        var regionH = Math.Clamp(packet.ViewportHeightDip * (packet.IsCompactCard ? 0.72f : 0.54f), 90f, packet.ViewportHeightDip - 24f);
        var region = StyleLayoutHelper.ClampLayerRectToViewport(anchorX, anchorY, regionW, regionH, packet.ViewportWidthDip, packet.ViewportHeightDip);

        var words = slice.CurrentLine.Words;
        var count = words.IsEmpty ? 1 : words.Length;
        var (nx, ny) = EstimateStarAnchor(slice.CurrentLine, activeWordIdx, count, region);
        var dotActive = packet.ShowFollowingDot && _aesthetics.AdvanceDot(key, nx, ny, dt, reducedMotion: false, _lastWordDurationSec);

        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        return !_flareScale.IsAtRest || !_labelLift.IsAtRest || dotActive || _highlightFade < 0.999f || isPlaying;
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

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.48f);
        var regionW = Math.Clamp(packet.ViewportWidthDip * (packet.IsCompactCard ? 0.88f : 0.78f), 140f, packet.ViewportWidthDip - 24f);
        var regionH = Math.Clamp(packet.ViewportHeightDip * (packet.IsCompactCard ? 0.72f : 0.54f), 90f, packet.ViewportHeightDip - 24f);
        var region = StyleLayoutHelper.ClampLayerRectToViewport(anchorX, anchorY, regionW, regionH, packet.ViewportWidthDip, packet.ViewportHeightDip);

        BuildDeterministicStarfield(slice.CurrentLine, region, font, context.Fonts, _nodes, spreadScale: 1.0f);
        if (_nodes.Count == 0)
        {
            return;
        }

        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, _nodes.Count - 1);
        context.Legibility.DrawAmbientScrim(
            context.Canvas,
            region,
            fontSize,
            packet.Palette.ScrimTint,
            packet.ScrimIntensity,
            packet.HighContrastScrim);

        // Layer 0.5: Subtle ghosted previous-line constellation drifting in deep background (Verci craft)
        if (!packet.IsCompactCard && slice.PreviousLine is not null)
        {
            var ghostRegion = new SKRect(region.Left - 26f, Math.Max(16f, region.Top - 32f), region.Right - 40f, region.Bottom - 26f);
            BuildDeterministicStarfield(slice.PreviousLine, ghostRegion, font, context.Fonts, _ghostNodes, spreadScale: 0.86f);
            DrawGhostConstellation(context.Canvas, packet);
        }

        var traceProgress = packet.ReducedMotion ? 1.0f : (float)Math.Clamp(slice.WordProgress / 0.55, 0.0, 1.0);
        DrawConstellationThreads(context.Canvas, packet, activeIdx, traceProgress);

        var highlightEased = Ease.Highlight(_highlightFade);
        for (var i = 0; i < _nodes.Count; i++)
        {
            var node = _nodes[i];
            var isPast = i < activeIdx;
            var isActive = i == activeIdx;
            DrawStarAndLabel(in context, font, fontId, node, isPast, isActive, highlightEased, slice.WordProgress);
        }

        if (!packet.ReducedMotion && packet.ShowFollowingDot)
        {
            var activeStar = _nodes[activeIdx];
            _aesthetics.DrawFollowingDot(context.Canvas, packet, activeStar.X, activeStar.Y, Math.Clamp(fontSize * 0.14f, 3.0f, 6.5f));
        }

        StyleLayoutHelper.DrawSecondarySubline(
            in context,
            slice.CurrentLine,
            region.MidX,
            region.Bottom + Math.Max(14f, fontSize * 0.55f),
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
        _threadPaint.Dispose();
        _threadGlowPaint.Dispose();
        _starPaint.Dispose();
        _diamondStarPath.Dispose();
        _glowBlur.Dispose();
    }

    private void DrawGhostConstellation(SKCanvas canvas, FrameRenderPacket packet)
    {
        if (_ghostNodes.Count < 2)
        {
            return;
        }

        _threadPaint.StrokeWidth = 0.85f;
        _threadPaint.Color = packet.Palette.AccentColor3.ToSkColorWithAlpha(0.14f);
        for (var i = 1; i < _ghostNodes.Count; i++)
        {
            canvas.DrawLine(_ghostNodes[i - 1].X, _ghostNodes[i - 1].Y, _ghostNodes[i].X, _ghostNodes[i].Y, _threadPaint);
        }

        foreach (var node in _ghostNodes)
        {
            DrawPinchedStar(canvas, node.X, node.Y, 3.4f, packet.Palette.AccentColor3.ToSkColorWithAlpha(0.22f));
        }
    }

    private void DrawConstellationThreads(SKCanvas canvas, FrameRenderPacket packet, int activeIdx, float traceProgress)
    {
        if (_nodes.Count < 2)
        {
            return;
        }

        var energy = packet.SafePartyEnergy;
        for (var i = 1; i < _nodes.Count; i++)
        {
            var a = _nodes[i - 1];
            var b = _nodes[i];

            if (packet.ReducedMotion)
            {
                var lit = i <= activeIdx;
                _threadPaint.StrokeWidth = lit ? 1.6f : 0.95f;
                _threadPaint.Color = (lit ? packet.Palette.AccentColor2 : packet.Palette.AccentColor3).ToSkColorWithAlpha(lit ? 0.72f : 0.24f);
                canvas.DrawLine(a.X, a.Y, b.X, b.Y, _threadPaint);
                continue;
            }

            if (i < activeIdx)
            {
                DrawGlowingSegment(canvas, a.X, a.Y, b.X, b.Y, packet.Palette.AccentColor1, 0.56f, 1.35f);
            }
            else if (i == activeIdx)
            {
                var endX = a.X + ((b.X - a.X) * traceProgress);
                var endY = a.Y + ((b.Y - a.Y) * traceProgress);
                DrawGlowingSegment(canvas, a.X, a.Y, endX, endY, packet.Palette.AccentColor1, 0.92f, 1.85f * Math.Clamp(energy, 0.85f, 1.3f));
            }
            else
            {
                _threadPaint.StrokeWidth = 0.85f;
                _threadPaint.Color = packet.Palette.AccentColor3.ToSkColorWithAlpha(0.18f);
                canvas.DrawLine(a.X, a.Y, b.X, b.Y, _threadPaint);
            }
        }
    }

    private void DrawGlowingSegment(SKCanvas canvas, float x1, float y1, float x2, float y2, RgbColor color, float alpha, float strokeW)
    {
        _threadGlowPaint.StrokeWidth = strokeW * 3.4f;
        _threadGlowPaint.Color = color.ToSkColorWithAlpha(alpha * 0.34f);
        _threadGlowPaint.ImageFilter = _glowBlur;
        try
        {
            canvas.DrawLine(x1, y1, x2, y2, _threadGlowPaint);
        }
        finally
        {
            _threadGlowPaint.ImageFilter = null;
        }

        _threadPaint.StrokeWidth = strokeW;
        _threadPaint.Color = color.ToSkColorWithAlpha(alpha);
        canvas.DrawLine(x1, y1, x2, y2, _threadPaint);
    }

    private void DrawStarAndLabel(
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        StarNode node,
        bool isPast,
        bool isActive,
        float highlightEased,
        double wordProgress)
    {
        var canvas = context.Canvas;
        var packet = context.Packet;
        var audioLift = packet.EffectiveAudioMetrics.BeatPulse * 0.25f;
        var flare = (packet.ReducedMotion ? 1.0f : _flareScale.Value) * (isActive ? (1.0f + audioLift) : 1.0f);

        if (isActive)
        {
            var auraR = (packet.IsCompactCard ? 8f : 14f) * flare;
            _starPaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(0.55f);
            _starPaint.ImageFilter = _glowBlur;
            try
            {
                canvas.DrawCircle(node.X, node.Y, auraR, _starPaint);
            }
            finally
            {
                _starPaint.ImageFilter = null;
            }

            var starRadius = (packet.IsCompactCard ? 9.5f : 15.5f) * flare;
            DrawPinchedStar(canvas, node.X, node.Y, starRadius, SKColors.White);
        }
        else if (isPast)
        {
            var pastRadius = packet.IsCompactCard ? 5.5f : 8.5f;
            DrawPinchedStar(canvas, node.X, node.Y, pastRadius, packet.Palette.AccentColor1.ToSkColorWithAlpha(0.88f));
        }
        else
        {
            DrawPinchedStar(canvas, node.X, node.Y, packet.IsCompactCard ? 3.2f : 4.6f, packet.Palette.AccentColor3.ToSkColorWithAlpha(0.45f));
        }

        var liftY = isActive && !packet.ReducedMotion ? _labelLift.Value : 0f;
        var labelY = node.Y + (font.Size * 1.08f) + liftY;
        var halfW = node.MeasuredWidth * 0.5f;
        var clampedX = Math.Clamp(node.X, halfW + 12f, Math.Max(halfW + 12f, packet.ViewportWidthDip - halfW - 12f));
        var clampedY = Math.Clamp(labelY, font.Size + 8f, Math.Max(font.Size + 8f, packet.ViewportHeightDip - 12f));

        var opacity = isActive
            ? (packet.ReducedMotion ? (0.90f + (0.10f * highlightEased)) : 1.0f)
            : isPast ? 0.72f : 0.34f;
        var color = isActive
            ? StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor1, SKColors.White.ToRgb(), 0.45f * highlightEased)
            : packet.Palette.AccentColor3;

        if (isActive && packet.ShowColorSweepLayer)
        {
            var sweepRect = new SKRect(clampedX - halfW, clampedY - (font.Size * 0.85f), clampedX + halfW, clampedY + (font.Size * 0.16f));
            _aesthetics.DrawWordColorSweep(canvas, sweepRect, wordProgress, packet, opacity);
        }

        var scale = isActive ? 1.06f : 0.90f;
        var save = canvas.Save();
        try
        {
            canvas.Translate(clampedX, clampedY);
            canvas.Scale(scale, scale);
            context.Legibility.DrawLegibleText(
                canvas,
                node.Text,
                0f,
                0f,
                SKTextAlign.Center,
                font,
                fontId,
                color,
                opacity: opacity,
                activeGlowColor: isActive ? packet.Palette.AccentColor2 : null,
                fonts: context.Fonts);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    private void DrawPinchedStar(SKCanvas canvas, float cx, float cy, float radius, SKColor color)
    {
        var inner = radius * 0.24f;
        _diamondStarPath.Rewind();
        _diamondStarPath.MoveTo(cx, cy - radius);
        _diamondStarPath.QuadTo(cx + inner, cy - inner, cx + radius, cy);
        _diamondStarPath.QuadTo(cx + inner, cy + inner, cx, cy + radius);
        _diamondStarPath.QuadTo(cx - inner, cy + inner, cx - radius, cy);
        _diamondStarPath.QuadTo(cx - inner, cy - inner, cx, cy - radius);
        _diamondStarPath.Close();

        _starPaint.Color = color;
        canvas.DrawPath(_diamondStarPath, _starPaint);
    }

    private static (float X, float Y) EstimateStarAnchor(LyricLine line, int idx, int count, SKRect region)
    {
        var seed = StyleLayoutHelper.DeterministicHash(line.Index, line.RawText);
        var xFrac = count <= 1 ? 0.50f : 0.08f + ((idx / (float)(count - 1)) * 0.84f);
        var hashShift = (((seed >> ((idx * 3) % 24)) & 0xF) / 15f) - 0.5f;
        var verticalSwing = (((idx % 2) == 0 ? -0.24f : 0.24f) + (hashShift * 0.28f));
        var x = region.Left + (region.Width * xFrac);
        var y = region.MidY + (region.Height * verticalSwing);
        return (x, y);
    }

    private static void BuildDeterministicStarfield(
        LyricLine line,
        SKRect region,
        SKFont font,
        FontAssetRegistry fonts,
        List<StarNode> destination,
        float spreadScale)
    {
        destination.Clear();
        var words = line.Words;
        var count = words.IsEmpty ? (string.IsNullOrWhiteSpace(line.RawText) ? 0 : 1) : words.Length;
        if (count == 0)
        {
            return;
        }

        var seed = StyleLayoutHelper.DeterministicHash(line.Index, line.RawText);
        var minDist = Math.Max(font.Size * 2.15f * spreadScale, 40f);
        var padX = Math.Clamp(region.Width * 0.08f, 16f, 44f);
        var padY = Math.Clamp(region.Height * 0.14f, 14f, 38f);
        var usableW = Math.Max(40f, region.Width - (padX * 2f));
        var usableH = Math.Max(30f, region.Height - (padY * 2f));

        for (var i = 0; i < count; i++)
        {
            var text = words.IsEmpty ? line.RawText : words[i].Text;
            var xFrac = count == 1 ? 0.50f : (i / (float)(count - 1));
            var hVal = (seed >> ((i * 5) % 24)) & 0xFFu;
            var yBias = ((i % 2 == 0) ? -0.34f : 0.34f) + ((((hVal & 0xF) / 15f) - 0.5f) * 0.30f);
            var xJitter = ((((hVal >> 4) & 0xF) / 15f) - 0.5f) * (usableW / Math.Max(count * 3.0f, 3f));

            var x = Math.Clamp(region.Left + padX + (xFrac * usableW) + xJitter, region.Left + padX, region.Right - padX);
            var y = Math.Clamp(region.MidY + (yBias * usableH), region.Top + padY, region.Bottom - padY);
            var measured = TextShaperService.MeasureWidth(text, font, fonts);
            destination.Add(new StarNode(text, x, y, measured));
        }

        for (var iter = 0; iter < 5; iter++)
        {
            for (var i = 0; i < destination.Count; i++)
            {
                for (var j = i + 1; j < destination.Count; j++)
                {
                    var a = destination[i];
                    var b = destination[j];
                    var dx = b.X - a.X;
                    var dy = b.Y - a.Y;
                    var dist = MathF.Sqrt((dx * dx) + (dy * dy));
                    if (dist < minDist && dist > 0.001f)
                    {
                        var push = (minDist - dist) * 0.35f;
                        var nx = dx / dist;
                        var ny = dy / dist;
                        destination[i] = a with { Y = Math.Clamp(a.Y - (ny * push), region.Top + padY, region.Bottom - padY), X = Math.Clamp(a.X - (nx * push * 0.3f), region.Left + padX, region.Right - padX) };
                        destination[j] = b with { Y = Math.Clamp(b.Y + (ny * push), region.Top + padY, region.Bottom - padY), X = Math.Clamp(b.X + (nx * push * 0.3f), region.Left + padX, region.Right - padX) };
                    }
                }
            }
        }
    }

    private readonly record struct StarNode(string Text, float X, float Y, float MeasuredWidth);
}
