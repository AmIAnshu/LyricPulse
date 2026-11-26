using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Host;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Renders full-perimeter Ambilight / concert-stage glow and multi-layer liquid Neon Rush
/// plasma ribbons (<see cref="EdgeGlowMode"/>) reacting smoothly to WASAPI audio metrics.
/// Uses delta-integrated phase accumulation so speed never warps on beat transients, and
/// pre-allocated Skia objects for locked 60-144 FPS performance without blur-filter overhead.
/// Strictly skipped on transparent DesktopOverlay and compact GalleryCard thumbnails.
/// </summary>
public sealed class EdgeGlowRenderer : IDisposable
{
    private const int TaperSegmentCount = 8;

    private readonly SKPaint _edgePaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _rimPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _headPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath _perimeterPath = new();
    private readonly SKPath _beamPath = new();
    private readonly SKPathMeasure _pathMeasure = new();

    private readonly SKColor[] _edgeColors = new SKColor[4];
    private readonly float[] _edgeStops = [0f, 0.28f, 0.65f, 1f];
    private readonly SKColor[] _cornerColors = new SKColor[4];
    private readonly float[] _cornerStops = [0f, 0.35f, 0.68f, 1f];
    private readonly SKColor[] _beamColors = new SKColor[4];
    private readonly float[] _beamStops = [0f, 0.22f, 0.56f, 1f];

    private double _lastRenderTimeSec = -1.0;
    private double _rushPhase;
    private float _smoothedBass;
    private float _smoothedBeat;
    private bool _disposed;

