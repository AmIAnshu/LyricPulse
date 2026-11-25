using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #9 — Kinetic Wall / Ship (Verci-inspired): organic staggered word cascade floating on a
/// 3D-tilted pinhole perspective plane (<see cref="SKMatrix"/> homography), depth-drifting ghost lines,
/// inverted tilted Verci paper cutout highlights on focal words, and word-locked beat dot.
/// </summary>
public sealed class KineticWallStyleRenderer : IKineticStyleRenderer
{
    public const float RestingPitchDegrees = 5.5f;
    public const float MaxYawDegrees = 10.0f;
    public const float MaxRollDegrees = 1.5f;
    public const float PerspectiveFocalLength = 1150f;

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _accentRulePaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    private SpringState _cameraX = SpringState.AtRest(0f);
    private SpringState _cameraY = SpringState.AtRest(0f);
    private SpringState _cameraYawDeg = SpringState.AtRest(-6.5f);
    private SpringState _wordSlamScale = SpringState.AtRest(1.0f);
    private float _highlightFade = 1.0f;
    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private float _lastWordDurationSec = 0.32f;
    private bool _disposed;

    public OverlayStyleId StyleId => OverlayStyleId.KineticWall;

    public void ResetState()
    {
        _cameraX = SpringState.AtRest(0f);
        _cameraY = SpringState.AtRest(0f);
        _cameraYawDeg = SpringState.AtRest(-6.5f);
        _wordSlamScale = SpringState.AtRest(1.0f);
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

        var wordFrac = wordCount <= 1 ? 0.5f : (activeIdx / (float)(wordCount - 1));
        var targetYaw = ((wordFrac - 0.5f) * 2f) * MaxYawDegrees * Math.Clamp(packet.SafePartyEnergy, 0.7f, 1.25f);
        var targetCamX = (0.5f - wordFrac) * Math.Min(packet.ViewportWidthDip * 0.12f, 95f);

        var key = (slice.ActiveLineIndex, activeIdx);
        if (key != _lastWordKey)
        {
            _lastWordKey = key;
            if (packet.ReducedMotion)
            {
                _wordSlamScale = SpringState.AtRest(1.0f);
                _cameraX = SpringState.AtRest(0f);
                _cameraY = SpringState.AtRest(0f);
                _cameraYawDeg = SpringState.AtRest(0f);
                _highlightFade = 0f;
            }
            else
            {
                var slamStart = 1.20f + ((packet.SafePartyEnergy - 1.0f) * 0.12f);
                _wordSlamScale = new SpringState(Value: slamStart, Velocity: 0f, Target: 1.00f);
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
            _wordSlamScale = SpringState.AtRest(1.0f);
            _cameraX = SpringState.AtRest(0f);
            _cameraY = SpringState.AtRest(0f);
            _cameraYawDeg = SpringState.AtRest(0f);
            _aesthetics.ResetDot();
            return _highlightFade < 0.999f;
        }

        _cameraX = _cameraX.WithTarget(targetCamX).Step(SpringTokens.CameraDrift, dt);
        _cameraY = _cameraY.WithTarget(0f).Step(SpringTokens.CameraDrift, dt);
        _cameraYawDeg = _cameraYawDeg.WithTarget(Math.Clamp(targetYaw, -MaxYawDegrees, MaxYawDegrees)).Step(SpringTokens.CameraDrift, dt);
        _wordSlamScale = _wordSlamScale.Step(SpringTokens.WordEnter.WithTempoCompression(_lastWordDurationSec), dt);

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.50f);
        var fontSize = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 0.78f);
        var rowSpreadX = packet.ViewportWidthDip * (packet.IsCompactCard ? 0.22f : 0.14f);
        var staggerFrac = ComputeCascadeOffsetFrac(activeIdx, slice.ActiveLineIndex);
        var approxX = anchorX + (staggerFrac * rowSpreadX) + _cameraX.Value;
        var approxY = anchorY + (fontSize * 0.6f);
        var dotActive = packet.ShowFollowingDot && _aesthetics.AdvanceDot(key, approxX, approxY, dt, reducedMotion: false, _lastWordDurationSec);

        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        return !_cameraX.IsAtRest ||
               !_cameraY.IsAtRest ||
               !_cameraYawDeg.IsAtRest ||
               !_wordSlamScale.IsAtRest ||
               dotActive ||
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
        var fontSize = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 0.78f);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.50f);

        var scrimRect = new SKRect(
            anchorX - (packet.ViewportWidthDip * 0.42f),
            anchorY - (packet.ViewportHeightDip * 0.34f),
            anchorX + (packet.ViewportWidthDip * 0.42f),
            anchorY + (packet.ViewportHeightDip * 0.34f));

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
            if (!packet.ReducedMotion)
            {
                var rollDeg = Math.Clamp((_cameraYawDeg.Value / MaxYawDegrees) * MaxRollDegrees, -MaxRollDegrees, MaxRollDegrees);
                var perspective = Create3DPerspectiveMatrix(
                    RestingPitchDegrees,
                    _cameraYawDeg.Value,
                    rollDeg,
                    PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f),
                    anchorX,
                    anchorY);
                context.Canvas.Concat(in perspective);
                context.Canvas.Translate(_cameraX.Value, _cameraY.Value);
            }

            DrawScatteredWordCascade(in context, font, fontId, slice, anchorX, anchorY);
        }
        finally
        {
            context.Canvas.RestoreToCount(save);
        }

        StyleLayoutHelper.DrawSecondarySubline(
            in context,
            slice.CurrentLine,
            anchorX,
            anchorY + (fontSize * 2.8f),
            fontSize);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _aesthetics.Dispose();
        _accentRulePaint.Dispose();
    }

    internal static SKMatrix Create3DPerspectiveMatrix(
        float pitchDeg,
        float yawDeg,
        float rollDeg,
        float focalLength,
        float originX,
        float originY)
    {
        var f = Math.Max(focalLength, 200f);
        var rx = pitchDeg * (MathF.PI / 180f);
        var ry = yawDeg * (MathF.PI / 180f);
        var rz = rollDeg * (MathF.PI / 180f);

        var cx = MathF.Cos(rx);
        var sx = MathF.Sin(rx);
        var cy = MathF.Cos(ry);
        var sy = MathF.Sin(ry);
        var cz = MathF.Cos(rz);
        var sz = MathF.Sin(rz);

        var r00 = cz * cy;
        var r01 = (cz * sy * sx) - (sz * cx);
        var r10 = sz * cy;
        var r11 = (sz * sy * sx) + (cz * cx);
        var r20 = -sy;
        var r21 = cy * sx;

        var p0 = -r20 / f;
        var p1 = -r21 / f;

        var homography = new SKMatrix(
            r00, r01, 0f,
            r10, r11, 0f,
            p0,  p1,  1f);

        var toOrigin = SKMatrix.CreateTranslation(-originX, -originY);
        var fromOrigin = SKMatrix.CreateTranslation(originX, originY);
        return SKMatrix.Concat(fromOrigin, SKMatrix.Concat(homography, toOrigin));
    }

    private void DrawScatteredWordCascade(
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        Core.Runtime.ActiveLyricSlice slice,
        float centerX,
        float centerY)
    {
        var packet = context.Packet;
        var line = slice.CurrentLine!;
        var words = line.Words;
        var highlightEased = Ease.Highlight(_highlightFade);

        font.GetFontMetrics(out var metrics);
        var baselineOffset = -((metrics.Ascent + metrics.Descent) * 0.5f);

        if (words.IsEmpty)
        {
            var measured = TextShaperService.MeasureWidth(line.RawText, font, context.Fonts);
            var fit = TextShaperService.ComputeAutoFitScale(measured, packet.ViewportWidthDip * 0.80f, 0.94f);
            var save = context.Canvas.Save();
            try
            {
                context.Canvas.Translate(centerX, centerY);
                context.Canvas.Scale(fit, fit);
                context.Legibility.DrawLegibleText(context.Canvas, line.RawText, 0f, baselineOffset, SKTextAlign.Center, font, fontId, SKColors.White.ToRgb(), 1f, packet.Palette.AccentColor2, fonts: context.Fonts);
            }
            finally
            {
                context.Canvas.RestoreToCount(save);
            }

            return;
        }

        // Show up to 5 visible cascade words centered around active word (inspired by Verci's "Ship": hum / when / you / [walk] / in)
        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, words.Length - 1);
        var windowRadius = packet.IsCompactCard ? 1 : 2;
        var startIdx = Math.Max(0, activeIdx - 3);
        var endIdx = Math.Min(words.Length - 1, activeIdx + windowRadius);
        var visibleCount = (endIdx - startIdx) + 1;
        var slotPitch = font.Size * (packet.IsCompactCard ? 1.18f : 1.28f);
        var spreadX = packet.ViewportWidthDip * (packet.IsCompactCard ? 0.14f : 0.12f);

        // Soft ghosted previous line floating at upper-right
        if (!packet.IsCompactCard && slice.PreviousLine is not null)
        {
            var ghostSave = context.Canvas.Save();
            try
            {
                context.Canvas.Translate(centerX + (spreadX * 0.9f), centerY - (slotPitch * 2.2f));
                context.Canvas.Scale(0.55f, 0.55f);
                context.Legibility.DrawLegibleText(
                    context.Canvas,
                    slice.PreviousLine.RawText,
                    0f,
                    baselineOffset,
                    SKTextAlign.Center,
                    font,
                    fontId,
                    packet.Palette.AccentColor3,
                    opacity: 0.24f,
                    fonts: context.Fonts);
            }
            finally
            {
                context.Canvas.RestoreToCount(ghostSave);
            }
        }

        var dotX = centerX;
        var dotY = centerY;

        for (var i = startIdx; i <= endIdx; i++)
        {
            var word = words[i];
            var relRow = (i - startIdx) - (visibleCount - 1) * 0.5f;
            var offsetFrac = ComputeCascadeOffsetFrac(i, line.Index);
            var wordX = centerX + (offsetFrac * spreadX);
            var wordY = centerY + (relRow * slotPitch);

            var isActive = i == activeIdx;
            var isPast = i < activeIdx;
            var opacity = isActive
                ? (packet.ReducedMotion ? (0.92f + (0.08f * highlightEased)) : 1.0f)
                : isPast ? 0.72f : 0.34f;
            var scale = isActive && !packet.ReducedMotion ? _wordSlamScale.Value : (isPast ? 0.92f : 0.86f);
            var measured = TextShaperService.MeasureWidth(word.Text, font, context.Fonts);
            var fit = TextShaperService.ComputeAutoFitScale(measured * scale, packet.ViewportWidthDip * 0.74f, 1.0f);
            var finalScale = scale * fit;
            var clampedX = Math.Clamp(wordX, (measured * finalScale * 0.5f) + 18f, Math.Max((measured * finalScale * 0.5f) + 18f, packet.ViewportWidthDip - (measured * finalScale * 0.5f) - 18f));

            if (isActive)
            {
                dotX = clampedX - (measured * finalScale * 0.5f) + (measured * finalScale * (float)Math.Clamp(slice.WordProgress, 0.0, 1.0));
                dotY = wordY + (font.Size * finalScale * 0.62f);

                if (packet.ShowColorSweepLayer)
                {
                    var sweepRect = new SKRect(clampedX - (measured * finalScale * 0.5f), wordY - (font.Size * finalScale * 0.52f), clampedX + (measured * finalScale * 0.5f), wordY + (font.Size * finalScale * 0.52f));
                    _aesthetics.DrawWordColorSweep(context.Canvas, sweepRect, slice.WordProgress, packet, opacity);
                }
            }

            var useCutout = isActive && (word.Visual is not null || word.Text.Length >= 4);
            if (useCutout)
            {
                var tilt = ((i % 2 == 0) ? -1.8f : 1.8f);
                var cutoutSave = context.Canvas.Save();
                try
                {
                    context.Canvas.Translate(clampedX, wordY);
                    context.Canvas.Scale(finalScale, finalScale);
                    _aesthetics.DrawInvertedCutoutWord(context.Canvas, in context, font, word.Text, 0f, 0f, measured, tilt, opacity);
                }
                finally
                {
                    context.Canvas.RestoreToCount(cutoutSave);
                }

                continue;
            }

            var color = isActive
                ? StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor1, SKColors.White.ToRgb(), 0.45f * highlightEased)
                : isPast ? packet.Palette.AccentColor1 : packet.Palette.AccentColor3;

            var wordSave = context.Canvas.Save();
            try
            {
                context.Canvas.Translate(clampedX, wordY);
                context.Canvas.Scale(finalScale, finalScale);
                context.Legibility.DrawLegibleText(
                    context.Canvas,
                    word.Text,
                    0f,
                    baselineOffset,
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
                context.Canvas.RestoreToCount(wordSave);
            }
        }

        _aesthetics.DrawFollowingDot(context.Canvas, packet, dotX, dotY, Math.Clamp(font.Size * 0.12f, 3.0f, 6.8f));
    }

    private static float ComputeCascadeOffsetFrac(int wordIdx, int lineIdx) =>
        ((wordIdx + lineIdx) % 4) switch
        {
            0 => -0.78f,
            1 => 0.64f,
            2 => -0.18f,
            _ => 0.82f
        };
}
