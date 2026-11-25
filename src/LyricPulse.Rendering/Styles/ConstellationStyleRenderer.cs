using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #6 — Constellation: deterministic seeded celestial starfield per lyric line with
/// force-relaxed non-overlapping star nodes, luminous aurora connecting threads traced word-by-word
/// with a shooting spark, and 4-point diffraction flares (<c>Spring.IconPop</c>).
/// </summary>
public sealed class ConstellationStyleRenderer : IKineticStyleRenderer
{
    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _threadPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _threadGlowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _starPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _spikePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKImageFilter _glowBlur = SKImageFilter.CreateBlur(6.0f, 6.0f);
    private readonly List<StarNode> _nodes = new(16);

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
                _flareScale = new SpringState(Value: 0.25f, Velocity: 0f, Target: 1.0f + ((packet.SafePartyEnergy - 1.0f) * 0.25f));
                _labelLift = new SpringState(Value: 7.0f, Velocity: 0f, Target: 0f);
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

        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        return !_flareScale.IsAtRest || !_labelLift.IsAtRest || _highlightFade < 0.999f || isPlaying;
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
        var fontSize = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 0.66f);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.48f);
        var regionW = Math.Clamp(packet.ViewportWidthDip * (packet.IsCompactCard ? 0.86f : 0.72f), 140f, packet.ViewportWidthDip - 24f);
        var regionH = Math.Clamp(packet.ViewportHeightDip * (packet.IsCompactCard ? 0.68f : 0.48f), 90f, packet.ViewportHeightDip - 24f);
        var region = StyleLayoutHelper.ClampLayerRectToViewport(anchorX, anchorY, regionW, regionH, packet.ViewportWidthDip, packet.ViewportHeightDip);

        BuildDeterministicStarfield(slice.CurrentLine, region, font, context.Fonts, _nodes);
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
            float dotX = activeStar.X;
            float dotY = activeStar.Y;
            if (activeIdx > 0 && traceProgress < 1.0f)
            {
                var prevStar = _nodes[activeIdx - 1];
                dotX = prevStar.X + ((activeStar.X - prevStar.X) * traceProgress);
                dotY = prevStar.Y + ((activeStar.Y - prevStar.Y) * traceProgress);
            }

            _aesthetics.DrawFollowingDot(context.Canvas, packet, dotX, dotY, Math.Clamp(fontSize * 0.14f, 3.0f, 7.0f));
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
        _spikePaint.Dispose();
        _glowBlur.Dispose();
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
                _threadPaint.StrokeWidth = lit ? 1.8f : 1.1f;
                _threadPaint.Color = (lit ? packet.Palette.AccentColor2 : packet.Palette.AccentColor3).ToSkColorWithAlpha(lit ? 0.78f : 0.28f);
                canvas.DrawLine(a.X, a.Y, b.X, b.Y, _threadPaint);
                continue;
            }

            if (i < activeIdx)
            {
                DrawGlowingSegment(canvas, a.X, a.Y, b.X, b.Y, packet.Palette.AccentColor2, 0.72f, 1.7f);
            }
            else if (i == activeIdx)
            {
                var endX = a.X + ((b.X - a.X) * traceProgress);
                var endY = a.Y + ((b.Y - a.Y) * traceProgress);
                DrawGlowingSegment(canvas, a.X, a.Y, endX, endY, packet.Palette.AccentColor1, 0.92f, 2.0f * Math.Clamp(energy, 0.8f, 1.3f));
            }
            else
            {
                _threadPaint.StrokeWidth = 1.0f;
                _threadPaint.Color = packet.Palette.AccentColor3.ToSkColorWithAlpha(0.18f);
                canvas.DrawLine(a.X, a.Y, b.X, b.Y, _threadPaint);
            }
        }
    }

    private void DrawGlowingSegment(SKCanvas canvas, float x1, float y1, float x2, float y2, RgbColor color, float alpha, float strokeW)
    {
        _threadGlowPaint.StrokeWidth = strokeW * 3.2f;
        _threadGlowPaint.Color = color.ToSkColorWithAlpha(alpha * 0.35f);
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
        var flare = packet.ReducedMotion ? 1.0f : _flareScale.Value;

        if (isActive)
        {
            // Outer aura + 4-point diffraction spikes + white-hot core
            var auraR = (packet.IsCompactCard ? 7f : 12f) * flare;
            _starPaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(0.75f);
            _starPaint.ImageFilter = _glowBlur;
            try
            {
                canvas.DrawCircle(node.X, node.Y, auraR, _starPaint);
            }
            finally
            {
                _starPaint.ImageFilter = null;
            }

            var spikeLen = (packet.IsCompactCard ? 10f : 18f) * flare;
            _spikePaint.StrokeWidth = 1.3f;
            _spikePaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.85f);
            canvas.DrawLine(node.X - spikeLen, node.Y, node.X + spikeLen, node.Y, _spikePaint);
            canvas.DrawLine(node.X, node.Y - spikeLen, node.X, node.Y + spikeLen, _spikePaint);

            _starPaint.Color = SKColors.White;
            canvas.DrawCircle(node.X, node.Y, packet.IsCompactCard ? 3.5f : 5.2f, _starPaint);
        }
        else if (isPast)
        {
            _starPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.45f);
            canvas.DrawCircle(node.X, node.Y, packet.IsCompactCard ? 5f : 8f, _starPaint);
            _starPaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(0.85f);
            canvas.DrawCircle(node.X, node.Y, packet.IsCompactCard ? 2.5f : 3.8f, _starPaint);
        }
        else
        {
            _starPaint.Color = packet.Palette.AccentColor3.ToSkColorWithAlpha(0.45f);
            canvas.DrawCircle(node.X, node.Y, packet.IsCompactCard ? 1.8f : 2.6f, _starPaint);
        }

        // Word label: upcoming faint, active bright bloom, past settled readable star-map
        var labelOpacity = isActive
            ? (packet.ReducedMotion ? (0.90f + (0.10f * highlightEased)) : 1.0f)
            : isPast ? 0.78f : 0.34f;
        var labelColor = isActive
            ? StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor3, packet.Palette.AccentColor1, highlightEased)
            : packet.Palette.AccentColor3;

        var lift = isActive && !packet.ReducedMotion ? _labelLift.Value : 0f;
        var labelY = node.LabelAbove
            ? node.Y - (font.Size * 0.65f) - lift
            : node.Y + (font.Size * 1.10f) + lift;

        if (isActive && packet.ShowColorSweepLayer)
        {
            var sweepRect = new SKRect(node.X - (node.MeasuredWidth * 0.5f), labelY - (font.Size * 0.85f), node.X + (node.MeasuredWidth * 0.5f), labelY + (font.Size * 0.18f));
            _aesthetics.DrawWordColorSweep(canvas, sweepRect, wordProgress, packet, labelOpacity);
        }

        context.Legibility.DrawLegibleText(
            canvas,
            node.Text,
            node.X,
            labelY,
            SKTextAlign.Center,
            font,
            fontId,
            labelColor,
            opacity: labelOpacity,
            activeGlowColor: isActive ? packet.Palette.AccentColor2 : null,
            fonts: context.Fonts);
    }

    internal static void BuildDeterministicStarfield(
        LyricLine line,
        SKRect region,
        SKFont font,
        FontAssetRegistry fonts,
        List<StarNode> destination)
    {
        destination.Clear();
        var count = line.Words.IsEmpty ? (string.IsNullOrWhiteSpace(line.RawText) ? 0 : 1) : line.Words.Length;
        if (count == 0)
        {
            return;
        }

        var seed = StyleLayoutHelper.DeterministicHash(line.Index, line.RawText);
        var padX = Math.Min(region.Width * 0.12f, font.Size * 1.8f);
        var padY = Math.Min(region.Height * 0.22f, font.Size * 1.4f);
        var usableW = Math.Max(40f, region.Width - (padX * 2f));
        var usableH = Math.Max(30f, region.Height - (padY * 2f));

        for (var i = 0; i < count; i++)
        {
            var text = line.Words.IsEmpty ? line.RawText : line.Words[i].Text;
            var measured = TextShaperService.MeasureWidth(text, font, fonts);
            var h = StyleLayoutHelper.DeterministicHash((int)seed + (i * 31), text);
            var jitterX = (((h & 0xFF) / 255f) - 0.5f) * 0.26f;
            var waveY = (((h >> 8) & 0xFF) / 255f);

            var fracX = count == 1 ? 0.5f : (i / (float)(count - 1));
            var x = region.Left + padX + (Math.Clamp(fracX + (jitterX / Math.Max(count, 2)), 0.04f, 0.96f) * usableW);
            var altBias = (i % 2 == 0) ? 0.28f : 0.72f;
            var y = region.Top + padY + (Math.Clamp((altBias * 0.65f) + (waveY * 0.35f), 0.12f, 0.88f) * usableH);

            destination.Add(new StarNode(text, x, y, measured, LabelAbove: i % 2 == 0));
        }

        // 8-iteration force-relaxation pass guarantees stars & word labels never overlap
        var minGapBase = font.Size * 1.85f;
        for (var iter = 0; iter < 8; iter++)
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
                    var minDist = Math.Max(minGapBase, ((a.MeasuredWidth + b.MeasuredWidth) * 0.36f));
                    if (dist < minDist && dist > 0.001f)
                    {
                        var push = (minDist - dist) * 0.32f;
                        var nx = dx / dist;
                        var ny = dy / dist;
                        destination[i] = a with
                        {
                            X = Math.Clamp(a.X - (nx * push), region.Left + padX * 0.6f, region.Right - padX * 0.6f),
                            Y = Math.Clamp(a.Y - (ny * push), region.Top + padY * 0.6f, region.Bottom - padY * 0.6f)
                        };
                        destination[j] = b with
                        {
                            X = Math.Clamp(b.X + (nx * push), region.Left + padX * 0.6f, region.Right - padX * 0.6f),
                            Y = Math.Clamp(b.Y + (ny * push), region.Top + padY * 0.6f, region.Bottom - padY * 0.6f)
                        };
                    }
                }
            }
        }
    }

    internal readonly record struct StarNode(string Text, float X, float Y, float MeasuredWidth, bool LabelAbove);
}
