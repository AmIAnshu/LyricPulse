using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using LyricPulse.Rendering.Shaders;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #2 — Fisheye Visual: spherical convex-bulge 3-word triad paired with alternating
/// left/right TTF-verified Fluent Icons / Emoji (with subtle <see cref="OverlayPlaybackSnapshot.TrackMoodVisual"/>
/// fallback), playful Verci-inspired tilted high-contrast inverted cutout sticker highlight on key
/// visual words, and 13-tap vertical motion blur (0.48 shutter).
/// </summary>
public sealed class FisheyeVisualStyleRenderer : IKineticStyleRenderer
{
    public const float VerticalShutterFactor = 0.48f;
    public const float SlotPitchRatio = 1.24f;
    public const float RestingActiveScale = 1.08f;
    public const float ContextWordScale = 0.78f;

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _iconAuraPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKImageFilter _iconAuraBlur = SKImageFilter.CreateBlur(10f, 10f);

    private SpringState _scrollOffsetY = SpringState.AtRest(0f);
    private SpringState _activeScale = SpringState.AtRest(RestingActiveScale);
    private SpringState _iconPopScale = SpringState.AtRest(1.0f);
    private SpringState _iconTiltDeg = SpringState.AtRest(0f);
    private SpringState _lensStrength = SpringState.AtRest(MotionBlurMath.BaselineBulgeStrength);
    private float _highlightFade = 1.0f;

    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private float _lastWordDurationSec = 0.32f;
    private bool _disposed;

    internal bool AllowCpuShadersForTesting { get; set; }

    public OverlayStyleId StyleId => OverlayStyleId.FisheyeVisual;

    public void ResetState()
    {
        _scrollOffsetY = SpringState.AtRest(0f);
        _activeScale = SpringState.AtRest(RestingActiveScale);
        _iconPopScale = SpringState.AtRest(1.0f);
        _iconTiltDeg = SpringState.AtRest(0f);
        _lensStrength = SpringState.AtRest(MotionBlurMath.BaselineBulgeStrength);
        _highlightFade = 1.0f;
        _lastWordKey = (-1, -1);
        _lastWordDurationSec = 0.32f;
        _aesthetics.ResetDot();
    }

    public bool Advance(in FrameRenderPacket packet, in LifecycleFrameState lifecycle)
    {
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        var (_, active, _, wordKey, wordDurationSec) = StyleLayoutHelper.ResolveWordTriad(slice);
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
            _lastWordKey = wordKey;

            if (packet.ReducedMotion)
            {
                _scrollOffsetY = SpringState.AtRest(0f);
                _activeScale = SpringState.AtRest(1.00f);
                _iconPopScale = SpringState.AtRest(1.00f);
                _iconTiltDeg = SpringState.AtRest(0f);
                _lensStrength = SpringState.AtRest(0f);
                _highlightFade = 0f;
            }
            else
            {
                var energy = packet.SafePartyEnergy;
                if (wasInitialized)
                {
                    _scrollOffsetY = new SpringState(Value: slotPitch, Velocity: _scrollOffsetY.Velocity * 0.25f, Target: 0f);
                }

                _activeScale = new SpringState(Value: ContextWordScale, Velocity: 0f, Target: RestingActiveScale + ((energy - 1f) * 0.08f));
                _iconPopScale = new SpringState(Value: 0.45f, Velocity: 0f, Target: 1.00f);
                var tiltSign = (wordKey.WordIndex % 2 == 0) ? 1f : -1f;
                _iconTiltDeg = new SpringState(Value: tiltSign * 6.5f * energy, Velocity: 0f, Target: 0f);

                var pacing = Math.Clamp(wordDurationSec / 0.32f, 0.42f, 1.35f);
                var impulse = (isLineTransition ? 2.0f : 1.55f * Math.Min(1.0f, pacing)) * Math.Clamp(energy, 0.7f, 1.35f);
                var nextLens = _lensStrength.WithTarget(MotionBlurMath.BaselineBulgeStrength).WithImpulse(impulse);
                _lensStrength = nextLens with
                {
                    Value = Math.Clamp(nextLens.Value, MotionBlurMath.BaselineBulgeStrength, MotionBlurMath.MaxBulgeStrength)
                };
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
            _scrollOffsetY = SpringState.AtRest(0f);
            _activeScale = SpringState.AtRest(1.00f);
            _iconPopScale = SpringState.AtRest(1.00f);
            _iconTiltDeg = SpringState.AtRest(0f);
            _lensStrength = SpringState.AtRest(0f);
            _aesthetics.ResetDot();
            return _highlightFade < 0.999f;
        }

        var wordSpring = SpringTokens.WordSnap.WithTempoCompression(_lastWordDurationSec);
        var lineSpring = SpringTokens.LineShift.WithTempoCompression(_lastWordDurationSec);
        var iconSpring = SpringTokens.IconPop.WithTempoCompression(_lastWordDurationSec);

        _scrollOffsetY = _scrollOffsetY.Step(lineSpring, dt);
        _activeScale = _activeScale.Step(wordSpring, dt);
        _iconPopScale = _iconPopScale.Step(iconSpring, dt);
        _iconTiltDeg = _iconTiltDeg.Step(iconSpring, dt);
        _lensStrength = _lensStrength.Step(SpringTokens.LensBreath, dt);
        if (_lensStrength.Value > MotionBlurMath.MaxBulgeStrength)
        {
            _lensStrength = _lensStrength with { Value = MotionBlurMath.MaxBulgeStrength };
        }

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet);
        var dotProgress = (float)Math.Clamp(slice.WordProgress, 0.0, 1.0);
        var dotTargetX = anchorX + ((dotProgress - 0.5f) * Math.Min(packet.ViewportWidthDip * 0.28f, fontSize * 3.2f));
        var dotTargetY = anchorY + (fontSize * 0.66f);
        var dotActive = packet.ShowFollowingDot && _aesthetics.AdvanceDot(dotTargetX, dotTargetY, dt, reducedMotion: false, _lastWordDurationSec);

