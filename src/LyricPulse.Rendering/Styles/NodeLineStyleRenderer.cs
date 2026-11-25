using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #8 — Node Line / Branches (Verci-inspired): undulating botanical cubic-Bezier spine
/// drawing smoothly across the viewport, sprouting curved upper/lower branch stems with illuminated
/// milestone blossoms, word-locked pulse dot (zero running jitter), and wrapping long lines cleanly
/// under Reduced Motion.
/// </summary>
public sealed class NodeLineStyleRenderer : IKineticStyleRenderer
{
    public const float RippleDurationSeconds = 0.32f;

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _guidePathPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _progressGlowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8.0f, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _progressPathPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.6f, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _stemPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _nodeFillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _nodeRingPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
    private readonly SKPath _splinePath = new();
    private readonly SKPath _activePath = new();
    private readonly SKPath _stemPath = new();
    private readonly SKImageFilter _glowBlur = SKImageFilter.CreateBlur(8.0f, 8.0f);
    private readonly List<BranchNode> _nodes = new(16);

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

                _activeWordScale = new SpringState(Value: 0.90f, Velocity: 0f, Target: 1.08f + ((packet.SafePartyEnergy - 1f) * 0.08f));
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

        var targetPan = wordCount > 8
            ? -Math.Max(0f, (activeIdx - 3) * (packet.ViewportWidthDip * 0.075f))
            : 0f;
        _cameraPanX = _cameraPanX.WithTarget(targetPan).Step(SpringTokens.CameraDrift, dt);
        _activeWordScale = _activeWordScale.Step(SpringTokens.WordSnap.WithTempoCompression(_lastWordDurationSec), dt);

