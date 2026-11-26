using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using LyricPulse.Rendering.Shaders;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #2 — Fisheye Visual (Optical Lens &amp; Editorial Overhaul):
/// Spherical convex-bulge 5-word depth triad with wide organic scatter, optical lens glass
/// refraction bezel &amp; glint arcs, RGB chromatic split dispersion on beat hits, larger
/// orbiting custom procedural vector illustrations, rotated Verci cutout stickers,
/// 13-tap vertical motion blur (0.48 shutter), and word-locked beat dot hopping.
/// </summary>
public sealed class FisheyeVisualStyleRenderer : IKineticStyleRenderer
{
    public const float VerticalShutterFactor = 0.48f;
    public const float SlotPitchRatio = 1.30f;
    public const float RestingActiveScale = 1.16f;
    public const float ContextWordScale = 0.74f;
    public const float DistantWordScale = 0.54f;

    private static readonly RgbColor ChromaticCyan = new(0x22, 0xD3, 0xEE);
    private static readonly RgbColor ChromaticMagenta = new(0xF4, 0x3F, 0x5E);

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _iconAuraPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _lensRingPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _lensGlowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _sparkPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKImageFilter _iconAuraBlur = SKImageFilter.CreateBlur(11f, 11f);

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
                    _scrollOffsetY = new SpringState(Value: slotPitch * 0.92f, Velocity: _scrollOffsetY.Velocity * 0.25f, Target: 0f);
                }

                _activeScale = new SpringState(
                    Value: ContextWordScale,
                    Velocity: 0f,
                    Target: RestingActiveScale + ((energy - 1f) * 0.12f));
                _iconPopScale = new SpringState(Value: 0.38f, Velocity: 0f, Target: 1.06f);
                var tiltSign = (wordKey.WordIndex % 2 == 0) ? 1f : -1f;
                _iconTiltDeg = new SpringState(Value: tiltSign * 11.5f * energy, Velocity: 0f, Target: tiltSign * 2.5f);

                var pacing = Math.Clamp(wordDurationSec / 0.32f, 0.42f, 1.35f);
                var impulse = (isLineTransition ? 2.15f : 1.70f * Math.Min(1.0f, pacing)) * Math.Clamp(energy, 0.72f, 1.38f);
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
        var dotTargetX = anchorX;
        var dotTargetY = anchorY + (fontSize * 0.72f);
        var dotActive = packet.ShowFollowingDot && _aesthetics.AdvanceDot(wordKey, dotTargetX, dotTargetY, dt, reducedMotion: false, _lastWordDurationSec);

        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        return !_scrollOffsetY.IsAtRest ||
               !_activeScale.IsAtRest ||
               !_iconPopScale.IsAtRest ||
               !_iconTiltDeg.IsAtRest ||
               !_lensStrength.IsAtRest ||
               dotActive ||
               _highlightFade < 0.999f ||
               isPlaying;
    }

    public void Render(in StyleRenderContext context, in LifecycleFrameState lifecycle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var packet = context.Packet;
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        var (prev2Word, prevWord, activeWord, nextWord, next2Word, wordKey) = ResolveExtendedWindow(slice);
        if (activeWord is null)
        {
            return;
        }

        var fontId = packet.ActiveFontId;
        var fontSize = ResolveFontSize(packet);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var slotPitch = fontSize * SlotPitchRatio;
        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet);

        var maxUnitWidth = MeasureMaxUnitWidth(prevWord, activeWord, nextWord, font, context.Fonts);
        var fitScale = TextShaperService.ComputeAutoFitScale(maxUnitWidth * RestingActiveScale, packet.ViewportWidthDip, 0.70f);

        var dpi = packet.RenderScaling;
        var velocityPxPerSec = new SKPoint(0f, _scrollOffsetY.Velocity * dpi);
        var clampedVel = MotionBlurMath.ComputeClampedVelocity(velocityPxPerSec, VerticalShutterFactor, dpi, out var blurDeadZone);
        var (_, blurPadYPx) = blurDeadZone ? (0, 0) : MotionBlurMath.ComputeDirectionalPaddingPx(clampedVel);
        var blurPadYDip = blurPadYPx / Math.Max(dpi, 0.5f);

        var layerWidthDip = Math.Clamp(
            Math.Max(maxUnitWidth * fitScale * 2.25f, fontSize * 10.5f),
            packet.IsCompactCard ? 140f : 300f,
            packet.ViewportWidthDip);
        var layerHeightDip = Math.Clamp(
            (fontSize * 6.8f) + (blurPadYDip * 2f),
            packet.IsCompactCard ? 114f : 228f,
            packet.ViewportHeightDip);

        var destRect = StyleLayoutHelper.ClampLayerRectToViewport(
            anchorX,
            anchorY,
            layerWidthDip,
            layerHeightDip,
            packet.ViewportWidthDip,
            packet.ViewportHeightDip);

        // Layer 0 radial scrim on main canvas (strictly unobstructed outside lens pass)
        var scrimCluster = new SKRect(
            destRect.MidX - Math.Max(fontSize * 3.8f, maxUnitWidth * fitScale * 0.72f),
            destRect.MidY - (slotPitch * 1.65f),
            destRect.MidX + Math.Max(fontSize * 3.8f, maxUnitWidth * fitScale * 0.72f),
            destRect.MidY + (slotPitch * 1.65f));

        context.Legibility.DrawAmbientScrim(
            context.Canvas,
            scrimCluster,
            font.Size * fitScale,
            packet.Palette.ScrimTint,
            packet.ScrimIntensity,
            packet.HighContrastScrim);

        // Layer 0.5: Optical spherical lens reticle & glass glint (StageCanvas / GalleryCard only — keeps DesktopOverlay corners 100% transparent!)
        if (packet.SurfaceMode != StageRenderSurfaceMode.DesktopOverlay && !packet.ReducedMotion)
        {
            DrawOpticalLensAperture(context.Canvas, in context, destRect, slice.CurrentLine?.Index ?? 0);
        }

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
                prev2Word,
                prevWord,
                activeWord,
                nextWord,
                next2Word,
                wordKey,
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
                    prev2Word,
                    prevWord,
                    activeWord,
                    nextWord,
                    next2Word,
                    wordKey,
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

            var power = fontId == LyricFontId.BodoniModa ? 1.98f : Math.Max(1.65f, MotionBlurMath.DefaultBulgePower);
            var beatBulgeBonus = packet.EffectiveAudioMetrics.BeatPulse * 0.045f;
            var strength = MotionBlurMath.ClampBulgeStrength(_lensStrength.Value + beatBulgeBonus);

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

        var dotX = destRect.MidX;
        var dotY = destRect.MidY + (fontSize * fitScale * 0.72f);
        _aesthetics.DrawFollowingDot(context.Canvas, packet, dotX, dotY, Math.Clamp(fontSize * 0.095f, 3.0f, 8.0f));

        StyleLayoutHelper.DrawSecondarySubline(
            in context,
            slice.CurrentLine,
            destRect.MidX,
            Math.Min(packet.ViewportHeightDip - 16f, destRect.Bottom + Math.Max(14f, fontSize * 0.25f)),
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
        _lensRingPaint.Dispose();
        _lensGlowPaint.Dispose();
        _sparkPaint.Dispose();
        _iconAuraBlur.Dispose();
    }

    private void DrawOpticalLensAperture(SKCanvas canvas, in StyleRenderContext context, SKRect lensBounds, int lineIndex)
    {
        var packet = context.Packet;
        var audio = packet.EffectiveAudioMetrics;
        var pulse = 1.0f + (audio.BassEnergy * 0.055f) + (audio.BeatPulse * 0.045f);
        var radius = MathF.Min(lensBounds.Width, lensBounds.Height) * 0.44f * pulse;
        if (radius < 18f)
        {
            return;
        }

        var cx = lensBounds.MidX;
        var cy = lensBounds.MidY;
        var time = (float)packet.RenderTimeSeconds;

        // Soft outer chromatic optical bezel ring
        _lensGlowPaint.StrokeWidth = packet.IsCompactCard ? 2.5f : 5.5f;
        _lensGlowPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.14f + (audio.BeatPulse * 0.14f));
        _lensGlowPaint.ImageFilter = _iconAuraBlur;
        try
        {
            canvas.DrawCircle(cx, cy, radius, _lensGlowPaint);
        }
        finally
        {
            _lensGlowPaint.ImageFilter = null;
        }

        _lensRingPaint.StrokeWidth = packet.IsCompactCard ? 0.8f : 1.15f;
        _lensRingPaint.Color = SKColors.White.WithAlpha((byte)(34 + (audio.BeatPulse * 28f)));
        canvas.DrawCircle(cx, cy, radius, _lensRingPaint);

        // Specular curved glass highlight arcs
        if (!packet.IsCompactCard)
        {
            var arcRect = new SKRect(cx - (radius * 0.94f), cy - (radius * 0.94f), cx + (radius * 0.94f), cy + (radius * 0.94f));
            _lensRingPaint.StrokeWidth = 1.4f;
            _lensRingPaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(0.24f + (audio.TrebleEnergy * 0.14f));
            var sweepStart = 206f + (MathF.Sin(time * 0.6f) * 6f);
            canvas.DrawArc(arcRect, sweepStart, 52f, useCenter: false, _lensRingPaint);

            _lensRingPaint.Color = ChromaticMagenta.ToSkColorWithAlpha(0.18f);
            canvas.DrawArc(arcRect, sweepStart - 178f, 38f, useCenter: false, _lensRingPaint);
        }

        // Subtle peripheral mood motif in lens outer rim on StageCanvas when available
        if (!packet.IsCompactCard && packet.PlaybackSnapshot?.TrackMoodVisual is { } moodVisual && ((lineIndex % 2) == 0))
        {
            var orbX = cx - (radius * 0.72f);
            var orbY = cy - (radius * 0.56f);
            _aesthetics.DrawReactiveVisual(
                canvas,
                in context,
                moodVisual,
                orbX,
                orbY,
                Math.Clamp(radius * 0.18f, 18f, 34f),
                opacity: 0.22f + (audio.BeatPulse * 0.12f),
                isActive: false);
        }
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
        LyricWord? prev2Word,
        LyricWord? prevWord,
        LyricWord activeWord,
        LyricWord? nextWord,
        LyricWord? next2Word,
        (int LineIndex, int WordIndex) wordKey,
        double wordProgress,
        bool reducedMotion)
    {
        var packet = context.Packet;
        var scrollY = reducedMotion ? 0f : _scrollOffsetY.Value;
        var highlightEased = Ease.Highlight(_highlightFade);
        var activeIdx = wordKey.WordIndex;

        // Organic barrel-arc horizontal scattering across all 5 depth slots
        var spreadBase = packet.IsCompactCard ? 22f : Math.Clamp(slotPitch * 0.62f, 24f, 68f) * fitScale;
        var altSign = (activeIdx & 1) == 0 ? 1f : -1f;
        var prev2ShiftX = reducedMotion ? 0f : altSign * spreadBase * 1.35f;
        var prevShiftX = reducedMotion ? 0f : -altSign * spreadBase * 0.92f;
        var nextShiftX = reducedMotion ? 0f : altSign * spreadBase * 0.98f;
        var next2ShiftX = reducedMotion ? 0f : -altSign * spreadBase * 1.42f;

        if (!packet.IsCompactCard && !reducedMotion && prev2Word is not null)
        {
            DrawUnitSlot(
                canvas,
                in context,
                font,
                fontId,
                prev2Word.Text,
                prev2Word.Visual,
                iconOnLeft: ((activeIdx - 2) & 1) != 0,
                centerX + prev2ShiftX,
                centerY - (slotPitch * 1.88f) + (scrollY * 0.85f),
                DistantWordScale * fitScale,
                iconScaleMultiplier: 0.72f,
                iconTiltDeg: altSign * 4f,
                slotTiltDeg: -altSign * 3.2f,
                textColor: packet.Palette.AccentColor3,
                opacity: 0.24f,
                isActive: false,
                useCutoutSticker: false,
                wordProgress: 1.0,
                activeIdx - 2);
        }

        if (prevWord is not null)
        {
            DrawUnitSlot(
                canvas,
                in context,
                font,
                fontId,
                prevWord.Text,
                prevWord.Visual,
                iconOnLeft: ((activeIdx - 1) & 1) != 0,
                centerX + prevShiftX,
                centerY - (slotPitch * 0.98f) + scrollY,
                ContextWordScale * fitScale,
                iconScaleMultiplier: 0.86f,
                iconTiltDeg: -altSign * 4.5f,
                slotTiltDeg: reducedMotion ? 0f : (altSign * 2.4f),
                textColor: packet.Palette.AccentColor3,
                opacity: 0.54f,
                isActive: false,
                useCutoutSticker: false,
                wordProgress: 1.0,
                activeIdx - 1);
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
                iconOnLeft: ((activeIdx + 1) & 1) != 0,
                centerX + nextShiftX,
                centerY + (slotPitch * 0.98f) + scrollY,
                ContextWordScale * fitScale,
                iconScaleMultiplier: 0.78f,
                iconTiltDeg: altSign * 4.0f,
                slotTiltDeg: reducedMotion ? 0f : (-altSign * 2.4f),
                textColor: packet.Palette.AccentColor3,
                opacity: 0.46f,
                isActive: false,
                useCutoutSticker: false,
                wordProgress: 0.0,
                activeIdx + 1);
        }

        if (!packet.IsCompactCard && !reducedMotion && next2Word is not null)
        {
            DrawUnitSlot(
                canvas,
                in context,
                font,
                fontId,
                next2Word.Text,
                next2Word.Visual,
                iconOnLeft: ((activeIdx + 2) & 1) != 0,
                centerX + next2ShiftX,
                centerY + (slotPitch * 1.88f) + (scrollY * 0.85f),
                DistantWordScale * fitScale,
                iconScaleMultiplier: 0.65f,
                iconTiltDeg: -altSign * 3.5f,
                slotTiltDeg: altSign * 3.0f,
                textColor: packet.Palette.AccentColor3,
                opacity: 0.20f,
                isActive: false,
                useCutoutSticker: false,
                wordProgress: 0.0,
                activeIdx + 2);
        }

        var activeVisual = StyleLayoutHelper.ResolveWordVisual(activeWord, packet, allowTrackMoodFallback: false);
        var activeScale = (reducedMotion ? 1.00f : _activeScale.Value) * fitScale;
        var activeOpacity = reducedMotion ? (0.90f + (0.10f * highlightEased)) : 1.0f;
        var activeColor = StyleLayoutHelper.BlendSrgb(
            packet.Palette.AccentColor1,
            new RgbColor(255, 255, 255),
            0.46f * highlightEased);
        var iconPop = reducedMotion ? 1.00f : _iconPopScale.Value;
        var iconTilt = reducedMotion ? 0f : _iconTiltDeg.Value;
        var useSticker = packet.ShowColorSweepLayer &&
                         (activeWord.Visual is not null ||
                          (!reducedMotion && !packet.IsCompactCard && !StyleLayoutHelper.IsConnectorWord(activeWord.Text) && activeWord.Text.Length >= 4 && (activeIdx % 3 == 1)));

        DrawUnitSlot(
            canvas,
            in context,
            font,
            fontId,
            activeWord.Text,
            activeVisual,
            iconOnLeft: (activeIdx & 1) != 0,
            centerX,
            centerY + scrollY,
            activeScale,
            iconScaleMultiplier: iconPop,
            iconTiltDeg: iconTilt,
            slotTiltDeg: 0f,
            textColor: activeColor,
            opacity: activeOpacity,
            isActive: true,
            useCutoutSticker: useSticker,
            wordProgress: wordProgress,
            activeIdx);
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
        float slotTiltDeg,
        RgbColor textColor,
        float opacity,
        bool isActive,
        bool useCutoutSticker,
        double wordProgress,
        int wordIndex)
    {
        if (string.IsNullOrWhiteSpace(text) || opacity <= 0.004f)
        {
            return;
        }

        var packet = context.Packet;
        var wordW = TextShaperService.MeasureWidth(text, font, context.Fonts);
        var iconSize = font.Size * 1.12f;
        var gap = font.Size * 0.34f;
        var hasVisual = visual is not null;
        var totalW = wordW + (hasVisual ? iconSize + gap : 0f);

        var save = canvas.Save();
        try
        {
            canvas.Translate(centerX, centerY);
            if (MathF.Abs(slotTiltDeg) > 0.05f)
            {
                canvas.RotateDegrees(slotTiltDeg);
            }

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
                        var beatScale = 1.0f + (packet.EffectiveAudioMetrics.BeatPulse * 0.16f);
                        _iconAuraPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.46f * opacity);
                        _iconAuraPaint.ImageFilter = _iconAuraBlur;
                        try
                        {
                            canvas.DrawCircle(0f, 0f, iconSize * 0.58f * beatScale, _iconAuraPaint);
                        }
                        finally
                        {
                            _iconAuraPaint.ImageFilter = null;
                        }

                        // Orbiting stardust sparkles around active hero illustration
                        if (!packet.ReducedMotion && !packet.IsCompactCard)
                        {
                            var time = (float)packet.RenderTimeSeconds;
                            for (var s = 0; s < 3; s++)
                            {
                                var ang = (time * 2.1f) + (s * (MathF.Tau / 3f));
                                var sr = iconSize * 0.62f * beatScale;
                                _sparkPaint.Color = (s == 0 ? SKColors.White : packet.Palette.AccentColor1.ToSkColor()).WithAlpha((byte)(200 * opacity));
                                canvas.DrawCircle(MathF.Cos(ang) * sr, MathF.Sin(ang) * sr, 2.2f, _sparkPaint);
                            }
                        }
                    }

                    _aesthetics.DrawReactiveVisual(
                        canvas,
                        in context,
                        visual,
                        centerX: 0f,
                        centerY: 0f,
                        iconSizeDip: iconSize * 1.18f,
                        opacity: opacity,
                        isActive: isActive);
                }
                finally
                {
                    canvas.RestoreToCount(iconSave);
                }
            }

            if (useCutoutSticker && isActive)
            {
                var stickerTilt = (wordIndex % 2 == 0) ? 2.8f : -2.8f;
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

            // Subtle optical RGB chromatic dispersion fringe on active beat hits / word snap (never blurs glyphs)
            if (isActive && !packet.ReducedMotion && !packet.IsCompactCard)
            {
                var splitPx = Math.Clamp((packet.EffectiveAudioMetrics.BeatPulse * 3.8f) + ((1f - Ease.Highlight(_highlightFade)) * 3.2f), 0f, 5.2f);
                if (splitPx > 0.65f)
                {
                    context.Legibility.DrawLegibleText(
                        canvas,
                        text,
                        textCenterX - splitPx,
                        baselineY,
                        SKTextAlign.Center,
                        font,
                        fontId,
                        ChromaticCyan,
                        opacity: 0.36f * opacity,
                        activeGlowColor: null,
                        includeStrokeHalo: false,
                        fonts: context.Fonts,
                        includeAmbientShadow: false);

                    context.Legibility.DrawLegibleText(
                        canvas,
                        text,
                        textCenterX + splitPx,
                        baselineY,
                        SKTextAlign.Center,
                        font,
                        fontId,
                        ChromaticMagenta,
                        opacity: 0.36f * opacity,
                        activeGlowColor: null,
                        includeStrokeHalo: false,
                        fonts: context.Fonts,
                        includeAmbientShadow: false);
                }
            }

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

    private static (LyricWord? Prev2, LyricWord? Prev1, LyricWord? Active, LyricWord? Next1, LyricWord? Next2, (int LineIndex, int WordIndex) WordKey)
        ResolveExtendedWindow(Core.Runtime.ActiveLyricSlice slice)
    {
        var (prev1, active, next1, wordKey, _) = StyleLayoutHelper.ResolveWordTriad(slice);
        if (active is null || slice.CurrentLine is null || slice.CurrentLine.Words.IsEmpty)
        {
            return (null, prev1, active, next1, null, wordKey);
        }

        var words = slice.CurrentLine.Words;
        var idx = wordKey.WordIndex;

        LyricWord? prev2 = null;
        if (idx >= 2)
        {
            prev2 = words[idx - 2];
        }
        else if (idx == 1 && slice.PreviousLine is { Words.IsEmpty: false } pl)
        {
            prev2 = pl.Words[^1];
        }
        else if (idx == 0 && slice.PreviousLine is { Words.Length: >= 2 } pl2)
        {
            prev2 = pl2.Words[^2];
        }

        LyricWord? next2 = null;
        if (idx + 2 < words.Length)
        {
            next2 = words[idx + 2];
        }
        else if (idx + 1 == words.Length - 1 && slice.NextLine is { Words.IsEmpty: false } nl)
        {
            next2 = nl.Words[0];
        }
        else if (idx + 1 >= words.Length && slice.NextLine is { Words.Length: >= 2 } nl2)
        {
            next2 = nl2.Words[1];
        }

        return (prev2, prev1, active, next1, next2, wordKey);
    }

    private static float ResolveFontSize(FrameRenderPacket packet) =>
        StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 0.96f);

    private static float MeasureMaxUnitWidth(
        LyricWord? prev,
        LyricWord active,
        LyricWord? next,
        SKFont font,
        FontAssetRegistry fonts)
    {
        var iconAllowance = font.Size * 1.48f;
        var activeHasVisual = active.Visual is not null;
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