        return !_scrollOffsetY.IsAtRest ||
               !_activeScale.IsAtRest ||
               !_iconPopScale.IsAtRest ||
               !_iconTiltDeg.IsAtRest ||
               !_lensStrength.IsAtRest ||
               dotActive ||
               _highlightFade < 0.999f;
    }

    public void Render(in StyleRenderContext context, in LifecycleFrameState lifecycle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var packet = context.Packet;
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        var (prevWord, activeWord, nextWord, wordKey, _) = StyleLayoutHelper.ResolveWordTriad(slice);
        if (activeWord is null)
        {
            return;
        }

        var fontId = packet.ActiveFontId;
        var fontSize = ResolveFontSize(packet);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var slotPitch = fontSize * SlotPitchRatio;
        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet);

        var maxUnitWidth = MeasureMaxUnitWidth(prevWord, activeWord, nextWord, font, context.Fonts, packet);
        var fitScale = TextShaperService.ComputeAutoFitScale(maxUnitWidth * RestingActiveScale, packet.ViewportWidthDip, 0.72f);

        var dpi = packet.RenderScaling;
        var velocityPxPerSec = new SKPoint(0f, _scrollOffsetY.Velocity * dpi);
        var clampedVel = MotionBlurMath.ComputeClampedVelocity(velocityPxPerSec, VerticalShutterFactor, dpi, out var blurDeadZone);
        var (_, blurPadYPx) = blurDeadZone ? (0, 0) : MotionBlurMath.ComputeDirectionalPaddingPx(clampedVel);
        var blurPadYDip = blurPadYPx / Math.Max(dpi, 0.5f);

        var layerWidthDip = Math.Clamp(Math.Max(maxUnitWidth * fitScale * 1.90f, fontSize * 8.6f), packet.IsCompactCard ? 130f : 260f, packet.ViewportWidthDip);
        var layerHeightDip = Math.Clamp((fontSize * 5.6f) + (blurPadYDip * 2f), packet.IsCompactCard ? 104f : 196f, packet.ViewportHeightDip);

        var destRect = StyleLayoutHelper.ClampLayerRectToViewport(
            anchorX,
            anchorY,
            layerWidthDip,
            layerHeightDip,
            packet.ViewportWidthDip,
            packet.ViewportHeightDip);

        // Layer 0 radial scrim directly on main canvas (never clipped inside offscreen shader passes!)
        var scrimCluster = new SKRect(
            destRect.MidX - Math.Max(fontSize * 3.4f, maxUnitWidth * fitScale * 0.65f),
            destRect.MidY - (slotPitch * 1.48f),
            destRect.MidX + Math.Max(fontSize * 3.4f, maxUnitWidth * fitScale * 0.65f),
            destRect.MidY + (slotPitch * 1.48f));

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
            DrawVisualTriad(
                context.Canvas,
                in context,
                font,
                fontId,
                destRect.MidX,
                destRect.MidY,
                slotPitch,
                fitScale,
                prevWord,
                activeWord,
                nextWord,
                wordKey.WordIndex,
                slice.WordProgress,
                packet.ReducedMotion);
        }
        else
        {
            var physW = Math.Clamp((int)MathF.Ceiling(destRect.Width * dpi), 64, OffscreenSurfacePool.MaxSurfaceDimensionPx);
            var physH = Math.Clamp((int)MathF.Ceiling(destRect.Height * dpi), 64, OffscreenSurfacePool.MaxSurfaceDimensionPx);

            var bulgeSurface = context.SurfacePool.RentSurface(OffscreenSurfaceSlot.Bulge, context.GrContext, physW, physH);
            bulgeSurface.Canvas.Clear(SKColors.Transparent);

            var offSave = bulgeSurface.Canvas.Save();
            try
            {
                bulgeSurface.Canvas.Scale(dpi, dpi);
                DrawVisualTriad(
                    bulgeSurface.Canvas,
                    in context,
                    font,
                    fontId,
                    destRect.Width * 0.5f,
                    destRect.Height * 0.5f,
                    slotPitch,
                    fitScale,
                    prevWord,
                    activeWord,
                    nextWord,
                    wordKey.WordIndex,
                    slice.WordProgress,
                    reducedMotion: false);
            }
            finally
            {
                bulgeSurface.Canvas.RestoreToCount(offSave);
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

        var activeW = TextShaperService.MeasureWidth(activeWord.Text, font, context.Fonts) * fitScale;
        var dotX = destRect.MidX + (((float)Math.Clamp(slice.WordProgress, 0.0, 1.0) - 0.5f) * activeW);
        var dotY = destRect.MidY + (fontSize * fitScale * 0.68f);
        _aesthetics.DrawFollowingDot(context.Canvas, packet, dotX, dotY, Math.Clamp(fontSize * 0.09f, 3.0f, 8.0f));

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
        _iconAuraPaint.Dispose();
        _iconAuraBlur.Dispose();
    }

    private void DrawVisualTriad(
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
        int activeWordIndex,
        double wordProgress,
        bool reducedMotion)
    {
        var packet = context.Packet;
        var scrollY = reducedMotion ? 0f : _scrollOffsetY.Value;
        var highlightEased = Ease.Highlight(_highlightFade);

        if (prevWord is not null)
        {
            DrawUnitSlot(
                canvas,
                in context,
                font,
                fontId,
                prevWord.Text,
                prevWord.Visual,
                iconOnLeft: ((activeWordIndex - 1) & 1) != 0,
                centerX,
                centerY - slotPitch + scrollY,
                ContextWordScale * fitScale,
                iconScaleMultiplier: 0.78f,
                iconTiltDeg: 0f,
                textColor: packet.Palette.AccentColor3,
                iconTint: packet.Palette.AccentColor3,
                opacity: 0.52f,
                isActive: false,
                useCutoutSticker: false,
                wordProgress: 1.0);
        }

        if (nextWord is not null)
        {
            DrawUnitSlot(
                canvas,
                in context,
                font,
                fontId,
                nextWord.Text,
                nextWord.Visual,
                iconOnLeft: ((activeWordIndex + 1) & 1) != 0,
                centerX,
                centerY + slotPitch + scrollY,
                ContextWordScale * fitScale,
                iconScaleMultiplier: 0.68f,
                iconTiltDeg: 0f,
                textColor: packet.Palette.AccentColor3,
                iconTint: packet.Palette.AccentColor3,
                opacity: 0.44f,
                isActive: false,
                useCutoutSticker: false,
                wordProgress: 0.0);
        }

        var activeVisual = StyleLayoutHelper.ResolveWordVisual(activeWord, packet, allowTrackMoodFallback: true);
        var activeScale = (reducedMotion ? 1.00f : _activeScale.Value) * fitScale;
        var activeOpacity = reducedMotion ? (0.90f + (0.10f * highlightEased)) : 1.0f;
        var activeColor = StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor3, packet.Palette.AccentColor1, highlightEased);
        var iconPop = reducedMotion ? 1.00f : _iconPopScale.Value;
        var iconTilt = reducedMotion ? 0f : _iconTiltDeg.Value;
        var useSticker = packet.ShowColorSweepLayer && activeWord.Visual is not null;

        DrawUnitSlot(
            canvas,
            in context,
            font,
            fontId,
            activeWord.Text,
            activeVisual,
            iconOnLeft: (activeWordIndex & 1) != 0,
            centerX,
            centerY + scrollY,
            activeScale,
            iconScaleMultiplier: iconPop,
            iconTiltDeg: iconTilt,
            textColor: activeColor,
            iconTint: packet.Palette.AccentColor2,
            opacity: activeOpacity,
            isActive: true,
            useCutoutSticker: useSticker,
            wordProgress: wordProgress);
    }

    private void DrawUnitSlot(
        SKCanvas canvas,
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        string text,
        WordVisualMatch? visual,
        bool iconOnLeft,
        float centerX,
        float centerY,
        float unitScale,
        float iconScaleMultiplier,
        float iconTiltDeg,
        RgbColor textColor,
        RgbColor iconTint,
        float opacity,
        bool isActive,
        bool useCutoutSticker,
        double wordProgress)
    {
        if (string.IsNullOrWhiteSpace(text) || opacity <= 0.004f)
        {
            return;
        }

        var packet = context.Packet;
        var wordW = TextShaperService.MeasureWidth(text, font, context.Fonts);
        var iconSize = font.Size * 0.92f;
        var gap = font.Size * 0.30f;
        var hasVisual = visual is not null;
        var totalW = wordW + (hasVisual ? iconSize + gap : 0f);

        var save = canvas.Save();
        try
        {
            canvas.Translate(centerX, centerY);
            canvas.Scale(unitScale, unitScale);

            var startX = -totalW * 0.5f;
            float textCenterX;
            float iconCenterX;

            if (hasVisual && iconOnLeft)
            {
                iconCenterX = startX + (iconSize * 0.5f);
                textCenterX = startX + iconSize + gap + (wordW * 0.5f);
            }
            else if (hasVisual)
            {
                textCenterX = startX + (wordW * 0.5f);
                iconCenterX = startX + wordW + gap + (iconSize * 0.5f);
            }
            else
            {
                textCenterX = 0f;
                iconCenterX = 0f;
            }

            if (hasVisual && visual is not null)
            {
                var iconSave = canvas.Save();
                try
                {
                    canvas.Translate(iconCenterX, 0f);
                    if (MathF.Abs(iconTiltDeg) > 0.05f)
                    {
                        canvas.RotateDegrees(iconTiltDeg);
                    }

                    canvas.Scale(iconScaleMultiplier, iconScaleMultiplier);

                    if (isActive)
                    {
                        _iconAuraPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.38f * opacity);
                        _iconAuraPaint.ImageFilter = _iconAuraBlur;
                        try
                        {
                            canvas.DrawCircle(0f, 0f, iconSize * 0.52f, _iconAuraPaint);
                        }
                        finally
                        {
                            _iconAuraPaint.ImageFilter = null;
                        }
                    }

                    StyleLayoutHelper.DrawVisualGlyph(
                        canvas,
                        context.Fonts,
                        context.Legibility,
                        visual,
                        centerX: 0f,
                        centerY: 0f,
                        iconSizeDip: iconSize,
                        tintColor: iconTint,
                        opacity: opacity);
                }
                finally
                {
                    canvas.RestoreToCount(iconSave);
                }
            }

            if (useCutoutSticker && isActive)
            {
                var stickerTilt = iconOnLeft ? 2.6f : -2.6f;
                _aesthetics.DrawInvertedCutoutWord(
                    canvas,
                    in context,
                    font,
                    text,
                    textCenterX,
                    0f,
                    wordW,
                    tiltDegrees: stickerTilt,
                    opacity: opacity);
                return;
            }

            if (isActive && packet.ShowColorSweepLayer)
            {
                var sweepRect = new SKRect(textCenterX - (wordW * 0.5f), -font.Size * 0.54f, textCenterX + (wordW * 0.5f), font.Size * 0.54f);
                _aesthetics.DrawWordColorSweep(canvas, sweepRect, wordProgress, packet, opacity);
            }

            font.GetFontMetrics(out var metrics);
            var baselineY = -((metrics.Ascent + metrics.Descent) * 0.5f);

            context.Legibility.DrawLegibleText(
                canvas,
                text,
                textCenterX,
                baselineY,
                SKTextAlign.Center,
                font,
                fontId,
                textColor,
                opacity: opacity,
                activeGlowColor: isActive ? packet.Palette.AccentColor2 : null,
                fonts: context.Fonts);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    private static float ResolveFontSize(FrameRenderPacket packet) =>
        StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 0.96f);

    private static float MeasureMaxUnitWidth(
        LyricWord? prev,
        LyricWord active,
        LyricWord? next,
        SKFont font,
        FontAssetRegistry fonts,
        FrameRenderPacket packet)
    {
        var iconAllowance = font.Size * 1.25f;
        var activeHasVisual = StyleLayoutHelper.ResolveWordVisual(active, packet, allowTrackMoodFallback: true) is not null;
        var maxW = TextShaperService.MeasureWidth(active.Text, font, fonts) + (activeHasVisual ? iconAllowance : 0f);
        if (prev is not null)
        {
            maxW = Math.Max(maxW, TextShaperService.MeasureWidth(prev.Text, font, fonts) + (prev.Visual is not null ? iconAllowance : 0f));
        }

        if (next is not null)
        {
            maxW = Math.Max(maxW, TextShaperService.MeasureWidth(next.Text, font, fonts) + (next.Visual is not null ? iconAllowance : 0f));
        }

        return maxW;
    }
}
