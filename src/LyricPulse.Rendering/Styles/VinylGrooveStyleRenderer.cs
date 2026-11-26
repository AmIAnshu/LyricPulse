using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #11 — Vinyl Groove: Tactile Hi-Fi turntable platter spinning smoothly with playback,
/// oversized 0.50R center song album cover (decoded safely across JPEG/WebP/PNG or rich procedural
/// studio sleeve in Demo/fallback mode), anisotropic album-tinted specular grooves, song-progress
/// pivoting tonearm with luminous stylus spark, beat strobe rim, and 3D-tilted extruded kinetic
/// typography stage right beside the platter.
/// </summary>
public sealed class VinylGrooveStyleRenderer : IKineticStyleRenderer
{
    private const int GrooveRingCount = 11;
    private const int StrobeDotCount = 36;
    private const float SpinDegreesPerSecond = 44f;
    private const float PerspectiveFocalLength = 1050f;

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly ReactiveVisualIconRenderer _moodIcons = new();
    private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _glowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath _armPath = new();
    private readonly SKPath _labelClipPath = new();
    private readonly SKPath _sheenPath = new();
    private readonly SKColor[] _sheenColors = new SKColor[5];
    private readonly float[] _sheenStops = [0.00f, 0.25f, 0.50f, 0.75f, 1.00f];
    private readonly SKColor[] _radialColors = new SKColor[3];
    private readonly float[] _radialStops = [0.00f, 0.48f, 1.00f];
    private readonly List<WrappedWordRow> _rows = new(4);

    private SpringState _wordScale = SpringState.AtRest(1.14f);
    private SpringState _tonearmProgress = SpringState.AtRest(0.15f);
    private SpringState _spinRate = SpringState.AtRest(1.0f);
    private SpringState _lyricYawDeg = SpringState.AtRest(-7.5f);
    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private float _lastWordDurationSec = 0.32f;
    private float _spinAngleDeg = 18f;
    private float _smoothedBass;
    private float _smoothedBeat;
    private float _beatKick;
    private bool _hasArmInit;
    private string? _cachedLabelHash;
    private SKImage? _cachedLabelImage;
    private bool _disposed;

    private sealed class WrappedWordRow
    {
        public readonly List<(int Index, LyricWord Word, float Width)> Items = new(8);
        public float TotalWidth;
    }

    public OverlayStyleId StyleId => OverlayStyleId.VinylGroove;

    public void ResetState()
    {
        _wordScale = SpringState.AtRest(1.14f);
        _tonearmProgress = SpringState.AtRest(0.15f);
        _spinRate = SpringState.AtRest(1.0f);
        _lyricYawDeg = SpringState.AtRest(-7.5f);
        _lastWordKey = (-1, -1);
        _lastWordDurationSec = 0.32f;
        _smoothedBass = 0f;
        _smoothedBeat = 0f;
        _beatKick = 0f;
        _hasArmInit = false;
        _aesthetics.ResetDot();
    }

    public bool Advance(in FrameRenderPacket packet, in LifecycleFrameState lifecycle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var dt = Math.Clamp(packet.DeltaSeconds, 0f, SpringState.MaxDeltaSeconds);
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        var (_, activeWord, _, wordKey, wordDurationSec) = StyleLayoutHelper.ResolveWordTriad(slice);
        var audio = packet.EffectiveAudioMetrics;
        var isPlaying = (packet.PlaybackSnapshot?.IsPlaying ?? true)
                        && packet.SceneSnapshot.PlaybackState != MediaPlaybackState.Paused
                        && packet.SceneSnapshot.PlaybackState != MediaPlaybackState.Stopped;

        var bassTarget = packet.ReducedMotion ? 0f : audio.BassEnergy;
        var beatTarget = packet.ReducedMotion ? 0f : audio.BeatPulse;
        _smoothedBass += (bassTarget - _smoothedBass) * Math.Clamp(dt * 12f, 0.1f, 0.65f);
        _smoothedBeat += (beatTarget - _smoothedBeat) * Math.Clamp(dt * 16f, 0.1f, 0.75f);
        _beatKick = packet.ReducedMotion
            ? 0f
            : Math.Max(0f, _beatKick - (dt * 3.6f));

        if (wordKey.LineIndex >= 0 && wordKey != _lastWordKey)
        {
            _lastWordKey = wordKey;
            _lastWordDurationSec = wordDurationSec;
            _beatKick = packet.ReducedMotion ? 0f : Math.Clamp(0.45f + (audio.BeatPulse * 0.45f) + (audio.ClimaxIntensity * 0.40f), 0f, 1f);
            if (!packet.ReducedMotion)
            {
                _wordScale = new SpringState(Value: 0.92f, Velocity: 2.15f * packet.SafePartyEnergy, Target: 1.15f)
                    .Step(SpringTokens.WordSnap.WithTempoCompression(_lastWordDurationSec), dt);
            }
            else
            {
                _wordScale = SpringState.AtRest(1.09f);
            }
        }
        else if (!packet.ReducedMotion)
        {
            _wordScale = _wordScale.WithTarget(1.15f).Step(SpringTokens.WordSnap.WithTempoCompression(_lastWordDurationSec), dt);
        }

        // Smooth 3D yaw sweep across active word progression inside the right typography plane
        var wordCount = slice.CurrentLine?.Words.IsEmpty == false ? slice.CurrentLine.Words.Length : 1;
        var wordFrac = wordCount <= 1 ? 0.5f : Math.Clamp(slice.ActiveWordIndex / (float)(wordCount - 1), 0f, 1f);
        var targetYaw = packet.ReducedMotion
            ? 0f
            : Math.Clamp((-9.5f + (wordFrac * 13.0f)) * Math.Clamp(packet.SafePartyEnergy, 0.75f, 1.3f), -11f, 8f);
        _lyricYawDeg = packet.ReducedMotion
            ? SpringState.AtRest(0f)
            : _lyricYawDeg.WithTarget(targetYaw).Step(SpringTokens.CameraDrift, dt);

        // Smooth platter rotation inertia
        var targetSpin = (isPlaying && !packet.ReducedMotion) ? 1.0f : 0.0f;
        _spinRate = packet.ReducedMotion
            ? SpringState.AtRest(0f)
            : _spinRate.WithTarget(targetSpin).Step(SpringTokens.CameraDrift, dt);
        if (!packet.ReducedMotion && _spinRate.Value > 0.005f)
        {
            var climaxSpeedBonus = (audio.IsClimaxActive && BeatDropEffectRenderer.IsEnabled(in packet))
                ? audio.ClimaxIntensity * 0.34f
                : 0f;
            _spinAngleDeg = (_spinAngleDeg + (dt * SpinDegreesPerSecond * (_spinRate.Value + climaxSpeedBonus))) % 360f;
        }

        // Tonearm tracking progress across playable groove band (0..1)
        var targetTrackProgress = ComputeNormalizedSongProgress(packet, slice);
        if (!_hasArmInit || packet.ReducedMotion)
        {
            _tonearmProgress = SpringState.AtRest(targetTrackProgress);
            _hasArmInit = true;
        }
        else
        {
            _tonearmProgress = _tonearmProgress.WithTarget(targetTrackProgress).Step(SpringTokens.CameraDrift, dt);
        }

        if (activeWord is not null)
        {
            var geom = ComputeTurntableGeometry(packet);
            _aesthetics.AdvanceDot(wordKey, geom.LyricMidX, geom.LyricCy + (geom.PlatterRadius * 0.25f), dt, packet.ReducedMotion, _lastWordDurationSec);
        }

        UpdateCachedLabelImage(packet.PlaybackSnapshot?.AlbumArt);

        return !packet.ReducedMotion && (isPlaying || !_wordScale.IsAtRest || !_tonearmProgress.IsAtRest || !_lyricYawDeg.IsAtRest || _spinRate.Value > 0.01f);
    }

