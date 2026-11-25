using LyricPulse.Core.Models;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using LyricPulse.Rendering.Shaders;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #1 — Fisheye: tight centered vertical 3-word triad (Previous, Active, Next)
/// advancing through a monotonic convex spherical lens (<c>EllipticalBulge.sksl</c>)
/// with cinema vertical <c>DirectionalMotionBlur13.sksl</c> (shutter 0.48) and 4-layer legibility.
/// Layer 0 radial scrim renders directly on the main canvas (never clipped inside offscreen surfaces).
/// </summary>
public sealed class FisheyeStyleRenderer : IKineticStyleRenderer
{
    public const float VerticalShutterFactor = 0.48f;
    public const float SlotPitchRatio = 1.18f;
    public const float RestingActiveScale = 1.08f;
    public const float ContextWordScale = 0.78f;
    public const float DepartingExitDurationSec = 0.14f;

    private readonly AestheticsOverlayRenderer _aesthetics = new();

    private SpringState _scrollOffsetY = SpringState.AtRest(0f);
    private SpringState _activeScale = SpringState.AtRest(RestingActiveScale);
    private SpringState _lensStrength = SpringState.AtRest(MotionBlurMath.BaselineBulgeStrength);
    private float _highlightFade = 1.0f;

    private string? _departingWordText;
    private float _departingElapsedSec = DepartingExitDurationSec;

    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private string? _lastPrevWordText;
    private float _lastWordDurationSec = 0.32f;
    private bool _disposed;

    internal bool AllowCpuShadersForTesting { get; set; }

    public OverlayStyleId StyleId => OverlayStyleId.Fisheye;

    public void ResetState()
    {
        _scrollOffsetY = SpringState.AtRest(0f);
        _activeScale = SpringState.AtRest(RestingActiveScale);
        _lensStrength = SpringState.AtRest(MotionBlurMath.BaselineBulgeStrength);
        _highlightFade = 1.0f;
        _departingWordText = null;
        _departingElapsedSec = DepartingExitDurationSec;
        _lastWordKey = (-1, -1);
        _lastPrevWordText = null;
        _lastWordDurationSec = 0.32f;
        _aesthetics.ResetDot();
    }

