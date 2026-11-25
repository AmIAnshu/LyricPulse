using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #9 — Kinetic Wall: energetic 3D-tilted perspective typography wall using a zero-allocation
/// 3D homography <see cref="SKMatrix"/> (pitch <c>+6.5°</c>, dynamic yaw <c>±11°</c>, roll <c>±1.5°</c>,
/// focal length <c>1100</c>), critically damped <c>Spring.CameraDrift</c> tracking, and bold Z-slam word
/// landings (<c>Spring.WordEnter</c>). Flattens to crisp 2D editorial rows (0° tilt) under Reduced Motion.
/// </summary>
public sealed class KineticWallStyleRenderer : IKineticStyleRenderer
{
    public const float RestingPitchDegrees = 6.5f;
    public const float MaxYawDegrees = 11.0f;
    public const float MaxRollDegrees = 1.5f;
    public const float PerspectiveFocalLength = 1100f;

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _accentRulePaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    private SpringState _cameraX = SpringState.AtRest(0f);
    private SpringState _cameraY = SpringState.AtRest(0f);
    private SpringState _cameraYawDeg = SpringState.AtRest(-7.5f);
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
        _cameraYawDeg = SpringState.AtRest(-7.5f);
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
        var targetCamX = (0.5f - wordFrac) * Math.Min(packet.ViewportWidthDip * 0.14f, 110f);

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
                var slamStart = 1.22f + ((packet.SafePartyEnergy - 1.0f) * 0.12f);
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

        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        return !_cameraX.IsAtRest ||
               !_cameraY.IsAtRest ||
               !_cameraYawDeg.IsAtRest ||
               !_wordSlamScale.IsAtRest ||
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
        var fontSize = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 0.94f);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var lineLeading = fontSize * 1.12f;

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.50f);
        var scrimRect = new SKRect(
            anchorX - (packet.ViewportWidthDip * 0.40f),
            anchorY - (packet.ViewportHeightDip * 0.30f),
            anchorX + (packet.ViewportWidthDip * 0.40f),
            anchorY + (packet.ViewportHeightDip * 0.30f));

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

            var maxRowWidth = packet.ViewportWidthDip * (packet.IsCompactCard ? 0.86f : 0.78f);

            // Row -1: Past line with spatial depth falloff
            if (slice.PreviousLine is not null)
            {
                DrawWallContextLine(in context, font, fontId, slice.PreviousLine.RawText, anchorX, anchorY - lineLeading, maxRowWidth, opacity: 0.52f);
            }

            // Row +1: Faint upcoming preview row
            if (slice.NextLine is not null)
            {
                DrawWallContextLine(in context, font, fontId, slice.NextLine.RawText, anchorX, anchorY + lineLeading, maxRowWidth, opacity: 0.34f);
            }

            // Row 0: Active slamming line
            DrawActiveWallLine(in context, font, fontId, slice, anchorX, anchorY, maxRowWidth);
        }
        finally
        {
            context.Canvas.RestoreToCount(save);
        }

        StyleLayoutHelper.DrawSecondarySubline(
            in context,
            slice.CurrentLine,
            anchorX,
            anchorY + (lineLeading * 1.90f),
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

    /// <summary>
    /// Computes an exact zero-allocation 2D projective homography <see cref="SKMatrix"/> for a 3D plane
    /// rotated around (<paramref name="originX"/>, <paramref name="originY"/>) by Euler pitch/yaw/roll
    /// viewed through a pinhole camera of focal length <paramref name="focalLength"/>.
    /// </summary>
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

        // R = Rz * Ry * Rx applied to plane point (x, y, 0)
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

    private void DrawActiveWallLine(
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        Core.Runtime.ActiveLyricSlice slice,
        float centerX,
        float centerY,
        float maxRowWidth)
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
            var fit = TextShaperService.ComputeAutoFitScale(measured, maxRowWidth, 0.94f);
            var save = context.Canvas.Save();
            try
            {
                context.Canvas.Translate(centerX, centerY);
                context.Canvas.Scale(fit, fit);
                context.Legibility.DrawLegibleText(context.Canvas, line.RawText, 0f, baselineOffset, SKTextAlign.Center, font, fontId, packet.Palette.AccentColor1, 1f, packet.Palette.AccentColor2, fonts: context.Fonts);
            }
            finally
            {
                context.Canvas.RestoreToCount(save);
            }

            return;
        }

        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, words.Length - 1);
        var spaceW = TextShaperService.MeasureWidth(" ", font, context.Fonts);
        var totalW = 0f;
        Span<float> widths = words.Length <= 32 ? stackalloc float[words.Length] : new float[words.Length];
        for (var i = 0; i < words.Length; i++)
        {
            widths[i] = TextShaperService.MeasureWidth(words[i].Text, font, context.Fonts);
            totalW += widths[i];
            if (i > 0 && words[i].IsSpaceSeparated)
            {
                totalW += spaceW;
            }
        }

        var rowFit = TextShaperService.ComputeAutoFitScale(totalW, maxRowWidth, 0.94f);
        var cursorX = centerX - (totalW * rowFit * 0.5f);
        var dotX = centerX;

        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0 && words[i].IsSpaceSeparated)
            {
                cursorX += spaceW * rowFit;
            }

            var wScaled = widths[i] * rowFit;
            var wordCenterX = cursorX + (wScaled * 0.5f);
            var isActive = i == activeIdx;
            var isPast = i < activeIdx;

            var opacity = isActive
                ? (packet.ReducedMotion ? (0.90f + (0.10f * highlightEased)) : 1.0f)
                : isPast ? 0.78f : 0.34f;
            var color = isActive
                ? StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor3, packet.Palette.AccentColor1, highlightEased)
                : isPast ? packet.Palette.AccentColor1 : packet.Palette.AccentColor3;
            var scale = rowFit * (isActive && !packet.ReducedMotion ? _wordSlamScale.Value : 1.0f);

            if (isActive)
            {
                dotX = cursorX + (wScaled * (float)Math.Clamp(slice.WordProgress, 0.0, 1.0));
                if (packet.ShowColorSweepLayer)
                {
                    var sweepRect = new SKRect(cursorX, centerY - (font.Size * rowFit * 0.54f), cursorX + wScaled, centerY + (font.Size * rowFit * 0.54f));
                    _aesthetics.DrawWordColorSweep(context.Canvas, sweepRect, slice.WordProgress, packet, opacity);
                }

                // Hairline 2px active accent rule sweeping cleanly beneath word
                var ruleW = Math.Max(4f, wScaled * (float)Math.Clamp(slice.WordProgress, 0.12, 1.0));
                _accentRulePaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.90f * opacity);
                context.Canvas.DrawRect(cursorX, centerY + (font.Size * rowFit * 0.56f), ruleW, 2.2f, _accentRulePaint);
            }

            var wordSave = context.Canvas.Save();
            try
            {
                context.Canvas.Translate(wordCenterX, centerY);
                context.Canvas.Scale(scale, scale);
                context.Legibility.DrawLegibleText(
                    context.Canvas,
                    words[i].Text,
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

            cursorX += wScaled;
        }

        _aesthetics.DrawFollowingDot(context.Canvas, packet, dotX, centerY + (font.Size * rowFit * 0.72f), Math.Clamp(font.Size * rowFit * 0.10f, 3.0f, 7.0f));
    }

    private static void DrawWallContextLine(
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        string text,
        float centerX,
        float centerY,
        float maxRowWidth,
        float opacity)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var measured = TextShaperService.MeasureWidth(text, font, context.Fonts);
        var fit = TextShaperService.ComputeAutoFitScale(measured, maxRowWidth, 0.90f) * 0.84f;
        font.GetFontMetrics(out var metrics);
        var baselineOffset = -((metrics.Ascent + metrics.Descent) * 0.5f);

        var save = context.Canvas.Save();
        try
        {
            context.Canvas.Translate(centerX, centerY);
            context.Canvas.Scale(fit, fit);
            context.Legibility.DrawLegibleText(
                context.Canvas,
                text,
                0f,
                baselineOffset,
                SKTextAlign.Center,
                font,
                fontId,
                context.Packet.Palette.AccentColor3,
                opacity: opacity,
                fonts: context.Fonts);
        }
        finally
        {
            context.Canvas.RestoreToCount(save);
        }
    }
}
