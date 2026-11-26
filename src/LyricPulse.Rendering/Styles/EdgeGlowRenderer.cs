using LyricPulse.Core.Audio;
using LyricPulse.Core.Color;
using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Host;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Renders Lumn-inspired rounded perimeter ambient lighting and reactive Neon Rush rim glows
/// across both the Stage Window and system-wide transparent desktop monitor bounds.
/// Zero per-frame heap allocations; Skia filters are cached by quantized radius.
/// </summary>
public sealed class EdgeGlowRenderer : IDisposable
{
    private readonly SKPaint _edgePaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _rimPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _fadePaint = new() { IsAntialias = true, BlendMode = SKBlendMode.DstIn };
    private readonly SKPath _outerClipPath = new();
    private readonly SKPath _perimeterPath = new();
    private readonly SKPath _spillPath = new();

    private readonly SKColor[] _twoColorSweep = new SKColor[5];
    private readonly float[] _twoColorStops = [0.00f, 0.30f, 0.56f, 0.82f, 1.00f];
    private readonly SKColor[] _rushSweepColors = new SKColor[10];
    private readonly float[] _rushSweepStops = [0.00f, 0.14f, 0.28f, 0.42f, 0.54f, 0.66f, 0.78f, 0.88f, 0.95f, 1.00f];
    private readonly SKColor[] _bottomFadeColors = [SKColors.White, SKColors.Transparent];
    private readonly float[] _bottomFadeStops = [0f, 1f];

    private SKMaskFilter? _spillMaskFilter;
    private float _cachedSpillSigma = -1f;
    private SKMaskFilter? _tubeMaskFilter;
    private float _cachedTubeSigma = -1f;

    private double _lastRenderTimeSec = -1.0;
    private double _rushPhase;
    private float _smoothedBass;
    private float _smoothedBeat;
    private bool _disposed;

