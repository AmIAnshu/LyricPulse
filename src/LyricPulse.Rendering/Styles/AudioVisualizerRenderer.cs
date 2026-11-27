using LyricPulse.Core.Audio;
using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Host;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Renders 5 distinct audio-reactive background visualizers layered cleanly behind kinetic lyrics:
/// <see cref="AudioVisualizerMode.SpectrumBars"/>, <see cref="AudioVisualizerMode.RadialHalo"/>,
/// <see cref="AudioVisualizerMode.FluidWave"/>, <see cref="AudioVisualizerMode.ParticleField"/>,
/// and <see cref="AudioVisualizerMode.BassHorizon"/>.
/// Zero per-frame SKPaint/SKPath allocations.
/// </summary>
public sealed class AudioVisualizerRenderer : IDisposable
{
    private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _glowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPath _wavePath = new();
    private readonly SKPath _waveFillPath = new();
    private readonly SKPath _haloPath = new();
    private readonly SKColor[] _gradColors = new SKColor[3];
    private readonly float[] _gradStops = [0f, 0.55f, 1f];
    private bool _disposed;

    public void Render(in StyleRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var packet = context.Packet;
        var settings = packet.PlaybackSnapshot?.Settings;
        var mode = settings?.AudioVisualizerMode ?? AppSettings.Default.AudioVisualizerMode;
        if (mode == AudioVisualizerMode.None || packet.ReducedMotion || packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            return;
        }

        var intensity = Math.Clamp(
            settings?.AudioVisualizerIntensity ?? AppSettings.DefaultVisualizerIntensity,
            AppSettings.MinVisualizerIntensity,
            AppSettings.MaxVisualizerIntensity);
        if (packet.IsCompactCard)
        {
            intensity *= 0.65f;
        }

        var audio = packet.EffectiveAudioMetrics;
        var palette = StageBackdropRenderer.ResolveBackdropPalette(
            packet.EffectiveStyle,
            packet.Palette,
            settings?.AutoSyncColors ?? true);

        switch (mode)
        {
            case AudioVisualizerMode.SpectrumBars:
                DrawSpectrumBars(context.Canvas, packet, audio, palette, intensity);
                break;
            case AudioVisualizerMode.RadialHalo:
                DrawRadialHalo(context.Canvas, packet, audio, palette, intensity);
                break;
            case AudioVisualizerMode.FluidWave:
                DrawFluidWaves(context.Canvas, packet, audio, palette, intensity);
                break;
            case AudioVisualizerMode.ParticleField:
                DrawParticleField(context.Canvas, packet, audio, palette, intensity);
                break;
            case AudioVisualizerMode.BassHorizon:
                DrawBassHorizon(context.Canvas, packet, audio, palette, intensity);
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fillPaint.Dispose();
        _strokePaint.Dispose();
        _glowPaint.Dispose();
        _wavePath.Dispose();
        _waveFillPath.Dispose();
        _haloPath.Dispose();
    }

    private void DrawSpectrumBars(
        SKCanvas canvas,
        FrameRenderPacket packet,
        AudioFrameMetrics audio,
        AlbumAccentPalette palette,
        float intensity)
    {
        var bands = audio.SafeBands;
        var count = bands.Length;
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        var maxBarH = h * (packet.IsCompactCard ? 0.28f : 0.24f) * intensity;
        var floorY = packet.BottomFadeToBlack ? h * 0.86f : h * 0.94f;
        var spanW = w * 0.84f;
        var startX = (w - spanW) * 0.5f;
        var step = spanW / count;
        var barW = Math.Max(3f, step * 0.56f);

        for (var i = 0; i < count; i++)
        {
            var mag = Math.Clamp(bands[i], 0.03f, 1.0f);
            var barH = Math.Max(3f, mag * maxBarH);
            var x = startX + (i * step) + ((step - barW) * 0.5f);
            var t = i / (float)Math.Max(1, count - 1);
            var barColor = StyleLayoutHelper.BlendSrgb(palette.AccentColor1, palette.AccentColor2, t);

            _fillPaint.Shader = null;
            _fillPaint.Color = barColor.ToSkColorWithAlpha(0.26f + (mag * 0.40f));
            var barRect = new SKRect(x, floorY - barH, x + barW, floorY);
            canvas.DrawRoundRect(barRect, barW * 0.5f, barW * 0.5f, _fillPaint);

            // Soft reflection below floor
            _fillPaint.Color = barColor.ToSkColorWithAlpha(0.08f + (mag * 0.12f));
            var reflRect = new SKRect(x, floorY + 2f, x + barW, floorY + 2f + (barH * 0.35f));
            canvas.DrawRoundRect(reflRect, barW * 0.5f, barW * 0.5f, _fillPaint);
        }
    }

    private void DrawRadialHalo(
        SKCanvas canvas,
        FrameRenderPacket packet,
        AudioFrameMetrics audio,
        AlbumAccentPalette palette,
        float intensity)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        var cx = w * 0.5f;
        var cy = h * 0.48f;
        var baseR = MathF.Min(w, h) * (packet.IsCompactCard ? 0.24f : 0.27f);
        var beatScale = 1f + (audio.BassEnergy * 0.12f * intensity) + (audio.BeatPulse * 0.08f * intensity);
        var radius = baseR * beatScale;

        // Soft inner aura bloom
        _gradColors[0] = palette.AccentColor1.ToSkColorWithAlpha(0.0f);
        _gradColors[1] = palette.AccentColor2.ToSkColorWithAlpha(0.14f * intensity + (audio.BassEnergy * 0.14f));
        _gradColors[2] = palette.AccentColor1.ToSkColorWithAlpha(0.0f);
        using (var aura = SKShader.CreateRadialGradient(new SKPoint(cx, cy), Math.Max(radius * 1.45f, 10f), _gradColors, _gradStops, SKShaderTileMode.Clamp))
        {
            _fillPaint.Shader = aura;
            _fillPaint.Color = SKColors.White;
            try
            {
                canvas.DrawCircle(cx, cy, radius * 1.45f, _fillPaint);
            }
            finally
            {
                _fillPaint.Shader = null;
            }
        }

        var bands = audio.SafeBands;
        var pts = bands.Length * 2;
        var timeRot = (float)(packet.RenderTimeSeconds * 0.22);
        _haloPath.Rewind();

        for (var i = 0; i <= pts; i++)
        {
            var bandIdx = (i % bands.Length);
            var amp = bands[bandIdx] * MathF.Min(w, h) * 0.065f * intensity;
            var angle = timeRot + ((i / (float)pts) * MathF.Tau);
            var r = radius + amp;
            var x = cx + (MathF.Cos(angle) * r);
            var y = cy + (MathF.Sin(angle) * r);
            if (i == 0)
            {
                _haloPath.MoveTo(x, y);
            }
            else
            {
                _haloPath.LineTo(x, y);
            }
        }

        _haloPath.Close();

        _glowPaint.StrokeWidth = packet.IsCompactCard ? 4.5f : 9.5f;
        _glowPaint.Color = palette.AccentColor2.ToSkColorWithAlpha(0.22f * intensity);
        canvas.DrawPath(_haloPath, _glowPaint);

        _strokePaint.StrokeWidth = packet.IsCompactCard ? 1.2f : 2.1f;
        _strokePaint.Color = palette.AccentColor1.ToSkColorWithAlpha(0.42f * intensity);
        canvas.DrawPath(_haloPath, _strokePaint);
    }