    public void Render(in StyleRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var packet = context.Packet;
        var settings = packet.PlaybackSnapshot?.Settings;
        var mode = settings?.EdgeGlowMode ?? AppSettings.Default.EdgeGlowMode;
        if (mode == EdgeGlowMode.Off || packet.SurfaceMode != StageRenderSurfaceMode.StageCanvas)
        {
            return;
        }

        if (packet.ReducedMotion && mode != EdgeGlowMode.SoftAmbient)
        {
            return;
        }

        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        if (w <= 8f || h <= 8f)
        {
            return;
        }

        var intensity = Math.Clamp(
            settings?.EdgeGlowIntensity ?? AppSettings.DefaultEdgeGlowIntensity,
            AppSettings.MinGlowIntensity,
            AppSettings.MaxGlowIntensity);
        var speedSetting = settings is { EdgeGlowSpeed: > 0f } s1 && float.IsFinite(s1.EdgeGlowSpeed)
            ? Math.Clamp(s1.EdgeGlowSpeed, AppSettings.MinEdgeGlowSpeed, AppSettings.MaxEdgeGlowSpeed)
            : AppSettings.DefaultEdgeGlowSpeed;
        var thicknessSetting = settings is { EdgeGlowThickness: > 0f } s2 && float.IsFinite(s2.EdgeGlowThickness)
            ? Math.Clamp(s2.EdgeGlowThickness, AppSettings.MinEdgeGlowThickness, AppSettings.MaxEdgeGlowThickness)
            : AppSettings.DefaultEdgeGlowThickness;

        var audio = packet.EffectiveAudioMetrics;
        UpdateSmoothedMotionState(packet.RenderTimeSeconds, speedSetting, packet.SafePartyEnergy, audio.BassEnergy, audio.BeatPulse, packet.ReducedMotion);

        var palette = StageBackdropRenderer.ResolveBackdropPalette(
            packet.EffectiveStyle,
            packet.Palette,
            settings?.AutoSyncColors ?? true);

        var beatLift = packet.ReducedMotion
            ? 0f
            : (_smoothedBass * 0.54f) + (_smoothedBeat * 0.46f) + (audio.TrebleEnergy * 0.14f);

        var (reachFrac, baseAlpha, rimAlpha) = mode switch
        {
            EdgeGlowMode.SoftAmbient => (0.15f + (beatLift * 0.04f), 0.25f, 0.0f),
            EdgeGlowMode.NeonRim => (0.17f + (beatLift * 0.07f), 0.28f + (beatLift * 0.22f), 0.58f + (beatLift * 0.34f)),
            EdgeGlowMode.NeonRush => (0.19f + (beatLift * 0.08f), 0.29f + (beatLift * 0.24f), 0.45f + (beatLift * 0.30f)),
            _ => (0.16f + (beatLift * 0.08f), 0.26f + (beatLift * 0.30f), 0.26f + (beatLift * 0.34f))
        };

        var reachThicknessScale = 0.78f + (thicknessSetting * 0.20f);
        var reachX = Math.Clamp(w * reachFrac * reachThicknessScale, 28f, w * 0.34f);
        var reachY = Math.Clamp(h * reachFrac * reachThicknessScale, 28f, h * 0.34f);
        var alpha = Math.Clamp(baseAlpha * intensity * packet.GlobalOpacity, 0f, 0.85f);

        var phase = packet.ReducedMotion ? 0f : (float)((packet.RenderTimeSeconds * (0.18 * speedSetting)) % 1.0);
        var cTop = StyleLayoutHelper.BlendSrgb(palette.AccentColor1, palette.AccentColor2, 0.5f + (0.5f * MathF.Sin(phase * MathF.Tau)));
        var cBottom = StyleLayoutHelper.BlendSrgb(palette.AccentColor2, palette.AccentColor3, 0.5f + (0.5f * MathF.Cos(phase * MathF.Tau)));
        var cLeft = StyleLayoutHelper.BlendSrgb(palette.AccentColor1, palette.AccentColor3, 0.5f + (0.5f * MathF.Sin((phase + 0.25f) * MathF.Tau)));
        var cRight = StyleLayoutHelper.BlendSrgb(palette.AccentColor2, palette.AccentColor1, 0.5f + (0.5f * MathF.Cos((phase + 0.35f) * MathF.Tau)));

        var canvas = context.Canvas;

        // 1. Multi-stop silky perimeter edge washes
        DrawEdgeBand(canvas, new SKRect(0f, 0f, w, reachY), new SKPoint(0f, 0f), new SKPoint(0f, reachY), cTop, alpha * 0.90f);
        if (!packet.BottomFadeToBlack)
        {
            DrawEdgeBand(canvas, new SKRect(0f, h - reachY, w, h), new SKPoint(0f, h), new SKPoint(0f, h - reachY), cBottom, alpha * 0.94f);
        }

        DrawEdgeBand(canvas, new SKRect(0f, 0f, reachX, h), new SKPoint(0f, 0f), new SKPoint(reachX, 0f), cLeft, alpha * 0.86f);
        DrawEdgeBand(canvas, new SKRect(w - reachX, 0f, w, h), new SKPoint(w, 0f), new SKPoint(w - reachX, 0f), cRight, alpha * 0.86f);

        // 2. Soft corner bass blooms
        var minDim = MathF.Min(w, h);
        var cornerRadius = Math.Clamp(minDim * (0.24f + (_smoothedBass * 0.07f) + (thicknessSetting * 0.03f)), 32f, minDim * 0.36f);
        var cornerAlpha = Math.Clamp(alpha * (mode == EdgeGlowMode.NeonRush ? 0.94f : 0.72f), 0f, 0.74f);
        DrawRadialBloom(canvas, 0f, 0f, cornerRadius, cTop, cornerAlpha);
        DrawRadialBloom(canvas, w, 0f, cornerRadius, cRight, cornerAlpha);
        if (!packet.BottomFadeToBlack)
        {
            DrawRadialBloom(canvas, 0f, h, cornerRadius, cLeft, cornerAlpha);
            DrawRadialBloom(canvas, w, h, cornerRadius, cBottom, cornerAlpha);
        }

        // 3. Rim & Neon Rush liquid plasma perimeter
        if (mode == EdgeGlowMode.NeonRush)
        {
            DrawNeonRushPerimeter(canvas, w, h, packet, palette, intensity, thicknessSetting);
        }
        else if (rimAlpha > 0.01f && !packet.BottomFadeToBlack)
        {
            var outerAlpha = Math.Clamp(rimAlpha * 0.30f * intensity * packet.GlobalOpacity, 0f, 0.50f);
            _rimPaint.Shader = null;
            _rimPaint.StrokeWidth = Math.Clamp((9.0f + (_smoothedBeat * 5.5f)) * thicknessSetting, 4f, 28f);
            _rimPaint.Color = palette.AccentColor2.ToSkColorWithAlpha(outerAlpha);
            canvas.DrawRect(2.5f, 2.5f, w - 5f, h - 5f, _rimPaint);

            _rimPaint.StrokeWidth = Math.Clamp((2.6f + (_smoothedBeat * 2.4f)) * thicknessSetting, 1.5f, 9f);
            _rimPaint.Color = palette.AccentColor1.ToSkColorWithAlpha(Math.Clamp(rimAlpha * intensity * packet.GlobalOpacity, 0f, 0.95f));
            canvas.DrawRect(2.0f, 2.0f, w - 4f, h - 4f, _rimPaint);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _edgePaint.Dispose();
        _rimPaint.Dispose();
        _headPaint.Dispose();
        _perimeterPath.Dispose();
        _beamPath.Dispose();
        _pathMeasure.Dispose();
    }

    private void UpdateSmoothedMotionState(
        double renderTimeSeconds,
        float speedSetting,
        float safePartyEnergy,
        float bassEnergy,
        float beatPulse,
        bool reducedMotion)
    {
        // Smooth audio transients slightly so thickness & surge breathe organically
        _smoothedBass = (_smoothedBass * 0.72f) + (Math.Clamp(bassEnergy, 0f, 1.5f) * 0.28f);
        _smoothedBeat = (_smoothedBeat * 0.65f) + (Math.Clamp(beatPulse, 0f, 1.5f) * 0.35f);

        if (reducedMotion)
        {
            _lastRenderTimeSec = renderTimeSeconds;
            return;
        }

        // True delta-time integration (~0.095 laps/sec at 1.0x speed, ~15s per full perimeter lap at default 0.70x)
        // prevents time-multiplication phase jumps when bass hits mid-song.
        var baseRate = 0.095f * speedSetting * Math.Clamp(safePartyEnergy, 0.75f, 1.35f);
        var dt = (_lastRenderTimeSec >= 0.0 && renderTimeSeconds >= _lastRenderTimeSec)
            ? renderTimeSeconds - _lastRenderTimeSec
            : -1.0;

        if (dt < 0.0 || dt > 0.25)
        {
            _rushPhase = (renderTimeSeconds * baseRate) % 1.0;
            if (_rushPhase < 0.0) _rushPhase += 1.0;
        }
        else if (dt > 0.0)
        {
            var effectiveRate = baseRate * (1.0f + (_smoothedBass * 0.22f) + (_smoothedBeat * 0.14f));
            _rushPhase = (_rushPhase + (dt * effectiveRate)) % 1.0;
        }

        _lastRenderTimeSec = renderTimeSeconds;
    }

    private void DrawNeonRushPerimeter(
        SKCanvas canvas,
        float w,
        float h,
        FrameRenderPacket packet,
        AlbumAccentPalette palette,
        float intensity,
        float thickness)
    {
        var isOpenArch = packet.BottomFadeToBlack;
        var minDim = MathF.Min(w, h);
        var inset = Math.Clamp(4.0f * thickness, 3.0f, Math.Min(12.0f, minDim * 0.06f));
        var cornerR = Math.Clamp(12f + (thickness * 4f), 8f, minDim * 0.08f);

        _perimeterPath.Rewind();
        if (isOpenArch)
        {
            // Rounded Open Top-U arch stops cleanly well above bottomFadeToBlack region
            var archY = h * 0.72f;
            _perimeterPath.MoveTo(inset, archY);
            _perimeterPath.LineTo(inset, inset + cornerR);
            _perimeterPath.QuadTo(inset, inset, inset + cornerR, inset);
            _perimeterPath.LineTo(w - inset - cornerR, inset);
            _perimeterPath.QuadTo(w - inset, inset, w - inset, inset + cornerR);
            _perimeterPath.LineTo(w - inset, archY);
        }
        else
        {
            _perimeterPath.AddRoundRect(new SKRect(inset, inset, w - inset, h - inset), cornerR, cornerR);
        }

        _pathMeasure.SetPath(_perimeterPath, forceClosed: !isOpenArch);
        var totalLen = _pathMeasure.Length;
        if (totalLen <= 20f)
        {
            return;
        }

        if (isOpenArch)
        {
            // Clip strictly above bottom 8% so even thick radial glow blooms leave bottom rows at alpha=0
            canvas.Save();
            canvas.ClipRect(new SKRect(0f, 0f, w, h * 0.92f));
        }

        try
        {
            // Continuous frosted glass diffuser rail so racing ribbons live inside a luminous frame
            _rimPaint.Shader = null;
            _rimPaint.StrokeWidth = Math.Clamp(18.0f * thickness, 7f, 44f);
            _rimPaint.Color = palette.AccentColor2.ToSkColorWithAlpha(Math.Clamp(0.09f * intensity * packet.GlobalOpacity, 0f, 0.22f));
            canvas.DrawPath(_perimeterPath, _rimPaint);

            _rimPaint.StrokeWidth = Math.Clamp(3.2f * thickness, 1.5f, 8.5f);
            _rimPaint.Color = palette.AccentColor1.ToSkColorWithAlpha(Math.Clamp(0.18f * intensity * packet.GlobalOpacity, 0f, 0.36f));
            canvas.DrawPath(_perimeterPath, _rimPaint);

            // Asymmetric lush beam lengths (38% and 32% of perimeter) for graceful liquid sweep
            var beamLen1 = totalLen * (0.38f + (_smoothedBeat * 0.08f));
            var beamLen2 = totalLen * (0.32f + (_smoothedBass * 0.07f));

            float headDist1;
            float headDist2;
            if (isOpenArch)
            {
                // Smooth eased ping-pong travel along the open U-arch so beams never pop off corners
                var sweep1 = 0.5f - (0.5f * MathF.Cos((float)(_rushPhase * Math.PI * 2.0)));
                var sweep2 = 0.5f - (0.5f * MathF.Cos((float)(((_rushPhase + 0.50) % 1.0) * Math.PI * 2.0)));
                var travelMin = Math.Min(totalLen * 0.14f, beamLen1 * 0.45f);
                var travelMax = Math.Max(travelMin + 12f, totalLen * 0.96f);
                headDist1 = travelMin + ((travelMax - travelMin) * sweep1);
                headDist2 = travelMin + ((travelMax - travelMin) * sweep2);
            }
            else
            {
                var p1 = (float)_rushPhase;
                var p2 = (float)((_rushPhase + 0.50) % 1.0);
                headDist1 = p1 * totalLen;
                headDist2 = p2 * totalLen;
            }

            // Iridescent harmonic color shift between album palette and neon cyan/magenta accents
            var shimmer = 0.5f + (0.5f * MathF.Sin((float)(_rushPhase * Math.PI * 2.0)));
            var cyanNeon = StyleLayoutHelper.BlendSrgb(palette.AccentColor1, new RgbColor(0x38, 0xBD, 0xF8), 0.32f + (0.14f * shimmer));
            var magentaNeon = StyleLayoutHelper.BlendSrgb(palette.AccentColor2, new RgbColor(0xEC, 0x48, 0x99), 0.30f + (0.14f * (1f - shimmer)));

            // Traveling edge-coupled light pools + tapered multi-layer plasma ribbons
            DrawTaperedNeonRibbon(canvas, w, h, totalLen, headDist1, beamLen1, cyanNeon, palette.AccentColor3, intensity * packet.GlobalOpacity, thickness, !isOpenArch);
            DrawTaperedNeonRibbon(canvas, w, h, totalLen, headDist2, beamLen2, magentaNeon, palette.AccentColor1, intensity * packet.GlobalOpacity, thickness, !isOpenArch);
        }
        finally
        {
            if (isOpenArch)
            {
                canvas.Restore();
            }
        }
    }

    private void DrawTaperedNeonRibbon(
        SKCanvas canvas,
        float w,
        float h,
        float totalLen,
        float headDist,
        float segLen,
        RgbColor neonColor,
        RgbColor tailColor,
        float opacityIntensity,
        float thickness,
        bool isClosedLoop)
    {
        var clampedHead = Math.Clamp(headDist, 0f, totalLen);
        _pathMeasure.GetPosition(clampedHead, out var headPos);

        // 1. Dynamic inward light spill tracking the beam head across the glass stage
        var minDim = MathF.Min(w, h);
        var poolRadius = Math.Clamp(
            minDim * (0.20f + (thickness * 0.055f) + (_smoothedBeat * 0.04f)),
            36f,
            minDim * 0.42f);
        DrawRadialBloom(canvas, headPos.X, headPos.Y, poolRadius, neonColor, Math.Clamp(0.28f * opacityIntensity, 0f, 0.42f));

        // 2. Tapered 8-segment multi-layer plasma trail (feathers smoothly to 0% alpha & thin tip)
        var coreWidthBase = (28.0f + (_smoothedBeat * 12.0f) + (_smoothedBass * 6.0f)) * thickness * Math.Clamp(opacityIntensity, 0.45f, 1.35f);
        for (var i = 0; i < TaperSegmentCount; i++)
        {
            var t0 = i / (float)TaperSegmentCount;
            var t1 = (i + 1.15f) / TaperSegmentCount; // slight overlap hides segment seams cleanly
            t1 = MathF.Min(t1, 1.0f);

            var segStartDist = headDist - (segLen * (1f - t0));
            var segEndDist = headDist - (segLen * (1f - t1));

            if (!ExtractPathSegment(totalLen, segStartDist, segEndDist, isClosedLoop))
            {
                continue;
            }

            var mid = (t0 + t1) * 0.5f;
            var alphaCurve = MathF.Pow(mid, 1.55f);
            var widthCurve = 0.24f + (0.76f * MathF.Sqrt(mid));
            var blendColor = StyleLayoutHelper.BlendSrgb(tailColor, neonColor, 0.25f + (0.75f * mid));

            _rimPaint.Shader = null;

            // Layer A: Wide atmospheric bleed halo
            _rimPaint.StrokeWidth = Math.Clamp(coreWidthBase * 1.45f * widthCurve, 3f, 92f);
            _rimPaint.Color = blendColor.ToSkColorWithAlpha(Math.Clamp(0.16f * alphaCurve * opacityIntensity, 0f, 0.34f));
            canvas.DrawPath(_beamPath, _rimPaint);

            // Layer B: Rich saturated liquid neon body
            _rimPaint.StrokeWidth = Math.Clamp(coreWidthBase * 0.72f * widthCurve, 2f, 52f);
            _rimPaint.Color = blendColor.ToSkColorWithAlpha(Math.Clamp(0.48f * alphaCurve * opacityIntensity, 0f, 0.80f));
            canvas.DrawPath(_beamPath, _rimPaint);

            // Layer C: Luminous iridescent inner tube (leading 75%)
            if (i >= 2)
            {
                _rimPaint.StrokeWidth = Math.Clamp(coreWidthBase * 0.28f * widthCurve, 1.5f, 22f);
                _rimPaint.Color = StyleLayoutHelper.BlendSrgb(blendColor, new RgbColor(255, 255, 255), 0.52f)
                    .ToSkColorWithAlpha(Math.Clamp(0.76f * alphaCurve * opacityIntensity, 0f, 0.94f));
                canvas.DrawPath(_beamPath, _rimPaint);
            }

            // Layer D: Silky white-hot plasma highlight (front 38% only)
            if (i >= TaperSegmentCount - 3)
            {
                _rimPaint.StrokeWidth = Math.Clamp(coreWidthBase * 0.11f * widthCurve, 1.2f, 8.5f);
                _rimPaint.Color = StyleLayoutHelper.BlendSrgb(neonColor, new RgbColor(255, 255, 255), 0.82f)
                    .ToSkColorWithAlpha(Math.Clamp(0.92f * alphaCurve * opacityIntensity, 0f, 1.0f));
                canvas.DrawPath(_beamPath, _rimPaint);
            }
        }

        // 3. Soft volumetric plasma head flare (harmonized halo instead of a small hard white dot)
        var flareR = Math.Clamp((24f + (_smoothedBeat * 14f)) * thickness * Math.Clamp(opacityIntensity, 0.5f, 1.35f), 10f, 64f);
        _beamColors[0] = StyleLayoutHelper.BlendSrgb(neonColor, new RgbColor(255, 255, 255), 0.88f)
            .ToSkColorWithAlpha(Math.Clamp(0.92f * opacityIntensity, 0f, 1.0f));
        _beamColors[1] = neonColor.ToSkColorWithAlpha(Math.Clamp(0.58f * opacityIntensity, 0f, 0.85f));
        _beamColors[2] = neonColor.ToSkColorWithAlpha(Math.Clamp(0.18f * opacityIntensity, 0f, 0.35f));
        _beamColors[3] = neonColor.ToSkColorWithAlpha(0f);

        using var headShader = SKShader.CreateRadialGradient(headPos, flareR, _beamColors, _beamStops, SKShaderTileMode.Clamp);
        _headPaint.Shader = headShader;
        _headPaint.Color = SKColors.White;
        try
        {
            canvas.DrawCircle(headPos, flareR, _headPaint);
        }
        finally
        {
            _headPaint.Shader = null;
        }
    }

    private bool ExtractPathSegment(float totalLen, float startDist, float endDist, bool isClosedLoop)
    {
        _beamPath.Rewind();

        if (isClosedLoop)
        {
            var s = ((startDist % totalLen) + totalLen) % totalLen;
            var e = ((endDist % totalLen) + totalLen) % totalLen;
            if (e < s)
            {
                _pathMeasure.GetSegment(s, totalLen, _beamPath, startWithMoveTo: true);
                _pathMeasure.GetSegment(0f, e, _beamPath, startWithMoveTo: false);
            }
            else if (e - s > 2f)
            {
                _pathMeasure.GetSegment(s, e, _beamPath, startWithMoveTo: true);
            }
        }
        else
        {
            var cs = Math.Clamp(startDist, 0f, totalLen);
            var ce = Math.Clamp(endDist, 0f, totalLen);
            if (ce - cs <= 2f)
            {
                return false;
            }

            _pathMeasure.GetSegment(cs, ce, _beamPath, startWithMoveTo: true);
        }

        return !_beamPath.IsEmpty;
    }

    private void DrawRadialBloom(SKCanvas canvas, float cx, float cy, float radius, RgbColor color, float alpha)
    {
        if (alpha <= 0.004f || radius <= 2f)
        {
            return;
        }

        _cornerColors[0] = color.ToSkColorWithAlpha(alpha);
        _cornerColors[1] = color.ToSkColorWithAlpha(alpha * 0.42f);
        _cornerColors[2] = color.ToSkColorWithAlpha(alpha * 0.10f);
        _cornerColors[3] = color.ToSkColorWithAlpha(0f);

        using var shader = SKShader.CreateRadialGradient(new SKPoint(cx, cy), radius, _cornerColors, _cornerStops, SKShaderTileMode.Clamp);
        _edgePaint.Shader = shader;
        _edgePaint.Color = SKColors.White;
        try
        {
            canvas.DrawCircle(cx, cy, radius, _edgePaint);
        }
        finally
        {
            _edgePaint.Shader = null;
        }
    }

    private void DrawEdgeBand(SKCanvas canvas, SKRect rect, SKPoint start, SKPoint end, RgbColor color, float alpha)
    {
        if (alpha <= 0.004f || rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        _edgeColors[0] = color.ToSkColorWithAlpha(alpha);
        _edgeColors[1] = color.ToSkColorWithAlpha(alpha * 0.52f);
        _edgeColors[2] = color.ToSkColorWithAlpha(alpha * 0.14f);
        _edgeColors[3] = color.ToSkColorWithAlpha(0f);

        using var shader = SKShader.CreateLinearGradient(start, end, _edgeColors, _edgeStops, SKShaderTileMode.Clamp);
        _edgePaint.Shader = shader;
        _edgePaint.Color = SKColors.White;
        try
        {
            canvas.DrawRect(rect, _edgePaint);
        }
        finally
        {
            _edgePaint.Shader = null;
        }
    }
}
