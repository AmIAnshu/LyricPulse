using System.Globalization;
using LyricPulse.Core.Audio;
using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #9 — Kinetic Wall Monument Stage: multi-plane 3D-tilted pinhole perspective wall
/// featuring an architectural beat-reactive coordinate stage, monumental active-word watermark echo,
/// luminous completed-word trace path, editorial Verci cutouts with semantic vector emblems,
/// chromatic RGB depth split on beats, left VU telemetry spine, and word-locked beat dot.
/// </summary>
public sealed class KineticWallStyleRenderer : IKineticStyleRenderer
{
    public const float RestingPitchDegrees = 5.5f;
    public const float MaxYawDegrees = 10.0f;
    public const float MaxRollDegrees = 1.8f;
    public const float PerspectiveFocalLength = 1150f;

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _hairlinePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _tracePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPath _tracePath = new();
    private readonly SKPoint[] _wordAnchors = new SKPoint[8];

    private SpringState _cameraX = SpringState.AtRest(0f);
    private SpringState _cameraY = SpringState.AtRest(0f);
    private SpringState _cameraYawDeg = SpringState.AtRest(-6.5f);
    private SpringState _wordSlamScale = SpringState.AtRest(1.16f);
    private float _highlightFade = 1.0f;
    private float _smoothedBass;
    private float _smoothedBeat;
    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private float _lastWordDurationSec = 0.32f;
    private bool _disposed;

    public OverlayStyleId StyleId => OverlayStyleId.KineticWall;

    public void ResetState()
    {
        _cameraX = SpringState.AtRest(0f);
        _cameraY = SpringState.AtRest(0f);
        _cameraYawDeg = SpringState.AtRest(-6.5f);
        _wordSlamScale = SpringState.AtRest(1.16f);
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

        var wordFrac = wordCount <= 1 ? 0.5f : (activeIdx / (float)(wordCount - 1));
        var targetYaw = ((wordFrac - 0.5f) * 2f) * MaxYawDegrees * Math.Clamp(packet.SafePartyEnergy, 0.7f, 1.3f);
        var targetCamX = (0.5f - wordFrac) * Math.Min(packet.ViewportWidthDip * 0.14f, 110f);
        var targetCamY = MathF.Sin((float)(packet.RenderTimeSeconds * 0.75)) * (packet.IsCompactCard ? 0f : 4.5f);

        var activeRestScale = packet.IsCompactCard ? 1.08f : 1.18f;
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
                var slamStart = activeRestScale + 0.16f + ((packet.SafePartyEnergy - 1.0f) * 0.12f) + (_smoothedBeat * 0.08f);
                _wordSlamScale = new SpringState(Value: slamStart, Velocity: 0f, Target: activeRestScale);
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
            _wordSlamScale = SpringState.AtRest(1.0f);
            _cameraX = SpringState.AtRest(0f);
            _cameraY = SpringState.AtRest(0f);
            _cameraYawDeg = SpringState.AtRest(0f);
            _aesthetics.ResetDot();
            return _highlightFade < 0.999f;
        }

