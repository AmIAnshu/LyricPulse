using LyricPulse.Core.Audio;
using LyricPulse.Core.Color;
using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Host;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Renders strip-free, deeply beat-reactive volumetric ambient edge lighting and counter-revolving
/// Neon Rush orbital plasma across both the Stage Window and system-wide transparent desktop monitors.
/// Every perimeter light source is projected from behind the bezel (off-edge outset + large Gaussian
/// diffusion + travelling radial plasma pools) so zero hard border strip ever appears while kicks,
/// bass swells, and drops automatically surge glow intensity, inward reach, and orbital flow.
/// Zero per-frame heap allocations; Skia filters are cached by quantized radius.
/// </summary>
public sealed class EdgeGlowRenderer : IDisposable
{
    private readonly SKPaint _strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _orbPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _fadePaint = new() { IsAntialias = true, BlendMode = SKBlendMode.DstIn };
    private readonly SKPath _outerClipPath = new();
    private readonly SKPath _spillPath = new();
    private readonly SKPath _counterPath = new();
    private readonly SKPath _velvetPath = new();

    private readonly SKColor[] _twoColorSweep = new SKColor[5];
    private readonly float[] _twoColorStops = [0.00f, 0.28f, 0.54f, 0.80f, 1.00f];
    private readonly SKColor[] _revolvingSpillColors = new SKColor[11];
    private readonly float[] _revolvingSpillStops = [0.00f, 0.08f, 0.20f, 0.36f, 0.46f, 0.54f, 0.64f, 0.78f, 0.88f, 0.95f, 1.00f];
    private readonly SKColor[] _counterSweepColors = new SKColor[11];
    private readonly float[] _counterSweepStops = [0.00f, 0.10f, 0.24f, 0.38f, 0.48f, 0.56f, 0.68f, 0.80f, 0.90f, 0.96f, 1.00f];
    private readonly SKColor[] _velvetSweepColors = new SKColor[11];
    private readonly float[] _velvetSweepStops = [0.00f, 0.09f, 0.22f, 0.38f, 0.48f, 0.54f, 0.64f, 0.78f, 0.88f, 0.95f, 1.00f];
    private readonly SKColor[] _orbColors = new SKColor[4];
    private readonly float[] _orbStops = [0.00f, 0.32f, 0.68f, 1.00f];
    private readonly SKColor[] _bottomFadeColors = [SKColors.White, SKColors.Transparent];
    private readonly float[] _bottomFadeStops = [0f, 1f];

    private SKMaskFilter? _spillMaskFilter;
    private float _cachedSpillSigma = -1f;
    private SKMaskFilter? _counterMaskFilter;
    private float _cachedCounterSigma = -1f;
    private SKMaskFilter? _velvetMaskFilter;
    private float _cachedVelvetSigma = -1f;

    private double _lastRenderTimeSec = -1.0;
    private double _rushPhase;
    private double _counterPhase = 0.37;

    private bool _hasAudioSample;
    private float _prevRawBass;
    private float _prevRawMid;
    private float _smoothedBass;
    private float _smoothedMid;
    private float _smoothedTreble;
    private float _beatEnvelope;
    private float _rollingEnergy = 0.25f;
    private float _dropSurge;
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

        var dt = UpdateReactiveAudioState(packet, settings, animMode);
        AdvanceOrbitalPhases(dt, settings, animMode);

        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        var compactFactor = packet.IsCompactCard ? 0.56f : 1.0f;
        var desktopIdleScale = packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay &&
                               !isPlaying &&
                               animMode == EdgeGlowAnimationMode.MusicSync
            ? 0.78f
            : 1.0f;