    public void Render(in StyleRenderContext context, in LifecycleFrameState lifecycle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var packet = context.Packet;
        UpdateCachedLabelImage(packet.PlaybackSnapshot?.AlbumArt);

        var slice = StyleLayoutHelper.ResolveSlice(packet);
        if (slice.CurrentLine is null)
        {
            return;
        }

        var canvas = context.Canvas;
        var geom = ComputeTurntableGeometry(packet);
        var fontId = packet.ActiveFontId;
        var fontSize = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: packet.IsCompactCard ? 0.56f : 0.68f);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);

        // Balanced floating scrim strictly around turntable + lyric cluster (leaves DesktopOverlay 4 corners alpha == 0)
        var scrimLeft = Math.Max(8f, geom.Cx - (geom.PlatterRadius * 1.18f));
        var scrimRight = Math.Min(packet.ViewportWidthDip - 8f, geom.LyricRight + 16f);
        var scrimTop = Math.Max(8f, geom.Cy - (geom.PlatterRadius * 1.12f));
        var scrimBottom = Math.Min(packet.ViewportHeightDip - 8f, geom.Cy + (geom.PlatterRadius * 1.12f));
        if (packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            scrimLeft = Math.Max(22f, scrimLeft);
            scrimRight = Math.Min(packet.ViewportWidthDip - 22f, scrimRight);
            scrimTop = Math.Max(18f, scrimTop);
            scrimBottom = Math.Min(packet.ViewportHeightDip - 18f, scrimBottom);
        }

        context.Legibility.DrawAmbientScrim(
            canvas,
            new SKRect(scrimLeft, scrimTop, Math.Max(scrimLeft + 10f, scrimRight), Math.Max(scrimTop + 10f, scrimBottom)),
            fontSize,
            packet.Palette.ScrimTint,
            packet.ScrimIntensity,
            packet.HighContrastScrim);

        var audio = packet.EffectiveAudioMetrics;
        var climaxGlow = (!packet.ReducedMotion && audio.IsClimaxActive && BeatDropEffectRenderer.IsEnabled(in packet))
            ? audio.ClimaxIntensity * 0.55f * BeatDropEffectRenderer.ResolveDropIntensity(in packet)
            : 0f;

        // 1. Turntable LP Disc, Strobe Rim, Oversized Song Album Cover Center, and Tracking Tonearm
        var progress = Math.Clamp(_tonearmProgress.Value, 0f, 1f);
        DrawTurntablePlatter(canvas, in context, geom, progress, climaxGlow);
        var (needleX, needleY) = DrawTonearmAndStylus(canvas, packet, geom, progress, climaxGlow);

        // 2. Luminous Stylus-to-Lyric Horizon Light Beam on StageCanvas
        if (geom.IsFullStage && !packet.ReducedMotion)
        {
            DrawStylusBridgeThread(canvas, packet.Palette, needleX, needleY, geom, climaxGlow);
        }

        // 3. Right-Column 3D Perspective Kinetic Typography Stage beside the Vinyl Player
        DrawRightColumn3DLyricStage(in context, font, fontId, slice, geom, fontSize, climaxGlow);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cachedLabelImage?.Dispose();
        _cachedLabelImage = null;
        _aesthetics.Dispose();
        _moodIcons.Dispose();
        _fillPaint.Dispose();
        _strokePaint.Dispose();
        _glowPaint.Dispose();
        _armPath.Dispose();
        _labelClipPath.Dispose();
        _sheenPath.Dispose();
    }

    private void DrawTurntablePlatter(
        SKCanvas canvas,
        in StyleRenderContext context,
        TurntableGeometry geom,
        float trackProgress,
        float climaxGlow)
    {
        var packet = context.Packet;
        var palette = packet.Palette;
        var r = geom.PlatterRadius;
        var isFullStage = geom.IsFullStage;

        // Warm stage backlight halo behind turntable platter (StageCanvas / GalleryCard only)
        if (packet.SurfaceMode != StageRenderSurfaceMode.DesktopOverlay)
        {
            var haloR = r * (1.30f + (_smoothedBass * 0.16f) + (climaxGlow * 0.28f));
            var haloAlpha = Math.Clamp(0.18f + (_smoothedBass * 0.14f) + (climaxGlow * 0.24f), 0f, 0.56f);
            _radialColors[0] = palette.AccentColor2.ToSkColorWithAlpha(haloAlpha);
            _radialColors[1] = palette.AccentColor3.ToSkColorWithAlpha(haloAlpha * 0.38f);
            _radialColors[2] = palette.AccentColor3.ToSkColorWithAlpha(0f);

            using var haloShader = SKShader.CreateRadialGradient(
                new SKPoint(geom.Cx, geom.Cy),
                Math.Max(haloR, 2f),
                _radialColors,
                _radialStops,
                SKShaderTileMode.Clamp);
            _glowPaint.Shader = haloShader;
            _glowPaint.Color = SKColors.White;
            try
            {
                canvas.DrawCircle(geom.Cx, geom.Cy, haloR, _glowPaint);
            }
            finally
            {
                _glowPaint.Shader = null;
            }
        }

        // Outer turntable metallic plinth bezel
        var plinthR = r * 1.065f;
        _fillPaint.Shader = null;
        _fillPaint.Color = new SKColor(0x16, 0x19, 0x26, 238);
        canvas.DrawCircle(geom.Cx, geom.Cy, plinthR, _fillPaint);

        _strokePaint.StrokeWidth = Math.Max(1.0f, r * 0.018f);
        _strokePaint.Color = palette.AccentColor1.ToSkColorWithAlpha(0.30f + (_beatKick * 0.28f) + (climaxGlow * 0.34f));
        canvas.DrawCircle(geom.Cx, geom.Cy, plinthR, _strokePaint);

        // Strobe perimeter timing dots on StageCanvas
        if (isFullStage)
        {
            var strobeR = r * 1.032f;
            var dotBaseAlpha = 0.24f + (_smoothedBeat * 0.45f) + (climaxGlow * 0.40f);
            var phaseOffset = packet.ReducedMotion ? 0f : (_spinAngleDeg * MathF.PI / 180f);
            for (var i = 0; i < StrobeDotCount; i++)
            {
                var ang = (MathF.Tau * i / StrobeDotCount) + (phaseOffset * 0.25f);
                var pulse = (i % 3 == 0) ? Math.Clamp(dotBaseAlpha, 0f, 0.96f) : 0.18f;
                _fillPaint.Color = (i % 3 == 0 ? palette.AccentColor2 : palette.AccentColor3).ToSkColorWithAlpha(pulse);
                canvas.DrawCircle(
                    geom.Cx + (MathF.Cos(ang) * strobeR),
                    geom.Cy + (MathF.Sin(ang) * strobeR),
                    Math.Max(1.1f, r * 0.011f),
                    _fillPaint);
            }
        }

        // Deep carbon pressed vinyl disc
        _radialColors[0] = new SKColor(0x18, 0x1B, 0x28, 252);
        _radialColors[1] = new SKColor(0x0D, 0x0F, 0x17, 254);
        _radialColors[2] = new SKColor(0x07, 0x08, 0x0D, 255);
        using (var discGrad = SKShader.CreateRadialGradient(
                   new SKPoint(geom.Cx, geom.Cy),
                   Math.Max(r, 2f),
                   _radialColors,
                   _radialStops,
                   SKShaderTileMode.Clamp))
        {
            _fillPaint.Shader = discGrad;
            _fillPaint.Color = SKColors.White;
            try
            {
                canvas.DrawCircle(geom.Cx, geom.Cy, r, _fillPaint);
            }
            finally
            {
                _fillPaint.Shader = null;
            }
        }

        // Anisotropic conical studio light sheen across the black vinyl grooves
        var sheenAlpha = Math.Clamp(0.18f + (_beatKick * 0.14f) + (climaxGlow * 0.22f), 0f, 0.52f);
        _sheenColors[0] = palette.AccentColor1.ToSkColorWithAlpha(0f);
        _sheenColors[1] = palette.AccentColor1.ToSkColorWithAlpha(sheenAlpha);
        _sheenColors[2] = palette.AccentColor2.ToSkColorWithAlpha(0f);
        _sheenColors[3] = palette.AccentColor2.ToSkColorWithAlpha(sheenAlpha * 0.85f);
        _sheenColors[4] = palette.AccentColor1.ToSkColorWithAlpha(0f);
        var sheenMatrix = SKMatrix.CreateRotation(((_spinAngleDeg * 0.18f) - 28f) * (MathF.PI / 180f), geom.Cx, geom.Cy);
        using (var sheenShader = SKShader.CreateSweepGradient(
                   new SKPoint(geom.Cx, geom.Cy),
                   _sheenColors,
                   _sheenStops,
                   sheenMatrix))
        {
            _fillPaint.Shader = sheenShader;
            _fillPaint.Color = SKColors.White;
            try
            {
                canvas.DrawCircle(geom.Cx, geom.Cy, r * 0.97f, _fillPaint);
            }
            finally
            {
                _fillPaint.Shader = null;
            }
        }

        // 11 concentric micro-grooves outside center cover (0.54R..0.95R)
        var grooveOuter = r * 0.95f;
        var grooveInner = r * 0.54f;
        var activeGrooveR = grooveOuter - ((grooveOuter - grooveInner) * trackProgress);
        for (var i = 0; i < GrooveRingCount; i++)
        {
            var t = i / (float)(GrooveRingCount - 1);
            var gr = grooveInner + ((grooveOuter - grooveInner) * t);
            var distToStylus = MathF.Abs(gr - activeGrooveR);
            var isHotGroove = distToStylus < ((grooveOuter - grooveInner) / (GrooveRingCount * 0.85f));
            _strokePaint.StrokeWidth = isHotGroove ? Math.Max(1.3f, r * 0.014f) : Math.Max(0.75f, r * 0.008f);
            _strokePaint.Color = isHotGroove
                ? palette.AccentColor2.ToSkColorWithAlpha(0.62f + (climaxGlow * 0.35f))
                : SKColors.White.WithAlpha((byte)(isFullStage ? 28 : 18));
            canvas.DrawCircle(geom.Cx, geom.Cy, gr, _strokePaint);
        }

        // Oversized 0.50R Spinning Song Album Cover Picture-Disc Center
        var coverR = r * 0.50f;
        var spinSave = canvas.Save();
        try
        {
            canvas.RotateDegrees(_spinAngleDeg, geom.Cx, geom.Cy);
            _labelClipPath.Rewind();
            _labelClipPath.AddCircle(geom.Cx, geom.Cy, coverR);
            canvas.ClipPath(_labelClipPath, antialias: true);

            if (_cachedLabelImage is not null)
            {
                var dstRect = new SKRect(geom.Cx - coverR, geom.Cy - coverR, geom.Cx + coverR, geom.Cy + coverR);
                _fillPaint.Shader = null;
                _fillPaint.Color = SKColors.White;
                canvas.DrawImage(_cachedLabelImage, dstRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), _fillPaint);
            }
            else
            {
                DrawProceduralAlbumSleeveCover(canvas, in context, geom, coverR);
            }
        }
        finally
        {
            canvas.RestoreToCount(spinSave);
        }

        // Glossy glass sheen highlight over stationary upper-left cover arc
        _sheenPath.Rewind();
        _sheenPath.AddArc(new SKRect(geom.Cx - coverR * 0.92f, geom.Cy - coverR * 0.92f, geom.Cx + coverR * 0.92f, geom.Cy + coverR * 0.92f), 198f, 118f);
        _strokePaint.StrokeWidth = Math.Max(2.2f, r * 0.045f);
        _strokePaint.Color = SKColors.White.WithAlpha(34);
        canvas.DrawPath(_sheenPath, _strokePaint);

        // Luminous foil collar bezel around cover + subtle minimalist center spindle
        _strokePaint.StrokeWidth = Math.Max(1.4f, r * 0.018f);
        _strokePaint.Color = palette.AccentColor1.ToSkColorWithAlpha(0.55f + (climaxGlow * 0.35f));
        canvas.DrawCircle(geom.Cx, geom.Cy, coverR, _strokePaint);

        _fillPaint.Shader = null;
        _fillPaint.Color = new SKColor(0xE2, 0xE8, 0xF0, 230);
        canvas.DrawCircle(geom.Cx, geom.Cy, Math.Max(2.4f, r * 0.036f), _fillPaint);
        _fillPaint.Color = new SKColor(0x0B, 0x0C, 0x16, 255);
        canvas.DrawCircle(geom.Cx, geom.Cy, Math.Max(1.3f, r * 0.018f), _fillPaint);
    }

    private void DrawProceduralAlbumSleeveCover(
        SKCanvas canvas,
        in StyleRenderContext context,
        TurntableGeometry geom,
        float coverR)
    {
        var packet = context.Packet;
        var palette = packet.Palette;
        var coverBounds = new SKRect(geom.Cx - coverR, geom.Cy - coverR, geom.Cx + coverR, geom.Cy + coverR);

        // Multi-stop diagonal album-cover backdrop (synth-sleeve aesthetic when art missing/Demo)
        _radialColors[0] = palette.AccentColor1.ToSkColorWithAlpha(0.95f);
        _radialColors[1] = palette.AccentColor2.ToSkColorWithAlpha(0.92f);
        _radialColors[2] = StyleLayoutHelper.BlendSrgb(palette.AccentColor3, new RgbColor(0x0B, 0x0C, 0x16), 0.45f).ToSkColorWithAlpha(0.98f);
        using (var sleeveGrad = SKShader.CreateLinearGradient(
                   new SKPoint(coverBounds.Left, coverBounds.Top),
                   new SKPoint(coverBounds.Right, coverBounds.Bottom),
                   _radialColors,
                   _radialStops,
                   SKShaderTileMode.Clamp))
        {
            _fillPaint.Shader = sleeveGrad;
            _fillPaint.Color = SKColors.White;
            try
            {
                canvas.DrawRect(coverBounds, _fillPaint);
            }
            finally
            {
                _fillPaint.Shader = null;
            }
        }

        // Horizon sun orb & lower reflection bands inside procedural album cover
        var horizonY = geom.Cy + (coverR * 0.14f);
        _fillPaint.Color = palette.AccentColor1.ToSkColorWithAlpha(0.28f);
        canvas.DrawCircle(geom.Cx, geom.Cy - (coverR * 0.12f), coverR * 0.44f, _fillPaint);
        _fillPaint.Color = new SKColor(0x0A, 0x0C, 0x16, 175);
        canvas.DrawRect(new SKRect(coverBounds.Left, horizonY, coverBounds.Right, coverBounds.Bottom), _fillPaint);

        _strokePaint.StrokeWidth = Math.Max(0.9f, coverR * 0.018f);
        _strokePaint.Color = palette.AccentColor2.ToSkColorWithAlpha(0.60f);
        canvas.DrawLine(coverBounds.Left, horizonY, coverBounds.Right, horizonY, _strokePaint);

        // Ambient TrackMoodVisual emblem above horizon line
        if (packet.PlaybackSnapshot?.TrackMoodVisual is { } mood && coverR >= 16f)
        {
            var emblemSize = Math.Clamp(coverR * 0.48f, 12f, 44f);
            _moodIcons.DrawVisual(
                canvas,
                in context,
                mood,
                geom.Cx,
                geom.Cy - (coverR * 0.24f),
                emblemSize,
                opacity: 0.94f,
                isActive: true);
        }

        // Crisp track title strip stamped onto bottom half of album sleeve
        var rawTitle = packet.PlaybackSnapshot?.Track?.RawTitle;
        if (!string.IsNullOrWhiteSpace(rawTitle) && coverR >= 26f)
        {
            var sleeveFont = context.Fonts.GetOrCreateFont(LyricFontId.Jost, Math.Clamp(coverR * 0.19f, 8.5f, 17f), enforceMinOverlayFloor: false);
            var labelText = rawTitle.ToUpperInvariant();
            var measured = TextShaperService.MeasureWidth(labelText, sleeveFont, context.Fonts);
            var fit = TextShaperService.ComputeAutoFitScale(measured, coverR * 1.48f, 1.0f);
            var textSave = canvas.Save();
            try
            {
                canvas.Translate(geom.Cx, horizonY + (coverR * 0.34f));
                canvas.Scale(fit, fit);
                _fillPaint.Color = SKColors.White.WithAlpha(225);
                canvas.DrawText(labelText, 0f, 0f, SKTextAlign.Center, sleeveFont, _fillPaint);
            }
            finally
            {
                canvas.RestoreToCount(textSave);
            }
        }
    }

    private (float NeedleX, float NeedleY) DrawTonearmAndStylus(
        SKCanvas canvas,
        FrameRenderPacket packet,
        TurntableGeometry geom,
        float trackProgress,
        float climaxGlow)
    {
        var palette = packet.Palette;
        var r = geom.PlatterRadius;

        var pivotX = Math.Clamp(geom.Cx + (r * 0.96f), 14f, packet.ViewportWidthDip - 14f);
        var pivotY = Math.Clamp(geom.Cy - (r * 0.84f), 14f, packet.ViewportHeightDip - 14f);

        // Stylus rides across outer vinyl groove band (0.94R -> 0.55R) cleanly outside 0.50R center cover
        var grooveRadius = r * (0.94f - (0.39f * trackProgress));
        var landingAngle = (-34f + (24f * trackProgress)) * (MathF.PI / 180f);
        var needleX = geom.Cx + (MathF.Cos(landingAngle) * grooveRadius);
        var needleY = geom.Cy + (MathF.Sin(landingAngle) * grooveRadius);

        var gimbalR = Math.Clamp(r * 0.125f, 7f, 24f);
        _fillPaint.Shader = null;
        _fillPaint.Color = new SKColor(0x23, 0x27, 0x38, 245);
        canvas.DrawCircle(pivotX, pivotY, gimbalR, _fillPaint);
        _strokePaint.StrokeWidth = Math.Max(1.2f, r * 0.015f);
        _strokePaint.Color = palette.AccentColor1.ToSkColorWithAlpha(0.55f);
        canvas.DrawCircle(pivotX, pivotY, gimbalR, _strokePaint);

        var dx = needleX - pivotX;
        var dy = needleY - pivotY;
        var len = Math.Max(1f, MathF.Sqrt((dx * dx) + (dy * dy)));
        var ux = dx / len;
        var uy = dy / len;
        var perpX = -uy;
        var perpY = ux;

        var tailX = pivotX - (ux * gimbalR * 1.45f);
        var tailY = pivotY - (uy * gimbalR * 1.45f);
        _strokePaint.StrokeWidth = Math.Max(3.0f, r * 0.062f);
        _strokePaint.Color = new SKColor(0x64, 0x74, 0x8B, 235);
        canvas.DrawLine(pivotX, pivotY, tailX, tailY, _strokePaint);

        var midX = ((pivotX + needleX) * 0.5f) + (perpX * r * 0.14f);
        var midY = ((pivotY + needleY) * 0.5f) + (perpY * r * 0.14f);
        _armPath.Rewind();
        _armPath.MoveTo(pivotX, pivotY);
        _armPath.QuadTo(midX, midY, needleX, needleY);

        _strokePaint.StrokeWidth = Math.Clamp(r * 0.048f, 2.2f, 8.5f);
        _strokePaint.Color = new SKColor(0x0B, 0x0C, 0x16, 170);
        canvas.DrawPath(_armPath, _strokePaint);

        _strokePaint.StrokeWidth = Math.Clamp(r * 0.028f, 1.5f, 5.2f);
        _strokePaint.Color = new SKColor(0xE2, 0xE8, 0xF0, 235);
        canvas.DrawPath(_armPath, _strokePaint);

        var cartStartX = needleX - (ux * r * 0.14f);
        var cartStartY = needleY - (uy * r * 0.14f);
        _strokePaint.StrokeWidth = Math.Clamp(r * 0.052f, 2.6f, 9.2f);
        _strokePaint.Color = palette.AccentColor2.ToSkColorWithAlpha(0.94f);
        canvas.DrawLine(cartStartX, cartStartY, needleX, needleY, _strokePaint);

        var sparkAlpha = Math.Clamp(0.45f + (_beatKick * 0.35f) + (climaxGlow * 0.45f), 0.35f, 1.0f);
        var sparkR = Math.Max(3.2f, r * (0.048f + (climaxGlow * 0.036f)));
        _fillPaint.Color = palette.AccentColor2.ToSkColorWithAlpha(0.30f * sparkAlpha);
        canvas.DrawCircle(needleX, needleY, sparkR * 2.4f, _fillPaint);
        _fillPaint.Color = palette.AccentColor1.ToSkColorWithAlpha(0.74f * sparkAlpha);
        canvas.DrawCircle(needleX, needleY, sparkR * 1.25f, _fillPaint);
        _fillPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(MathF.Round(245f * sparkAlpha), 0f, 255f));
        canvas.DrawCircle(needleX, needleY, sparkR * 0.54f, _fillPaint);

        return (needleX, needleY);
    }

    private void DrawStylusBridgeThread(
        SKCanvas canvas,
        AlbumAccentPalette palette,
        float needleX,
        float needleY,
        TurntableGeometry geom,
        float climaxGlow)
    {
        var targetX = Math.Max(needleX + 12f, geom.LyricLeft - 14f);
        var alpha = Math.Clamp(0.24f + (_beatKick * 0.25f) + (climaxGlow * 0.36f), 0.18f, 0.78f);
        _strokePaint.StrokeWidth = 1.2f + (climaxGlow * 1.4f);
        _strokePaint.Color = palette.AccentColor2.ToSkColorWithAlpha(alpha);
        canvas.DrawLine(needleX, needleY, targetX, geom.LyricCy, _strokePaint);
    }

    private void DrawRightColumn3DLyricStage(
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        ActiveLyricSlice slice,
        TurntableGeometry geom,
        float fontSize,
        float climaxGlow)
    {
        var packet = context.Packet;
        var canvas = context.Canvas;
        var line = slice.CurrentLine!;
        var words = line.Words;
        var align = SKTextAlign.Center;

        // Monumental active-word 3D watermark & micro LP header behind right column on StageCanvas
        if (geom.IsFullStage)
        {
            var watermarkText = !words.IsEmpty
                ? words[Math.Clamp(slice.ActiveWordIndex, 0, words.Length - 1)].Text.ToUpperInvariant()
                : line.RawText.ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(watermarkText))
            {
                var wmScale = 2.05f + (_smoothedBeat * 0.16f) + (climaxGlow * 0.14f);
                var wmMeasured = TextShaperService.MeasureWidth(watermarkText, font, context.Fonts);
                var wmFit = TextShaperService.ComputeAutoFitScale(wmMeasured * wmScale, geom.LyricWidth * 0.96f, 1.0f);
                var wmAlpha = Math.Clamp(0.06f + (_smoothedBeat * 0.045f) + (climaxGlow * 0.06f), 0.05f, 0.18f);

                var wmSave = canvas.Save();
                try
                {
                    if (!packet.ReducedMotion)
                    {
                        var back3D = KineticWallStyleRenderer.Create3DPerspectiveMatrix(
                            3.5f,
                            _lyricYawDeg.Value * 0.45f,
                            0f,
                            PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f),
                            geom.LyricMidX,
                            geom.LyricCy);
                        canvas.Concat(in back3D);
                    }

                    canvas.Translate(geom.LyricMidX, geom.LyricCy + (fontSize * 0.28f));
                    canvas.Scale(wmScale * wmFit, wmScale * wmFit);
                    context.Legibility.DrawLegibleText(
                        canvas,
                        watermarkText,
                        0f,
                        0f,
                        SKTextAlign.Center,
                        font,
                        fontId,
                        StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor2, packet.Palette.AccentColor3, 0.5f),
                        opacity: wmAlpha,
                        fonts: context.Fonts);
                }
                finally
                {
                    canvas.RestoreToCount(wmSave);
                }
            }

            if (packet.PlaybackSnapshot?.Track is { IsValid: true } track)
            {
                var captionFont = context.Fonts.GetOrCreateFont(LyricFontId.Jost, Math.Clamp(fontSize * 0.32f, 11f, 17f), enforceMinOverlayFloor: false);
                var headerText = $"33\u2153 RPM \u00B7 {track.RawTitle.ToUpperInvariant()}";
                var measuredH = TextShaperService.MeasureWidth(headerText, captionFont, context.Fonts);
                var fitH = TextShaperService.ComputeAutoFitScale(measuredH, geom.LyricWidth, 0.94f);
                var headerSave = canvas.Save();
                try
                {
                    canvas.Translate(geom.LyricMidX, Math.Max(22f, geom.Cy - (geom.PlatterRadius * 0.94f)));
                    canvas.Scale(fitH, fitH);
                    context.Legibility.DrawLegibleText(
                        canvas,
                        headerText,
                        0f,
                        0f,
                        SKTextAlign.Center,
                        captionFont,
                        LyricFontId.Jost,
                        packet.Palette.AccentColor3,
                        opacity: 0.44f,
                        fonts: context.Fonts);
                }
                finally
                {
                    canvas.RestoreToCount(headerSave);
                }
            }
        }

        // Previous / Next line whispers anchored gently on StageCanvas
        if (geom.IsFullStage && slice.PreviousLine is not null)
        {
            var prevSave = canvas.Save();
            try
            {
                canvas.Translate(geom.LyricMidX - (geom.LyricWidth * 0.06f), geom.LyricCy - (fontSize * 1.92f));
                canvas.Scale(0.48f, 0.48f);
                context.Legibility.DrawLegibleText(canvas, slice.PreviousLine.RawText, 0f, 0f, align, font, fontId, packet.Palette.AccentColor3, opacity: 0.24f, fonts: context.Fonts);
            }
            finally
            {
                canvas.RestoreToCount(prevSave);
            }
        }

        // Apply living 3D pinhole perspective tilt across the main lyric stage beside the vinyl player
        var stage3DSave = canvas.Save();
        try
        {
            if (!packet.ReducedMotion)
            {
                var pitchDeg = 6.2f + (_smoothedBass * 3.0f) + (climaxGlow * 2.5f);
                var rollDeg = Math.Clamp(_lyricYawDeg.Value * 0.16f, -1.8f, 1.8f);
                var mat = KineticWallStyleRenderer.Create3DPerspectiveMatrix(
                    pitchDeg,
                    _lyricYawDeg.Value,
                    rollDeg,
                    PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f),
                    geom.LyricMidX,
                    geom.LyricCy);
                canvas.Concat(in mat);
            }

            if (words.IsEmpty)
            {
                var measuredLine = TextShaperService.MeasureWidth(line.RawText, font, context.Fonts);
                var fit = TextShaperService.ComputeAutoFitScale(measuredLine, geom.LyricWidth * 0.90f, 0.96f);
                var save = canvas.Save();
                try
                {
                    canvas.Translate(geom.LyricMidX, geom.LyricCy);
                    canvas.Scale(fit, fit);
                    Draw3DExtrudedWord(in context, font, fontId, line.RawText, SKTextAlign.Center, packet.Palette.AccentColor1, 1f, isActive: true, climaxGlow);
                }
                finally
                {
                    canvas.RestoreToCount(save);
                }

                return;
            }

            // Wrap timed words inside the 3D right editorial column
            var spaceW = fontSize * 0.34f;
            var rowLimitW = geom.LyricWidth * 0.90f;
            _rows.Clear();
            var currentRow = new WrappedWordRow();
            for (var i = 0; i < words.Length; i++)
            {
                var wWidth = TextShaperService.MeasureWidth(words[i].Text, font, context.Fonts);
                var nextW = currentRow.Items.Count == 0
                    ? wWidth
                    : currentRow.TotalWidth + spaceW + wWidth;
                if (currentRow.Items.Count > 0 && nextW > rowLimitW && _rows.Count < 3)
                {
                    _rows.Add(currentRow);
                    currentRow = new WrappedWordRow();
                }

                currentRow.Items.Add((i, words[i], wWidth));
                currentRow.TotalWidth = currentRow.Items.Count == 1 ? wWidth : currentRow.TotalWidth + spaceW + wWidth;
            }

            if (currentRow.Items.Count > 0)
            {
                _rows.Add(currentRow);
            }

            var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, words.Length - 1);
            var lineH = fontSize * 1.24f;
            var startBaselineY = geom.LyricCy - (((_rows.Count - 1) * lineH) * 0.5f) + (fontSize * 0.34f);

            var dotX = geom.LyricMidX;
            var dotY = geom.LyricCy;
            var foundActive = false;

            for (var rIdx = 0; rIdx < _rows.Count; rIdx++)
            {
                var row = _rows[rIdx];
                var rowScale = row.TotalWidth > rowLimitW ? Math.Clamp(rowLimitW / row.TotalWidth, 0.45f, 1f) : 1f;
                var baselineY = startBaselineY + (rIdx * lineH);
                var cursorX = geom.LyricMidX - ((row.TotalWidth * rowScale) * 0.5f);

                for (var itemIdx = 0; itemIdx < row.Items.Count; itemIdx++)
                {
                    var (wordIndex, word, rawW) = row.Items[itemIdx];
                    var scaledW = rawW * rowScale;
                    var wordCx = cursorX + (scaledW * 0.5f);
                    var isActive = wordIndex == activeIdx;
                    var isPast = wordIndex < activeIdx;

                    var color = isActive ? packet.Palette.AccentColor1
                        : isPast ? packet.Palette.AccentColor3
                        : packet.Palette.AccentColor1;
                    var opacity = isActive ? 1.0f : isPast ? 0.72f : 0.40f;
                    var wordScale = isActive ? _wordScale.Value * rowScale : rowScale;

                    if (isActive)
                    {
                        foundActive = true;
                        dotX = wordCx;
                        dotY = baselineY + (fontSize * 0.36f);
                        var wordBounds = new SKRect(
                            wordCx - (scaledW * 0.52f),
                            baselineY - (fontSize * 0.85f * wordScale),
                            wordCx + (scaledW * 0.52f),
                            baselineY + (fontSize * 0.18f));
                        _aesthetics.DrawWordColorSweep(canvas, wordBounds, slice.WordProgress, packet, 0.94f);
                    }

                    var save = canvas.Save();
                    try
                    {
                        canvas.Translate(wordCx, baselineY);
                        canvas.Scale(wordScale, wordScale);
                        Draw3DExtrudedWord(in context, font, fontId, word.Text, SKTextAlign.Center, color, opacity, isActive, climaxGlow);
                    }
                    finally
                    {
                        canvas.RestoreToCount(save);
                    }

                    cursorX += scaledW + (spaceW * rowScale);
                }
            }

            if (foundActive && !packet.ReducedMotion)
            {
                _aesthetics.DrawFollowingDot(canvas, packet, dotX, dotY, Math.Clamp(fontSize * 0.11f, 3f, 7f), 0.92f);
            }

            var subBaseY = startBaselineY + ((_rows.Count - 0.1f) * lineH);
            StyleLayoutHelper.DrawSecondarySubline(in context, line, geom.LyricMidX, subBaseY, fontSize, opacity: 0.74f);
        }
        finally
        {
            canvas.RestoreToCount(stage3DSave);
        }

        if (geom.IsFullStage && slice.NextLine is not null && !slice.NextLine.IsInstrumentalBreak)
        {
            var nextY = Math.Min(packet.ViewportHeightDip - 16f, geom.LyricCy + (fontSize * 2.15f));
            var nextSave = canvas.Save();
            try
            {
                canvas.Translate(geom.LyricMidX + (geom.LyricWidth * 0.06f), nextY);
                canvas.Scale(0.46f, 0.46f);
                context.Legibility.DrawLegibleText(canvas, slice.NextLine.RawText, 0f, 0f, align, font, fontId, packet.Palette.AccentColor1, opacity: 0.22f, fonts: context.Fonts);
            }
            finally
            {
                canvas.RestoreToCount(nextSave);
            }
        }
    }

    private void Draw3DExtrudedWord(
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        string text,
        SKTextAlign align,
        RgbColor foregroundColor,
        float opacity,
        bool isActive,
        float climaxGlow)
    {
        var canvas = context.Canvas;
        var packet = context.Packet;
        var depthSteps = isActive ? 3 : 1;
        var stepPx = packet.IsCompactCard ? 0.9f : 1.55f;

        // Multi-step physical 3D extrusion bevel shadow behind glyphs (solid offset strokes/fills)
        if (!packet.ReducedMotion)
        {
            for (var layer = depthSteps; layer >= 1; layer--)
            {
                var off = layer * stepPx;
                var layerAlpha = Math.Clamp((isActive ? 0.44f : 0.20f) * opacity * (1f - ((layer - 1) * 0.22f)), 0.04f, 0.65f);
                var extrudeColor = layer == depthSteps
                    ? new SKColor(0x09, 0x0A, 0x14, (byte)MathF.Round(255f * layerAlpha))
                    : packet.Palette.AccentColor3.ToSkColorWithAlpha(layerAlpha);

                _fillPaint.Shader = null;
                _fillPaint.Color = extrudeColor;
                canvas.DrawText(text, off, off, align, font, _fillPaint);
            }

            // Chromatic stereo prism split on beats and Beat Drop surges
            if (isActive && (_smoothedBeat > 0.06f || climaxGlow > 0.04f))
            {
                var splitDx = (2.1f * _smoothedBeat) + (3.6f * climaxGlow);
                context.Legibility.DrawLegibleText(canvas, text, -splitDx, -0.5f, align, font, fontId, packet.Palette.AccentColor1, opacity: 0.28f * opacity, fonts: context.Fonts);
                context.Legibility.DrawLegibleText(canvas, text, splitDx, 0.5f, align, font, fontId, packet.Palette.AccentColor2, opacity: 0.28f * opacity, fonts: context.Fonts);
            }
        }

        // Front AAA-legible glyph face
        context.Legibility.DrawLegibleText(
            canvas,
            text,
            0f,
            0f,
            align,
            font,
            fontId,
            foregroundColor,
            opacity: opacity,
            activeGlowColor: isActive ? packet.Palette.AccentColor2 : null,
            fonts: context.Fonts);
    }

    private void UpdateCachedLabelImage(AlbumArtPayload? art)
    {
        var hash = art?.Sha256Hash;
        if (art is null || art.ImageBytes.IsEmpty || string.IsNullOrEmpty(hash))
        {
            if (_cachedLabelImage is not null)
            {
                _cachedLabelImage.Dispose();
                _cachedLabelImage = null;
            }

            _cachedLabelHash = null;
            return;
        }

        if (string.Equals(hash, _cachedLabelHash, StringComparison.Ordinal))
        {
            return;
        }

        _cachedLabelHash = hash;
        _cachedLabelImage?.Dispose();
        _cachedLabelImage = SkiaAlbumColorExtractor.DecodeBoundedCoverImage(art.ImageBytes.Span, targetDimension: 288);
    }

    private static float ComputeNormalizedSongProgress(FrameRenderPacket packet, ActiveLyricSlice slice)
    {
        var dur = packet.PlaybackSnapshot?.Track?.Duration ?? TimeSpan.Zero;
        if (dur > TimeSpan.FromSeconds(5))
        {
            return (float)Math.Clamp(packet.SceneSnapshot.EffectivePosition.TotalSeconds / dur.TotalSeconds, 0.0, 1.0);
        }

        var lineCount = packet.SceneSnapshot.Document?.Lines.Length ?? 0;
        if (lineCount > 1 && slice.ActiveLineIndex >= 0)
        {
            return Math.Clamp(slice.ActiveLineIndex / (float)(lineCount - 1), 0f, 1f);
        }

        return 0.25f;
    }

    private static TurntableGeometry ComputeTurntableGeometry(FrameRenderPacket packet)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        var isFullStage = packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas && !packet.IsCompactCard;

        if (packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            var (ax, ay) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.50f);
            var deskR = Math.Clamp(MathF.Min(w, h) * 0.148f, 34f, 68f);
            var deskCx = MathF.Max(deskR + 30f, ax - Math.Min(w * 0.18f, 195f));
            var lyricLeft = deskCx + deskR + 22f;
            var lyricRight = MathF.Max(lyricLeft + 120f, w - 34f);
            return new TurntableGeometry(
                Cx: deskCx,
                Cy: ay,
                PlatterRadius: deskR,
                LyricLeft: lyricLeft,
                LyricRight: lyricRight,
                LyricMidX: (lyricLeft + lyricRight) * 0.5f,
                LyricWidth: Math.Max(110f, lyricRight - lyricLeft),
                LyricCy: ay,
                IsFullStage: false);
        }

        var platterR = Math.Max(
            packet.IsCompactCard ? 28f : 76f,
            MathF.Min(w * (packet.IsCompactCard ? 0.20f : 0.21f), h * (packet.IsCompactCard ? 0.36f : 0.35f)));
        var cx = w * (packet.IsCompactCard ? 0.27f : 0.28f);
        var cy = h * 0.50f;
        var stageLyricLeft = cx + platterR + Math.Max(packet.IsCompactCard ? 16f : 34f, w * 0.048f);
        var stageLyricRight = w - (packet.IsCompactCard ? 14f : 44f);
        var stageLyricW = Math.Max(110f, stageLyricRight - stageLyricLeft);
        return new TurntableGeometry(
            Cx: cx,
            Cy: cy,
            PlatterRadius: platterR,
            LyricLeft: stageLyricLeft,
            LyricRight: stageLyricRight,
            LyricMidX: stageLyricLeft + (stageLyricW * 0.5f),
            LyricWidth: stageLyricW,
            LyricCy: cy,
            IsFullStage: isFullStage);
    }

    private readonly record struct TurntableGeometry(
        float Cx,
        float Cy,
        float PlatterRadius,
        float LyricLeft,
        float LyricRight,
        float LyricMidX,
        float LyricWidth,
        float LyricCy,
        bool IsFullStage);
}
