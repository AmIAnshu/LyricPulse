using LyricPulse.Core.Models;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using LyricPulse.Rendering.Shaders;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #1 — Fisheye: tight centered vertical 3-word triad (Previous, Active, Next)
/// advancing through a breathing convex spherical lens (<c>EllipticalBulge.sksl</c>)
/// with cinema vertical <c>DirectionalMotionBlur13.sksl</c> (shutter 0.48) and 4-layer legibility.
/// </summary>
public sealed class FisheyeStyleRenderer : IKineticStyleRenderer
{
    public const float VerticalShutterFactor = 0.48f;
    public const float SlotPitchRatio = 1.18f;
    public const float RestingActiveScale = 1.08f;
    public const float ContextWordScale = 0.78f;

    private SpringState _scrollOffsetY = SpringState.AtRest(0f);
    private SpringState _activeScale = SpringState.AtRest(RestingActiveScale);
    private SpringState _lensStrength = SpringState.AtRest(MotionBlurMath.BaselineBulgeStrength);
    private float _highlightFade = 1.0f;

    private int _lastGlobalWordIndex = -1;
    private int _lastLineIndex = -1;
    private float _lastWordDurationSec = 0.32f;

    internal bool AllowCpuShadersForTesting { get; set; }

    public OverlayStyleId StyleId => OverlayStyleId.Fisheye;

    public void ResetState()
    {
        _scrollOffsetY = SpringState.AtRest(0f);
        _activeScale = SpringState.AtRest(RestingActiveScale);
        _lensStrength = SpringState.AtRest(MotionBlurMath.BaselineBulgeStrength);
        _highlightFade = 1.0f;
        _lastGlobalWordIndex = -1;
        _lastLineIndex = -1;
        _lastWordDurationSec = 0.32f;
    }

