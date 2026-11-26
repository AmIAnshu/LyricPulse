using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Arena Concert Drop Suite reacting to real-time FFT &amp; manual Beat Drop climaxes:
/// sweeping volumetric stadium light cones, heavy dual-pass bass shockwaves, perimeter edge
/// energy surges, 110 swirl-curled glowing sparks, photosensitive-safe spotlight swell, and
/// elastic scale slam + bass micro-tremor applied cleanly to lyrics.
/// Zero per-frame SKPaint/blur allocations; skipped cleanly on ReducedMotion and DesktopOverlay corners.
/// </summary>
public sealed class BeatDropEffectRenderer : IDisposable
{
    private const int MaxSparks = 110;
    private const int MaxRings = 4;
    private const int SpotlightConeCount = 12;

    private struct DropSpark
    {
        public float X;
        public float Y;
        public float PrevX;
        public float PrevY;
        public float Vx;
        public float Vy;
        public float SwirlRadPerSec;
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
    private readonly SKPath _conePath = new();
    private readonly SKColor[] _bloomColors = new SKColor[3];
    private readonly float[] _bloomStops = [0f, 0.44f, 1f];
    private readonly SKColor[] _beamColors = new SKColor[2];
    private readonly float[] _beamStops = [0f, 1f];

    private int _activeSparks;
    private int _activeRings;
    private float _edgeCooldownSec;
    private float _lyricPunch;
    private float _slamElapsedSec = 1.0f;
    private float _bloomAlpha;
    private float _beamSweepPhase;
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
        _slamElapsedSec = 1.0f;
        _bloomAlpha = 0f;
        _beamSweepPhase = 0f;
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
        _slamElapsedSec = MathF.Min(1.5f, _slamElapsedSec + dt);
        _beamSweepPhase = (_beamSweepPhase + (dt * (0.45f + (audio.ClimaxIntensity * 1.15f)))) % MathF.Tau;

        _lyricPunch *= MathF.Exp(-dt * 6.8f);
        if (_lyricPunch < 0.002f)
        {
            _lyricPunch = 0f;
        }

        _bloomAlpha *= MathF.Exp(-dt * 2.75f);
        if (audio.IsClimaxActive)
        {
            _bloomAlpha = MathF.Max(_bloomAlpha, MathF.Min(0.24f, audio.ClimaxIntensity * 0.20f * dropIntensity));
        }

        if (audio.DropTrigger >= 0.50f && _edgeCooldownSec <= 0f)
        {
            _edgeCooldownSec = 0.42f;
            _slamElapsedSec = 0f;
            _lyricPunch = MathF.Min(1.35f, 0.95f + (0.28f * dropIntensity));
            _bloomAlpha = MathF.Max(_bloomAlpha, MathF.Min(0.26f, 0.22f * dropIntensity));
            SpawnDropExplosion(packet.ViewportWidthDip, packet.ViewportHeightDip, packet.IsCompactCard, dropIntensity);
        }

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

        var drag = MathF.Exp(-dt * 1.85f);
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

            // Gentle rotational curl so sparks spiral like euphoric stadium embers
            if (MathF.Abs(sp.SwirlRadPerSec) > 0.001f)
            {
                var angleStep = sp.SwirlRadPerSec * dt;
                var c = MathF.Cos(angleStep);
                var s = MathF.Sin(angleStep);
                var rvx = (sp.Vx * c) - (sp.Vy * s);
                var rvy = (sp.Vx * s) + (sp.Vy * c);
                sp.Vx = rvx;
                sp.Vy = rvy;
            }

            sp.X += sp.Vx * dt;
            sp.Y += sp.Vy * dt;
            sp.Vx *= drag;
            sp.Vy *= drag;
            _sparks[writeSpark++] = sp;
        }
        _activeSparks = writeSpark;