        _cameraX = _cameraX.WithTarget(targetCamX).Step(SpringTokens.CameraDrift, dt);
        _cameraY = _cameraY.WithTarget(targetCamY).Step(SpringTokens.CameraDrift, dt);
        _cameraYawDeg = _cameraYawDeg.WithTarget(Math.Clamp(targetYaw, -MaxYawDegrees, MaxYawDegrees)).Step(SpringTokens.CameraDrift, dt);
        _wordSlamScale = _wordSlamScale.WithTarget(activeRestScale).Step(SpringTokens.WordEnter.WithTempoCompression(_lastWordDurationSec), dt);

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.50f);
        var fontSize = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 0.80f);
        var rowSpreadX = packet.ViewportWidthDip * (packet.IsCompactCard ? 0.20f : 0.18f);
        var staggerFrac = ComputeCascadeOffsetFrac(activeIdx, slice.ActiveLineIndex);
        var approxX = anchorX + (staggerFrac * rowSpreadX) + _cameraX.Value;
        var approxY = anchorY + (fontSize * 0.65f);
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
        var fontSize = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 0.80f);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.50f);
        var isFullStage = packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas && !packet.IsCompactCard;

        var scrimHalfW = packet.ViewportWidthDip * (isFullStage ? 0.44f : 0.30f);
        var scrimHalfH = packet.ViewportHeightDip * (isFullStage ? 0.36f : 0.18f);
        var scrimRect = new SKRect(
            anchorX - scrimHalfW,
            anchorY - scrimHalfH,
            anchorX + scrimHalfW,
            anchorY + scrimHalfH);

        context.Legibility.DrawAmbientScrim(
            context.Canvas,
            scrimRect,
            fontSize,
            packet.Palette.ScrimTint,
            packet.ScrimIntensity,
            packet.HighContrastScrim);

        // Layer 1 (StageCanvas only): Deep Parallax Architectural Grid + Giant Typography Watermark + VU Spine
        if (isFullStage)
        {
            DrawArchitecturalBackplane(in context, font, fontId, slice, anchorX, anchorY);
        }

        // Layer 2: Primary 3D-projected Typography Wall
        // Project every anchor into pure 2D affine transforms (never concatenate projective Persp0/Persp1
        // onto a GPU SKCanvas that draws text or SKImageFilter blurs, preventing Skia glyph-atlas artifacts).
        DrawScatteredWordCascade(in context, font, fontId, slice, anchorX, anchorY, isFullStage);

        // Layer 3 (StageCanvas only): Left Line Index Pill & Audio Telemetry VU Spine (clear of DesktopOverlay corners)
        if (isFullStage)
        {
            DrawStageTelemetryRail(in context, fontId, slice, fontSize);
        }

        StyleLayoutHelper.DrawSecondarySubline(
            in context,
            slice.CurrentLine,
            anchorX,
            anchorY + (fontSize * 3.0f),
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
        _hairlinePaint.Dispose();
        _fillPaint.Dispose();
        _tracePaint.Dispose();
        _tracePath.Dispose();
    }

    internal static (SKPoint Projected, float DepthScale) Project3DPoint(
        float x,
        float y,
        float z,
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

        var dx = x - originX;
        var dy = y - originY;
        var dz = z;

        // Rotate around X (pitch), then Y (yaw), then Z (roll)
        var y1 = (dy * cx) - (dz * sx);
        var z1 = (dy * sx) + (dz * cx);
        var x2 = (dx * cy) + (z1 * sy);
        var z2 = (-dx * sy) + (z1 * cy);
        var x3 = (x2 * cz) - (y1 * sz);
        var y3 = (x2 * sz) + (y1 * cz);

        var denom = Math.Max(f * 0.28f, f + z2);
        var depthScale = Math.Clamp(f / denom, 0.36f, 2.15f);
        return (new SKPoint(originX + (x3 * depthScale), originY + (y3 * depthScale)), depthScale);
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

    private void DrawArchitecturalBackplane(
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        Core.Runtime.ActiveLyricSlice slice,
        float centerX,
        float centerY)
    {
        var canvas = context.Canvas;
        var packet = context.Packet;
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        var palette = packet.Palette;

        var padX = Math.Clamp(w * 0.11f, 44f, 120f);
        var padY = Math.Clamp(h * 0.14f, 40f, 96f);
        var frame = new SKRect(padX, padY, w - padX, h - padY);
        var tickLen = Math.Clamp(MathF.Min(w, h) * 0.025f, 10f, 20f);

        var pitch = packet.ReducedMotion ? 0f : RestingPitchDegrees * 0.55f;
        var yaw = packet.ReducedMotion ? 0f : _cameraYawDeg.Value * 0.38f;
        var f = PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f);
        var panX = packet.ReducedMotion ? 0f : _cameraX.Value * 0.35f;
        var panY = packet.ReducedMotion ? 0f : _cameraY.Value * 0.35f;

        SKPoint ProjBack(float x, float y, float z = 95f)
        {
            var (pt, _) = Project3DPoint(x + panX, y + panY, z, pitch, yaw, 0f, f, centerX, centerY);
            return pt;
        }

        // Editorial stage frame registration brackets & horizon rules in projected 3D coordinates (affine CTM)
        var ruleAlpha = Math.Clamp(0.11f + (_smoothedBass * 0.12f), 0.08f, 0.28f);
        _hairlinePaint.StrokeWidth = 1.0f;
        _hairlinePaint.Color = palette.AccentColor3.ToSkColorWithAlpha(ruleAlpha * 0.55f);
        canvas.DrawLine(ProjBack(frame.Left, centerY), ProjBack(frame.Right, centerY), _hairlinePaint);

        _hairlinePaint.Color = palette.AccentColor1.ToSkColorWithAlpha(ruleAlpha * 0.35f);
        canvas.DrawLine(ProjBack(w * 0.26f, frame.Top), ProjBack(w * 0.26f, frame.Bottom), _hairlinePaint);
        canvas.DrawLine(ProjBack(w * 0.74f, frame.Top), ProjBack(w * 0.74f, frame.Bottom), _hairlinePaint);

        _hairlinePaint.StrokeWidth = 1.5f;
        _hairlinePaint.Color = palette.AccentColor1.ToSkColorWithAlpha(ruleAlpha);
        var tl = ProjBack(frame.Left, frame.Top);
        var tr = ProjBack(frame.Right, frame.Top);
        DrawCornerBracket(canvas, tl.X, tl.Y, tickLen, tickLen);
        DrawCornerBracket(canvas, tr.X, tr.Y, -tickLen, tickLen);
        if (!packet.BottomFadeToBlack)
        {
            var bl = ProjBack(frame.Left, frame.Bottom);
            var br = ProjBack(frame.Right, frame.Bottom);
            DrawCornerBracket(canvas, bl.X, bl.Y, tickLen, -tickLen);
            DrawCornerBracket(canvas, br.X, br.Y, -tickLen, -tickLen);
        }

        // Monumental active-word watermark echo floating in deep shadow behind wall (pure affine Scale!)
        var line = slice.CurrentLine!;
        var watermarkText = !line.Words.IsEmpty
            ? line.Words[Math.Clamp(slice.ActiveWordIndex, 0, line.Words.Length - 1)].Text.ToUpperInvariant()
            : line.RawText.ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(watermarkText))
        {
            var wmCenter = ProjBack(centerX, centerY, 130f);
            var wmScale = 2.65f + (_smoothedBeat * 0.18f);
            var wmMeasured = TextShaperService.MeasureWidth(watermarkText, font, context.Fonts);
            var wmFit = TextShaperService.ComputeAutoFitScale(wmMeasured * wmScale, w * 0.90f, 1.0f);
            var finalWmScale = wmScale * wmFit;
            var wmAlpha = Math.Clamp(0.055f + (_smoothedBeat * 0.045f), 0.04f, 0.13f);

            font.GetFontMetrics(out var metrics);
            var baselineOffset = -((metrics.Ascent + metrics.Descent) * 0.5f);

            var wmSave = canvas.Save();
            try
            {
                canvas.Translate(wmCenter.X, wmCenter.Y);
                canvas.Scale(finalWmScale, finalWmScale);
                context.Legibility.DrawLegibleText(
                    canvas,
                    watermarkText,
                    0f,
                    baselineOffset,
                    SKTextAlign.Center,
                    font,
                    fontId,
                    StyleLayoutHelper.BlendSrgb(palette.AccentColor2, palette.AccentColor3, 0.5f),
                    opacity: wmAlpha,
                    fonts: context.Fonts);
            }
            finally
            {
                canvas.RestoreToCount(wmSave);
            }
        }
    }

    private void DrawCornerBracket(SKCanvas canvas, float x, float y, float dx, float dy)
    {
        canvas.DrawLine(x, y, x + dx, y, _hairlinePaint);
        canvas.DrawLine(x, y, x, y + dy, _hairlinePaint);
    }

    private void DrawStageTelemetryRail(
        in StyleRenderContext context,
        LyricFontId fontId,
        Core.Runtime.ActiveLyricSlice slice,
        float fontSize)
    {
        var packet = context.Packet;
        var canvas = context.Canvas;
        var doc = packet.SceneSnapshot.Document;
        var totalLines = doc?.Lines.Length ?? 0;
        if (totalLines <= 0)
        {
            return;
        }

        var railX = Math.Clamp(packet.ViewportWidthDip * 0.06f, 28f, 64f);
        var centerY = packet.ViewportHeightDip * 0.50f;
        var monoFont = context.Fonts.GetOrCreateFont(LyricFontId.SystemMono, Math.Clamp(fontSize * 0.28f, 11f, 14f), enforceMinOverlayFloor: false);

        var lineLabel = string.Create(
            CultureInfo.InvariantCulture,
            $"{Math.Clamp(slice.ActiveLineIndex + 1, 1, totalLines):D2}/{totalLines:D2}");

        context.Legibility.DrawLegibleText(
            canvas,
            lineLabel,
            railX,
            centerY - 46f,
            SKTextAlign.Left,
            monoFont,
            LyricFontId.SystemMono,
            packet.Palette.AccentColor1,
            opacity: 0.62f,
            fonts: context.Fonts);

        // Minimalist 6-bar vertical equalizer spine next to line index
        var bands = packet.EffectiveAudioMetrics.SafeBands;
        for (var b = 0; b < 6 && b < bands.Length; b++)
        {
            var mag = Math.Clamp(bands[b * 2 % bands.Length], 0.08f, 1.0f);
            var barW = 4f + (mag * 22f);
            var y = centerY - 26f + (b * 8f);
            var barColor = StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor1, packet.Palette.AccentColor2, b / 5f);
            _fillPaint.Color = barColor.ToSkColorWithAlpha(0.28f + (mag * 0.52f));
            canvas.DrawRoundRect(new SKRect(railX, y, railX + barW, y + 3.5f), 1.75f, 1.75f, _fillPaint);
        }
    }

    private void DrawScatteredWordCascade(
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        Core.Runtime.ActiveLyricSlice slice,
        float centerX,
        float centerY,
        bool isFullStage)
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

        // Show up to 6 visible cascade words on full stage, 5 on compact/overlay
        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, words.Length - 1);
        var lookBack = isFullStage ? 3 : 2;
        var windowRadius = packet.IsCompactCard ? 1 : 2;
        var startIdx = Math.Max(0, activeIdx - lookBack);
        var endIdx = Math.Min(words.Length - 1, activeIdx + windowRadius);
        var visibleCount = (endIdx - startIdx) + 1;
        var slotPitch = font.Size * (packet.IsCompactCard ? 1.15f : 1.24f);
        var spreadX = packet.ViewportWidthDip * (packet.IsCompactCard ? 0.14f : 0.15f);

        var pitchDeg = packet.ReducedMotion ? 0f : RestingPitchDegrees + (_smoothedBass * 2.4f);
        var yawDeg = packet.ReducedMotion ? 0f : _cameraYawDeg.Value;
        var rollDeg = packet.ReducedMotion ? 0f : Math.Clamp((_cameraYawDeg.Value / MaxYawDegrees) * MaxRollDegrees, -MaxRollDegrees, MaxRollDegrees);
        var focal = PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f);
        var camX = packet.ReducedMotion ? 0f : _cameraX.Value;
        var camY = packet.ReducedMotion ? 0f : _cameraY.Value;

        // Previous line whisper (top-left editorial 3D depth context)
        if (!packet.IsCompactCard && slice.PreviousLine is not null)
        {
            var (prevPt, prevDepth) = packet.ReducedMotion
                ? (new SKPoint(centerX - (spreadX * 0.82f), centerY - (slotPitch * 2.35f)), 1f)
                : Project3DPoint(centerX - (spreadX * 0.82f) + camX, centerY - (slotPitch * 2.35f) + camY, 85f, pitchDeg, yawDeg, rollDeg, focal, centerX, centerY);
            var ghostSave = context.Canvas.Save();
            try
            {
                context.Canvas.Translate(prevPt.X, prevPt.Y);
                context.Canvas.Scale(0.52f * prevDepth, 0.52f * prevDepth);
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

        // Next line preview whisper (lower-right diagonal anticipation on full stage)
        if (isFullStage && slice.NextLine is not null && !slice.NextLine.IsInstrumentalBreak)
        {
            var (nextPt, nextDepth) = packet.ReducedMotion
                ? (new SKPoint(centerX + (spreadX * 0.86f), centerY + (slotPitch * 2.38f)), 1f)
                : Project3DPoint(centerX + (spreadX * 0.86f) + camX, centerY + (slotPitch * 2.38f) + camY, 85f, pitchDeg, yawDeg, rollDeg, focal, centerX, centerY);
            var nextSave = context.Canvas.Save();
            try
            {
                context.Canvas.Translate(nextPt.X, nextPt.Y);
                context.Canvas.Scale(0.48f * nextDepth, 0.48f * nextDepth);
                context.Legibility.DrawLegibleText(
                    context.Canvas,
                    slice.NextLine.RawText,
                    0f,
                    baselineOffset,
                    SKTextAlign.Center,
                    font,
                    fontId,
                    packet.Palette.AccentColor1,
                    opacity: 0.25f,
                    fonts: context.Fonts);
            }
            finally
            {
                context.Canvas.RestoreToCount(nextSave);
            }
        }

        // Pass 1: Project sculpted 3D cascade positions into pure affine space & draw kinetic trace links
        Span<float> depthScales = stackalloc float[8];
        depthScales.Fill(1.0f);
        var anchorCount = Math.Min(visibleCount, _wordAnchors.Length);
        for (var i = startIdx; i <= endIdx && (i - startIdx) < anchorCount; i++)
        {
            var k = i - startIdx;
            var relRow = k - ((visibleCount - 1) * 0.5f);
            var offsetFrac = ComputeCascadeOffsetFrac(i, line.Index);
            var rawX = centerX + (offsetFrac * spreadX);
            var wordY = centerY + (relRow * slotPitch);
            var measured = TextShaperService.MeasureWidth(words[i].Text, font, context.Fonts);
            var isActive = i == activeIdx;
            var scale = isActive && !packet.ReducedMotion ? _wordSlamScale.Value : (i < activeIdx ? 0.88f : 0.80f);
            var zOffset = isActive ? -32f : (relRow * 42f);

            var (projPt, depthScale) = packet.ReducedMotion
                ? (new SKPoint(rawX, wordY), 1.0f)
                : Project3DPoint(rawX + camX, wordY + camY, zOffset, pitchDeg, yawDeg, rollDeg, focal, centerX, centerY);

            var fit = TextShaperService.ComputeAutoFitScale(measured * scale * depthScale, packet.ViewportWidthDip * 0.72f, 1.0f);
            var finalScale = scale * fit * depthScale;
            var halfW = (measured * finalScale * 0.5f) + 20f;
            var clampedX = Math.Clamp(projPt.X, halfW, Math.Max(halfW, packet.ViewportWidthDip - halfW));
            _wordAnchors[k] = new SKPoint(clampedX, projPt.Y);
            depthScales[k] = depthScale;
        }

        var completedAnchors = Math.Min(anchorCount - 1, activeIdx - startIdx);
        if (!packet.IsCompactCard && completedAnchors > 0)
        {
            _tracePath.Rewind();
            _tracePath.MoveTo(_wordAnchors[0]);
            for (var c = 1; c <= completedAnchors; c++)
            {
                var p0 = _wordAnchors[c - 1];
                var p1 = _wordAnchors[c];
                var midY = (p0.Y + p1.Y) * 0.5f;
                _tracePath.CubicTo(p0.X, midY, p1.X, midY, p1.X, p1.Y);
            }

            _tracePaint.StrokeWidth = 2.0f + (_smoothedBeat * 1.4f);
            _tracePaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.24f + (_smoothedBass * 0.16f));
            context.Canvas.DrawPath(_tracePath, _tracePaint);
        }

        var dotX = centerX;
        var dotY = centerY;

        // Pass 2: Render all cascade words, hero chromatic cutout plaques, semantic vector visuals, and sweep bar
        for (var i = startIdx; i <= endIdx; i++)
        {
            var word = words[i];
            var k = i - startIdx;
            var posPt = k < anchorCount ? _wordAnchors[k] : new SKPoint(centerX, centerY);
            var depthScale = k < anchorCount ? depthScales[k] : 1.0f;
            var clampedX = posPt.X;
            var wordY = posPt.Y;

            var isActive = i == activeIdx;
            var isPast = i < activeIdx;
            var opacity = isActive
                ? (packet.ReducedMotion ? (0.94f + (0.06f * highlightEased)) : 1.0f)
                : isPast ? 0.72f : 0.34f;
            var scale = isActive && !packet.ReducedMotion ? _wordSlamScale.Value : (isPast ? 0.88f : 0.80f);
            var measured = TextShaperService.MeasureWidth(word.Text, font, context.Fonts);
            var fit = TextShaperService.ComputeAutoFitScale(measured * scale * depthScale, packet.ViewportWidthDip * 0.72f, 1.0f);
            var finalScale = scale * fit * depthScale;

            if (isActive)
            {
                var boxHalfW = measured * finalScale * 0.5f;
                dotX = clampedX - boxHalfW + (measured * finalScale * (float)Math.Clamp(slice.WordProgress, 0.0, 1.0));
                dotY = wordY + (font.Size * finalScale * 0.64f);

                if (packet.ShowColorSweepLayer)
                {
                    var sweepRect = new SKRect(clampedX - boxHalfW, wordY - (font.Size * finalScale * 0.52f), clampedX + boxHalfW, wordY + (font.Size * finalScale * 0.52f));
                    _aesthetics.DrawWordColorSweep(context.Canvas, sweepRect, slice.WordProgress, packet, opacity);
                }

                // Semantic animated vector icon badge anchored crisply beside focal cutout/word
                if (word.Visual is { } matchedVisual)
                {
                    var badgeOffsetX = (boxHalfW + (font.Size * 0.62f)) * (clampedX > centerX ? -1f : 1f);
                    var badgeX = Math.Clamp(clampedX + badgeOffsetX, 42f, packet.ViewportWidthDip - 42f);
                    var badgeY = wordY - (font.Size * 0.08f);
                    var badgeSize = Math.Clamp(font.Size * finalScale * 0.72f, 22f, 44f);
                    _aesthetics.DrawReactiveVisual(context.Canvas, in context, matchedVisual, badgeX, badgeY, badgeSize, opacity, isActive: true);
                }
            }

            var useCutout = isActive && (word.Visual is not null || word.Text.Length >= 4);
            if (useCutout)
            {
                var tilt = ((i + line.Index) % 2 == 0) ? -2.1f : 2.1f;

                // Subtle dual accent edge glow behind editorial sticker cutout on beat hits
                if (!packet.ReducedMotion && _smoothedBeat > 0.05f)
                {
                    var plaqueHalfW = (measured * finalScale * 0.5f) + 12f;
                    var plaqueHalfH = (font.Size * finalScale * 0.56f);
                    _fillPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(Math.Clamp(_smoothedBeat * 0.24f, 0f, 0.30f));
                    context.Canvas.DrawRoundRect(
                        new SKRect(clampedX - plaqueHalfW - 4f, wordY - plaqueHalfH - 4f, clampedX + plaqueHalfW + 4f, wordY + plaqueHalfH + 4f),
                        12f, 12f, _fillPaint);
                }

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
                ? StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor1, SKColors.White.ToRgb(), 0.55f * highlightEased)
                : isPast ? packet.Palette.AccentColor1 : packet.Palette.AccentColor3;

            var wordSave = context.Canvas.Save();
            try
            {
                context.Canvas.Translate(clampedX, wordY);
                context.Canvas.Scale(finalScale, finalScale);

                // Chromatic depth split echo on non-cutout active words
                if (isActive && !packet.ReducedMotion && _smoothedBeat > 0.06f)
                {
                    var splitDx = 2.6f * (0.4f + _smoothedBeat);
                    context.Legibility.DrawLegibleText(
                        context.Canvas,
                        word.Text,
                        -splitDx,
                        baselineOffset,
                        SKTextAlign.Center,
                        font,
                        fontId,
                        packet.Palette.AccentColor1,
                        opacity: 0.30f,
                        fonts: context.Fonts);
                    context.Legibility.DrawLegibleText(
                        context.Canvas,
                        word.Text,
                        splitDx,
                        baselineOffset,
                        SKTextAlign.Center,
                        font,
                        fontId,
                        packet.Palette.AccentColor2,
                        opacity: 0.30f,
                        fonts: context.Fonts);
                }

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
        ((wordIdx * 3 + lineIdx) % 5) switch
        {
            0 => -0.82f,
            1 => 0.68f,
            2 => -0.22f,
            3 => 0.86f,
            _ => 0.14f
        };
}
