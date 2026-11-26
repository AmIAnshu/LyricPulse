using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #8 — Node Line Bioluminescent Neural Vine: multi-depth dual harmonic splines,
/// S-curve botanical branches with secondary leaflets &amp; bass-driven organic sway,
/// 3-pass blur-free glowing progress vein, multi-petal active blossom ripple, semantic
/// mood vector blooms, floating pollen spores, and previous/next context whispers.
/// </summary>
public sealed class NodeLineStyleRenderer : IKineticStyleRenderer
{
    public const float RippleDurationSeconds = 0.36f;

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _guidePathPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _progressPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _stemPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _nodeFillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _nodeRingPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };
    private readonly SKPath _splinePath = new();
    private readonly SKPath _harmonicPath = new();
    private readonly SKPath _activePath = new();
    private readonly SKPath _stemPath = new();
    private readonly SKPath _tendrilPath = new();
    private readonly List<BranchNode> _nodes = new(16);

    private SpringState _cameraPanX = SpringState.AtRest(0f);
    private SpringState _activeWordScale = SpringState.AtRest(1.14f);
    private float _rippleElapsedSec = RippleDurationSeconds;
    private float _highlightFade = 1.0f;
    private float _smoothedBass;
    private float _smoothedBeat;
    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private float _lastWordDurationSec = 0.32f;
    private bool _disposed;

    public OverlayStyleId StyleId => OverlayStyleId.NodeLine;

    public void ResetState()
    {
        _cameraPanX = SpringState.AtRest(0f);
        _activeWordScale = SpringState.AtRest(1.14f);
        _rippleElapsedSec = RippleDurationSeconds;
        _highlightFade = 1.0f;
        _smoothedBass = 0f;
        _smoothedBeat = 0f;
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
        var audio = packet.EffectiveAudioMetrics;
        _smoothedBass = (_smoothedBass * 0.72f) + (Math.Clamp(audio.BassEnergy, 0f, 1.5f) * 0.28f);
        _smoothedBeat = (_smoothedBeat * 0.65f) + (Math.Clamp(audio.BeatPulse, 0f, 1.5f) * 0.35f);

        var words = slice.CurrentLine.Words;
        var wordCount = words.IsEmpty ? 1 : words.Length;
        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, wordCount - 1);

        if (!words.IsEmpty)
        {
            var word = words[activeIdx];
            _lastWordDurationSec = Math.Clamp((float)(word.EndTime - word.StartTime).TotalSeconds, 0.08f, 2.5f);
        }

        var activeRestScale = packet.IsCompactCard ? 1.06f : 1.15f;
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

                var punchStart = activeRestScale - 0.22f;
                var overshootTarget = activeRestScale + ((packet.SafePartyEnergy - 1f) * 0.07f);
                _activeWordScale = new SpringState(Value: punchStart, Velocity: 0f, Target: overshootTarget);
                _rippleElapsedSec = 0f;
                _highlightFade = 0f;
            }
        }

        var fadeRate = packet.ReducedMotion ? (1f / 0.14f) : (1f / 0.15f);
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
        _activeWordScale = _activeWordScale.WithTarget(activeRestScale + ((packet.SafePartyEnergy - 1f) * 0.06f))
            .Step(SpringTokens.WordSnap.WithTempoCompression(_lastWordDurationSec), dt);

        if (_rippleElapsedSec < RippleDurationSeconds)
        {
            _rippleElapsedSec = Math.Min(RippleDurationSeconds, _rippleElapsedSec + dt);
        }

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.52f);
        var spanW = Math.Clamp(packet.ViewportWidthDip * (packet.IsCompactCard ? 0.84f : 0.78f), 140f, packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas ? 1520f : 1180f);
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
        var fontSize = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 0.64f);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.52f);
        var isFullStage = packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas && !packet.IsCompactCard;
        var spanW = Math.Clamp(packet.ViewportWidthDip * (packet.IsCompactCard ? 0.84f : 0.78f), 140f, packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas ? 1520f : 1180f);
        var waveAmp = packet.IsCompactCard ? 11f : Math.Clamp(fontSize * 0.54f, 15f, 34f);

        BuildBranchNodes(slice.CurrentLine, anchorX, anchorY, spanW, waveAmp, font, context.Fonts, packet.ReducedMotion, (float)packet.RenderTimeSeconds, _smoothedBass, _nodes);
        if (_nodes.Count == 0)
        {
            return;
        }

        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, _nodes.Count - 1);
        var panX = packet.ReducedMotion ? 0f : _cameraPanX.Value;

        var scrimHalfW = spanW * (isFullStage ? 0.54f : 0.42f);
        var scrimHalfH = fontSize * (isFullStage ? 2.6f : 1.75f);
        var scrimRect = new SKRect(anchorX - scrimHalfW, anchorY - scrimHalfH, anchorX + scrimHalfW, anchorY + scrimHalfH);
        context.Legibility.DrawAmbientScrim(
            context.Canvas,
            scrimRect,
            fontSize,
            packet.Palette.ScrimTint,
            packet.ScrimIntensity,
            packet.HighContrastScrim);

        // Previous & Next line whispers on full StageCanvas
        if (isFullStage)
        {
            DrawLineContextWhispers(in context, font, fontId, slice, anchorX, anchorY, waveAmp, fontSize);
        }

        var save = context.Canvas.Save();
        try
        {
            context.Canvas.Translate(panX, 0f);

            // 1. Secondary harmonic counter-vine (organic dual filament behind spine)
            if (packet.SurfaceMode != StageRenderSurfaceMode.DesktopOverlay && _nodes.Count > 1)
            {
                BuildHarmonicCounterSpline(_nodes, (float)packet.RenderTimeSeconds, _smoothedBass, packet.ReducedMotion, _harmonicPath);
                _guidePathPaint.StrokeWidth = 1.15f;
                _guidePathPaint.Color = packet.Palette.AccentColor3.ToSkColorWithAlpha(0.16f + (_smoothedBass * 0.08f));
                context.Canvas.DrawPath(_harmonicPath, _guidePathPaint);
            }

            // 2. Primary undulating botanical vine guide (subtle halo + filament)
            BuildSmoothCubicPath(_nodes, _nodes.Count - 1, 1.0f, _splinePath);
            _guidePathPaint.StrokeWidth = 5.5f;
            _guidePathPaint.Color = packet.Palette.AccentColor3.ToSkColorWithAlpha(0.08f);
            context.Canvas.DrawPath(_splinePath, _guidePathPaint);

            _guidePathPaint.StrokeWidth = 1.5f;
            _guidePathPaint.Color = packet.Palette.AccentColor3.ToSkColorWithAlpha(0.28f);
            context.Canvas.DrawPath(_splinePath, _guidePathPaint);

            // 3. 3-Pass blur-free glowing active energy vine traced smoothly to active progress tip
            var segProgress = packet.ReducedMotion ? 1.0f : (float)Math.Clamp(slice.WordProgress, 0.0, 1.0);
            var cometPos = BuildSmoothCubicPath(_nodes, activeIdx, segProgress, _activePath);

            _progressPaint.StrokeWidth = (packet.IsCompactCard ? 7.5f : 12.0f) + (_smoothedBeat * 5.0f);
            _progressPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.16f + (_smoothedBeat * 0.12f));
            context.Canvas.DrawPath(_activePath, _progressPaint);

            _progressPaint.StrokeWidth = (packet.IsCompactCard ? 3.5f : 5.2f) + (_smoothedBeat * 2.0f);
            _progressPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.44f);
            context.Canvas.DrawPath(_activePath, _progressPaint);

            _progressPaint.StrokeWidth = packet.IsCompactCard ? 1.8f : 2.5f;
            _progressPaint.Color = StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor1, SKColors.White.ToRgb(), 0.35f).ToSkColorWithAlpha(0.95f);
            context.Canvas.DrawPath(_activePath, _progressPaint);

            // Bioluminescent sap flow head halo at live progress point
            if (!packet.ReducedMotion && _nodes.Count > 1)
            {
                _nodeFillPaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(0.32f + (_smoothedBeat * 0.25f));
                context.Canvas.DrawCircle(cometPos, packet.IsCompactCard ? 5.5f : 8.5f, _nodeFillPaint);
                _nodeFillPaint.Color = SKColors.White.WithAlpha(220);
                context.Canvas.DrawCircle(cometPos, packet.IsCompactCard ? 2.0f : 3.0f, _nodeFillPaint);
            }

            // 4. Floating pollen firefly spores on StageCanvas (never near DesktopOverlay corners)
            if (isFullStage && !packet.ReducedMotion)
            {
                DrawPollenSpores(context.Canvas, packet, anchorX, anchorY, spanW, waveAmp);
            }

            // 5. Curved S-stems, side leaflet tendrils, multi-petal blossoms & word labels
            var highlightEased = Ease.Highlight(_highlightFade);
            for (var i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                var isActive = i == activeIdx;
                var isPast = i < activeIdx;

                DrawBotanicalBranch(context.Canvas, node, i, isActive, isPast, packet, highlightEased);

                var labelY = node.IsUpperBranch
                    ? node.TipY - (font.Size * 0.62f)
                    : node.TipY + (font.Size * 1.10f);
                var labelScale = isActive
                    ? (packet.ReducedMotion ? 1.0f : _activeWordScale.Value)
                    : isPast ? 0.92f : 0.85f;
                var labelOpacity = isActive
                    ? (packet.ReducedMotion ? (0.92f + (0.08f * highlightEased)) : 1.0f)
                    : isPast ? 0.78f : 0.35f;
                var labelColor = isActive
                    ? StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor1, SKColors.White.ToRgb(), 0.48f * highlightEased)
                    : isPast ? packet.Palette.AccentColor1 : packet.Palette.AccentColor3;

                if (isActive && packet.ShowColorSweepLayer)
                {
                    var scaledW = node.MeasuredWidth * labelScale;
                    var sweepRect = new SKRect(node.TipX - (scaledW * 0.5f), labelY - (font.Size * 0.85f * labelScale), node.TipX + (scaledW * 0.5f), labelY + (font.Size * 0.16f * labelScale));
                    _aesthetics.DrawWordColorSweep(context.Canvas, sweepRect, slice.WordProgress, packet, labelOpacity);
                }

                // Semantic mood vector visual blossoming gently opposite active branch tip
                if (isActive && node.Visual is { } matchedVisual)
                {
                    var badgeOffsetY = node.IsUpperBranch ? -font.Size * 1.48f : font.Size * 1.62f;
                    var badgeSize = Math.Clamp(font.Size * (packet.IsCompactCard ? 0.58f : 0.72f), 18f, 38f);
                    var badgeX = Math.Clamp(node.TipX, 34f, packet.ViewportWidthDip - 34f);
                    var badgeY = Math.Clamp(node.TipY + badgeOffsetY, 34f, packet.ViewportHeightDip - 34f);
                    _aesthetics.DrawReactiveVisual(context.Canvas, in context, matchedVisual, badgeX, badgeY, badgeSize, labelOpacity, isActive: true);
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
            anchorY + waveAmp + (fontSize * 2.35f),
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
        _progressPaint.Dispose();
        _stemPaint.Dispose();
        _nodeFillPaint.Dispose();
        _nodeRingPaint.Dispose();
        _splinePath.Dispose();
        _harmonicPath.Dispose();
        _activePath.Dispose();
        _stemPath.Dispose();
        _tendrilPath.Dispose();
    }

    private static void DrawLineContextWhispers(
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        Core.Runtime.ActiveLyricSlice slice,
        float anchorX,
        float anchorY,
        float waveAmp,
        float fontSize)
    {
        var canvas = context.Canvas;
        var packet = context.Packet;

        if (slice.PreviousLine is not null)
        {
            var prevSave = canvas.Save();
            try
            {
                canvas.Translate(anchorX - (packet.ViewportWidthDip * 0.14f), anchorY - waveAmp - (fontSize * 2.15f));
                canvas.Scale(0.48f, 0.48f);
                context.Legibility.DrawLegibleText(
                    canvas,
                    slice.PreviousLine.RawText,
                    0f,
                    0f,
                    SKTextAlign.Center,
                    font,
                    fontId,
                    packet.Palette.AccentColor3,
                    opacity: 0.22f,
                    fonts: context.Fonts);
            }
            finally
            {
                canvas.RestoreToCount(prevSave);
            }
        }

        if (slice.NextLine is not null && !slice.NextLine.IsInstrumentalBreak)
        {
            var nextSave = canvas.Save();
            try
            {
                canvas.Translate(anchorX + (packet.ViewportWidthDip * 0.14f), anchorY + waveAmp + (fontSize * 2.05f));
                canvas.Scale(0.46f, 0.46f);
                context.Legibility.DrawLegibleText(
                    canvas,
                    slice.NextLine.RawText,
                    0f,
                    0f,
                    SKTextAlign.Center,
                    font,
                    fontId,
                    packet.Palette.AccentColor1,
                    opacity: 0.23f,
                    fonts: context.Fonts);
            }
            finally
            {
                canvas.RestoreToCount(nextSave);
            }
        }
    }

    private void DrawBotanicalBranch(
        SKCanvas canvas,
        BranchNode node,
        int index,
        bool isActive,
        bool isPast,
        FrameRenderPacket packet,
        float highlightEased)
    {
        // Graceful S-curve stem from spine joint -> blossom tip
        var dx = node.TipX - node.X;
        var dy = node.TipY - node.Y;
        _stemPath.Rewind();
        _stemPath.MoveTo(node.X, node.Y);
        _stemPath.CubicTo(
            node.X,
            node.Y + (dy * 0.55f),
            node.TipX - (dx * 0.18f),
            node.Y + (dy * 0.72f),
            node.TipX,
            node.TipY);

        if (isActive)
        {
            _stemPaint.StrokeWidth = (packet.IsCompactCard ? 4.0f : 5.8f) + (_smoothedBeat * 2.0f);
            _stemPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.28f);
            canvas.DrawPath(_stemPath, _stemPaint);
        }

        _stemPaint.StrokeWidth = isActive ? 2.2f : (isPast ? 1.5f : 1.0f);
        _stemPaint.Color = isActive
            ? packet.Palette.AccentColor1.ToSkColorWithAlpha(0.95f)
            : isPast
                ? packet.Palette.AccentColor2.ToSkColorWithAlpha(0.65f)
                : packet.Palette.AccentColor3.ToSkColorWithAlpha(0.24f);
        canvas.DrawPath(_stemPath, _stemPaint);

        // Secondary delicate side leaflet tendril on active & past branches
        if ((isActive || isPast) && !packet.IsCompactCard)
        {
            var forkX = node.X + (dx * 0.42f);
            var forkY = node.Y + (dy * 0.56f);
            var sideDir = (index % 2 == 0) ? -1f : 1f;
            var leafTipX = forkX + (sideDir * 14f);
            var leafTipY = forkY + (dy * 0.22f);

            _tendrilPath.Rewind();
            _tendrilPath.MoveTo(forkX, forkY);
            _tendrilPath.QuadTo(forkX + (sideDir * 8f), forkY + (dy * 0.04f), leafTipX, leafTipY);

            _stemPaint.StrokeWidth = isActive ? 1.4f : 1.0f;
            _stemPaint.Color = (isActive ? packet.Palette.AccentColor1 : packet.Palette.AccentColor3).ToSkColorWithAlpha(isActive ? 0.65f : 0.36f);
            canvas.DrawPath(_tendrilPath, _stemPaint);

            _nodeFillPaint.Color = (isActive ? packet.Palette.AccentColor1 : packet.Palette.AccentColor2).ToSkColorWithAlpha(isActive ? 0.80f : 0.42f);
            canvas.DrawCircle(leafTipX, leafTipY, isActive ? 2.2f : 1.6f, _nodeFillPaint);
        }

        // Spine joint pearl
        _nodeFillPaint.Color = isActive
            ? SKColors.White
            : isPast
                ? packet.Palette.AccentColor1.ToSkColorWithAlpha(0.88f)
                : packet.Palette.AccentColor3.ToSkColorWithAlpha(0.38f);
        canvas.DrawCircle(node.X, node.Y, isActive ? (packet.IsCompactCard ? 3.2f : 4.8f) : (packet.IsCompactCard ? 1.8f : 2.8f), _nodeFillPaint);

        // Blossom crown at branch tip
        if (isActive)
        {
            if (!packet.ReducedMotion && _rippleElapsedSec < RippleDurationSeconds)
            {
                var rt = Ease.Enter(_rippleElapsedSec / RippleDurationSeconds);
                var ringR = (packet.IsCompactCard ? 5f : 8f) + ((packet.IsCompactCard ? 11f : 20f) * rt * packet.SafePartyEnergy);
                _nodeRingPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.72f * (1f - rt));
                canvas.DrawCircle(node.TipX, node.TipY, ringR, _nodeRingPaint);
            }

            // Soft blossom outer aura + 4-petal cross star shimmer + luminous core
            var auraR = (packet.IsCompactCard ? 7.5f : 12.0f) + (_smoothedBeat * 5.0f);
            _nodeFillPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.26f + (_smoothedBeat * 0.16f));
            canvas.DrawCircle(node.TipX, node.TipY, auraR, _nodeFillPaint);

            if (!packet.IsCompactCard)
            {
                var petalLen = (7.5f + (_smoothedBeat * 3.5f)) * (0.7f + (0.3f * highlightEased));
                _stemPaint.StrokeWidth = 1.3f;
                _stemPaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(0.68f);
                canvas.DrawLine(node.TipX - petalLen, node.TipY, node.TipX + petalLen, node.TipY, _stemPaint);
                canvas.DrawLine(node.TipX, node.TipY - petalLen, node.TipX, node.TipY + petalLen, _stemPaint);
            }

            _nodeFillPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.95f);
            canvas.DrawCircle(node.TipX, node.TipY, packet.IsCompactCard ? 4.2f : 6.2f, _nodeFillPaint);
            _nodeFillPaint.Color = SKColors.White;
            canvas.DrawCircle(node.TipX, node.TipY, packet.IsCompactCard ? 1.9f : 3.0f, _nodeFillPaint);
        }
        else
        {
            if (isPast)
            {
                _nodeRingPaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(0.45f);
                canvas.DrawCircle(node.TipX, node.TipY, packet.IsCompactCard ? 3.6f : 5.0f, _nodeRingPaint);
            }

            _nodeFillPaint.Color = (isPast ? packet.Palette.AccentColor2 : packet.Palette.AccentColor3).ToSkColorWithAlpha(isPast ? 0.78f : 0.36f);
            canvas.DrawCircle(node.TipX, node.TipY, packet.IsCompactCard ? 2.2f : 3.3f, _nodeFillPaint);
        }
    }

    private void DrawPollenSpores(
        SKCanvas canvas,
        FrameRenderPacket packet,
        float centerX,
        float centerY,
        float spanW,
        float waveAmp)
    {
        var time = (float)packet.RenderTimeSeconds;
        var halfSpan = spanW * 0.48f;
        var trebleLift = packet.EffectiveAudioMetrics.TrebleEnergy * 0.35f;

        for (var p = 0; p < 7; p++)
        {
            var phase = (p * 1.37f) + (time * (0.38f + (p * 0.05f)));
            var px = centerX + (MathF.Sin(phase * 0.73f) * halfSpan * 0.88f);
            var py = centerY + (MathF.Cos(phase * 1.15f) * waveAmp * 1.75f);
            var alpha = Math.Clamp(0.14f + (0.18f * (0.5f + (0.5f * MathF.Sin(phase * 2.1f)))) + trebleLift, 0.08f, 0.55f);
            var color = (p % 2 == 0) ? packet.Palette.AccentColor1 : packet.Palette.AccentColor2;

            _nodeFillPaint.Color = color.ToSkColorWithAlpha(alpha * 0.35f);
            canvas.DrawCircle(px, py, 5.5f + (_smoothedBeat * 3.0f), _nodeFillPaint);
            _nodeFillPaint.Color = color.ToSkColorWithAlpha(alpha);
            canvas.DrawCircle(px, py, 2.0f, _nodeFillPaint);
        }
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
        float timeSec,
        float smoothedBass,
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
            var singleVisual = words.IsEmpty ? null : words[0].Visual;
            destination.Add(new BranchNode(singleText, centerX, baseY, centerX + 12f, baseY - waveAmp, true, TextShaperService.MeasureWidth(singleText, font, fonts), singleVisual));
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

        var effectiveSpan = !reducedMotion && count > 8 ? spanW * (count / 8f) : spanW;
        var left = centerX - (effectiveSpan * 0.5f);
        var cursorWeight = 0f;
        var stepX = effectiveSpan / Math.Max(count, 1);

        for (var i = 0; i < count; i++)
        {
            var word = words[i];
            var text = word.Text;
            var centerWeight = cursorWeight + (weights[i] * 0.5f);
            cursorWeight += weights[i];

            var t = Math.Clamp(centerWeight / Math.Max(totalWeight, 1f), 0.04f, 0.96f);
            var x = left + (t * effectiveSpan);
            var harmonicOffset = reducedMotion ? 0f : MathF.Sin((timeSec * 1.4f) + (i * 0.85f)) * (2.5f + (smoothedBass * 4.5f));
            var spineY = baseY + (MathF.Sin((t * MathF.Tau) + (line.Index * 0.9f)) * waveAmp * 0.45f) + (harmonicOffset * 0.35f);
            var isUpper = (i % 2) == 0;
            var reachY = waveAmp * (1.18f + ((((seed >> (i % 16)) & 0x7) / 7f) * 0.56f));
            var tipY = spineY + (isUpper ? -reachY : +reachY) + harmonicOffset;
            var tipX = x + ((i % 2 == 0 ? 1f : -1f) * Math.Min(stepX * 0.25f, 30f));

            destination.Add(new BranchNode(text, x, spineY, tipX, tipY, isUpper, TextShaperService.MeasureWidth(text, font, fonts), word.Visual));
        }
    }

    private static void BuildHarmonicCounterSpline(
        List<BranchNode> nodes,
        float timeSec,
        float smoothedBass,
        bool reducedMotion,
        SKPath path)
    {
        path.Rewind();
        if (nodes.Count < 2)
        {
            return;
        }

        var ampOffset = (reducedMotion ? 6f : 10f + (smoothedBass * 8f));
        float OffsetAt(int idx) => MathF.Sin((idx * 1.3f) + (reducedMotion ? 0f : timeSec * 1.6f)) * ampOffset;

        path.MoveTo(nodes[0].X, nodes[0].Y + OffsetAt(0));
        for (var i = 0; i < nodes.Count - 1; i++)
        {
            var a = nodes[i];
            var b = nodes[i + 1];
            var ya = a.Y + OffsetAt(i);
            var yb = b.Y + OffsetAt(i + 1);
            var midX = (a.X + b.X) * 0.5f;
            path.CubicTo(midX, ya, midX, yb, b.X, yb);
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

    private readonly record struct BranchNode(
        string Text,
        float X,
        float Y,
        float TipX,
        float TipY,
        bool IsUpperBranch,
        float MeasuredWidth,
        WordVisualMatch? Visual);
}