    public bool Advance(in FrameRenderPacket packet, in LifecycleFrameState lifecycle)
    {
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        var (prevWord, active, _, wordKey, wordDurationSec) = StyleLayoutHelper.ResolveWordTriad(slice);
        if (active is null)
        {
            return false;
        }

        var dt = Math.Clamp(packet.DeltaSeconds, 0f, SpringState.MaxDeltaSeconds);
        var fontSize = ResolveFontSize(packet);
        var slotPitch = fontSize * SlotPitchRatio;
        _lastWordDurationSec = wordDurationSec;

        if (wordKey != _lastWordKey)
        {
            var isLineTransition = _lastWordKey.LineIndex >= 0 && wordKey.LineIndex != _lastWordKey.LineIndex;
            var wasInitialized = _lastWordKey.LineIndex >= 0;

            if (wasInitialized && !packet.ReducedMotion && !string.IsNullOrEmpty(_lastPrevWordText))
            {
                _departingWordText = _lastPrevWordText;
                _departingElapsedSec = 0f;
            }

            _lastWordKey = wordKey;
            _lastPrevWordText = prevWord?.Text;

            if (packet.ReducedMotion)
            {
                _scrollOffsetY = SpringState.AtRest(0f);
                _activeScale = SpringState.AtRest(1.00f);
                _lensStrength = SpringState.AtRest(0f);
                _departingWordText = null;
                _departingElapsedSec = DepartingExitDurationSec;
                _highlightFade = 0f;
            }
            else
            {
                if (wasInitialized)
                {
                    _scrollOffsetY = new SpringState(Value: slotPitch, Velocity: _scrollOffsetY.Velocity * 0.25f, Target: 0f);
                }

                var energy = packet.SafePartyEnergy;
                var targetScale = RestingActiveScale + ((energy - 1.0f) * 0.08f);
                _activeScale = new SpringState(Value: ContextWordScale, Velocity: 0f, Target: targetScale);
                var pacing = Math.Clamp(wordDurationSec / 0.32f, 0.42f, 1.35f);
                var impulse = (isLineTransition ? 2.0f : 1.5f * Math.Min(1.0f, pacing)) * Math.Clamp(energy, 0.7f, 1.35f);
                var nextLens = _lensStrength.WithTarget(MotionBlurMath.BaselineBulgeStrength).WithImpulse(impulse);
                _lensStrength = nextLens with
                {
                    Value = Math.Clamp(nextLens.Value, MotionBlurMath.BaselineBulgeStrength, MotionBlurMath.MaxBulgeStrength)
                };
                _highlightFade = 0f;
            }
        }
        else
        {
            _lastPrevWordText = prevWord?.Text;
        }

        var fadeRate = packet.ReducedMotion ? (1f / 0.14f) : (1f / 0.15f);
        if (_highlightFade < 1f)
        {
            _highlightFade = Math.Min(1f, _highlightFade + (dt * fadeRate));
        }

        if (_departingElapsedSec < DepartingExitDurationSec)
        {
            _departingElapsedSec = Math.Min(DepartingExitDurationSec, _departingElapsedSec + dt);
        }

        if (packet.ReducedMotion)
        {
            _scrollOffsetY = SpringState.AtRest(0f);
            _activeScale = SpringState.AtRest(1.00f);
            _lensStrength = SpringState.AtRest(0f);
            _aesthetics.ResetDot();
            return _highlightFade < 0.999f;
        }

        var wordSpring = SpringTokens.WordSnap.WithTempoCompression(_lastWordDurationSec);
        var lineSpring = SpringTokens.LineShift.WithTempoCompression(_lastWordDurationSec);

        _scrollOffsetY = _scrollOffsetY.Step(lineSpring, dt);
        _activeScale = _activeScale.Step(wordSpring, dt);
        _lensStrength = _lensStrength.Step(SpringTokens.LensBreath, dt);
        if (_lensStrength.Value > MotionBlurMath.MaxBulgeStrength)
        {
            _lensStrength = _lensStrength with { Value = MotionBlurMath.MaxBulgeStrength };
        }

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet);
        var dotTargetX = anchorX;
        var dotTargetY = anchorY + (fontSize * 0.62f);
        var dotActive = packet.ShowFollowingDot && _aesthetics.AdvanceDot(wordKey, dotTargetX, dotTargetY, dt, reducedMotion: false, _lastWordDurationSec);

