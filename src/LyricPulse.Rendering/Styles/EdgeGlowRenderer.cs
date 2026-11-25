using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Host;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Renders full-perimeter 4-edge Ambilight / concert-stage glow (<see cref="EdgeGlowMode"/>)
/// reacting in real time to WASAPI <c>BassEnergy</c> &amp; <c>BeatPulse</c> using the active
/// stadium/album accent palette. Zero per-frame SKPaint allocations.
/// </summary>
public sealed class EdgeGlowRenderer : IDisposable
{
    private readonly SKPaint _edgePaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _rimPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKColor[] _edgeColors = new SKColor[3];
    private readonly float[] _edgeStops = [0f, 0.42f, 1f];
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
        if (w <= 4f || h <= 4f)
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
            : (audio.BassEnergy * 0.55f) + (audio.BeatPulse * 0.45f) + (audio.TrebleEnergy * 0.15f);

        var (reachFrac, baseAlpha, rimAlpha) = mode switch
        {
            EdgeGlowMode.SoftAmbient => (0.14f + (beatLift * 0.04f), 0.24f, 0.0f),
            EdgeGlowMode.NeonRim => (0.17f + (beatLift * 0.08f), 0.34f + (beatLift * 0.24f), 0.55f + (beatLift * 0.35f)),
            _ => (0.15f + (beatLift * 0.09f), 0.26f + (beatLift * 0.32f), 0.22f + (beatLift * 0.38f))
        };

        var reachX = Math.Clamp(w * reachFrac, 28f, w * 0.35f);
        var reachY = Math.Clamp(h * reachFrac, 28f, h * 0.35f);
        var alpha = Math.Clamp(baseAlpha * intensity * packet.GlobalOpacity, 0f, 0.85f);

        var phase = packet.ReducedMotion ? 0f : (float)(packet.RenderTimeSeconds * 0.45 % 1.0);
        var cTop = StyleLayoutHelper.BlendSrgb(palette.AccentColor1, palette.AccentColor2, 0.5f + (0.5f * MathF.Sin(phase * MathF.Tau)));
        var cBottom = StyleLayoutHelper.BlendSrgb(palette.AccentColor2, palette.AccentColor3, 0.5f + (0.5f * MathF.Cos(phase * MathF.Tau)));
        var cLeft = StyleLayoutHelper.BlendSrgb(palette.AccentColor1, palette.AccentColor3, 0.5f + (0.5f * MathF.Sin((phase + 0.25f) * MathF.Tau)));
        var cRight = StyleLayoutHelper.BlendSrgb(palette.AccentColor2, palette.AccentColor1, 0.5f + (0.5f * MathF.Cos((phase + 0.35f) * MathF.Tau)));

        var canvas = context.Canvas;

        // Top edge
        DrawEdgeBand(canvas, new SKRect(0f, 0f, w, reachY), new SKPoint(0f, 0f), new SKPoint(0f, reachY), cTop, alpha * 0.92f);

        // Bottom edge (suppress if smoothly fading hero into #000000 gallery in windowed mode)
        if (!packet.BottomFadeToBlack)
        {
            DrawEdgeBand(canvas, new SKRect(0f, h - reachY, w, h), new SKPoint(0f, h), new SKPoint(0f, h - reachY), cBottom, alpha);
        }

        // Left & Right edges
        DrawEdgeBand(canvas, new SKRect(0f, 0f, reachX, h), new SKPoint(0f, 0f), new SKPoint(reachX, 0f), cLeft, alpha * 0.88f);
        DrawEdgeBand(canvas, new SKRect(w - reachX, 0f, w, h), new SKPoint(w, 0f), new SKPoint(w - reachX, 0f), cRight, alpha * 0.88f);

        if (rimAlpha > 0.01f && !packet.BottomFadeToBlack)
        {
            _rimPaint.StrokeWidth = Math.Clamp(2.2f * intensity + (audio.BeatPulse * 2.5f), 1.5f, 6.0f);
            _rimPaint.Color = palette.AccentColor1.ToSkColorWithAlpha(Math.Clamp(rimAlpha * intensity * packet.GlobalOpacity, 0f, 0.95f));
            canvas.DrawRect(1f, 1f, w - 2f, h - 2f, _rimPaint);
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
    }

    private void DrawEdgeBand(SKCanvas canvas, SKRect rect, SKPoint start, SKPoint end, RgbColor color, float alpha)
    {
        if (alpha <= 0.004f || rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        _edgeColors[0] = color.ToSkColorWithAlpha(alpha);
        _edgeColors[1] = color.ToSkColorWithAlpha(alpha * 0.34f);
        _edgeColors[2] = color.ToSkColorWithAlpha(0f);

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
