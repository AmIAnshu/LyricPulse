using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Concert Drop Suite renderer reacting to real-time FFT / manual Beat Drop climaxes:
/// expanding concentric bass shockwave rings, 72 supersonic tapered energy sparks,
/// photosensitive-safe radial spotlight surge, and centered spring lyric zoom kick.
/// Zero per-frame SKPaint/SKPath allocations; strictly skipped on ReducedMotion and DesktopOverlay corners.
/// </summary>
public sealed class BeatDropEffectRenderer : IDisposable
{
    private const int MaxSparks = 72;
    private const int MaxRings = 4;

    private struct DropSpark
    {
        public float X;
        public float Y;
        public float PrevX;
        public float PrevY;
        public float Vx;
        public float Vy;
        public float Age;
        public float LifeSec;
        public float Width;
        public byte ColorSlot;
    }

    private struct ShockwaveRing
    {
        public float Age;
        public float DurationSec;
        public float StartRadius;
        public float EndRadius;
        public byte ColorSlot;
    }

    private readonly DropSpark[] _sparks = new DropSpark[MaxSparks];
    private readonly ShockwaveRing[] _rings = new ShockwaveRing[MaxRings];
    private readonly SKPaint _bloomPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _ringPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _sparkPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKColor[] _bloomColors = new SKColor[3];
    private readonly float[] _bloomStops = [0f, 0.45f, 1f];

    private int _activeSparks;
    private int _activeRings;
    private float _edgeCooldownSec;
    private float _lyricPunch;
    private float _bloomAlpha;
    private uint _rngState = 0x9E3779B9u;
    private bool _disposed;

    public static bool IsEnabled(in FrameRenderPacket packet) =>
        packet.PlaybackSnapshot?.Settings.BeatDropEffectEnabled ?? true;

    public static float ResolveDropIntensity(in FrameRenderPacket packet)
    {
        var raw = packet.PlaybackSnapshot?.Settings.BeatDropIntensity ?? AppSettings.DefaultBeatDropIntensity;
        return float.IsFinite(raw)
            ? Math.Clamp(raw, AppSettings.MinBeatDropIntensity, AppSettings.MaxBeatDropIntensity)
            : AppSettings.DefaultBeatDropIntensity;
    }

    public void Reset()
    {
        _activeSparks = 0;
        _activeRings = 0;
        _edgeCooldownSec = 0f;
        _lyricPunch = 0f;
        _bloomAlpha = 0f;
    }

    public bool Advance(in FrameRenderPacket packet)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (packet.ReducedMotion || !IsEnabled(in packet))
        {
            if (_activeSparks > 0 || _activeRings > 0 || _lyricPunch > 0f || _bloomAlpha > 0f)
            {
                Reset();
            }

            return false;
        }

        var dt = Math.Clamp(packet.DeltaSeconds, 0.001f, SpringState.MaxDeltaSeconds);
        var audio = packet.EffectiveAudioMetrics;
        var dropIntensity = ResolveDropIntensity(in packet);

        _edgeCooldownSec = MathF.Max(0f, _edgeCooldownSec - dt);
        _lyricPunch *= MathF.Exp(-dt * 8.2f);
        if (_lyricPunch < 0.002f)
        {
            _lyricPunch = 0f;
        }

        _bloomAlpha *= MathF.Exp(-dt * 3.4f);
        if (audio.IsClimaxActive)
        {
            _bloomAlpha = MathF.Max(_bloomAlpha, audio.ClimaxIntensity * 0.14f * dropIntensity);
        }

        // Trigger shockwave + sparks on drop surge rising edge
        if (audio.DropTrigger >= 0.55f && _edgeCooldownSec <= 0f)
        {
            _edgeCooldownSec = 0.42f;
            _lyricPunch = MathF.Min(1.2f, 0.85f + (0.20f * dropIntensity));
            _bloomAlpha = MathF.Max(_bloomAlpha, MathF.Min(0.22f, 0.18f * dropIntensity));
            SpawnDropExplosion(packet.ViewportWidthDip, packet.ViewportHeightDip, packet.IsCompactCard, dropIntensity);
        }