        // Dynamic auto-reactivity: quiet passages stay calm & hypnotic, while kicks/bass/drops automatically
        // surge glow luminosity, inward reach, and vividness.
        float autoIntensity;
        float autoReach;
        float beatImpact;
        if (animMode == EdgeGlowAnimationMode.MusicSync && isPlaying)
        {
            beatImpact = Math.Clamp((_beatEnvelope * 0.82f) + (_dropSurge * 0.65f), 0f, 1.55f);
            autoIntensity = Math.Clamp(
                0.56f + (_smoothedBass * 0.48f) + (_smoothedMid * 0.16f) + (_beatEnvelope * 0.66f) + (_dropSurge * 0.44f),
                0.44f,
                1.95f);
            autoReach = Math.Clamp(
                0.80f + (_smoothedBass * 0.36f) + (_beatEnvelope * 0.42f) + (_dropSurge * 0.32f),
                0.76f,
                1.62f);
        }
        else if (animMode == EdgeGlowAnimationMode.AmbientDrift)
        {
            var breath = 0.5f + (0.5f * MathF.Sin((float)(_rushPhase * Math.PI * 2.0)));
            beatImpact = breath * 0.24f;
            autoIntensity = 0.94f + (breath * 0.18f);
            autoReach = 0.96f + (breath * 0.12f);
        }
        else
        {
            beatImpact = 0f;
            autoIntensity = 0.96f;
            autoReach = 1.0f;
        }

