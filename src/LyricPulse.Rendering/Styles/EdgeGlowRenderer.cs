using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Host;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Renders full-perimeter Ambilight / concert-stage glow and Neon Rush — a single,
/// continuous, unbroken GPU-shaded neon plasma ribbon gliding smoothly along a
/// true #000000 OLED frame without segmentation joints, dots, or blur-filter lag.
/// Strictly skipped on transparent DesktopOverlay and compact GalleryCard thumbnails.
/// </summary>
public sealed class EdgeGlowRenderer : IDisposable
{
    private readonly SKPaint _edgePaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _rimPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPath _perimeterPath = new();

    private readonly SKColor[] _edgeColors = new SKColor[4];
    private readonly float[] _edgeStops = [0f, 0.28f, 0.65f, 1f];
    private readonly SKColor[] _cornerColors = new SKColor[4];
    private readonly float[] _cornerStops = [0f, 0.35f, 0.68f, 1f];
    private readonly SKColor[] _sweepColors = new SKColor[12];
    private readonly float[] _sweepStops = [0.00f, 0.38f, 0.46f, 0.54f, 0.63f, 0.72f, 0.80f, 0.87f, 0.92f, 0.96f, 0.985f, 1.00f];

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

        var canvas = context.Canvas;

        // Neon Rush keeps unlit borders & interior pitch black (#000000); one single seamless ribbon glides around the frame.
        if (mode == EdgeGlowMode.NeonRush)
        {
            DrawSingleNeonRushLine(canvas, w, h, packet, palette, intensity, thicknessSetting);
            return;
        }

        var beatLift = packet.ReducedMotion
            ? 0f
            : (_smoothedBass * 0.54f) + (_smoothedBeat * 0.46f) + (audio.TrebleEnergy * 0.14f);

        var (reachFrac, baseAlpha, rimAlpha) = mode switch
        {
            EdgeGlowMode.SoftAmbient => (0.15f + (beatLift * 0.04f), 0.25f, 0.0f),
            EdgeGlowMode.NeonRim => (0.17f + (beatLift * 0.07f), 0.28f + (beatLift * 0.22f), 0.58f + (beatLift * 0.34f)),
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

        // 1. Multi-stop silky perimeter edge washes (SoftAmbient / BeatPulse / NeonRim)
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
        var cornerAlpha = Math.Clamp(alpha * 0.72f, 0f, 0.74f);
        DrawRadialBloom(canvas, 0f, 0f, cornerRadius, cTop, cornerAlpha);
        DrawRadialBloom(canvas, w, 0f, cornerRadius, cRight, cornerAlpha);
        if (!packet.BottomFadeToBlack)
        {
            DrawRadialBloom(canvas, 0f, h, cornerRadius, cLeft, cornerAlpha);
            DrawRadialBloom(canvas, w, h, cornerRadius, cBottom, cornerAlpha);
        }

        // 3. Rim strokes for BeatPulse / NeonRim
        if (rimAlpha > 0.01f && !packet.BottomFadeToBlack)
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
        _perimeterPath.Dispose();
    }

    private void UpdateSmoothedMotionState(
        double renderTimeSeconds,
        float speedSetting,
        float safePartyEnergy,
        float bassEnergy,
        float beatPulse,
        bool reducedMotion)
    {
        _smoothedBass = (_smoothedBass * 0.74f) + (Math.Clamp(bassEnergy, 0f, 1.5f) * 0.26f);
        _smoothedBeat = (_smoothedBeat * 0.68f) + (Math.Clamp(beatPulse, 0f, 1.5f) * 0.32f);

        if (reducedMotion)
        {
            _lastRenderTimeSec = renderTimeSeconds;
            return;
        }

        // Silky slow delta-integrated rotation (~0.075 laps/sec at 1.0x -> ~19s per lap at 0.70x default)
        var baseRate = 0.075f * speedSetting * Math.Clamp(safePartyEnergy, 0.75f, 1.35f);
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
            var effectiveRate = baseRate * (1.0f + (_smoothedBass * 0.16f) + (_smoothedBeat * 0.10f));
            _rushPhase = (_rushPhase + (dt * effectiveRate)) % 1.0;
        }