    private static float SampleSmoothBand(ReadOnlySpan<float> bands, float u)
    {
        if (bands.Length == 0)
        {
            return 0f;
        }

        if (bands.Length == 1)
        {
            return bands[0];
        }

        var scaled = Math.Clamp(u, 0f, 1f) * (bands.Length - 1);
        var idx0 = (int)MathF.Floor(scaled);
        var idx1 = Math.Min(bands.Length - 1, idx0 + 1);
        var frac = scaled - idx0;
        var smoothFrac = frac * frac * (3f - (2f * frac));
        return bands[idx0] + ((bands[idx1] - bands[idx0]) * smoothFrac);
    }

    private void DrawFluidWaves(
        SKCanvas canvas,
        FrameRenderPacket packet,
        AudioFrameMetrics audio,
        AlbumAccentPalette palette,
        float intensity)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        var t = (float)packet.RenderTimeSeconds;
        var bands = audio.SafeBands.AsSpan();
        const int segments = 36;

        for (var wave = 0; wave < 3; wave++)
        {
            // Anchor waves slightly lower so silky aurora ribbons cradle under active lyrics rather than slicing across them
            var baseY = h * (0.58f + (wave * 0.075f));
            var ampBase = h * (0.042f + (wave * 0.018f)) * intensity * (0.45f + (audio.BassEnergy * 0.90f) + (audio.MidEnergy * 0.45f));
            var color = wave switch
            {
                0 => palette.AccentColor1,
                1 => palette.AccentColor2,
                _ => palette.AccentColor3
            };

            _wavePath.Rewind();
            _waveFillPath.Rewind();

            var prevX = 0f;
            var prevY = baseY;
            var lastX = w;
            var lastY = baseY;

            for (var s = 0; s <= segments; s++)
            {
                var u = s / (float)segments;
                var x = u * w;
                var bandVal = SampleSmoothBand(bands, u);
                var envelope = MathF.Sin(u * MathF.PI);
                var primaryOsc = MathF.Sin((u * 5.8f) + (t * (1.35f + (wave * 0.38f))) + (wave * 1.45f));
                var harmonicOsc = 0.28f * MathF.Sin((u * 10.5f) - (t * (0.95f + (wave * 0.25f))) + (wave * 2.1f));
                var y = baseY + ((primaryOsc + harmonicOsc) * ampBase * envelope * (0.58f + (bandVal * 0.72f)));

                if (s == 0)
                {
                    _wavePath.MoveTo(x, y);
                    _waveFillPath.MoveTo(x, y);
                }
                else
                {
                    var midX = (prevX + x) * 0.5f;
                    var midY = (prevY + y) * 0.5f;
                    _wavePath.QuadTo(prevX, prevY, midX, midY);
                    _waveFillPath.QuadTo(prevX, prevY, midX, midY);
                }

                prevX = x;
                prevY = y;
                lastX = x;
                lastY = y;
            }

            _wavePath.LineTo(lastX, lastY);
            _waveFillPath.LineTo(lastX, lastY);

            var fillBottom = baseY + (h * 0.14f);
            _waveFillPath.LineTo(w, fillBottom);
            _waveFillPath.LineTo(0f, fillBottom);
            _waveFillPath.Close();

            _gradColors[0] = color.ToSkColorWithAlpha(0.11f * intensity);
            _gradColors[1] = color.ToSkColorWithAlpha(0.03f * intensity);
            _gradColors[2] = color.ToSkColorWithAlpha(0f);
            using (var ribbonFill = SKShader.CreateLinearGradient(
                       new SKPoint(w * 0.5f, baseY - ampBase),
                       new SKPoint(w * 0.5f, fillBottom),
                       _gradColors,
                       _gradStops,
                       SKShaderTileMode.Clamp))
            {
                _fillPaint.Shader = ribbonFill;
                _fillPaint.Color = SKColors.White;
                try
                {
                    canvas.DrawPath(_waveFillPath, _fillPaint);
                }
                finally
                {
                    _fillPaint.Shader = null;
                }
            }

            _glowPaint.StrokeWidth = packet.IsCompactCard ? 4.5f : 9.5f;
            _glowPaint.Color = color.ToSkColorWithAlpha(0.16f * intensity);
            canvas.DrawPath(_wavePath, _glowPaint);

            _strokePaint.StrokeWidth = packet.IsCompactCard ? 1.1f : 1.9f;
            _strokePaint.Color = color.ToSkColorWithAlpha(0.40f * intensity);
            canvas.DrawPath(_wavePath, _strokePaint);
        }
    }

    private void DrawParticleField(
        SKCanvas canvas,
        FrameRenderPacket packet,
        AudioFrameMetrics audio,
        AlbumAccentPalette palette,
        float intensity)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        var t = (float)packet.RenderTimeSeconds;
        var particleCount = packet.IsCompactCard ? 20 : 48;
        var bassBoost = 1f + (audio.BassEnergy * 0.65f * intensity) + (audio.BeatPulse * 0.55f);

        for (var i = 0; i < particleCount; i++)
        {
            var seedX = ((i * 73 + 19) % 97) / 97f;
            var seedSpeed = 0.035f + (((i * 31) % 29) / 29f) * 0.06f;
            var phaseY = (seedX * 0.7f + (t * seedSpeed * (0.7f + audio.OverallLevel * 0.8f))) % 1.0f;
            var x = (seedX * w) + (MathF.Sin((t * 0.9f) + i) * 18f);
            var y = h * (1.02f - (phaseY * 1.06f));
            var baseRadius = (packet.IsCompactCard ? 1.6f : 2.8f) + ((i % 4) * 0.9f);
            var radius = baseRadius * bassBoost * 0.75f;
            var fade = MathF.Sin(phaseY * MathF.PI);

            var color = (i % 3) switch
            {
                0 => palette.AccentColor1,
                1 => palette.AccentColor2,
                _ => palette.AccentColor3
            };

            _fillPaint.Shader = null;
            _fillPaint.Color = color.ToSkColorWithAlpha(Math.Clamp(0.28f * intensity * fade * (0.7f + audio.TrebleEnergy), 0f, 0.78f));
            canvas.DrawCircle(x, y, radius, _fillPaint);
        }
    }

    private void DrawBassHorizon(
        SKCanvas canvas,
        FrameRenderPacket packet,
        AudioFrameMetrics audio,
        AlbumAccentPalette palette,
        float intensity)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        var horizonY = h * 0.76f;
        var energy = Math.Clamp((audio.BassEnergy * 0.65f) + (audio.BeatPulse * 0.55f), 0.12f, 1.4f) * intensity;

        // Horizontal concert stage beam
        _gradColors[0] = palette.AccentColor1.ToSkColorWithAlpha(0f);
        _gradColors[1] = palette.AccentColor1.ToSkColorWithAlpha(Math.Clamp(0.32f * energy, 0f, 0.72f));
        _gradColors[2] = palette.AccentColor2.ToSkColorWithAlpha(0f);

        using (var horizonShader = SKShader.CreateLinearGradient(
            new SKPoint(w * 0.05f, horizonY),
            new SKPoint(w * 0.95f, horizonY),
            _gradColors,
            _gradStops,
            SKShaderTileMode.Clamp))
        {
            _fillPaint.Shader = horizonShader;
            _fillPaint.Color = SKColors.White;
            try
            {
                canvas.DrawRect(w * 0.05f, horizonY - 3f, w * 0.90f, 6f, _fillPaint);
            }
            finally
            {
                _fillPaint.Shader = null;
            }
        }

        // Volumetric vertical concert shafts rising on beat
        var bands = audio.SafeBands;
        const int shafts = 7;
        for (var i = 0; i < shafts; i++)
        {
            var u = 0.14f + ((i / (float)(shafts - 1)) * 0.72f);
            var band = bands[(i * 4) % bands.Length];
            var shaftH = h * (0.28f + (band * 0.36f)) * intensity;
            var shaftW = Math.Max(14f, w * 0.045f);
            var x = u * w;
            var color = (i % 2 == 0) ? palette.AccentColor1 : palette.AccentColor2;

            _gradColors[0] = color.ToSkColorWithAlpha(Math.Clamp(0.22f * intensity * (0.4f + band), 0f, 0.55f));
            _gradColors[1] = color.ToSkColorWithAlpha(0.08f * intensity);
            _gradColors[2] = color.ToSkColorWithAlpha(0f);

            var rect = new SKRect(x - (shaftW * 0.5f), horizonY - shaftH, x + (shaftW * 0.5f), horizonY);
            using var shaftShader = SKShader.CreateLinearGradient(
                new SKPoint(x, horizonY),
                new SKPoint(x, horizonY - shaftH),
                _gradColors,
                _gradStops,
                SKShaderTileMode.Clamp);

            _fillPaint.Shader = shaftShader;
            _fillPaint.Color = SKColors.White;
            try
            {
                canvas.DrawRect(rect, _fillPaint);
            }
            finally
            {
                _fillPaint.Shader = null;
            }
        }
    }
}
