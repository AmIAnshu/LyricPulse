using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Host;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Renders full-perimeter Ambilight / concert-stage glow and Neon Rush racing plasma ribbons
/// (<see cref="EdgeGlowMode"/>) reacting in real time to WASAPI <c>BassEnergy</c> &amp; <c>BeatPulse</c>.
/// Zero per-frame SKPaint/SKPath/blur-filter allocations for locked 60-144 FPS smoothness.
/// Strictly skipped on transparent DesktopOverlay and compact GalleryCard thumbnails.
/// </summary>
public sealed class EdgeGlowRenderer : IDisposable
{
    private readonly SKPaint _edgePaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _rimPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _headPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath _perimeterPath = new();
    private readonly SKPath _beamPath = new();
    private readonly SKPathMeasure _pathMeasure = new();

    private readonly SKColor[] _edgeColors = new SKColor[4];
    private readonly float[] _edgeStops = [0f, 0.28f, 0.65f, 1f];
    private readonly SKColor[] _cornerColors = new SKColor[3];
    private readonly float[] _cornerStops = [0f, 0.45f, 1f];
    private readonly SKColor[] _beamColors = new SKColor[3];
    private readonly float[] _beamStops = [0f, 0.5f, 1f];
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
        var audio = packet.EffectiveAudioMetrics;
        var palette = StageBackdropRenderer.ResolveBackdropPalette(
            packet.EffectiveStyle,
            packet.Palette,
            settings?.AutoSyncColors ?? true);

        var beatLift = packet.ReducedMotion
            ? 0f
            : (audio.BassEnergy * 0.58f) + (audio.BeatPulse * 0.48f) + (audio.TrebleEnergy * 0.16f);

        var (reachFrac, baseAlpha, rimAlpha) = mode switch
        {
            EdgeGlowMode.SoftAmbient => (0.16f + (beatLift * 0.04f), 0.24f, 0.0f),
            EdgeGlowMode.NeonRim => (0.18f + (beatLift * 0.08f), 0.28f + (beatLift * 0.24f), 0.58f + (beatLift * 0.34f)),
            EdgeGlowMode.NeonRush => (0.20f + (beatLift * 0.09f), 0.30f + (beatLift * 0.26f), 0.42f + (beatLift * 0.32f)),
            _ => (0.17f + (beatLift * 0.09f), 0.26f + (beatLift * 0.32f), 0.24f + (beatLift * 0.36f))
        };

        var reachX = Math.Clamp(w * reachFrac, 32f, w * 0.36f);
        var reachY = Math.Clamp(h * reachFrac, 32f, h * 0.36f);
        var alpha = Math.Clamp(baseAlpha * intensity * packet.GlobalOpacity, 0f, 0.85f);

        var phase = packet.ReducedMotion ? 0f : (float)((packet.RenderTimeSeconds * 0.38) % 1.0);
        var cTop = StyleLayoutHelper.BlendSrgb(palette.AccentColor1, palette.AccentColor2, 0.5f + (0.5f * MathF.Sin(phase * MathF.Tau)));
        var cBottom = StyleLayoutHelper.BlendSrgb(palette.AccentColor2, palette.AccentColor3, 0.5f + (0.5f * MathF.Cos(phase * MathF.Tau)));
        var cLeft = StyleLayoutHelper.BlendSrgb(palette.AccentColor1, palette.AccentColor3, 0.5f + (0.5f * MathF.Sin((phase + 0.25f) * MathF.Tau)));
        var cRight = StyleLayoutHelper.BlendSrgb(palette.AccentColor2, palette.AccentColor1, 0.5f + (0.5f * MathF.Cos((phase + 0.35f) * MathF.Tau)));

        var canvas = context.Canvas;

        // 1. Multi-stop silky edge wash
        DrawEdgeBand(canvas, new SKRect(0f, 0f, w, reachY), new SKPoint(0f, 0f), new SKPoint(0f, reachY), cTop, alpha * 0.90f);
        if (!packet.BottomFadeToBlack)
        {
            DrawEdgeBand(canvas, new SKRect(0f, h - reachY, w, h), new SKPoint(0f, h), new SKPoint(0f, h - reachY), cBottom, alpha * 0.94f);
        }

        DrawEdgeBand(canvas, new SKRect(0f, 0f, reachX, h), new SKPoint(0f, 0f), new SKPoint(reachX, 0f), cLeft, alpha * 0.86f);
        DrawEdgeBand(canvas, new SKRect(w - reachX, 0f, w, h), new SKPoint(w, 0f), new SKPoint(w - reachX, 0f), cRight, alpha * 0.86f);