        // Advance shockwave rings
        var writeRing = 0;
        for (var i = 0; i < _activeRings; i++)
        {
            var ring = _rings[i];
            ring.Age += dt;
            if (ring.Age < ring.DurationSec)
            {
                _rings[writeRing++] = ring;
            }
        }
        _activeRings = writeRing;

        // Advance velocity sparks with quadratic aerodynamic drag
        var drag = MathF.Exp(-dt * 2.15f);
        var writeSpark = 0;
        for (var i = 0; i < _activeSparks; i++)
        {
            var sp = _sparks[i];
            sp.Age += dt;
            if (sp.Age >= sp.LifeSec)
            {
                continue;
            }

            sp.PrevX = sp.X;
            sp.PrevY = sp.Y;
            sp.X += sp.Vx * dt;
            sp.Y += sp.Vy * dt;
            sp.Vx *= drag;
            sp.Vy *= drag;
            _sparks[writeSpark++] = sp;
        }
        _activeSparks = writeSpark;

        return _activeRings > 0 || _activeSparks > 0 || _lyricPunch > 0.002f || _bloomAlpha > 0.005f;
    }

    public float GetLyricZoomMultiplier(in FrameRenderPacket packet)
    {
        if (packet.ReducedMotion || !IsEnabled(in packet) || _lyricPunch <= 0.002f)
        {
            return 1.0f;
        }

        var dropIntensity = ResolveDropIntensity(in packet);
        var scaleBoost = 0.062f * _lyricPunch * dropIntensity;
        if (packet.IsCompactCard || packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            scaleBoost *= 0.45f;
        }

        return Math.Clamp(1.0f + scaleBoost, 1.0f, 1.08f);
    }

    public void RenderStageDrops(in StyleRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var packet = context.Packet;
        if (packet.ReducedMotion ||
            !IsEnabled(in packet) ||
            packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            return;
        }

        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        if (w <= 8f || h <= 8f)
        {
            return;
        }

        var dropIntensity = ResolveDropIntensity(in packet);
        var intensityFactor = packet.IsCompactCard ? dropIntensity * 0.55f : dropIntensity;
        var cx = w * 0.5f;
        var cy = h * 0.48f;
        var palette = packet.Palette;
        var canvas = context.Canvas;

        // 1. Photosensitive-safe spotlight surge glow (capped <= 22% alpha)
        if (_bloomAlpha > 0.006f)
        {
            var capped = Math.Clamp(_bloomAlpha * intensityFactor, 0f, 0.22f);
            var radius = MathF.Max(w, h) * 0.68f;
            _bloomColors[0] = palette.AccentColor1.ToSkColorWithAlpha(capped * 0.70f);
            _bloomColors[1] = palette.AccentColor2.ToSkColorWithAlpha(capped);
            _bloomColors[2] = palette.AccentColor2.ToSkColorWithAlpha(0f);

            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy),
                Math.Max(radius, 1f),
                _bloomColors,
                _bloomStops,
                SKShaderTileMode.Clamp);
            _bloomPaint.Shader = shader;
            _bloomPaint.Color = SKColors.White;
            try
            {
                canvas.DrawRect(new SKRect(0f, 0f, w, h), _bloomPaint);
            }
            finally
            {
                _bloomPaint.Shader = null;
            }
        }

        // 2. Expanding concentric acoustic shockwave rings (blur-free multi-pass glow)
        for (var i = 0; i < _activeRings; i++)
        {
            ref readonly var ring = ref _rings[i];
            var p = Math.Clamp(ring.Age / Math.Max(0.05f, ring.DurationSec), 0f, 1f);
            var eased = 1f - MathF.Pow(1f - p, 2.4f);
            var r = ring.StartRadius + ((ring.EndRadius - ring.StartRadius) * eased);
            var fade = MathF.Pow(1f - p, 1.5f);
            var color = PickPaletteColor(palette, ring.ColorSlot);

            _ringPaint.StrokeWidth = (packet.IsCompactCard ? 6f : 14f) * (1f - (p * 0.55f)) * MathF.Min(1.25f, intensityFactor);
            _ringPaint.Color = color.ToSkColorWithAlpha(0.20f * fade * intensityFactor);
            canvas.DrawCircle(cx, cy, r, _ringPaint);

            _ringPaint.StrokeWidth = (packet.IsCompactCard ? 1.5f : 2.8f) * MathF.Min(1.25f, intensityFactor);
            _ringPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(MathF.Round(205f * fade * MathF.Min(1.1f, intensityFactor)), 0f, 255f));
            canvas.DrawCircle(cx, cy, r, _ringPaint);
        }

        // 3. Radial supersonic spark burst
        for (var i = 0; i < _activeSparks; i++)
        {
            ref readonly var sp = ref _sparks[i];
            var p = Math.Clamp(sp.Age / Math.Max(0.05f, sp.LifeSec), 0f, 1f);
            var alpha = (1f - p) * (1f - p) * MathF.Min(1.2f, intensityFactor);
            if (alpha <= 0.02f)
            {
                continue;
            }

            var color = PickPaletteColor(palette, sp.ColorSlot);
            var tailDx = (sp.X - sp.PrevX) * 2.2f;
            var tailDy = (sp.Y - sp.PrevY) * 2.2f;

            _sparkPaint.StrokeWidth = sp.Width * 2.1f;
            _sparkPaint.Color = color.ToSkColorWithAlpha(0.36f * alpha);
            canvas.DrawLine(sp.X - tailDx, sp.Y - tailDy, sp.X, sp.Y, _sparkPaint);

            _sparkPaint.StrokeWidth = sp.Width;
            _sparkPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(MathF.Round(235f * alpha), 0f, 255f));
            canvas.DrawLine(sp.X - (tailDx * 0.45f), sp.Y - (tailDy * 0.45f), sp.X, sp.Y, _sparkPaint);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _bloomPaint.Dispose();
        _ringPaint.Dispose();
        _sparkPaint.Dispose();
    }

    private void SpawnDropExplosion(float width, float height, bool isCompact, float intensity)
    {
        var cx = width * 0.5f;
        var cy = height * 0.48f;
        var maxExtent = MathF.Sqrt((cx * cx) + (cy * cy)) * 1.08f;

        // Spawn 3 staggered concentric shockwave rings
        _activeRings = 0;
        for (var i = 0; i < 3 && _activeRings < MaxRings; i++)
        {
            _rings[_activeRings++] = new ShockwaveRing
            {
                Age = -i * 0.09f,
                DurationSec = 0.85f + (i * 0.24f),
                StartRadius = MathF.Min(width, height) * (0.04f + (i * 0.03f)),
                EndRadius = maxExtent * (0.88f + (i * 0.14f)),
                ColorSlot = (byte)i
            };
        }

        // Launch radial supersonic sparks (capped cleanly for compact cards)
        var sparkCount = isCompact ? 24 : MaxSparks;
        _activeSparks = 0;
        var baseSpeed = MathF.Max(width, height) * (0.58f + (0.28f * intensity));

        for (var i = 0; i < sparkCount && _activeSparks < MaxSparks; i++)
        {
            var angle = (MathF.PI * 2f * i / sparkCount) + ((NextFloat01() - 0.5f) * 0.22f);
            var speed = baseSpeed * (0.45f + (0.75f * NextFloat01()));
            var startOffset = MathF.Min(width, height) * (0.03f + (0.08f * NextFloat01()));
            var sx = cx + (MathF.Cos(angle) * startOffset);
            var sy = cy + (MathF.Sin(angle) * startOffset);

            _sparks[_activeSparks++] = new DropSpark
            {
                X = sx,
                Y = sy,
                PrevX = sx,
                PrevY = sy,
                Vx = MathF.Cos(angle) * speed,
                Vy = MathF.Sin(angle) * speed,
                Age = 0f,
                LifeSec = 1.15f + (1.15f * NextFloat01()),
                Width = isCompact ? 1.2f : (1.5f + (NextFloat01() * 1.4f)),
                ColorSlot = (byte)(i % 3)
            };
        }
    }

    private float NextFloat01()
    {
        var x = _rngState;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _rngState = x == 0 ? 0x9E3779B9u : x;
        return (_rngState & 0x00FFFFFFu) / 16777216f;
    }

    private static RgbColor PickPaletteColor(AlbumAccentPalette palette, byte slot) => slot switch
    {
        0 => palette.AccentColor1,
        1 => palette.AccentColor2,
        _ => palette.AccentColor3
    };
}