        return !_scrollOffsetY.IsAtRest ||
               !_activeScale.IsAtRest ||
               !_lensStrength.IsAtRest ||
               dotActive ||
               _highlightFade < 0.999f ||
               _departingElapsedSec < DepartingExitDurationSec;
    }

    public void Render(in StyleRenderContext context, in LifecycleFrameState lifecycle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var packet = context.Packet;
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        var (prevWord, activeWord, nextWord, _, _) = StyleLayoutHelper.ResolveWordTriad(slice);
        if (activeWord is null)
        {
            return;
        }

        var fontId = packet.ActiveFontId;
        var fontSize = ResolveFontSize(packet);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var slotPitch = fontSize * SlotPitchRatio;
        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet);

        var maxWordWidth = MeasureMaxTriadWidth(prevWord, activeWord, nextWord, font, context.Fonts);
        var fitScale = TextShaperService.ComputeAutoFitScale(maxWordWidth * RestingActiveScale, packet.ViewportWidthDip, 0.68f);

        var dpi = packet.RenderScaling;
        var velocityPxPerSec = new SKPoint(0f, _scrollOffsetY.Velocity * dpi);
        var clampedVel = MotionBlurMath.ComputeClampedVelocity(velocityPxPerSec, VerticalShutterFactor, dpi, out var blurDeadZone);
        var (_, blurPadYPx) = blurDeadZone ? (0, 0) : MotionBlurMath.ComputeDirectionalPaddingPx(clampedVel);
        var blurPadYDip = blurPadYPx / Math.Max(dpi, 0.5f);

        var layerWidthDip = Math.Clamp(Math.Max(maxWordWidth * fitScale * 1.85f, fontSize * 8.0f), packet.IsCompactCard ? 120f : 240f, packet.ViewportWidthDip);
        var layerHeightDip = Math.Clamp((fontSize * 5.2f) + (blurPadYDip * 2f), packet.IsCompactCard ? 100f : 180f, packet.ViewportHeightDip);

        // Clamp destRect strictly inside viewport so high DPI / BottomThird / 160% scale never clip
        var destRect = StyleLayoutHelper.ClampLayerRectToViewport(
            anchorX,
            anchorY,
            layerWidthDip,
            layerHeightDip,
            packet.ViewportWidthDip,
            packet.ViewportHeightDip);

        // Layer 0 radial scrim renders directly on main canvas (NEVER clipped or warped inside offscreen surface!)
        var clusterHalfW = Math.Max(font.Size * 3.0f, TextShaperService.MeasureWidth(activeWord.Text, font, context.Fonts) * fitScale * 0.65f);
        var scrimCluster = new SKRect(
            destRect.MidX - clusterHalfW,
            destRect.MidY - (slotPitch * 1.45f),
            destRect.MidX + clusterHalfW,
            destRect.MidY + (slotPitch * 1.45f));

        context.Legibility.DrawAmbientScrim(
            context.Canvas,
            scrimCluster,
            font.Size * fitScale,
            packet.Palette.ScrimTint,
            packet.ScrimIntensity,
            packet.HighContrastScrim);

        var useShaders = (context.ShadersEnabled || AllowCpuShadersForTesting) &&
                         !packet.ReducedMotion &&
                         context.Shaders.AreAllShadersReady;

        if (!useShaders)
        {
            DrawTriadGlyphs(
                context.Canvas,
                context,
                font,
                fontId,
                destRect.MidX,
                destRect.MidY,
                slotPitch,
                fitScale,
                prevWord,
                activeWord,
                nextWord,
                slice.WordProgress,
                packet.ReducedMotion);
        }
        else
        {
            var physW = Math.Clamp((int)MathF.Ceiling(destRect.Width * dpi), 64, OffscreenSurfacePool.MaxSurfaceDimensionPx);
            var physH = Math.Clamp((int)MathF.Ceiling(destRect.Height * dpi), 64, OffscreenSurfacePool.MaxSurfaceDimensionPx);

            var bulgeSurface = context.SurfacePool.RentSurface(OffscreenSurfaceSlot.Bulge, context.GrContext, physW, physH);
            var offCanvas = bulgeSurface.Canvas;
            offCanvas.Clear(SKColors.Transparent);

            var offSave = offCanvas.Save();
            try
            {
                offCanvas.Scale(dpi, dpi);
                DrawTriadGlyphs(
                    offCanvas,
                    context,
                    font,
                    fontId,
                    destRect.Width * 0.5f,
                    destRect.Height * 0.5f,
                    slotPitch,
                    fitScale,
                    prevWord,
                    activeWord,
                    nextWord,
                    slice.WordProgress,
                    reducedMotion: false);
            }
            finally
            {
                offCanvas.RestoreToCount(offSave);
            }

            var sourceForBulge = bulgeSurface;
            if (!context.Shaders.ShouldBypassMotionBlur(
                    context.IsGpuBackend,
                    packet.ReducedMotion,
                    velocityPxPerSec,
                    VerticalShutterFactor,
                    dpi,
                    out _,
                    AllowCpuShadersForTesting))
            {
                var blurSurface = context.SurfacePool.RentSurface(OffscreenSurfaceSlot.MotionBlur, context.GrContext, physW, physH);
                blurSurface.Canvas.Clear(SKColors.Transparent);
                var blurSave = blurSurface.Canvas.Save();
                try
                {
                    blurSurface.Canvas.Scale(dpi, dpi);
                    context.Shaders.ApplyDirectionalMotionBlur(
                        blurSurface.Canvas,
                        bulgeSurface,
                        new SKRect(0f, 0f, destRect.Width, destRect.Height),
                        physW,
                        physH,
                        dpi,
                        velocityPxPerSec,
                        VerticalShutterFactor,
                        context.IsGpuBackend,
                        packet.ReducedMotion,
                        AllowCpuShadersForTesting);
                }
                finally
                {
                    blurSurface.Canvas.RestoreToCount(blurSave);
                }

                sourceForBulge = blurSurface;
            }

            var power = fontId == LyricFontId.BodoniModa ? 2.05f : MotionBlurMath.DefaultBulgePower;
            var strength = MotionBlurMath.ClampBulgeStrength(_lensStrength.Value);

            context.Shaders.ApplyEllipticalBulge(
                context.Canvas,
                sourceForBulge,
                destRect,
                physW,
                physH,
                dpi,
                strength,
                power,
                context.IsGpuBackend,
                packet.ReducedMotion,
                AllowCpuShadersForTesting);
        }

        // Following Pulse Dot & optional bilingual sub-line rendered crisply above lens pass
        var fallbackDotX = destRect.MidX;
        var fallbackDotY = destRect.MidY + (fontSize * fitScale * 0.62f);
        _aesthetics.DrawFollowingDot(context.Canvas, packet, fallbackDotX, fallbackDotY, Math.Clamp(fontSize * 0.09f, 3.2f, 8f));

        StyleLayoutHelper.DrawSecondarySubline(
            in context,
            slice.CurrentLine,
            destRect.MidX,
            destRect.Bottom + Math.Max(14f, fontSize * 0.25f),
            fontSize * fitScale);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _aesthetics.Dispose();
    }

    private void DrawTriadGlyphs(
        SKCanvas canvas,
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        float centerX,
        float centerY,
        float slotPitch,
        float fitScale,
        LyricWord? prevWord,
        LyricWord activeWord,
        LyricWord? nextWord,
        double wordProgress,
        bool reducedMotion)
    {
        var packet = context.Packet;
        var highlightEased = Ease.Highlight(_highlightFade);
        var scrollY = reducedMotion ? 0f : _scrollOffsetY.Value;

        // Departing Slot -2 fast exit fade (140ms Ease.Exit)
        if (!reducedMotion && !string.IsNullOrEmpty(_departingWordText) && _departingElapsedSec < DepartingExitDurationSec)
        {
            var exitAlpha = 0.54f * (1f - Ease.Exit(_departingElapsedSec / DepartingExitDurationSec));
            DrawWordSlot(
                canvas,
                context,
                font,
                fontId,
                _departingWordText,
                centerX,
                centerY - (slotPitch * 2f) + scrollY,
                ContextWordScale * 0.90f * fitScale,
                packet.Palette.AccentColor3,
                opacity: exitAlpha,
                glowColor: null);
        }

        var prevShiftX = (_lastWordKey.WordIndex % 2 == 0 ? -1f : 1f) * Math.Min(font.Size * 0.24f, 16f);
        var nextShiftX = -prevShiftX * 0.85f;

        if (prevWord is not null)
        {
            DrawWordSlot(
                canvas,
                context,
                font,
                fontId,
                prevWord.Text,
                centerX + prevShiftX,
                centerY - slotPitch + scrollY,
                ContextWordScale * fitScale,
                packet.Palette.AccentColor3,
                opacity: 0.54f,
                glowColor: null);
        }

        if (nextWord is not null)
        {
            DrawWordSlot(
                canvas,
                context,
                font,
                fontId,
                nextWord.Text,
                centerX + nextShiftX,
                centerY + slotPitch + scrollY,
                ContextWordScale * fitScale,
                packet.Palette.AccentColor3,
                opacity: 0.48f,
                glowColor: null);
        }

        var activeScale = (reducedMotion ? 1.00f : _activeScale.Value) * fitScale;
        var activeOpacity = reducedMotion ? (0.90f + (0.10f * highlightEased)) : 1.0f;
        var activeColor = StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor1, SKColors.White.ToRgb(), 0.42f * highlightEased);

        var activeMeasuredW = TextShaperService.MeasureWidth(activeWord.Text, font, context.Fonts) * activeScale;
        var activeH = font.Size * activeScale * 1.05f;
        var sweepRect = new SKRect(
            centerX - (activeMeasuredW * 0.5f),
            centerY + scrollY - (activeH * 0.5f),
            centerX + (activeMeasuredW * 0.5f),
            centerY + scrollY + (activeH * 0.5f));
        _aesthetics.DrawWordColorSweep(canvas, sweepRect, wordProgress, packet, activeOpacity);

        var tiltDeg = !reducedMotion && packet.SafePartyEnergy > 1.02f
            ? ((_lastWordKey.WordIndex % 2 == 0 ? 1f : -1f) * Math.Max(1.6f, (packet.SafePartyEnergy - 1.0f) * 3.4f))
            : 0f;

        var useCutout = !reducedMotion && !packet.IsCompactCard && packet.ShowColorSweepLayer && activeWord.Text.Length >= 4 && (_lastWordKey.WordIndex % 3 == 1);
        if (useCutout)
        {
            var cutSave = canvas.Save();
            try
            {
                canvas.Translate(centerX, centerY + scrollY);
                canvas.Scale(activeScale, activeScale);
                _aesthetics.DrawInvertedCutoutWord(
                    canvas,
                    in context,
                    font,
                    activeWord.Text,
                    0f,
                    0f,
                    TextShaperService.MeasureWidth(activeWord.Text, font, context.Fonts),
                    tiltDeg == 0f ? -1.8f : tiltDeg,
                    activeOpacity);
            }
            finally
            {
                canvas.RestoreToCount(cutSave);
            }

            return;
        }

        DrawWordSlot(
            canvas,
            context,
            font,
            fontId,
            activeWord.Text,
            centerX,
            centerY + scrollY,
            activeScale,
            activeColor,
            opacity: activeOpacity,
            glowColor: packet.Palette.AccentColor2,
            tiltDegrees: tiltDeg);
    }

    private static void DrawWordSlot(
        SKCanvas canvas,
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        string text,
        float x,
        float y,
        float scale,
        RgbColor fillColor,
        float opacity,
        RgbColor? glowColor,
        float tiltDegrees = 0f)
    {
        if (string.IsNullOrWhiteSpace(text) || opacity <= 0.004f)
        {
            return;
        }

        var save = canvas.Save();
        try
        {
            canvas.Translate(x, y);
            if (MathF.Abs(tiltDegrees) > 0.05f)
            {
                canvas.RotateDegrees(tiltDegrees);
            }

            canvas.Scale(scale, scale);

            font.GetFontMetrics(out var metrics);
            var baselineY = -((metrics.Ascent + metrics.Descent) * 0.5f);

            context.Legibility.DrawLegibleText(
                canvas,
                text,
                x: 0f,
                y: baselineY,
                SKTextAlign.Center,
                font,
                fontId,
                fillColor,
                opacity: opacity,
                activeGlowColor: glowColor,
                fonts: context.Fonts);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    private static float ResolveFontSize(FrameRenderPacket packet) =>
        StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 1.0f);

    private static float MeasureMaxTriadWidth(
        LyricWord? prev,
        LyricWord active,
        LyricWord? next,
        SKFont font,
        FontAssetRegistry fonts)
    {
        var maxW = TextShaperService.MeasureWidth(active.Text, font, fonts);
        if (prev is not null)
        {
            maxW = Math.Max(maxW, TextShaperService.MeasureWidth(prev.Text, font, fonts));
        }

        if (next is not null)
        {
            maxW = Math.Max(maxW, TextShaperService.MeasureWidth(next.Text, font, fonts));
        }

        return maxW;
    }
}