    public void Render(in StyleRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var packet = context.Packet;
        if (packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay && !packet.AllowDesktopEdgeGlow)
        {
            return;
        }

        var settings = packet.PlaybackSnapshot?.Settings ?? AppSettings.Default;
        var mode = settings.EdgeGlowMode;
        if (mode == EdgeGlowMode.Off)
        {
            return;
        }

        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        if (w < 8f || h < 8f)
        {
            return;
        }

        var intensity = Math.Clamp(settings.EdgeGlowIntensity, 0f, AppSettings.MaxGlowIntensity);
        if (intensity <= 0.01f)
        {
            return;
        }

        var thicknessMult = Math.Clamp(settings.EdgeGlowThickness, AppSettings.MinEdgeGlowThickness, AppSettings.MaxEdgeGlowThickness);
        var spillMult = Math.Clamp(settings.EdgeGlowSpill, AppSettings.MinEdgeGlowSpill, AppSettings.MaxEdgeGlowSpill);
        var animMode = packet.ReducedMotion ? EdgeGlowAnimationMode.Still : settings.EdgeGlowAnimation;

        var dt = UpdateSmoothedMotionState(packet, settings, animMode);
        AdvancePhase(dt, settings, animMode);

        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        var compactFactor = packet.IsCompactCard ? 0.55f : 1.0f;
        var desktopIdleScale = packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay &&
                               !isPlaying &&
                               animMode == EdgeGlowAnimationMode.MusicSync
            ? 0.42f
            : 1.0f;

        var musicDrive = animMode == EdgeGlowAnimationMode.MusicSync && isPlaying
            ? (1.0f + (_smoothedBass * 0.48f) + (_smoothedBeat * 0.62f))
            : animMode == EdgeGlowAnimationMode.AmbientDrift
                ? (1.0f + (0.10f * MathF.Sin((float)(_rushPhase * Math.PI * 2.0))))
                : 1.0f;

        var (primaryColor, secondaryColor) = ResolveGlowColors(packet, settings);
        var minSide = MathF.Min(w, h);
        var isDesktop = packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay;
        var cornerRadius = isDesktop
            ? Math.Clamp(minSide * 0.016f, 10f, 22f)
            : packet.IsCompactCard
                ? Math.Clamp(minSide * 0.045f, 10f, 18f)
                : Math.Clamp(minSide * 0.028f, 14f, 32f);

        var canvas = context.Canvas;
        var save = packet.BottomFadeToBlack ? canvas.SaveLayer() : canvas.Save();
        try
        {
            if (isDesktop)
            {
                _outerClipPath.Reset();
                _outerClipPath.AddRoundRect(new SKRect(0f, 0f, w, h), cornerRadius * 0.55f, cornerRadius * 0.55f);
                canvas.ClipPath(_outerClipPath, SKClipOperation.Intersect, antialias: true);
            }
            else
            {
                canvas.ClipRect(new SKRect(0f, 0f, w, h));
            }

            switch (mode)
            {
                case EdgeGlowMode.SoftAmbient:
                    DrawLumnPerimeter(
                        canvas, w, h, cornerRadius, primaryColor, secondaryColor,
                        settings.EdgeGlowTwoColorGradient,
                        spillStrength: intensity * spillMult * 0.65f * compactFactor * desktopIdleScale,
                        tubeStrength: intensity * 0.34f * compactFactor * desktopIdleScale,
                        coreStrength: intensity * 0.16f * compactFactor * desktopIdleScale,
                        thicknessMult, spillMult, musicDrive, includeRushSpotlight: false);
                    break;

                case EdgeGlowMode.BeatPulse:
                    var pulseStrength = animMode == EdgeGlowAnimationMode.Still
                        ? 0.55f
                        : 0.36f + (_smoothedBass * 0.55f) + (_smoothedBeat * 0.72f);
                    DrawLumnPerimeter(
                        canvas, w, h, cornerRadius, primaryColor, secondaryColor,
                        settings.EdgeGlowTwoColorGradient,
                        spillStrength: intensity * spillMult * 0.85f * pulseStrength * compactFactor * desktopIdleScale,
                        tubeStrength: intensity * 0.58f * pulseStrength * compactFactor * desktopIdleScale,
                        coreStrength: intensity * 0.42f * pulseStrength * compactFactor * desktopIdleScale,
                        thicknessMult, spillMult, musicDrive, includeRushSpotlight: false);
                    break;

                case EdgeGlowMode.NeonRim:
                    DrawLumnPerimeter(
                        canvas, w, h, cornerRadius, primaryColor, secondaryColor,
                        settings.EdgeGlowTwoColorGradient,
                        spillStrength: intensity * spillMult * 0.78f * compactFactor * desktopIdleScale,
                        tubeStrength: intensity * 0.72f * compactFactor * desktopIdleScale,
                        coreStrength: intensity * 0.64f * compactFactor * desktopIdleScale,
                        thicknessMult, spillMult, musicDrive, includeRushSpotlight: false);
                    break;

                case EdgeGlowMode.NeonRush:
                    DrawLumnPerimeter(
                        canvas, w, h, cornerRadius, primaryColor, secondaryColor,
                        settings.EdgeGlowTwoColorGradient,
                        spillStrength: intensity * spillMult * 0.96f * compactFactor * desktopIdleScale,
                        tubeStrength: intensity * 0.88f * compactFactor * desktopIdleScale,
                        coreStrength: intensity * 0.82f * compactFactor * desktopIdleScale,
                        thicknessMult, spillMult, musicDrive,
                        includeRushSpotlight: animMode != EdgeGlowAnimationMode.Still);
                    break;
            }

            if (packet.BottomFadeToBlack)
            {
                var fadeStartY = Math.Max(0f, h - Math.Clamp(h * 0.14f, 28f, 64f));
                using var fadeShader = SKShader.CreateLinearGradient(
                    new SKPoint(0f, fadeStartY),
                    new SKPoint(0f, h - 2f),
                    _bottomFadeColors,
                    _bottomFadeStops,
                    SKShaderTileMode.Clamp);
                _fadePaint.Shader = fadeShader;
                try
                {
                    canvas.DrawRect(0f, fadeStartY, w, h - fadeStartY, _fadePaint);
                }
                finally
                {
                    _fadePaint.Shader = null;
                }
            }
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _spillMaskFilter?.Dispose();
        _spillMaskFilter = null;
        _tubeMaskFilter?.Dispose();
        _tubeMaskFilter = null;
        _outerClipPath.Dispose();
        _perimeterPath.Dispose();
        _spillPath.Dispose();
        _edgePaint.Dispose();
        _strokePaint.Dispose();
        _rimPaint.Dispose();
        _fadePaint.Dispose();
    }

    private float UpdateSmoothedMotionState(
        in FrameRenderPacket packet,
        AppSettings settings,
        EdgeGlowAnimationMode animMode)
    {
        var nowSec = packet.RenderTimeSeconds;
        var dt = _lastRenderTimeSec >= 0.0
            ? Math.Clamp((float)(nowSec - _lastRenderTimeSec), 0f, 0.05f)
            : Math.Clamp(packet.DeltaSeconds, 0f, 0.05f);
        _lastRenderTimeSec = nowSec;

        if (animMode != EdgeGlowAnimationMode.MusicSync || settings.ReducedMotion)
        {
            _smoothedBass = 0f;
            _smoothedBeat = 0f;
            return dt;
        }

        var audio = packet.EffectiveAudioMetrics;
        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        var bassTarget = isPlaying ? Math.Clamp(audio.BassEnergy, 0f, 1.5f) : 0.06f;
        var beatTarget = isPlaying ? Math.Clamp(audio.BeatPulse, 0f, 1.5f) : 0f;
        var smoothing = 1f - MathF.Exp(-dt * 14f);

        _smoothedBass += (bassTarget - _smoothedBass) * smoothing;
        _smoothedBeat += (beatTarget - _smoothedBeat) * smoothing;
        return dt;
    }

    private void AdvancePhase(float dt, AppSettings settings, EdgeGlowAnimationMode animMode)
    {
        if (animMode == EdgeGlowAnimationMode.Still || settings.ReducedMotion)
        {
            return;
        }

        var speedSetting = Math.Clamp(settings.EdgeGlowSpeed, AppSettings.MinEdgeGlowSpeed, AppSettings.MaxEdgeGlowSpeed);
        var reactivity = animMode == EdgeGlowAnimationMode.MusicSync
            ? 0.55f + (_smoothedBass * 0.90f) + (_smoothedBeat * 0.55f)
            : 0.42f;
        _rushPhase = (_rushPhase + (dt * 0.18 * speedSetting * reactivity)) % 1.0;
    }

    private static (RgbColor Primary, RgbColor Secondary) ResolveGlowColors(in FrameRenderPacket packet, AppSettings settings)
    {
        if (settings.EdgeGlowOverrideAlbumColors)
        {
            var p = BoostGlowVividness(settings.EdgeGlowPrimaryColor);
            var s = settings.EdgeGlowTwoColorGradient
                ? BoostGlowVividness(settings.EdgeGlowSecondaryColor)
                : p;
            return (p, s);
        }

        var srcPalette = StageBackdropRenderer.ResolveBackdropPalette(
            packet.EffectiveStyle,
            packet.Palette,
            settings.AutoSyncColors);

        var primary = BoostGlowVividness(srcPalette.AccentColor1);
        var secondary = settings.EdgeGlowTwoColorGradient
            ? BoostGlowVividness(PickDistinctSecondary(srcPalette, primary))
            : primary;

        return (primary, secondary);
    }

    private static RgbColor PickDistinctSecondary(AlbumAccentPalette palette, RgbColor primary)
    {
        var pLab = OklabColorMath.SrgbToOklab(primary);
        var c2 = palette.AccentColor2;
        var c3 = palette.AccentColor3;

        var d2 = ColorDistanceSquared(pLab, OklabColorMath.SrgbToOklab(c2));
        var d3 = ColorDistanceSquared(pLab, OklabColorMath.SrgbToOklab(c3));

        return d2 >= d3 ? c2 : c3;
    }

    private static double ColorDistanceSquared(OklabColor a, OklabColor b)
    {
        var dl = a.L - b.L;
        var da = a.A - b.A;
        var db = a.B - b.B;
        return (dl * dl) + (da * da) + (db * db);
    }

    private static RgbColor BoostGlowVividness(RgbColor color)
    {
        var lch = OklabColorMath.OklabToOklch(OklabColorMath.SrgbToOklab(color));
        if (lch.C < 0.025)
        {
            return new RgbColor(
                (byte)Math.Clamp((int)MathF.Round(color.R * 0.85f + 38f), 0, 255),
                (byte)Math.Clamp((int)MathF.Round(color.G * 0.85f + 38f), 0, 255),
                (byte)Math.Clamp((int)MathF.Round(color.B * 0.85f + 42f), 0, 255));
        }

        var targetL = Math.Clamp(lch.L < 0.58 ? 0.72 : lch.L + 0.06, 0.62, 0.86);
        var targetC = Math.Clamp(lch.C * 1.18, 0.14, 0.28);
        return OklabColorMath.OklchToSrgbGamutMapped(new OklchColor(targetL, targetC, lch.H));
    }

    private void DrawLumnPerimeter(
        SKCanvas canvas,
        float w,
        float h,
        float cornerRadius,
        RgbColor primaryColor,
        RgbColor secondaryColor,
        bool twoColorGradient,
        float spillStrength,
        float tubeStrength,
        float coreStrength,
        float thicknessMult,
        float spillMult,
        float musicDrive,
        bool includeRushSpotlight)
    {
        var cx = w * 0.5f;
        var cy = h * 0.5f;
        var minSide = MathF.Min(w, h);
        var angleDeg = (float)(_rushPhase * 360.0);
        var rotMatrix = SKMatrix.CreateRotationDegrees(angleDeg, cx, cy);

        // Build 60% Primary / 40% Secondary Lumn perimeter gradient (or lush single-color gradient)
        var midBlend = StyleLayoutHelper.BlendSrgb(primaryColor, secondaryColor, twoColorGradient ? 0.45f : 0.15f);
        var whiteHotPrimary = StyleLayoutHelper.BlendSrgb(primaryColor, new RgbColor(255, 255, 255), 0.46f);
        var whiteHotSecondary = StyleLayoutHelper.BlendSrgb(secondaryColor, new RgbColor(255, 255, 255), 0.42f);

        // Pass 1: Wide feathered Lumn inward wash hugging all 4 rounded screen edges (Glow Spill & Thickness envelope)
        var alphaEnvelope = Math.Clamp(0.46f * spillStrength * Math.Clamp(musicDrive, 0.85f, 1.38f), 0f, 0.76f);
        if (alphaEnvelope > 0.01f)
        {
            var thicknessReach = 0.64f + (0.36f * thicknessMult);
            var spillStrokeWidth = Math.Clamp(minSide * 0.068f * spillMult * thicknessReach * (0.90f + 0.25f * musicDrive), 14f, minSide * 0.24f);
            var blurSigma = Math.Clamp(spillStrokeWidth * 0.56f, 6f, 68f);
            var spillInset = Math.Clamp(spillStrokeWidth * 0.10f, 1f, 8f);

            _spillPath.Reset();
            _spillPath.AddRoundRect(new SKRect(spillInset, spillInset, w - spillInset, h - spillInset), cornerRadius, cornerRadius);

            PopulatePerimeterColors(_twoColorSweep, primaryColor, secondaryColor, midBlend, alphaEnvelope);
            using var spillShader = SKShader.CreateSweepGradient(
                new SKPoint(cx, cy),
                _twoColorSweep,
                _twoColorStops,
                rotMatrix);

            _strokePaint.Shader = spillShader;
            _strokePaint.Color = SKColors.White;
            _strokePaint.StrokeWidth = spillStrokeWidth;
            _strokePaint.MaskFilter = GetOrCreateSpillBlur(blurSigma);
            try
            {
                canvas.DrawPath(_spillPath, _strokePaint);
            }
            finally
            {
                _strokePaint.MaskFilter = null;
                _strokePaint.Shader = null;
            }
        }

        // Pass 2: Crisp glowing neon bezel tube around the rounded screen border (Thickness)
        var tubeAlpha = Math.Clamp(0.76f * tubeStrength * Math.Clamp(musicDrive, 0.90f, 1.35f), 0f, 0.95f);
        var tubeStrokeWidth = Math.Clamp(MathF.Max(4.0f, minSide * 0.010f) * thicknessMult * (0.92f + 0.18f * musicDrive), 3.0f, 22f);
        var tubeInset = Math.Clamp(tubeStrokeWidth * 0.36f, 1.2f, 8.0f);
        var tubeRadius = MathF.Max(4f, cornerRadius - tubeInset * 0.3f);

        _perimeterPath.Reset();
        _perimeterPath.AddRoundRect(new SKRect(tubeInset, tubeInset, w - tubeInset, h - tubeInset), tubeRadius, tubeRadius);

        if (tubeAlpha > 0.01f)
        {
            PopulatePerimeterColors(_twoColorSweep, primaryColor, secondaryColor, midBlend, tubeAlpha);
            using var tubeShader = SKShader.CreateSweepGradient(
                new SKPoint(cx, cy),
                _twoColorSweep,
                _twoColorStops,
                rotMatrix);

            _strokePaint.Shader = tubeShader;
            _strokePaint.Color = SKColors.White;
            _strokePaint.StrokeWidth = tubeStrokeWidth;
            _strokePaint.MaskFilter = GetOrCreateTubeBlur(Math.Clamp(tubeStrokeWidth * 0.85f, 3.0f, 16.0f));
            try
            {
                canvas.DrawPath(_perimeterPath, _strokePaint);
            }
            finally
            {
                _strokePaint.MaskFilter = null;
                _strokePaint.Shader = null;
            }
        }

        // Pass 3: Luminous sharp bezel hairline & traveling Neon Rush hotspot
        var coreAlpha = Math.Clamp(0.84f * coreStrength, 0f, 0.98f);
        if (coreAlpha <= 0.01f)
        {
            return;
        }

        if (includeRushSpotlight)
        {
            _rushSweepColors[0] = whiteHotPrimary.ToSkColorWithAlpha(coreAlpha * 0.24f);
            _rushSweepColors[1] = primaryColor.ToSkColorWithAlpha(coreAlpha * 0.30f);
            _rushSweepColors[2] = secondaryColor.ToSkColorWithAlpha(coreAlpha * 0.34f);
            _rushSweepColors[3] = secondaryColor.ToSkColorWithAlpha(coreAlpha * 0.48f);
            _rushSweepColors[4] = whiteHotSecondary.ToSkColorWithAlpha(coreAlpha * 0.72f);
            _rushSweepColors[5] = whiteHotPrimary.ToSkColorWithAlpha(Math.Min(1f, coreAlpha * 0.96f));
            _rushSweepColors[6] = SKColors.White.WithAlpha((byte)Math.Clamp(coreAlpha * 255f, 0f, 255f));
            _rushSweepColors[7] = whiteHotPrimary.ToSkColorWithAlpha(Math.Min(1f, coreAlpha * 0.92f));
            _rushSweepColors[8] = primaryColor.ToSkColorWithAlpha(coreAlpha * 0.45f);
            _rushSweepColors[9] = whiteHotPrimary.ToSkColorWithAlpha(coreAlpha * 0.24f);

            using var rushShader = SKShader.CreateSweepGradient(
                new SKPoint(cx, cy),
                _rushSweepColors,
                _rushSweepStops,
                rotMatrix);
            _rimPaint.Shader = rushShader;
        }
        else
        {
            PopulatePerimeterColors(_twoColorSweep, whiteHotPrimary, whiteHotSecondary, midBlend, coreAlpha * 0.78f);
            using var staticShader = SKShader.CreateSweepGradient(
                new SKPoint(cx, cy),
                _twoColorSweep,
                _twoColorStops,
                rotMatrix);
            _rimPaint.Shader = staticShader;
        }

        _rimPaint.Color = SKColors.White;
        _rimPaint.StrokeWidth = Math.Clamp(tubeStrokeWidth * 0.32f, 1.4f, 4.5f);
        try
        {
            canvas.DrawPath(_perimeterPath, _rimPaint);
        }
        finally
        {
            _rimPaint.Shader = null;
        }
    }

    private static void PopulatePerimeterColors(
        SKColor[] target,
        RgbColor primary,
        RgbColor secondary,
        RgbColor blend,
        float alpha)
    {
        // Primary covers ~60% of the rounded sweep, Secondary covers ~40% with smooth wrap at 0.0 / 1.0
        target[0] = primary.ToSkColorWithAlpha(alpha);
        target[1] = primary.ToSkColorWithAlpha(alpha);
        target[2] = blend.ToSkColorWithAlpha(alpha);
        target[3] = secondary.ToSkColorWithAlpha(alpha);
        target[4] = primary.ToSkColorWithAlpha(alpha);
    }

    private SKMaskFilter GetOrCreateSpillBlur(float sigma)
    {
        var quantized = MathF.Round(sigma * 2f) * 0.5f;
        if (_spillMaskFilter is not null && MathF.Abs(_cachedSpillSigma - quantized) < 0.01f)
        {
            return _spillMaskFilter;
        }

        _spillMaskFilter?.Dispose();
        _cachedSpillSigma = quantized;
        _spillMaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.8f, quantized));
        return _spillMaskFilter;
    }

    private SKMaskFilter GetOrCreateTubeBlur(float sigma)
    {
        var quantized = MathF.Round(sigma * 2f) * 0.5f;
        if (_tubeMaskFilter is not null && MathF.Abs(_cachedTubeSigma - quantized) < 0.01f)
        {
            return _tubeMaskFilter;
        }

        _tubeMaskFilter?.Dispose();
        _cachedTubeSigma = quantized;
        _tubeMaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.8f, quantized));
        return _tubeMaskFilter;
    }
}