        _lastRenderTimeSec = renderTimeSeconds;
    }

    private void DrawSingleNeonRushLine(
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
        var inset = Math.Clamp(2.2f * thickness, 2.0f, Math.Min(7.5f, minDim * 0.04f));
        var cornerR = Math.Clamp(8f + (thickness * 2.5f), 5f, minDim * 0.05f);

        _perimeterPath.Rewind();
        if (isOpenArch)
        {
            // Seamless Open Top-U arch ending well above bottomFadeToBlack rows
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

        if (isOpenArch)
        {
            canvas.Save();
            canvas.ClipRect(new SKRect(0f, 0f, w, h * 0.92f));
        }

        try
        {
            var sweepCenter = isOpenArch
                ? new SKPoint(w * 0.5f, h * 0.48f)
                : new SKPoint(w * 0.5f, h * 0.5f);

            // In fullscreen: continuous 360° glide; in windowed mode: smooth eased sweep along the upper arch
            var rotAngleRad = isOpenArch
                ? ((-155f + (170f * (0.5f - (0.5f * MathF.Cos((float)(_rushPhase * Math.PI * 2.0)))))) * (MathF.PI / 180f))
                : (float)(_rushPhase * Math.PI * 2.0);

            var localMatrix = SKMatrix.CreateRotation(rotAngleRad, sweepCenter.X, sweepCenter.Y);
            var shimmer = 0.5f + (0.5f * MathF.Sin((float)(_rushPhase * Math.PI * 2.0)));
            var tailTone = palette.AccentColor3;
            var midTone = StyleLayoutHelper.BlendSrgb(palette.AccentColor2, palette.AccentColor1, 0.25f + (0.28f * shimmer));
            var leadTone = StyleLayoutHelper.BlendSrgb(palette.AccentColor1, palette.AccentColor2, 0.22f * (1f - shimmer));
            var hotCoreTone = StyleLayoutHelper.BlendSrgb(leadTone, new RgbColor(255, 255, 255), 0.58f);

            var opacityScale = intensity * packet.GlobalOpacity;
            var bodyScale = thickness * Math.Clamp(opacityScale, 0.45f, 1.35f);

            // Layer 1: Wide silky atmospheric neon halation hugging bezel inward
            DrawContinuousSweepStroke(
                canvas,
                sweepCenter,
                localMatrix,
                strokeWidth: Math.Clamp((30.0f + (_smoothedBeat * 12.0f) + (_smoothedBass * 6.0f)) * bodyScale, 6f, 84f),
                tailTone,
                midTone,
                leadTone,
                peakAlpha: Math.Clamp(0.22f * opacityScale, 0f, 0.36f));

            // Layer 2: Rich saturated liquid neon body
            DrawContinuousSweepStroke(
                canvas,
                sweepCenter,
                localMatrix,
                strokeWidth: Math.Clamp((12.5f + (_smoothedBeat * 5.0f)) * bodyScale, 3.5f, 38f),
                tailTone,
                midTone,
                leadTone,
                peakAlpha: Math.Clamp(0.62f * opacityScale, 0f, 0.85f));

            // Layer 3: Razor-clean luminous inner core (no round ball dot, smooth both-end taper!)
            DrawContinuousSweepStroke(
                canvas,
                sweepCenter,
                localMatrix,
                strokeWidth: Math.Clamp((3.6f + (_smoothedBeat * 1.8f)) * thickness, 1.8f, 11f),
                midTone,
                leadTone,
                hotCoreTone,
                peakAlpha: Math.Clamp(0.95f * opacityScale, 0f, 1.0f));
        }
        finally
        {
            if (isOpenArch)
            {
                canvas.Restore();
            }
        }
    }

    private void DrawContinuousSweepStroke(
        SKCanvas canvas,
        SKPoint sweepCenter,
        SKMatrix localMatrix,
        float strokeWidth,
        RgbColor tailColor,
        RgbColor midColor,
        RgbColor peakColor,
        float peakAlpha)
    {
        if (peakAlpha <= 0.004f || strokeWidth <= 0.5f)
        {
            return;
        }

        // Smooth C1/C2 envelope over stops [0..1] spanning one unbroken ~62% long glowing line,
        // feathering seamlessly to 0% alpha at both ends so zero joints or blunt caps ever appear.
        _sweepColors[0] = SKColors.Transparent;
        _sweepColors[1] = SKColors.Transparent;
        _sweepColors[2] = tailColor.ToSkColorWithAlpha(peakAlpha * 0.04f);
        _sweepColors[3] = tailColor.ToSkColorWithAlpha(peakAlpha * 0.14f);
        _sweepColors[4] = midColor.ToSkColorWithAlpha(peakAlpha * 0.32f);
        _sweepColors[5] = midColor.ToSkColorWithAlpha(peakAlpha * 0.56f);
        _sweepColors[6] = StyleLayoutHelper.BlendSrgb(midColor, peakColor, 0.5f).ToSkColorWithAlpha(peakAlpha * 0.80f);
        _sweepColors[7] = peakColor.ToSkColorWithAlpha(peakAlpha * 0.96f);
        _sweepColors[8] = peakColor.ToSkColorWithAlpha(peakAlpha);
        _sweepColors[9] = StyleLayoutHelper.BlendSrgb(midColor, peakColor, 0.7f).ToSkColorWithAlpha(peakAlpha * 0.52f);
        _sweepColors[10] = midColor.ToSkColorWithAlpha(peakAlpha * 0.12f);
        _sweepColors[11] = SKColors.Transparent;

        using var shader = SKShader.CreateSweepGradient(sweepCenter, _sweepColors, _sweepStops, localMatrix);
        _rimPaint.Shader = shader;
        _rimPaint.StrokeWidth = strokeWidth;
        _rimPaint.Color = SKColors.White;
        try
        {
            canvas.DrawPath(_perimeterPath, _rimPaint);
        }
        finally
        {
            _rimPaint.Shader = null;
        }
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