    public bool Advance(in FrameRenderPacket packet, in LifecycleFrameState lifecycle)
    {
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        var (_, active, _, globalWordIdx, wordDurationSec) = StyleLayoutHelper.ResolveWordTriad(slice);
        if (active is null)
        {
            return false;
        }

        var dt = Math.Clamp(packet.DeltaSeconds, 0f, SpringState.MaxDeltaSeconds);
        var fontSize = ResolveFontSize(packet);
        var slotPitch = fontSize * SlotPitchRatio;
        _lastWordDurationSec = wordDurationSec;

        if (globalWordIdx != _lastGlobalWordIndex)
        {
            var isLineTransition = _lastLineIndex >= 0 && slice.ActiveLineIndex != _lastLineIndex;
            var wasInitialized = _lastGlobalWordIndex >= 0;
            _lastGlobalWordIndex = globalWordIdx;
            _lastLineIndex = slice.ActiveLineIndex;

            if (packet.ReducedMotion)
            {
                _scrollOffsetY = SpringState.AtRest(0f);
                _activeScale = SpringState.AtRest(1.00f);
                _lensStrength = SpringState.AtRest(0f);
                _highlightFade = 0f;
            }
            else
            {
                if (wasInitialized)
                {
                    _scrollOffsetY = new SpringState(Value: slotPitch, Velocity: _scrollOffsetY.Velocity * 0.25f, Target: 0f);
                }

                _activeScale = new SpringState(Value: ContextWordScale, Velocity: 0f, Target: RestingActiveScale);
                var pacing = Math.Clamp(wordDurationSec / 0.32f, 0.42f, 1.35f);
                var impulse = isLineTransition ? 2.5f : 1.8f * Math.Min(1.0f, pacing);
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
            _lensStrength = SpringState.AtRest(0f);
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

        return !_scrollOffsetY.IsAtRest ||
               !_activeScale.IsAtRest ||
               !_lensStrength.IsAtRest ||
               _highlightFade < 0.999f;
    }

    public void Render(in StyleRenderContext context, in LifecycleFrameState lifecycle)
    {
        var packet = context.Packet;
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        var (prevWord, activeWord, nextWord, _, _) = StyleLayoutHelper.ResolveWordTriad(slice);
        if (activeWord is null)
        {
            return;
        }

        var fontId = packet.ActiveFontId;
        var fontSize = ResolveFontSize(packet);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize);
        var slotPitch = fontSize * SlotPitchRatio;
        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet);

        var maxWordWidth = MeasureMaxTriadWidth(prevWord, activeWord, nextWord, font);
        var fitScale = TextShaperService.ComputeAutoFitScale(maxWordWidth * RestingActiveScale, packet.ViewportWidthDip, 0.68f);

        var layerWidthDip = Math.Clamp(Math.Max(maxWordWidth * fitScale * 1.75f, fontSize * 7.5f), 220f, packet.ViewportWidthDip);
        var layerHeightDip = Math.Clamp(fontSize * 5.0f, 160f, packet.ViewportHeightDip);

        var useShaders = (context.ShadersEnabled || AllowCpuShadersForTesting) &&
                         !packet.ReducedMotion &&
                         context.Shaders.AreAllShadersReady;

        if (!useShaders)
        {
            DrawTriadContent(
                context.Canvas,
                context,
                font,
                fontId,
                anchorX,
                anchorY,
                slotPitch,
                fitScale,
                prevWord,
                activeWord,
                nextWord,
                packet.ReducedMotion);
            return;
        }

        var dpi = packet.RenderScaling;
        var physW = Math.Clamp((int)MathF.Ceiling(layerWidthDip * dpi), 64, OffscreenSurfacePool.MaxSurfaceDimensionPx);
        var physH = Math.Clamp((int)MathF.Ceiling(layerHeightDip * dpi), 64, OffscreenSurfacePool.MaxSurfaceDimensionPx);

        var bulgeSurface = context.SurfacePool.RentSurface(OffscreenSurfaceSlot.Bulge, context.GrContext, physW, physH);
        var offCanvas = bulgeSurface.Canvas;
        offCanvas.Clear(SKColors.Transparent);

        var offSave = offCanvas.Save();
        try
        {
            offCanvas.Scale(dpi, dpi);
            DrawTriadContent(
                offCanvas,
                context,
                font,
                fontId,
                layerWidthDip * 0.5f,
                layerHeightDip * 0.5f,
                slotPitch,
                fitScale,
                prevWord,
                activeWord,
                nextWord,
                reducedMotion: false);
        }
        finally
        {
            offCanvas.RestoreToCount(offSave);
        }

        // Optional vertical directional motion blur pass into MotionBlur slot before convex lens warp
        var velocityPxPerSec = new SKPoint(0f, _scrollOffsetY.Velocity * dpi);
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
                    new SKRect(0f, 0f, layerWidthDip, layerHeightDip),
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

        var destRect = new SKRect(
            anchorX - (layerWidthDip * 0.5f),
            anchorY - (layerHeightDip * 0.5f),
            anchorX + (layerWidthDip * 0.5f),
            anchorY + (layerHeightDip * 0.5f));

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

    public void Dispose()
    {
        // All SKPaint/SKSurface resources are shared via LegibilityPipeline and OffscreenSurfacePool.
    }

    private void DrawTriadContent(
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
        bool reducedMotion)
    {
        var packet = context.Packet;
        var highlightEased = Ease.Highlight(_highlightFade);
        var scrollY = reducedMotion ? 0f : _scrollOffsetY.Value;

        var clusterHalfW = Math.Max(font.Size * 3.0f, TextShaperService.MeasureWidth(activeWord.Text, font) * fitScale * 0.65f);
        var clusterBounds = new SKRect(
            centerX - clusterHalfW,
            centerY - (slotPitch * 1.45f),
            centerX + clusterHalfW,
            centerY + (slotPitch * 1.45f));

        context.Legibility.DrawAmbientScrim(
            canvas,
            clusterBounds,
            font.Size * fitScale,
            packet.Palette.ScrimTint,
            packet.ScrimIntensity,
            packet.HighContrastScrim);

        if (prevWord is not null)
        {
            DrawWordSlot(
                canvas,
                context,
                font,
                fontId,
                prevWord.Text,
                centerX,
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
                centerX,
                centerY + slotPitch + scrollY,
                ContextWordScale * fitScale,
                packet.Palette.AccentColor3,
                opacity: 0.48f,
                glowColor: null);
        }

        var activeScale = (reducedMotion ? 1.00f : _activeScale.Value) * fitScale;
        var activeOpacity = reducedMotion ? (0.45f + (0.55f * highlightEased)) : 1.0f;
        DrawWordSlot(
            canvas,
            context,
            font,
            fontId,
            activeWord.Text,
            centerX,
            centerY + scrollY,
            activeScale,
            packet.Palette.AccentColor1,
            opacity: activeOpacity,
            glowColor: packet.Palette.AccentColor2);
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
        RgbColor? glowColor)
    {
        if (string.IsNullOrWhiteSpace(text) || opacity <= 0.004f)
        {
            return;
        }

        var save = canvas.Save();
        try
        {
            canvas.Translate(x, y);
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
                activeGlowColor: glowColor);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    private static float ResolveFontSize(FrameRenderPacket packet)
    {
        var metrics = FontAssetRegistry.GetOpticalMetrics(packet.ActiveFontId);
        return Math.Max(packet.BaseFontSizeDip, metrics.MinOverlaySizeDip);
    }

    private static float MeasureMaxTriadWidth(LyricWord? prev, LyricWord active, LyricWord? next, SKFont font)
    {
        var maxW = TextShaperService.MeasureWidth(active.Text, font);
        if (prev is not null)
        {
            maxW = Math.Max(maxW, TextShaperService.MeasureWidth(prev.Text, font));
        }

        if (next is not null)
        {
            maxW = Math.Max(maxW, TextShaperService.MeasureWidth(next.Text, font));
        }

        return maxW;
    }
}