        return _activeRings > 0 || _activeSparks > 0 || _lyricPunch > 0.002f || _bloomAlpha > 0.005f || audio.IsClimaxActive;
    }

    public float GetLyricZoomMultiplier(in FrameRenderPacket packet) =>
        GetLyricMotionTransform(in packet).Scale;

    public (float Scale, float OffsetX, float OffsetY) GetLyricMotionTransform(in FrameRenderPacket packet)
    {
        if (packet.ReducedMotion || !IsEnabled(in packet))
        {
            return (1.0f, 0f, 0f);
        }

        var dropIntensity = ResolveDropIntensity(in packet);
        var audio = packet.EffectiveAudioMetrics;
        var scaleBoost = (0.115f * _lyricPunch * dropIntensity) +
                         (audio.IsClimaxActive ? (audio.ClimaxIntensity * 0.025f * audio.BeatPulse * dropIntensity) : 0f);
        var offsetX = 0f;
        var offsetY = 0f;

        // Crisp concert bass tremor during the first 360ms of drop impact (StageCanvas only)
        if (_slamElapsedSec < 0.36f && packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas && !packet.IsCompactCard)
        {
            var env = MathF.Exp(-9.5f * _slamElapsedSec) * dropIntensity;
            offsetX = 4.2f * env * MathF.Sin(52f * _slamElapsedSec);
            offsetY = 3.2f * env * MathF.Cos(64f * _slamElapsedSec);
        }

        if (packet.IsCompactCard || packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            scaleBoost *= 0.42f;
        }

        var scale = Math.Clamp(1.0f + scaleBoost, 1.0f, 1.14f);
        return (scale, offsetX, offsetY);
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
        var audio = packet.EffectiveAudioMetrics;
        var cx = w * 0.5f;
        var cy = h * 0.48f;
        var palette = packet.Palette;
        var canvas = context.Canvas;

        // 1. Volumetric Arena Light Cones sweeping cleanly during Climax windows
        var climaxLevel = MathF.Max(audio.IsClimaxActive ? audio.ClimaxIntensity : 0f, _lyricPunch * 0.85f);
        if (!packet.IsCompactCard && climaxLevel > 0.05f)
        {
            DrawArenaLightCones(canvas, w, h, cx, cy, palette, climaxLevel * intensityFactor);
        }

        // 2. Photosensitive-safe spotlight surge bloom (capped <= 26% alpha)
        if (_bloomAlpha > 0.006f)
        {
            var capped = Math.Clamp(_bloomAlpha * intensityFactor, 0f, 0.26f);
            var radius = MathF.Max(w, h) * 0.74f;
            _bloomColors[0] = palette.AccentColor1.ToSkColorWithAlpha(capped * 0.78f);
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

        // 3. Expanding Dual-Layer Acoustic Shockwaves (outer plasma halo + white-hot wavefront)
        for (var i = 0; i < _activeRings; i++)
        {
            ref readonly var ring = ref _rings[i];
            if (ring.Age < 0f)
            {
                continue;
            }

            var p = Math.Clamp(ring.Age / Math.Max(0.05f, ring.DurationSec), 0f, 1f);
            var eased = 1f - MathF.Pow(1f - p, 2.3f);
            var r = ring.StartRadius + ((ring.EndRadius - ring.StartRadius) * eased);
            var fade = MathF.Pow(1f - p, 1.35f);
            var color = PickPaletteColor(palette, ring.ColorSlot);

            _ringPaint.StrokeWidth = (packet.IsCompactCard ? 8f : 22f) * (1f - (p * 0.5f)) * MathF.Min(1.3f, intensityFactor);
            _ringPaint.Color = color.ToSkColorWithAlpha(0.24f * fade * intensityFactor);
            canvas.DrawCircle(cx, cy, r, _ringPaint);

            _ringPaint.StrokeWidth = (packet.IsCompactCard ? 2.2f : 5.5f) * (1f - (p * 0.35f)) * MathF.Min(1.3f, intensityFactor);
            _ringPaint.Color = palette.AccentColor2.ToSkColorWithAlpha(0.55f * fade * intensityFactor);
            canvas.DrawCircle(cx, cy, r, _ringPaint);

            _ringPaint.StrokeWidth = (packet.IsCompactCard ? 1.4f : 2.6f) * MathF.Min(1.25f, intensityFactor);
            _ringPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(MathF.Round(235f * fade * MathF.Min(1.15f, intensityFactor)), 0f, 255f));
            canvas.DrawCircle(cx, cy, r, _ringPaint);
        }

        // 4. Radial Supersonic Swirling Ember Sparks
        for (var i = 0; i < _activeSparks; i++)
        {
            ref readonly var sp = ref _sparks[i];
            var p = Math.Clamp(sp.Age / Math.Max(0.05f, sp.LifeSec), 0f, 1f);
            var alpha = (1f - p) * (1f - p) * MathF.Min(1.3f, intensityFactor);
            if (alpha <= 0.02f)
            {
                continue;
            }

            var color = PickPaletteColor(palette, sp.ColorSlot);
            var tailDx = (sp.X - sp.PrevX) * 3.1f;
            var tailDy = (sp.Y - sp.PrevY) * 3.1f;

            _sparkPaint.StrokeWidth = sp.Width * 2.4f;
            _sparkPaint.Color = color.ToSkColorWithAlpha(0.45f * alpha);
            canvas.DrawLine(sp.X - tailDx, sp.Y - tailDy, sp.X, sp.Y, _sparkPaint);

            _sparkPaint.StrokeWidth = sp.Width;
            _sparkPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(MathF.Round(245f * alpha), 0f, 255f));
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
        _conePath.Dispose();
    }

    private void DrawArenaLightCones(
        SKCanvas canvas,
        float w,
        float h,
        float cx,
        float cy,
        AlbumAccentPalette palette,
        float strength)
    {
        var beamLen = MathF.Sqrt((w * w) + (h * h)) * 0.65f;
        var coneSpreadRad = 0.085f + (0.045f * MathF.Min(1.2f, strength));
        var peakAlpha = Math.Clamp(0.11f * strength, 0f, 0.20f);

        for (var i = 0; i < SpotlightConeCount; i++)
        {
            var dirSign = (i % 2 == 0) ? 1f : -1f;
            var baseAngle = (MathF.Tau * i / SpotlightConeCount) + (_beamSweepPhase * 0.45f * dirSign);
            var a1 = baseAngle - coneSpreadRad;
            var a2 = baseAngle + coneSpreadRad;

            var x1 = cx + (MathF.Cos(a1) * beamLen);
            var y1 = cy + (MathF.Sin(a1) * beamLen);
            var x2 = cx + (MathF.Cos(a2) * beamLen);
            var y2 = cy + (MathF.Sin(a2) * beamLen);
            var midX = cx + (MathF.Cos(baseAngle) * beamLen);
            var midY = cy + (MathF.Sin(baseAngle) * beamLen);

            var color = PickPaletteColor(palette, (byte)(i % 3));
            _beamColors[0] = color.ToSkColorWithAlpha(peakAlpha);
            _beamColors[1] = color.ToSkColorWithAlpha(0f);

            using var coneShader = SKShader.CreateLinearGradient(
                new SKPoint(cx, cy),
                new SKPoint(midX, midY),
                _beamColors,
                _beamStops,
                SKShaderTileMode.Clamp);

            _conePath.Rewind();
            _conePath.MoveTo(cx, cy);
            _conePath.LineTo(x1, y1);
            _conePath.LineTo(x2, y2);
            _conePath.Close();

            _bloomPaint.Shader = coneShader;
            _bloomPaint.Color = SKColors.White;
            try
            {
                canvas.DrawPath(_conePath, _bloomPaint);
            }
            finally
            {
                _bloomPaint.Shader = null;
            }
        }
    }

    private void SpawnDropExplosion(float width, float height, bool isCompact, float intensity)
    {
        var cx = width * 0.5f;
        var cy = height * 0.48f;
        var maxExtent = MathF.Sqrt((cx * cx) + (cy * cy)) * 1.15f;

        _activeRings = 0;
        for (var i = 0; i < 4 && _activeRings < MaxRings; i++)
        {
            _rings[_activeRings++] = new ShockwaveRing
            {
                Age = -i * 0.08f,
                DurationSec = 0.88f + (i * 0.22f),
                StartRadius = MathF.Min(width, height) * (0.03f + (i * 0.025f)),
                EndRadius = maxExtent * (0.85f + (i * 0.14f)),
                ColorSlot = (byte)(i % 3)
            };
        }

        var sparkCount = isCompact ? 30 : MaxSparks;
        _activeSparks = 0;
        var baseSpeed = MathF.Max(width, height) * (0.68f + (0.34f * intensity));

        for (var i = 0; i < sparkCount && _activeSparks < MaxSparks; i++)
        {
            var angle = (MathF.Tau * i / sparkCount) + ((NextFloat01() - 0.5f) * 0.24f);
            var speed = baseSpeed * (0.42f + (0.88f * NextFloat01()));
            var startOffset = MathF.Min(width, height) * (0.02f + (0.07f * NextFloat01()));
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
                SwirlRadPerSec = (NextFloat01() - 0.5f) * 1.15f,
                Age = 0f,
                LifeSec = 1.25f + (1.25f * NextFloat01()),
                Width = isCompact ? 1.3f : (1.7f + (NextFloat01() * 1.7f)),
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
