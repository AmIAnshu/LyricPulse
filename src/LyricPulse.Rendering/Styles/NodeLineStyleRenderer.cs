using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #8 — Node Line: smooth cubic Bezier melody ribbon drawing progressively across the viewport
/// (<c>LineProgress</c>), lighting glowing milestone nodes with alternating exterior above/below
/// word labels, expanding pulse rings, and silky <c>Spring.CameraDrift</c> horizontal panning on long lines.
/// </summary>
public sealed class NodeLineStyleRenderer : IKineticStyleRenderer
{
    public const float RippleDurationSeconds = 0.32f;

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _guidePathPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _progressGlowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8.0f, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _progressPathPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3.0f, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _nodeFillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _nodeRingPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f };
    private readonly SKPath _splinePath = new();
    private readonly SKPath _activePath = new();
    private readonly SKImageFilter _glowBlur = SKImageFilter.CreateBlur(8.0f, 8.0f);
    private readonly List<TimelineNode> _nodes = new(16);

    private SpringState _cameraPanX = SpringState.AtRest(0f);
    private SpringState _activeWordScale = SpringState.AtRest(1.06f);
    private float _rippleElapsedSec = RippleDurationSeconds;
    private float _highlightFade = 1.0f;
    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private float _lastWordDurationSec = 0.32f;
    private bool _disposed;

    public OverlayStyleId StyleId => OverlayStyleId.NodeLine;

    public void ResetState()
    {
        _cameraPanX = SpringState.AtRest(0f);
        _activeWordScale = SpringState.AtRest(1.06f);
        _rippleElapsedSec = RippleDurationSeconds;
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

        var key = (LineIndex: slice.ActiveLineIndex, WordIndex: activeIdx);
        if (key != _lastWordKey)
        {
            var lineChanged = _lastWordKey.LineIndex >= 0 && _lastWordKey.LineIndex != key.LineIndex;
            _lastWordKey = key;

            if (packet.ReducedMotion)
            {
                _cameraPanX = SpringState.AtRest(0f);
                _activeWordScale = SpringState.AtRest(1.00f);
                _rippleElapsedSec = RippleDurationSeconds;
                _highlightFade = 0f;
            }
            else
            {
                if (lineChanged)
                {
                    _cameraPanX = SpringState.AtRest(0f);
                }

                _activeWordScale = new SpringState(Value: 0.90f, Velocity: 0f, Target: 1.06f + ((packet.SafePartyEnergy - 1f) * 0.08f));
                _rippleElapsedSec = 0f;
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
            _cameraPanX = SpringState.AtRest(0f);
            _activeWordScale = SpringState.AtRest(1.00f);
            _rippleElapsedSec = RippleDurationSeconds;
            _aesthetics.ResetDot();
            return _highlightFade < 0.999f;
        }

        // Long-line camera drift keeps active node comfortably centered for >8-word lines
        var targetPan = wordCount > 8
            ? -Math.Max(0f, (activeIdx - 3) * (packet.ViewportWidthDip * 0.085f))
            : 0f;
        _cameraPanX = _cameraPanX.WithTarget(targetPan).Step(SpringTokens.CameraDrift, dt);
        _activeWordScale = _activeWordScale.Step(SpringTokens.WordSnap.WithTempoCompression(_lastWordDurationSec), dt);

        if (_rippleElapsedSec < RippleDurationSeconds)
        {
            _rippleElapsedSec = Math.Min(RippleDurationSeconds, _rippleElapsedSec + dt);
        }

        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        return !_cameraPanX.IsAtRest ||
               !_activeWordScale.IsAtRest ||
               _rippleElapsedSec < RippleDurationSeconds ||
               _highlightFade < 0.999f ||
               isPlaying;
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

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.54f);
        var spanW = Math.Clamp(packet.ViewportWidthDip * (packet.IsCompactCard ? 0.84f : 0.74f), 140f, packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas ? 1480f : 1180f);
        var waveAmp = packet.IsCompactCard ? 9f : Math.Clamp(fontSize * 0.42f, 12f, 26f);

        BuildTimelineNodes(slice.CurrentLine, anchorX, anchorY, spanW, waveAmp, font, context.Fonts, _nodes);
        if (_nodes.Count == 0)
        {
            return;
        }

        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, _nodes.Count - 1);
        var panX = packet.ReducedMotion ? 0f : _cameraPanX.Value;

        var scrimRect = new SKRect(anchorX - (spanW * 0.52f), anchorY - (fontSize * 2.1f), anchorX + (spanW * 0.52f), anchorY + (fontSize * 2.1f));
        context.Legibility.DrawAmbientScrim(
            context.Canvas,
            scrimRect,
            fontSize,
            packet.Palette.ScrimTint,
            packet.ScrimIntensity,
            packet.HighContrastScrim);

        var save = context.Canvas.Save();
        try
        {
            context.Canvas.Translate(panX, 0f);

            // 1. Base guide spline across all nodes
            BuildSmoothCubicPath(_nodes, _nodes.Count - 1, 1.0f, _splinePath);
            _guidePathPaint.Color = packet.Palette.AccentColor3.ToSkColorWithAlpha(0.28f);
            context.Canvas.DrawPath(_splinePath, _guidePathPaint);

            // 2. Illuminated active progress spline
            var segProgress = packet.ReducedMotion ? 1.0f : (float)Math.Clamp(slice.WordProgress, 0.0, 1.0);
            var cometPos = BuildSmoothCubicPath(_nodes, activeIdx, segProgress, _activePath);

            _progressGlowPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.36f);
            _progressGlowPaint.ImageFilter = _glowBlur;
            try
            {
                context.Canvas.DrawPath(_activePath, _progressGlowPaint);
            }
            finally
            {
                _progressGlowPaint.ImageFilter = null;
            }

            _progressPathPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.95f);
            context.Canvas.DrawPath(_activePath, _progressPathPaint);

            // 3. Milestone nodes & alternating exterior labels
            var highlightEased = Ease.Highlight(_highlightFade);
            for (var i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                var isActive = i == activeIdx;
                var isPast = i < activeIdx;

                if (isActive)
                {
                    if (!packet.ReducedMotion && _rippleElapsedSec < RippleDurationSeconds)
                    {
                        var rt = Ease.Enter(_rippleElapsedSec / RippleDurationSeconds);
                        var ringR = (packet.IsCompactCard ? 5f : 7f) + ((packet.IsCompactCard ? 10f : 16f) * rt * packet.SafePartyEnergy);
                        _nodeRingPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.68f * (1f - rt));
                        context.Canvas.DrawCircle(node.X, node.Y, ringR, _nodeRingPaint);
                    }

                    _nodeFillPaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(1.0f);
                    context.Canvas.DrawCircle(node.X, node.Y, packet.IsCompactCard ? 4.5f : 7.0f, _nodeFillPaint);
                    _nodeFillPaint.Color = SKColors.White;
                    context.Canvas.DrawCircle(node.X, node.Y, packet.IsCompactCard ? 2.0f : 3.2f, _nodeFillPaint);
                }
                else if (isPast)
                {
                    _nodeFillPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.88f);
                    context.Canvas.DrawCircle(node.X, node.Y, packet.IsCompactCard ? 3.2f : 5.0f, _nodeFillPaint);
                }
                else
                {
                    _nodeRingPaint.Color = packet.Palette.AccentColor3.ToSkColorWithAlpha(0.48f);
                    context.Canvas.DrawCircle(node.X, node.Y, packet.IsCompactCard ? 2.8f : 4.2f, _nodeRingPaint);
                }

                var isAbove = (i % 2) == 0;
                var lift = isActive && !packet.ReducedMotion ? 6.0f : 0f;
                var labelY = isAbove
                    ? node.Y - (font.Size * 0.78f) - lift
                    : node.Y + (font.Size * 1.12f) + lift;

                var labelScale = isActive
                    ? (packet.ReducedMotion ? 1.0f : _activeWordScale.Value)
                    : isPast ? 0.94f : 0.88f;
                var labelOpacity = isActive
                    ? (packet.ReducedMotion ? (0.90f + (0.10f * highlightEased)) : 1.0f)
                    : isPast ? 0.76f : 0.36f;
                var labelColor = isActive
                    ? StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor3, packet.Palette.AccentColor1, highlightEased)
                    : packet.Palette.AccentColor3;

                if (isActive && packet.ShowColorSweepLayer)
                {
                    var scaledW = node.MeasuredWidth * labelScale;
                    var sweepRect = new SKRect(node.X - (scaledW * 0.5f), labelY - (font.Size * 0.85f * labelScale), node.X + (scaledW * 0.5f), labelY + (font.Size * 0.16f * labelScale));
                    _aesthetics.DrawWordColorSweep(context.Canvas, sweepRect, slice.WordProgress, packet, labelOpacity);
                }

                var wordSave = context.Canvas.Save();
                try
                {
                    context.Canvas.Translate(node.X, labelY);
                    context.Canvas.Scale(labelScale, labelScale);
                    context.Legibility.DrawLegibleText(
                        context.Canvas,
                        node.Text,
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
                    context.Canvas.RestoreToCount(wordSave);
                }
            }

            // Following Pulse Dot glides cleanly along the spline comet head!
            _aesthetics.DrawFollowingDot(context.Canvas, packet, cometPos.X, cometPos.Y, Math.Clamp(fontSize * 0.14f, 3.2f, 7.5f));
        }
        finally
        {
            context.Canvas.RestoreToCount(save);
        }

        StyleLayoutHelper.DrawSecondarySubline(
            in context,
            slice.CurrentLine,
            anchorX,
            anchorY + waveAmp + (fontSize * 1.95f),
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
        _guidePathPaint.Dispose();
        _progressGlowPaint.Dispose();
        _progressPathPaint.Dispose();
        _nodeFillPaint.Dispose();
        _nodeRingPaint.Dispose();
        _splinePath.Dispose();
        _activePath.Dispose();
        _glowBlur.Dispose();
    }

    private static void BuildTimelineNodes(
        LyricLine line,
        float centerX,
        float baseY,
        float spanW,
        float waveAmp,
        SKFont font,
        FontAssetRegistry fonts,
        List<TimelineNode> destination)
    {
        destination.Clear();
        var words = line.Words;
        var count = words.IsEmpty ? (string.IsNullOrWhiteSpace(line.RawText) ? 0 : 1) : words.Length;
        if (count == 0)
        {
            return;
        }

        var left = centerX - (spanW * 0.5f);
        if (count == 1)
        {
            var singleText = words.IsEmpty ? line.RawText : words[0].Text;
            destination.Add(new TimelineNode(singleText, centerX, baseY, TextShaperService.MeasureWidth(singleText, font, fonts)));
            return;
        }

        // Lightly weight horizontal spacing by word length so long words enjoy wider horizontal clearance
        var totalWeight = 0f;
        Span<float> weights = count <= 32 ? stackalloc float[count] : new float[count];
        for (var i = 0; i < count; i++)
        {
            var text = words[i].Text;
            weights[i] = Math.Clamp(text.Length, 2, 12) + 3f;
            totalWeight += weights[i];
        }

        var effectiveSpan = count > 8 ? spanW * (count / 8f) : spanW;
        var cursorWeight = 0f;
        for (var i = 0; i < count; i++)
        {
            var text = words[i].Text;
            var centerWeight = cursorWeight + (weights[i] * 0.5f);
            cursorWeight += weights[i];

            var t = Math.Clamp(centerWeight / Math.Max(totalWeight, 1f), 0.04f, 0.96f);
            var x = left + (t * effectiveSpan);
            var y = baseY + ((i % 2 == 0) ? -waveAmp : +waveAmp);
            destination.Add(new TimelineNode(text, x, y, TextShaperService.MeasureWidth(text, font, fonts)));
        }
    }

    private static SKPoint BuildSmoothCubicPath(List<TimelineNode> nodes, int upToIndex, float segProgress, SKPath path)
    {
        path.Rewind();
        if (nodes.Count == 0)
        {
            return SKPoint.Empty;
        }

        path.MoveTo(nodes[0].X, nodes[0].Y);
        var maxIdx = Math.Clamp(upToIndex, 0, nodes.Count - 1);
        var lastPoint = new SKPoint(nodes[0].X, nodes[0].Y);

        for (var i = 0; i < maxIdx; i++)
        {
            var a = nodes[i];
            var b = nodes[i + 1];
            var midX = (a.X + b.X) * 0.5f;
            path.CubicTo(midX, a.Y, midX, b.Y, b.X, b.Y);
            lastPoint = new SKPoint(b.X, b.Y);
        }

        if (maxIdx + 1 < nodes.Count && segProgress > 0.001f)
        {
            var a = nodes[maxIdx];
            var b = nodes[maxIdx + 1];
            var p0 = new SKPoint(a.X, a.Y);
            var p1 = new SKPoint((a.X + b.X) * 0.5f, a.Y);
            var p2 = new SKPoint((a.X + b.X) * 0.5f, b.Y);
            var p3 = new SKPoint(b.X, b.Y);

            var t = Math.Clamp(segProgress, 0f, 1f);
            // De Casteljau split for exact partial cubic Bezier trace
            var q0 = Lerp(p0, p1, t);
            var q1 = Lerp(p1, p2, t);
            var q2 = Lerp(p2, p3, t);
            var r0 = Lerp(q0, q1, t);
            var r1 = Lerp(q1, q2, t);
            var s0 = Lerp(r0, r1, t);
            path.CubicTo(q0.X, q0.Y, r0.X, r0.Y, s0.X, s0.Y);
            lastPoint = s0;
        }

        return lastPoint;
    }

    private static SKPoint Lerp(SKPoint a, SKPoint b, float t) =>
        new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));

    private readonly record struct TimelineNode(string Text, float X, float Y, float MeasuredWidth);
}