        if (_rippleElapsedSec < RippleDurationSeconds)
        {
            _rippleElapsedSec = Math.Min(RippleDurationSeconds, _rippleElapsedSec + dt);
        }

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.52f);
        var spanW = Math.Clamp(packet.ViewportWidthDip * (packet.IsCompactCard ? 0.84f : 0.76f), 140f, packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas ? 1520f : 1180f);
        var tFrac = wordCount <= 1 ? 0.5f : (activeIdx / (float)(wordCount - 1));
        var approxDotX = anchorX - (spanW * 0.5f) + (tFrac * spanW) + _cameraPanX.Value;
        var approxDotY = anchorY;
        var dotActive = packet.ShowFollowingDot && _aesthetics.AdvanceDot(key, approxDotX, approxDotY, dt, reducedMotion: false, _lastWordDurationSec);

        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        return !_cameraPanX.IsAtRest ||
               !_activeWordScale.IsAtRest ||
               dotActive ||
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
        var fontSize = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 0.62f);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.52f);
        var spanW = Math.Clamp(packet.ViewportWidthDip * (packet.IsCompactCard ? 0.84f : 0.76f), 140f, packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas ? 1520f : 1180f);
        var waveAmp = packet.IsCompactCard ? 10f : Math.Clamp(fontSize * 0.48f, 14f, 30f);

        BuildBranchNodes(slice.CurrentLine, anchorX, anchorY, spanW, waveAmp, font, context.Fonts, packet.ReducedMotion, _nodes);
        if (_nodes.Count == 0)
        {
            return;
        }

        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, _nodes.Count - 1);
        var panX = packet.ReducedMotion ? 0f : _cameraPanX.Value;

        var scrimRect = new SKRect(anchorX - (spanW * 0.54f), anchorY - (fontSize * 2.5f), anchorX + (spanW * 0.54f), anchorY + (fontSize * 2.5f));
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

            // 1. Undulating main botanical vine guide
            BuildSmoothCubicPath(_nodes, _nodes.Count - 1, 1.0f, _splinePath);
            _guidePathPaint.Color = packet.Palette.AccentColor3.ToSkColorWithAlpha(0.24f);
            context.Canvas.DrawPath(_splinePath, _guidePathPaint);

            // 2. Illuminated progress vine traced smoothly to current node
            var segProgress = packet.ReducedMotion ? 1.0f : (float)Math.Clamp(slice.WordProgress, 0.0, 1.0);
            var cometPos = BuildSmoothCubicPath(_nodes, activeIdx, segProgress, _activePath);

            _progressGlowPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.38f);
            _progressGlowPaint.ImageFilter = _glowBlur;
            try
            {
                context.Canvas.DrawPath(_activePath, _progressGlowPaint);
            }
            finally
            {
                _progressGlowPaint.ImageFilter = null;
            }

            _progressPathPaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(0.92f);
            context.Canvas.DrawPath(_activePath, _progressPathPaint);

            // 3. Curved sprouting side-branches + blossom nodes + upright word labels
            var highlightEased = Ease.Highlight(_highlightFade);
            for (var i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                var isActive = i == activeIdx;
                var isPast = i < activeIdx;

                // Curved botanical tendril from spine joint (X, Y) -> tip blossom (TipX, TipY)
                _stemPath.Rewind();
                _stemPath.MoveTo(node.X, node.Y);
                var ctrlY = node.Y + ((node.TipY - node.Y) * 0.65f);
                _stemPath.QuadTo(node.X, ctrlY, node.TipX, node.TipY);

                _stemPaint.StrokeWidth = isActive ? 2.1f : (isPast ? 1.4f : 0.95f);
                _stemPaint.Color = isActive
                    ? packet.Palette.AccentColor1.ToSkColorWithAlpha(0.92f)
                    : isPast
                        ? packet.Palette.AccentColor2.ToSkColorWithAlpha(0.62f)
                        : packet.Palette.AccentColor3.ToSkColorWithAlpha(0.22f);
                context.Canvas.DrawPath(_stemPath, _stemPaint);

                // Spine joint bead
                _nodeFillPaint.Color = isActive
                    ? SKColors.White
                    : isPast
                        ? packet.Palette.AccentColor1.ToSkColorWithAlpha(0.85f)
                        : packet.Palette.AccentColor3.ToSkColorWithAlpha(0.38f);
                context.Canvas.DrawCircle(node.X, node.Y, isActive ? (packet.IsCompactCard ? 3.2f : 4.6f) : (packet.IsCompactCard ? 1.8f : 2.8f), _nodeFillPaint);

                // Blossom tip orb
                if (isActive)
                {
                    if (!packet.ReducedMotion && _rippleElapsedSec < RippleDurationSeconds)
                    {
                        var rt = Ease.Enter(_rippleElapsedSec / RippleDurationSeconds);
                        var ringR = (packet.IsCompactCard ? 5f : 7f) + ((packet.IsCompactCard ? 10f : 16f) * rt * packet.SafePartyEnergy);
                        _nodeRingPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.68f * (1f - rt));
                        context.Canvas.DrawCircle(node.TipX, node.TipY, ringR, _nodeRingPaint);
                    }

                    _nodeFillPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.95f);
                    context.Canvas.DrawCircle(node.TipX, node.TipY, packet.IsCompactCard ? 4.2f : 6.2f, _nodeFillPaint);
                    _nodeFillPaint.Color = SKColors.White;
                    context.Canvas.DrawCircle(node.TipX, node.TipY, packet.IsCompactCard ? 1.8f : 2.8f, _nodeFillPaint);
                }
                else
                {
                    _nodeFillPaint.Color = (isPast ? packet.Palette.AccentColor2 : packet.Palette.AccentColor3).ToSkColorWithAlpha(isPast ? 0.72f : 0.35f);
                    context.Canvas.DrawCircle(node.TipX, node.TipY, packet.IsCompactCard ? 2.2f : 3.2f, _nodeFillPaint);
                }

                var labelY = node.IsUpperBranch
                    ? node.TipY - (font.Size * 0.58f)
                    : node.TipY + (font.Size * 1.05f);
                var labelScale = isActive
                    ? (packet.ReducedMotion ? 1.0f : _activeWordScale.Value)
                    : isPast ? 0.94f : 0.88f;
                var labelOpacity = isActive
                    ? (packet.ReducedMotion ? (0.90f + (0.10f * highlightEased)) : 1.0f)
                    : isPast ? 0.76f : 0.34f;
                var labelColor = isActive
                    ? StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor1, SKColors.White.ToRgb(), 0.40f * highlightEased)
                    : packet.Palette.AccentColor3;

                if (isActive && packet.ShowColorSweepLayer)
                {
                    var scaledW = node.MeasuredWidth * labelScale;
                    var sweepRect = new SKRect(node.TipX - (scaledW * 0.5f), labelY - (font.Size * 0.85f * labelScale), node.TipX + (scaledW * 0.5f), labelY + (font.Size * 0.16f * labelScale));
                    _aesthetics.DrawWordColorSweep(context.Canvas, sweepRect, slice.WordProgress, packet, labelOpacity);
                }

                var wordSave = context.Canvas.Save();
                try
                {
                    context.Canvas.Translate(node.TipX, labelY);
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

            _aesthetics.DrawFollowingDot(context.Canvas, packet, _nodes[activeIdx].X, _nodes[activeIdx].Y, Math.Clamp(fontSize * 0.14f, 3.0f, 6.8f));
        }
        finally
        {
            context.Canvas.RestoreToCount(save);
        }

        StyleLayoutHelper.DrawSecondarySubline(
            in context,
            slice.CurrentLine,
            anchorX,
            anchorY + waveAmp + (fontSize * 2.25f),
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
        _stemPaint.Dispose();
        _nodeFillPaint.Dispose();
        _nodeRingPaint.Dispose();
        _splinePath.Dispose();
        _activePath.Dispose();
        _stemPath.Dispose();
        _glowBlur.Dispose();
    }

    private static void BuildBranchNodes(
        LyricLine line,
        float centerX,
        float baseY,
        float spanW,
        float waveAmp,
        SKFont font,
        FontAssetRegistry fonts,
        bool reducedMotion,
        List<BranchNode> destination)
    {
        destination.Clear();
        var words = line.Words;
        var count = words.IsEmpty ? (string.IsNullOrWhiteSpace(line.RawText) ? 0 : 1) : words.Length;
        if (count == 0)
        {
            return;
        }

        var seed = StyleLayoutHelper.DeterministicHash(line.Index, line.RawText);
        if (count == 1)
        {
            var singleText = words.IsEmpty ? line.RawText : words[0].Text;
            destination.Add(new BranchNode(singleText, centerX, baseY, centerX + 12f, baseY - waveAmp, true, TextShaperService.MeasureWidth(singleText, font, fonts)));
            return;
        }

        var totalWeight = 0f;
        Span<float> weights = count <= 32 ? stackalloc float[count] : new float[count];
        for (var i = 0; i < count; i++)
        {
            var text = words[i].Text;
            weights[i] = Math.Clamp(text.Length, 2, 12) + 3f;
            totalWeight += weights[i];
        }

        // Clamp span cleanly inside viewport when ReducedMotion disables camera pan (UI-16)
        var effectiveSpan = !reducedMotion && count > 8 ? spanW * (count / 8f) : spanW;
        var left = centerX - (effectiveSpan * 0.5f);
        var cursorWeight = 0f;
        var stepX = effectiveSpan / Math.Max(count, 1);

        for (var i = 0; i < count; i++)
        {
            var text = words[i].Text;
            var centerWeight = cursorWeight + (weights[i] * 0.5f);
            cursorWeight += weights[i];

            var t = Math.Clamp(centerWeight / Math.Max(totalWeight, 1f), 0.04f, 0.96f);
            var x = left + (t * effectiveSpan);
            var spineY = baseY + (MathF.Sin((t * MathF.Tau) + (line.Index * 0.9f)) * waveAmp * 0.45f);
            var isUpper = (i % 2) == 0;
            var reachY = waveAmp * (1.15f + ((((seed >> (i % 16)) & 0x7) / 7f) * 0.55f));
            var tipY = spineY + (isUpper ? -reachY : +reachY);
            var tipX = x + ((i % 2 == 0 ? 1f : -1f) * Math.Min(stepX * 0.24f, 28f));

            destination.Add(new BranchNode(text, x, spineY, tipX, tipY, isUpper, TextShaperService.MeasureWidth(text, font, fonts)));
        }
    }

    private static SKPoint BuildSmoothCubicPath(List<BranchNode> nodes, int upToIndex, float segProgress, SKPath path)
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

    private readonly record struct BranchNode(string Text, float X, float Y, float TipX, float TipY, bool IsUpperBranch, float MeasuredWidth);
}