        // 2. Corner bass blooms (enhances every mode smoothly with zero blur lag)
        var cornerRadius = MathF.Min(w, h) * (0.28f + (audio.BassEnergy * 0.10f));
        var cornerAlpha = Math.Clamp(alpha * (mode == EdgeGlowMode.NeonRush ? 0.92f : 0.72f), 0f, 0.72f);
        DrawCornerBloom(canvas, 0f, 0f, cornerRadius, cTop, cornerAlpha);
        DrawCornerBloom(canvas, w, 0f, cornerRadius, cRight, cornerAlpha);
        if (!packet.BottomFadeToBlack)
        {
            DrawCornerBloom(canvas, 0f, h, cornerRadius, cLeft, cornerAlpha);
            DrawCornerBloom(canvas, w, h, cornerRadius, cBottom, cornerAlpha);
        }

        // 3. Rim & Neon Rush racing perimeter beams
        if (mode == EdgeGlowMode.NeonRush)
        {
            DrawNeonRushBeams(canvas, w, h, packet, palette, intensity, audio.BassEnergy, audio.BeatPulse);
        }
        else if (rimAlpha > 0.01f && !packet.BottomFadeToBlack)
        {
            var outerAlpha = Math.Clamp(rimAlpha * 0.28f * intensity * packet.GlobalOpacity, 0f, 0.45f);
            _rimPaint.Shader = null;
            _rimPaint.StrokeWidth = Math.Clamp(8.0f * intensity + (audio.BeatPulse * 5.0f), 4f, 14f);
            _rimPaint.Color = palette.AccentColor2.ToSkColorWithAlpha(outerAlpha);
            canvas.DrawRect(2f, 2f, w - 4f, h - 4f, _rimPaint);

            _rimPaint.StrokeWidth = Math.Clamp(2.2f * intensity + (audio.BeatPulse * 2.2f), 1.4f, 5.5f);
            _rimPaint.Color = palette.AccentColor1.ToSkColorWithAlpha(Math.Clamp(rimAlpha * intensity * packet.GlobalOpacity, 0f, 0.95f));
            canvas.DrawRect(1.5f, 1.5f, w - 3f, h - 3f, _rimPaint);
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

    private void DrawNeonRushBeams(
        SKCanvas canvas,
        float w,
        float h,
        FrameRenderPacket packet,
        AlbumAccentPalette palette,
        float intensity,
        float bassEnergy,
        float beatPulse)
    {
        _perimeterPath.Rewind();
        const float inset = 2.0f;
        var isOpenArch = packet.BottomFadeToBlack;

        if (isOpenArch)
        {
            // Open U-arch hugs bottom-left->top->bottom-right cleanly so bottom fade to black stays pure alpha=0
            var archY = h * 0.78f;
            _perimeterPath.MoveTo(inset, archY);
            _perimeterPath.LineTo(inset, inset);
            _perimeterPath.LineTo(w - inset, inset);
            _perimeterPath.LineTo(w - inset, archY);
        }
        else
        {
            _perimeterPath.AddRoundRect(new SKRect(inset, inset, w - inset, h - inset), 8f, 8f);
        }

        _pathMeasure.SetPath(_perimeterPath, forceClosed: !isOpenArch);
        var totalLen = _pathMeasure.Length;
        if (totalLen <= 20f)
        {
            return;
        }

        var rushSpeed = (0.24f + (bassEnergy * 0.14f) + (beatPulse * 0.12f)) * Math.Clamp(packet.SafePartyEnergy, 0.7f, 1.45f);
        var tHead1 = (float)((packet.RenderTimeSeconds * rushSpeed) % 1.0);
        if (tHead1 < 0f) tHead1 += 1f;
        var tHead2 = (tHead1 + 0.50f) % 1.0f;

        var beamLen1 = totalLen * (0.28f + (beatPulse * 0.08f));
        var beamLen2 = totalLen * (0.24f + (bassEnergy * 0.07f));
        var cyberCyan = StyleLayoutHelper.BlendSrgb(palette.AccentColor1, new RgbColor(0x38, 0xBD, 0xF8), 0.35f);
        var cyberMagenta = StyleLayoutHelper.BlendSrgb(palette.AccentColor2, new RgbColor(0xF4, 0x3F, 0x5E), 0.30f);

        DrawSingleNeonBeam(canvas, totalLen, tHead1 * totalLen, beamLen1, cyberCyan, palette.AccentColor3, intensity, beatPulse, !isOpenArch);
        DrawSingleNeonBeam(canvas, totalLen, tHead2 * totalLen, beamLen2, cyberMagenta, palette.AccentColor1, intensity, beatPulse, !isOpenArch);
    }

    private void DrawSingleNeonBeam(
        SKCanvas canvas,
        float totalLen,
        float headDist,
        float segLen,
        RgbColor neonColor,
        RgbColor tailColor,
        float intensity,
        float beatPulse,
        bool isClosedLoop)
    {
        var startDist = headDist - segLen;
        _beamPath.Rewind();

        if (isClosedLoop)
        {
            if (startDist < 0f)
            {
                _pathMeasure.GetSegment(totalLen + startDist, totalLen, _beamPath, startWithMoveTo: true);
                _pathMeasure.GetSegment(0f, headDist, _beamPath, startWithMoveTo: false);
            }
            else
            {
                _pathMeasure.GetSegment(startDist, headDist, _beamPath, startWithMoveTo: true);
            }
        }
        else
        {
            // Bounce smoothly back and forth across open arch when windowed
            var clampStart = Math.Clamp(startDist, 0f, totalLen);
            var clampEnd = Math.Clamp(headDist, 0f, totalLen);
            if (clampEnd - clampStart < 8f)
            {
                return;
            }

            _pathMeasure.GetSegment(clampStart, clampEnd, _beamPath, startWithMoveTo: true);
            headDist = clampEnd;
        }

        if (_beamPath.IsEmpty)
        {
            return;
        }

        _pathMeasure.GetPosition(Math.Clamp(headDist, 0f, totalLen), out var headPos);

        // Pass 1: Wide outer neon glow (ultra-fast GPU stroke, zero blur filter lag)
        var outerW = (14.0f + (beatPulse * 7.0f)) * Math.Clamp(intensity, 0.5f, 1.4f);
        _rimPaint.Shader = null;
        _rimPaint.StrokeWidth = outerW;
        _rimPaint.Color = neonColor.ToSkColorWithAlpha(Math.Clamp(0.22f * intensity, 0f, 0.45f));
        canvas.DrawPath(_beamPath, _rimPaint);

        // Pass 2: Vibrant mid neon ribbon
        _rimPaint.StrokeWidth = outerW * 0.42f;
        _rimPaint.Color = StyleLayoutHelper.BlendSrgb(tailColor, neonColor, 0.65f).ToSkColorWithAlpha(Math.Clamp(0.56f * intensity, 0f, 0.85f));
        canvas.DrawPath(_beamPath, _rimPaint);

        // Pass 3: White-hot plasma filament core
        _rimPaint.StrokeWidth = Math.Clamp(2.2f * intensity + (beatPulse * 1.4f), 1.6f, 4.2f);
        _rimPaint.Color = StyleLayoutHelper.BlendSrgb(neonColor, new RgbColor(255, 255, 255), 0.72f)
            .ToSkColorWithAlpha(Math.Clamp(0.88f * intensity, 0f, 1.0f));
        canvas.DrawPath(_beamPath, _rimPaint);

        // Pass 4: Luminous leading comet spark head
        var sparkR = (14f + (beatPulse * 10f)) * Math.Clamp(intensity, 0.5f, 1.35f);
        _beamColors[0] = SKColors.White.WithAlpha((byte)Math.Clamp(235f * intensity, 0f, 255f));
        _beamColors[1] = neonColor.ToSkColorWithAlpha(Math.Clamp(0.55f * intensity, 0f, 0.85f));
        _beamColors[2] = neonColor.ToSkColorWithAlpha(0f);

        using var headShader = SKShader.CreateRadialGradient(headPos, Math.Max(sparkR, 4f), _beamColors, _beamStops, SKShaderTileMode.Clamp);
        _headPaint.Shader = headShader;
        _headPaint.Color = SKColors.White;
        try
        {
            canvas.DrawCircle(headPos, sparkR, _headPaint);
        }
        finally
        {
            _headPaint.Shader = null;
        }
    }

    private void DrawCornerBloom(SKCanvas canvas, float cx, float cy, float radius, RgbColor color, float alpha)
    {
        if (alpha <= 0.004f || radius <= 2f)
        {
            return;
        }

        _cornerColors[0] = color.ToSkColorWithAlpha(alpha);
        _cornerColors[1] = color.ToSkColorWithAlpha(alpha * 0.28f);
        _cornerColors[2] = color.ToSkColorWithAlpha(0f);

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