        var (primaryColor, secondaryColor, tertiaryColor) = ResolveGlowColors(packet, settings, beatImpact);
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
                _outerClipPath.AddRoundRect(new SKRect(0f, 0f, w, h), cornerRadius * 0.40f, cornerRadius * 0.40f);
                canvas.ClipPath(_outerClipPath, SKClipOperation.Intersect, antialias: true);
            }
            else
            {
                canvas.ClipRect(new SKRect(0f, 0f, w, h));
            }

            var scale = compactFactor * desktopIdleScale;
            var isAnimated = animMode != EdgeGlowAnimationMode.Still;

            switch (mode)
            {
                case EdgeGlowMode.SoftAmbient:
                    DrawVolumetricPlasmaEdge(
                        canvas, w, h, cornerRadius, primaryColor, secondaryColor, tertiaryColor,
                        settings.EdgeGlowTwoColorGradient,
                        spillStrength: intensity * spillMult * 0.72f * scale,
                        counterStrength: intensity * 0.24f * scale,
                        velvetStrength: intensity * 0.46f * scale,
                        orbStrength: intensity * (0.20f + (beatImpact * 0.30f)) * scale,
                        thicknessMult, spillMult, autoIntensity, autoReach, beatImpact,
                        isRevolvingRush: false,
                        drawCornerBlooms: isAnimated && beatImpact > 0.18f);
                    break;

                case EdgeGlowMode.BeatPulse:
                    var pulseWave = animMode == EdgeGlowAnimationMode.Still
                        ? 0.62f
                        : Math.Clamp(0.28f + (_smoothedBass * 0.68f) + (_beatEnvelope * 0.96f) + (_dropSurge * 0.58f), 0.24f, 1.95f);
                    DrawVolumetricPlasmaEdge(
                        canvas, w, h, cornerRadius, primaryColor, secondaryColor, tertiaryColor,
                        settings.EdgeGlowTwoColorGradient,
                        spillStrength: intensity * spillMult * 0.96f * pulseWave * scale,
                        counterStrength: intensity * 0.48f * pulseWave * scale,
                        velvetStrength: intensity * 0.76f * pulseWave * scale,
                        orbStrength: intensity * 0.58f * pulseWave * scale,
                        thicknessMult, spillMult,
                        autoIntensity: Math.Clamp(0.75f + (pulseWave * 0.55f), 0.55f, 1.95f),
                        autoReach: Math.Clamp(0.78f + (pulseWave * 0.48f), 0.75f, 1.65f),
                        beatImpact,
                        isRevolvingRush: false,
                        drawCornerBlooms: true);
                    break;

                case EdgeGlowMode.NeonRim:
                    DrawVolumetricPlasmaEdge(
                        canvas, w, h, cornerRadius, primaryColor, secondaryColor, tertiaryColor,
                        settings.EdgeGlowTwoColorGradient,
                        spillStrength: intensity * spillMult * 0.86f * scale,
                        counterStrength: intensity * 0.54f * scale,
                        velvetStrength: intensity * 0.88f * scale,
                        orbStrength: intensity * (0.34f + (beatImpact * 0.42f)) * scale,
                        thicknessMult, spillMult, autoIntensity, autoReach, beatImpact,
                        isRevolvingRush: false,
                        drawCornerBlooms: true);
                    break;

                case EdgeGlowMode.NeonRush:
                    DrawVolumetricPlasmaEdge(
                        canvas, w, h, cornerRadius, primaryColor, secondaryColor, tertiaryColor,
                        settings.EdgeGlowTwoColorGradient,
                        spillStrength: intensity * spillMult * 1.04f * scale,
                        counterStrength: intensity * 0.72f * scale,
                        velvetStrength: intensity * 0.98f * scale,
                        orbStrength: intensity * (0.68f + (beatImpact * 0.52f)) * scale,
                        thicknessMult, spillMult, autoIntensity, autoReach, beatImpact,
                        isRevolvingRush: isAnimated,
                        drawCornerBlooms: isAnimated && (beatImpact > 0.22f || _dropSurge > 0.16f));
                    break;
            }

            if (packet.BottomFadeToBlack)
            {
                var fadeStartY = Math.Max(0f, h - Math.Clamp(h * 0.15f, 30f, 68f));
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
        _counterMaskFilter?.Dispose();
        _counterMaskFilter = null;
        _velvetMaskFilter?.Dispose();
        _velvetMaskFilter = null;
        _outerClipPath.Dispose();
        _spillPath.Dispose();
        _counterPath.Dispose();
        _velvetPath.Dispose();
        _strokePaint.Dispose();
        _orbPaint.Dispose();
        _fadePaint.Dispose();
    }

    private float UpdateReactiveAudioState(
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
            _smoothedMid = 0f;
            _smoothedTreble = 0f;
            _beatEnvelope = 0f;
            _dropSurge = 0f;
            _hasAudioSample = false;
            return dt;
        }

        var audio = packet.EffectiveAudioMetrics;
        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        var bassRaw = isPlaying ? Math.Clamp(audio.BassEnergy, 0f, 1.5f) : 0.14f;
        var midRaw = isPlaying ? Math.Clamp(audio.MidEnergy, 0f, 1.5f) : 0.10f;
        var trebleRaw = isPlaying ? Math.Clamp(audio.TrebleEnergy, 0f, 1.5f) : 0.08f;
        var beatRaw = isPlaying ? Math.Clamp(audio.BeatPulse, 0f, 1.5f) : 0.04f;
        var dropRaw = isPlaying ? Math.Clamp(audio.DropTrigger, 0f, 1.5f) : 0f;
        var climaxRaw = isPlaying ? Math.Clamp(audio.ClimaxIntensity + (audio.IsClimaxActive ? 0.35f : 0f), 0f, 1.4f) : 0f;
        var instantEnergy = (bassRaw * 0.50f) + (midRaw * 0.30f) + (trebleRaw * 0.20f);

        if (!_hasAudioSample)
        {
            _hasAudioSample = true;
            _prevRawBass = bassRaw;
            _prevRawMid = midRaw;
            _smoothedBass = bassRaw;
            _smoothedMid = midRaw;
            _smoothedTreble = trebleRaw;
            _beatEnvelope = Math.Clamp(beatRaw, 0f, 1.4f);
            _rollingEnergy = Math.Clamp(instantEnergy * 0.85f, 0.08f, 0.90f);
            _dropSurge = Math.Clamp(MathF.Max(dropRaw, climaxRaw * 0.72f), 0f, 1.35f);
            return dt;
        }

        // Fast spectral-flux onset kick detector catches both deep kicks and punchy snares
        var bassFlux = MathF.Max(0f, bassRaw - _prevRawBass);
        var midFlux = MathF.Max(0f, midRaw - _prevRawMid);
        _prevRawBass = bassRaw;
        _prevRawMid = midRaw;

        var kickTarget = Math.Clamp(MathF.Max(beatRaw, (bassFlux * 3.1f) + (midFlux * 1.45f)), 0f, 1.45f);

        // Asymmetric envelopes: snappy instant attack on beat hits, smooth musical decay
        var bassCoeff = bassRaw > _smoothedBass ? (1f - MathF.Exp(-dt * 24f)) : (1f - MathF.Exp(-dt * 5.2f));
        var midCoeff = midRaw > _smoothedMid ? (1f - MathF.Exp(-dt * 20f)) : (1f - MathF.Exp(-dt * 6.5f));
        var trebleCoeff = trebleRaw > _smoothedTreble ? (1f - MathF.Exp(-dt * 26f)) : (1f - MathF.Exp(-dt * 8.0f));
        var kickCoeff = kickTarget > _beatEnvelope ? (1f - MathF.Exp(-dt * 44f)) : (1f - MathF.Exp(-dt * 5.6f));

        _smoothedBass += (bassRaw - _smoothedBass) * bassCoeff;
        _smoothedMid += (midRaw - _smoothedMid) * midCoeff;
        _smoothedTreble += (trebleRaw - _smoothedTreble) * trebleCoeff;
        _beatEnvelope += (kickTarget - _beatEnvelope) * kickCoeff;

        // Adaptive rolling energy floor (~2.2s window) automatically senses verse -> chorus/drop surges
        _rollingEnergy += (instantEnergy - _rollingEnergy) * (1f - MathF.Exp(-dt * 0.45f));
        var energyContrast = Math.Clamp((instantEnergy - _rollingEnergy) * 2.6f, 0f, 1.0f);
        var surgeTarget = Math.Clamp(MathF.Max(dropRaw, (climaxRaw * 0.78f) + (energyContrast * 0.72f)), 0f, 1.35f);
        var surgeCoeff = surgeTarget > _dropSurge ? (1f - MathF.Exp(-dt * 26f)) : (1f - MathF.Exp(-dt * 2.8f));
        _dropSurge += (surgeTarget - _dropSurge) * surgeCoeff;

        return dt;
    }

    private void AdvanceOrbitalPhases(float dt, AppSettings settings, EdgeGlowAnimationMode animMode)
    {
        if (animMode == EdgeGlowAnimationMode.Still || settings.ReducedMotion)
        {
            return;
        }

        var speedSetting = Math.Clamp(settings.EdgeGlowSpeed, AppSettings.MinEdgeGlowSpeed, AppSettings.MaxEdgeGlowSpeed);
        var primaryReactivity = animMode == EdgeGlowAnimationMode.MusicSync
            ? 0.58f + (_smoothedBass * 1.10f) + (_beatEnvelope * 1.38f) + (_dropSurge * 0.78f)
            : 0.62f;
        var counterReactivity = animMode == EdgeGlowAnimationMode.MusicSync
            ? 0.42f + (_smoothedMid * 0.85f) + (_smoothedTreble * 0.65f) + (_beatEnvelope * 0.55f)
            : 0.42f;

        _rushPhase = (_rushPhase + (dt * 0.36 * speedSetting * primaryReactivity)) % 1.0;
        _counterPhase = (_counterPhase - (dt * 0.24 * speedSetting * counterReactivity)) % 1.0;
        if (_counterPhase < 0.0)
        {
            _counterPhase += 1.0;
        }
    }

    private static (RgbColor Primary, RgbColor Secondary, RgbColor Tertiary) ResolveGlowColors(
        in FrameRenderPacket packet,
        AppSettings settings,
        float beatImpact)
    {
        if (settings.EdgeGlowOverrideAlbumColors)
        {
            var p = BoostGlowVividness(settings.EdgeGlowPrimaryColor, beatImpact);
            var s = settings.EdgeGlowTwoColorGradient
                ? BoostGlowVividness(settings.EdgeGlowSecondaryColor, beatImpact)
                : p;
            var t = StyleLayoutHelper.BlendSrgb(p, s, 0.5f);
            return (p, s, t);
        }

        var srcPalette = StageBackdropRenderer.ResolveBackdropPalette(
            packet.EffectiveStyle,
            packet.Palette,
            settings.AutoSyncColors);

        var primary = BoostGlowVividness(srcPalette.AccentColor1, beatImpact);
        var secondary = settings.EdgeGlowTwoColorGradient
            ? BoostGlowVividness(PickDistinctSecondary(srcPalette, primary), beatImpact)
            : primary;
        var tertiarySource = settings.EdgeGlowTwoColorGradient
            ? StyleLayoutHelper.BlendSrgb(srcPalette.AccentColor2, srcPalette.AccentColor3, 0.5f)
            : srcPalette.AccentColor1;
        var tertiary = BoostGlowVividness(tertiarySource, beatImpact * 0.8f);

        return (primary, secondary, tertiary);
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

    private static RgbColor BoostGlowVividness(RgbColor color, float beatImpact)
    {
        var lch = OklabColorMath.OklabToOklch(OklabColorMath.SrgbToOklab(color));
        var punch = Math.Clamp(beatImpact, 0f, 1.4f);
        if (lch.C < 0.025)
        {
            var lift = 36f + (punch * 20f);
            return new RgbColor(
                (byte)Math.Clamp((int)MathF.Round(color.R * 0.85f + lift), 0, 255),
                (byte)Math.Clamp((int)MathF.Round(color.G * 0.85f + lift), 0, 255),
                (byte)Math.Clamp((int)MathF.Round(color.B * 0.85f + lift + 4f), 0, 255));
        }

        // Preserve exact palette hue angle while surging chroma cleanly on beats (avoid milky white washout)
        var targetL = Math.Clamp((lch.L < 0.56 ? 0.64 : Math.Min(lch.L, 0.72)) + (punch * 0.045), 0.58, 0.78);
        var targetC = Math.Clamp(lch.C * (1.28 + (punch * 0.20)), 0.17, 0.33);
        return OklabColorMath.OklchToSrgbGamutMapped(new OklchColor(targetL, targetC, lch.H));
    }

    private void DrawVolumetricPlasmaEdge(
        SKCanvas canvas,
        float w,
        float h,
        float cornerRadius,
        RgbColor primaryColor,
        RgbColor secondaryColor,
        RgbColor tertiaryColor,
        bool twoColorGradient,
        float spillStrength,
        float counterStrength,
        float velvetStrength,
        float orbStrength,
        float thicknessMult,
        float spillMult,
        float autoIntensity,
        float autoReach,
        float beatImpact,
        bool isRevolvingRush,
        bool drawCornerBlooms)
    {
        var cx = w * 0.5f;
        var cy = h * 0.5f;
        var minSide = MathF.Min(w, h);
        var primaryAngleDeg = (float)(_rushPhase * 360.0);
        var counterAngleDeg = (float)(_counterPhase * 360.0);
        var rotPrimary = SKMatrix.CreateRotationDegrees(primaryAngleDeg, cx, cy);
        var rotCounter = SKMatrix.CreateRotationDegrees(counterAngleDeg, cx, cy);

        var midBlend = StyleLayoutHelper.BlendSrgb(primaryColor, secondaryColor, twoColorGradient ? 0.48f : 0.14f);
        var shimmerLift = Math.Clamp(0.06f + (beatImpact * 0.07f) + (_smoothedTreble * 0.05f), 0.05f, 0.18f);
        var radiantPrimary = StyleLayoutHelper.BlendSrgb(primaryColor, new RgbColor(255, 255, 255), shimmerLift);
        var radiantSecondary = StyleLayoutHelper.BlendSrgb(secondaryColor, new RgbColor(255, 255, 255), shimmerLift * 0.90f);

        var thicknessReach = 0.54f + (0.46f * thicknessMult);

        // Pass 1: Wide Off-Bezel Atmospheric Aurora Spill (pushed outside the viewport so ONLY smooth inward falloff enters)
        var spillAlpha = Math.Clamp(0.54f * spillStrength * autoIntensity, 0f, 0.90f);
        var spillStrokeWidth = Math.Clamp(minSide * 0.086f * spillMult * thicknessReach * autoReach, 16f, minSide * 0.23f);
        if (spillAlpha > 0.01f)
        {
            var spillOutset = spillStrokeWidth * 0.34f;
            var spillSigma = Math.Clamp(spillStrokeWidth * 0.56f, 9f, minSide * 0.112f);

            _spillPath.Reset();
            _spillPath.AddRoundRect(
                new SKRect(-spillOutset, -spillOutset, w + spillOutset, h + spillOutset),
                cornerRadius + spillOutset,
                cornerRadius + spillOutset);

            using var spillShader = isRevolvingRush
                ? BuildRevolvingSweepShader(cx, cy, rotPrimary, _revolvingSpillColors, _revolvingSpillStops, primaryColor, secondaryColor, radiantPrimary, radiantSecondary, spillAlpha, minAmbientFraction: 0.32f)
                : BuildStaticSweepShader(cx, cy, rotPrimary, _twoColorSweep, _twoColorStops, primaryColor, secondaryColor, midBlend, spillAlpha);

            _strokePaint.Shader = spillShader;
            _strokePaint.Color = SKColors.White;
            _strokePaint.StrokeWidth = spillStrokeWidth;
            _strokePaint.MaskFilter = GetOrCreateSpillBlur(spillSigma);
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

        // Pass 2: Counter-Orbiting Harmonic Plasma Stream (opposing phase creates living liquid interference swells)
        var counterAlpha = Math.Clamp(0.42f * counterStrength * autoIntensity, 0f, 0.78f);
        if (counterAlpha > 0.01f)
        {
            var counterWidth = Math.Clamp(spillStrokeWidth * 0.68f, 12f, minSide * 0.165f);
            var counterOutset = counterWidth * 0.40f;
            var counterSigma = Math.Clamp(counterWidth * 0.60f, 7.5f, minSide * 0.088f);

            _counterPath.Reset();
            _counterPath.AddRoundRect(
                new SKRect(-counterOutset, -counterOutset, w + counterOutset, h + counterOutset),
                cornerRadius + counterOutset,
                cornerRadius + counterOutset);

            using var counterShader = isRevolvingRush
                ? BuildRevolvingSweepShader(cx, cy, rotCounter, _counterSweepColors, _counterSweepStops, secondaryColor, tertiaryColor, radiantSecondary, radiantPrimary, counterAlpha, minAmbientFraction: 0.22f)
                : BuildStaticSweepShader(cx, cy, rotCounter, _twoColorSweep, _twoColorStops, secondaryColor, tertiaryColor, primaryColor, counterAlpha);

            _strokePaint.Shader = counterShader;
            _strokePaint.Color = SKColors.White;
            _strokePaint.StrokeWidth = counterWidth;
            _strokePaint.MaskFilter = GetOrCreateCounterBlur(counterSigma);
            try
            {
                canvas.DrawPath(_counterPath, _strokePaint);
            }
            finally
            {
                _strokePaint.MaskFilter = null;
                _strokePaint.Shader = null;
            }
        }

        // Pass 3: Strip-Free Velvet Rim Radiance (replaces hard neon strip — pushed behind bezel with soft Gaussian feather)
        var velvetAlpha = Math.Clamp(0.78f * velvetStrength * Math.Clamp(autoIntensity, 0.64f, 1.55f), 0f, 0.96f);
        if (velvetAlpha > 0.01f)
        {
            var velvetWidth = Math.Clamp(
                MathF.Max(11f, minSide * 0.028f) * thicknessReach * (0.88f + (beatImpact * 0.24f)),
                8f,
                minSide * 0.084f);
            var velvetOutset = velvetWidth * 0.40f;
            var velvetSigma = Math.Clamp(velvetWidth * 0.66f, 6.0f, minSide * 0.048f);

            _velvetPath.Reset();
            _velvetPath.AddRoundRect(
                new SKRect(-velvetOutset, -velvetOutset, w + velvetOutset, h + velvetOutset),
                cornerRadius + velvetOutset,
                cornerRadius + velvetOutset);

            using var velvetShader = isRevolvingRush
                ? BuildRevolvingSweepShader(cx, cy, rotPrimary, _velvetSweepColors, _velvetSweepStops, primaryColor, secondaryColor, radiantPrimary, radiantSecondary, velvetAlpha, minAmbientFraction: 0.28f)
                : BuildStaticSweepShader(cx, cy, rotPrimary, _twoColorSweep, _twoColorStops, radiantPrimary, radiantSecondary, midBlend, velvetAlpha * 0.92f);

            _strokePaint.Shader = velvetShader;
            _strokePaint.Color = SKColors.White;
            _strokePaint.StrokeWidth = velvetWidth;
            _strokePaint.MaskFilter = GetOrCreateVelvetBlur(velvetSigma);
            try
            {
                canvas.DrawPath(_velvetPath, _strokePaint);
            }
            finally
            {
                _strokePaint.MaskFilter = null;
                _strokePaint.Shader = null;
            }
        }

        // Pass 4: Travelling Off-Bezel Plasma Orbs & Beat-Surge Corner Light Pools (zero hard edge, pure volumetric depth)
        var orbAlpha = Math.Clamp(0.38f * orbStrength * Math.Clamp(autoIntensity, 0.65f, 1.65f), 0f, 0.72f);
        if (orbAlpha > 0.015f)
        {
            var orbRadius = Math.Clamp(
                minSide * (0.145f + (0.055f * spillMult)) * thicknessReach * Math.Clamp(autoReach, 0.80f, 1.48f),
                26f,
                minSide * 0.34f);

            if (isRevolvingRush)
            {
                var angle1 = (float)(_rushPhase * Math.PI * 2.0);
                var angle2 = angle1 + MathF.PI;
                DrawPerimeterPlasmaOrb(canvas, w, h, angle1, orbRadius, radiantPrimary, primaryColor, orbAlpha);
                DrawPerimeterPlasmaOrb(canvas, w, h, angle2, orbRadius * 0.96f, radiantSecondary, secondaryColor, orbAlpha * 0.92f);

                if (beatImpact > 0.22f)
                {
                    var harmonicAngle = (float)(_counterPhase * Math.PI * 2.0);
                    var harmonicAlpha = Math.Clamp(orbAlpha * 0.56f * MathF.Min(1.2f, beatImpact), 0f, 0.52f);
                    DrawPerimeterPlasmaOrb(canvas, w, h, harmonicAngle, orbRadius * 0.86f, tertiaryColor, secondaryColor, harmonicAlpha);
                }
            }

            if (drawCornerBlooms)
            {
                var cornerAlpha = Math.Clamp(orbAlpha * (isRevolvingRush ? 0.36f : 0.62f) * (0.55f + (beatImpact * 0.55f)), 0f, 0.54f);
                var cornerRad = Math.Clamp(orbRadius * 0.88f, 24f, minSide * 0.30f);
                if (cornerAlpha > 0.015f)
                {
                    var cornerOffset = cornerRad * 0.26f;
                    DrawSoftRadialPool(canvas, -cornerOffset, -cornerOffset, cornerRad, primaryColor, cornerAlpha);
                    DrawSoftRadialPool(canvas, w + cornerOffset, -cornerOffset, cornerRad, secondaryColor, cornerAlpha);
                    DrawSoftRadialPool(canvas, w + cornerOffset, h + cornerOffset, cornerRad, primaryColor, cornerAlpha);
                    DrawSoftRadialPool(canvas, -cornerOffset, h + cornerOffset, cornerRad, secondaryColor, cornerAlpha);
                }
            }
        }
    }

    private void DrawPerimeterPlasmaOrb(
        SKCanvas canvas,
        float w,
        float h,
        float angleRad,
        float radius,
        RgbColor coreColor,
        RgbColor outerColor,
        float alpha)
    {
        var (px, py, nx, ny) = ComputePerimeterPointAndOutwardNormal(angleRad, w, h);
        // Push center slightly behind the physical bezel so ONLY smooth Gaussian spill enters the viewport
        var centerX = px + (nx * radius * 0.24f);
        var centerY = py + (ny * radius * 0.24f);
        DrawSoftRadialPool(canvas, centerX, centerY, radius, coreColor, outerColor, alpha);
    }

    private void DrawSoftRadialPool(
        SKCanvas canvas,
        float cx,
        float cy,
        float radius,
        RgbColor color,
        float alpha) =>
        DrawSoftRadialPool(canvas, cx, cy, radius, color, color, alpha);

    private void DrawSoftRadialPool(
        SKCanvas canvas,
        float cx,
        float cy,
        float radius,
        RgbColor coreColor,
        RgbColor outerColor,
        float alpha)
    {
        _orbColors[0] = coreColor.ToSkColorWithAlpha(alpha);
        _orbColors[1] = outerColor.ToSkColorWithAlpha(alpha * 0.52f);
        _orbColors[2] = outerColor.ToSkColorWithAlpha(alpha * 0.14f);
        _orbColors[3] = outerColor.ToSkColorWithAlpha(0f);

        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy),
            radius,
            _orbColors,
            _orbStops,
            SKShaderTileMode.Clamp);

        _orbPaint.Shader = shader;
        _orbPaint.Color = SKColors.White;
        try
        {
            canvas.DrawCircle(cx, cy, radius, _orbPaint);
        }
        finally
        {
            _orbPaint.Shader = null;
        }
    }

    private static (float X, float Y, float OutNormalX, float OutNormalY) ComputePerimeterPointAndOutwardNormal(
        float angleRad,
        float w,
        float h)
    {
        var halfW = w * 0.5f;
        var halfH = h * 0.5f;
        var dx = MathF.Cos(angleRad);
        var dy = MathF.Sin(angleRad);
        var absX = MathF.Max(1e-5f, MathF.Abs(dx));
        var absY = MathF.Max(1e-5f, MathF.Abs(dy));

        var tx = halfW / absX;
        var ty = halfH / absY;
        if (tx <= ty)
        {
            var sx = dx >= 0f ? 1f : -1f;
            return (halfW + (sx * halfW), halfH + (dy * tx), sx, 0f);
        }

        var sy = dy >= 0f ? 1f : -1f;
        return (halfW + (dx * ty), halfH + (sy * halfH), 0f, sy);
    }

    private static SKShader BuildStaticSweepShader(
        float cx,
        float cy,
        SKMatrix rotMatrix,
        SKColor[] target,
        float[] stops,
        RgbColor primary,
        RgbColor secondary,
        RgbColor blend,
        float alpha)
    {
        target[0] = primary.ToSkColorWithAlpha(alpha);
        target[1] = primary.ToSkColorWithAlpha(alpha);
        target[2] = blend.ToSkColorWithAlpha(alpha);
        target[3] = secondary.ToSkColorWithAlpha(alpha);
        target[4] = primary.ToSkColorWithAlpha(alpha);
        return SKShader.CreateSweepGradient(new SKPoint(cx, cy), target, stops, rotMatrix);
    }

    private static SKShader BuildRevolvingSweepShader(
        float cx,
        float cy,
        SKMatrix rotMatrix,
        SKColor[] target,
        float[] stops,
        RgbColor primary,
        RgbColor secondary,
        RgbColor primaryPeak,
        RgbColor secondaryPeak,
        float alpha,
        float minAmbientFraction)
    {
        var dim = alpha * minAmbientFraction;
        var mid = alpha * Math.Clamp(minAmbientFraction + 0.34f, 0f, 0.90f);
        var bright = Math.Min(1f, alpha * 0.92f);
        var hotspot = Math.Min(1f, alpha * 1.00f);

        target[0] = primaryPeak.ToSkColorWithAlpha(hotspot);
        target[1] = primary.ToSkColorWithAlpha(bright);
        target[2] = primary.ToSkColorWithAlpha(mid);
        target[3] = secondary.ToSkColorWithAlpha(dim);
        target[4] = secondary.ToSkColorWithAlpha(mid);
        target[5] = secondaryPeak.ToSkColorWithAlpha(hotspot);
        target[6] = secondary.ToSkColorWithAlpha(bright);
        target[7] = primary.ToSkColorWithAlpha(dim);
        target[8] = primary.ToSkColorWithAlpha(mid);
        target[9] = primaryPeak.ToSkColorWithAlpha(bright);
        target[10] = primaryPeak.ToSkColorWithAlpha(hotspot);

        return SKShader.CreateSweepGradient(new SKPoint(cx, cy), target, stops, rotMatrix);
    }

    private SKMaskFilter GetOrCreateSpillBlur(float sigma) =>
        GetOrCreateCachedBlur(ref _spillMaskFilter, ref _cachedSpillSigma, sigma);

    private SKMaskFilter GetOrCreateCounterBlur(float sigma) =>
        GetOrCreateCachedBlur(ref _counterMaskFilter, ref _cachedCounterSigma, sigma);

    private SKMaskFilter GetOrCreateVelvetBlur(float sigma) =>
        GetOrCreateCachedBlur(ref _velvetMaskFilter, ref _cachedVelvetSigma, sigma);

    private static SKMaskFilter GetOrCreateCachedBlur(ref SKMaskFilter? field, ref float cachedSigma, float sigma)
    {
        var quantized = MathF.Round(sigma * 2f) * 0.5f;
        if (field is not null && MathF.Abs(cachedSigma - quantized) < 0.01f)
        {
            return field;
        }

        field?.Dispose();
        cachedSigma = quantized;
        field = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.8f, quantized));
        return field;
    }
}
