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
/// Style #11 — Vinyl Groove 3D Listening Room:
/// Perspective 3D studio room with receding wireframe depth, oversized 0.54R circularly-cropped
/// center song album cover (or procedural studio sleeve artwork) with punched spindle cutout,
/// 3-pass Z-depth orbiting 3D extruded lyrics that physically weave BEHIND and IN FRONT OF the
/// spinning turntable, tracking tonearm &amp; stylus beam, using strictly affine canvas transforms
/// so GPU glyph atlases remain 100% artifact-free.
/// </summary>
public sealed class VinylGrooveStyleRenderer : IKineticStyleRenderer
{
    private const int GrooveRingCount = 11;
    private const int StrobeDotCount = 36;
    private const float SpinDegreesPerSecond = 44f;
    private const float PerspectiveFocalLength = 960f;

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly ReactiveVisualIconRenderer _moodIcons = new();
    private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _glowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _roomPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPath _armPath = new();
    private readonly SKPath _labelClipPath = new();
    private readonly List<Projected3DWordSlot> _wordSlots = new(16);
    private readonly SKColor[] _sheenColors = new SKColor[5];
    private readonly float[] _sheenStops = [0.00f, 0.25f, 0.50f, 0.75f, 1.00f];
    private readonly SKColor[] _radialColors = new SKColor[3];
    private readonly float[] _radialStops = [0.00f, 0.48f, 1.00f];

    private SpringState _wordScale = SpringState.AtRest(1.15f);
    private SpringState _tonearmProgress = SpringState.AtRest(0.15f);
    private SpringState _spinRate = SpringState.AtRest(1.0f);
    private SpringState _roomYawDeg = SpringState.AtRest(-7.0f);
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

    private readonly record struct Projected3DWordSlot(
        int WordIndex,
        string Text,
        float ScreenX,
        float ScreenY,
        float WorldZ,
        float DepthScale,
        float WordScale,
        float MeasuredWidth,
        float SkewY,
        float Opacity,
        RgbColor Color,
        bool IsActive,
        bool IsFrontPass);

    public OverlayStyleId StyleId => OverlayStyleId.VinylGroove;

    public void ResetState()
    {
        _wordScale = SpringState.AtRest(1.15f);
        _tonearmProgress = SpringState.AtRest(0.15f);
        _spinRate = SpringState.AtRest(1.0f);
        _roomYawDeg = SpringState.AtRest(-7.0f);
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
                _wordScale = new SpringState(Value: 0.90f, Velocity: 2.25f * packet.SafePartyEnergy, Target: 1.16f)
                    .Step(SpringTokens.WordSnap.WithTempoCompression(_lastWordDurationSec), dt);
            }
            else
            {
                _wordScale = SpringState.AtRest(1.09f);
            }
        }
        else if (!packet.ReducedMotion)
        {
            _wordScale = _wordScale.WithTarget(1.16f).Step(SpringTokens.WordSnap.WithTempoCompression(_lastWordDurationSec), dt);
        }

        // Smooth 3D listening room camera orbit synchronized with line cadence
        var wordCount = slice.CurrentLine?.Words.IsEmpty == false ? slice.CurrentLine.Words.Length : 1;
        var wordFrac = wordCount <= 1 ? 0.5f : Math.Clamp(slice.ActiveWordIndex / (float)(wordCount - 1), 0f, 1f);
        var targetYaw = packet.ReducedMotion
            ? 0f
            : Math.Clamp((-8.5f + (wordFrac * 15.0f)) * Math.Clamp(packet.SafePartyEnergy, 0.75f, 1.3f), -11f, 10f);
        _roomYawDeg = packet.ReducedMotion
            ? SpringState.AtRest(0f)
            : _roomYawDeg.WithTarget(targetYaw).Step(SpringTokens.CameraDrift, dt);

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

        return !packet.ReducedMotion && (isPlaying || !_wordScale.IsAtRest || !_tonearmProgress.IsAtRest || !_roomYawDeg.IsAtRest || _spinRate.Value > 0.01f);
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

        // Tight ambient scrim keeping DesktopOverlay 4 corners strictly transparent (alpha == 0)
        var scrimLeft = Math.Max(10f, geom.Cx - (geom.PlatterRadius * 1.18f));
        var scrimRight = Math.Min(packet.ViewportWidthDip - 10f, geom.LyricRight + 16f);
        var scrimTop = Math.Max(10f, geom.Cy - (geom.PlatterRadius * 1.12f));
        var scrimBottom = Math.Min(packet.ViewportHeightDip - 10f, geom.Cy + (geom.PlatterRadius * 1.12f));
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
            ? audio.ClimaxIntensity * 0.58f * BeatDropEffectRenderer.ResolveDropIntensity(in packet)
            : 0f;

        // Precompute 3D spatial word slots (front Z < 40 vs back Z >= 40 relative to vinyl disc)
        BuildProjected3DWordSlots(in context, font, slice, geom, fontSize, climaxGlow);

        // PASS 0 (StageCanvas Only): 3D Perspective Listening Room Architecture (floor grid, walls, neon room arch)
        if (geom.IsFullStage)
        {
            Draw3DRoomEnvironment(canvas, packet, geom, climaxGlow);
        }

        // PASS 1 (Z >= 40 BEHIND VINYL): Back-plane orbiting 3D text + deep background watermark + Previous Line whisper
        DrawBackPlaneDepthPass(in context, font, fontId, slice, geom, fontSize, climaxGlow);

        // PASS 2 (Z = 0 MID-PLANE): Spinning Vinyl Turntable + Oversized 0.54R Cropped Album Cover + Tracking Tonearm
        var progress = Math.Clamp(_tonearmProgress.Value, 0f, 1f);
        DrawTurntablePlatter(canvas, in context, geom, progress, climaxGlow);
        var (needleX, needleY) = DrawTonearmAndStylus(canvas, packet, geom, progress, climaxGlow);

        if (geom.IsFullStage && !packet.ReducedMotion)
        {
            DrawStylusBridgeThread(canvas, packet.Palette, needleX, needleY, geom, climaxGlow);
        }

        // PASS 3 (Z < 40 IN FRONT OF VINYL): Foreground 3D extruded active & orbiting words + subtle next whisper
        DrawFrontPlaneDepthPass(in context, font, fontId, slice, geom, fontSize, climaxGlow);
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
        _roomPaint.Dispose();
        _armPath.Dispose();
        _labelClipPath.Dispose();
    }

    private void Draw3DRoomEnvironment(
        SKCanvas canvas,
        FrameRenderPacket packet,
        TurntableGeometry geom,
        float climaxGlow)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        var palette = packet.Palette;
        var focal = PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f);
        var pitch = packet.ReducedMotion ? 0f : (7.2f + (_smoothedBass * 2.2f));
        var yaw = packet.ReducedMotion ? 0f : (_roomYawDeg.Value * 0.52f);
        var vanishX = w * 0.48f;
        var vanishY = h * 0.49f;

        SKPoint ProjRoom(float rx, float ry, float rz)
        {
            var (pt, _) = KineticWallStyleRenderer.Project3DPoint(
                vanishX + rx,
                vanishY + ry,
                rz,
                pitch,
                yaw,
                rollDeg: 0f,
                focal,
                vanishX,
                vanishY);
            return pt;
        }

        var roomAlpha = Math.Clamp(0.14f + (_smoothedBass * 0.14f) + (climaxGlow * 0.24f), 0.10f, 0.44f);
        var halfW = w * 0.54f;
        var floorY = h * 0.46f;
        var ceilY = -h * 0.46f;
        const float zNear = -90f;
        const float zFar = 520f;

        // 4 receding corner rails forming a 3D room bounding perspective prism
        _roomPaint.StrokeWidth = 1.25f + (climaxGlow * 0.8f);
        _roomPaint.Color = palette.AccentColor3.ToSkColorWithAlpha(roomAlpha * 0.65f);
        canvas.DrawLine(ProjRoom(-halfW, floorY, zNear), ProjRoom(-halfW, floorY, zFar), _roomPaint);
        canvas.DrawLine(ProjRoom(halfW, floorY, zNear), ProjRoom(halfW, floorY, zFar), _roomPaint);
        canvas.DrawLine(ProjRoom(-halfW, ceilY, zNear), ProjRoom(-halfW, ceilY, zFar), _roomPaint);
        canvas.DrawLine(ProjRoom(halfW, ceilY, zNear), ProjRoom(halfW, ceilY, zFar), _roomPaint);

        // Glowing back soundstage wall outline at zFar
        var bl = ProjRoom(-halfW, floorY, zFar);
        var br = ProjRoom(halfW, floorY, zFar);
        var tl = ProjRoom(-halfW, ceilY, zFar);
        var tr = ProjRoom(halfW, ceilY, zFar);
        _roomPaint.StrokeWidth = 1.5f + (climaxGlow * 1.4f);
        _roomPaint.Color = palette.AccentColor2.ToSkColorWithAlpha(roomAlpha * 0.85f);
        canvas.DrawLine(tl, tr, _roomPaint);
        canvas.DrawLine(tr, br, _roomPaint);
        canvas.DrawLine(br, bl, _roomPaint);
        canvas.DrawLine(bl, tl, _roomPaint);

        // Perspective receding 3D floor grid rays
        _roomPaint.StrokeWidth = 0.9f;
        _roomPaint.Color = palette.AccentColor3.ToSkColorWithAlpha(roomAlpha * 0.45f);
        for (var i = -2; i <= 2; i++)
        {
            var gx = (halfW * 0.34f) * i;
            canvas.DrawLine(ProjRoom(gx, floorY, zNear), ProjRoom(gx, floorY, zFar), _roomPaint);
        }

        // Cross-depth floor horizon bars + subtle ceiling acoustic ribs
        for (var d = 0; d < 5; d++)
        {
            var z = zNear + ((zFar - zNear) * (d / 4f));
            var fade = 1f - (d * 0.14f);
            _roomPaint.Color = palette.AccentColor1.ToSkColorWithAlpha(roomAlpha * 0.34f * fade);
            canvas.DrawLine(ProjRoom(-halfW, floorY, z), ProjRoom(halfW, floorY, z), _roomPaint);
            if (d is 1 or 3)
            {
                canvas.DrawLine(ProjRoom(-halfW, ceilY, z), ProjRoom(halfW, ceilY, z), _roomPaint);
            }
        }
    }

    private void BuildProjected3DWordSlots(
        in StyleRenderContext context,
        SKFont font,
        ActiveLyricSlice slice,
        TurntableGeometry geom,
        float fontSize,
        float climaxGlow)
    {
        _wordSlots.Clear();
        var line = slice.CurrentLine!;
        var words = line.Words;
        var packet = context.Packet;
        if (words.IsEmpty)
        {
            return;
        }

        var focal = PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f);
        var pitch = packet.ReducedMotion ? 0f : (5.5f + (_smoothedBass * 2.8f) + (climaxGlow * 2.0f));
        var yaw = packet.ReducedMotion ? 0f : _roomYawDeg.Value;
        var roll = packet.ReducedMotion ? 0f : Math.Clamp(yaw * 0.14f, -1.6f, 1.6f);

        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, words.Length - 1);
        var rowLimitW = geom.LyricWidth * 0.90f;
        var spaceW = fontSize * 0.34f;

        // First wrap words into balanced rows on the right stage zone
        var rowBuckets = new List<(int Start, int End, float Width)>(4);
        var rowStart = 0;
        var accumW = 0f;
        for (var i = 0; i < words.Length; i++)
        {
            var wW = TextShaperService.MeasureWidth(words[i].Text, font, context.Fonts);
            var candidate = i == rowStart ? wW : accumW + spaceW + wW;
            if (i > rowStart && candidate > rowLimitW && rowBuckets.Count < 3)
            {
                rowBuckets.Add((rowStart, i - 1, accumW));
                rowStart = i;
                accumW = wW;
            }
            else
            {
                accumW = candidate;
            }
        }
        rowBuckets.Add((rowStart, words.Length - 1, accumW));

        var lineH = fontSize * 1.26f;
        var startBaselineY = geom.LyricCy - (((rowBuckets.Count - 1) * lineH) * 0.5f) + (fontSize * 0.34f);
        var tOrb = packet.ReducedMotion ? 0f : (float)(packet.RenderTimeSeconds * 0.72);

        for (var rIdx = 0; rIdx < rowBuckets.Count; rIdx++)
        {
            var (start, end, totalW) = rowBuckets[rIdx];
            var rowScale = totalW > rowLimitW ? Math.Clamp(rowLimitW / totalW, 0.45f, 1f) : 1f;
            var baseRowY = startBaselineY + (rIdx * lineH);
            var cursorX = geom.LyricMidX - ((totalW * rowScale) * 0.5f);

            for (var i = start; i <= end; i++)
            {
                var word = words[i];
                var rawW = TextShaperService.MeasureWidth(word.Text, font, context.Fonts);
                var scaledW = rawW * rowScale;
                var worldX = cursorX + (scaledW * 0.5f);

                var isActive = i == activeIdx;
                var isPast = i < activeIdx;

                // True 3D Z assignment: Active word jumps IN FRONT (Z < 0 closer to camera);
                // non-active words oscillate smoothly around vinyl plane so alternating words tuck BEHIND (Z >= 40)
                // and sweep IN FRONT (Z < 40) as the orbital room rotates!
                float worldZ;
                if (!geom.IsFullStage || packet.ReducedMotion)
                {
                    worldZ = isActive ? -35f : 15f;
                }
                else if (isActive)
                {
                    worldZ = -75f - (climaxGlow * 32f);
                    // Bring foreground spotlight slightly closer toward vinyl right edge for bold depth overlap
                    worldX = Math.Max(geom.Cx + (geom.PlatterRadius * 0.62f), worldX - (geom.PlatterRadius * 0.14f));
                }
                else
                {
                    var wave = MathF.Sin((i * 1.45f) + (line.Index * 0.85f) + tOrb);
                    if (wave > 0.12f)
                    {
                        // Orbit behind vinyl record edge!
                        worldZ = 90f + (wave * 135f);
                        worldX -= geom.PlatterRadius * (0.28f + (0.22f * wave));
                    }
                    else
                    {
                        // Float forward in foreground room lane
                        worldZ = -20f + (wave * 30f);
                    }
                }

                var (projPt, depthScale) = packet.ReducedMotion
                    ? (new SKPoint(worldX, baseRowY), 1.0f)
                    : KineticWallStyleRenderer.Project3DPoint(
                        worldX,
                        baseRowY,
                        worldZ,
                        pitch,
                        yaw,
                        roll,
                        focal,
                        geom.LyricMidX,
                        geom.LyricCy);

                var isFrontPass = isActive || worldZ < 40f || !geom.IsFullStage;
                var baseColor = isActive ? packet.Palette.AccentColor1
                    : isPast ? packet.Palette.AccentColor3
                    : packet.Palette.AccentColor1;
                var baseOpacity = isActive ? 1.0f
                    : !isFrontPass ? 0.52f
                    : isPast ? 0.72f : 0.44f;
                var wordScale = isActive ? _wordScale.Value * rowScale : rowScale;
                var skewY = packet.ReducedMotion ? 0f : Math.Clamp((worldX - geom.LyricMidX) / Math.Max(320f, geom.LyricWidth) * (yaw * 0.008f), -0.07f, 0.07f);

                _wordSlots.Add(new Projected3DWordSlot(
                    WordIndex: i,
                    Text: word.Text,
                    ScreenX: projPt.X,
                    ScreenY: projPt.Y,
                    WorldZ: worldZ,
                    DepthScale: depthScale,
                    WordScale: wordScale,
                    MeasuredWidth: scaledW,
                    SkewY: skewY,
                    Opacity: baseOpacity,
                    Color: baseColor,
                    IsActive: isActive,
                    IsFrontPass: isFrontPass));

                cursorX += scaledW + (spaceW * rowScale);
            }
        }

        // Depth-sort back-to-front so overlapping 3D words stack naturally!
        _wordSlots.Sort(static (a, b) => b.WorldZ.CompareTo(a.WorldZ));
    }

    private void DrawBackPlaneDepthPass(
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        ActiveLyricSlice slice,
        TurntableGeometry geom,
        float fontSize,
        float climaxGlow)
    {
        var canvas = context.Canvas;
        var packet = context.Packet;
        var words = slice.CurrentLine!.Words;

        if (geom.IsFullStage)
        {
            // Deep back-wall 3D extruded active word echo tucked partially behind turntable rim
            var wmText = !words.IsEmpty
                ? words[Math.Clamp(slice.ActiveWordIndex, 0, words.Length - 1)].Text.ToUpperInvariant()
                : slice.CurrentLine.RawText.ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(wmText))
            {
                var wmX = packet.ViewportWidthDip * 0.49f;
                var wmY = geom.Cy + (fontSize * 0.25f);
                var wmScale = 2.25f + (_smoothedBeat * 0.18f) + (climaxGlow * 0.16f);
                var wmMeasured = TextShaperService.MeasureWidth(wmText, font, context.Fonts);
                var wmFit = TextShaperService.ComputeAutoFitScale(wmMeasured * wmScale, packet.ViewportWidthDip * 0.72f, 1.0f);
                var wmSave = canvas.Save();
                try
                {
                    canvas.Translate(wmX, wmY);
                    canvas.Scale(wmScale * wmFit, wmScale * wmFit);
                    context.Legibility.DrawLegibleText(
                        canvas,
                        wmText,
                        0f,
                        0f,
                        SKTextAlign.Center,
                        font,
                        fontId,
                        StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor2, packet.Palette.AccentColor3, 0.5f),
                        opacity: Math.Clamp(0.08f + (_smoothedBeat * 0.05f) + (climaxGlow * 0.07f), 0.06f, 0.22f),
                        fonts: context.Fonts);
                }
                finally
                {
                    canvas.RestoreToCount(wmSave);
                }
            }

            // Previous line whisper floating in rear Z plane
            if (slice.PreviousLine is not null)
            {
                var prevSave = canvas.Save();
                try
                {
                    canvas.Translate(geom.LyricMidX - (geom.LyricWidth * 0.08f), geom.LyricCy - (fontSize * 2.02f));
                    canvas.Scale(0.48f, 0.48f);
                    context.Legibility.DrawLegibleText(canvas, slice.PreviousLine.RawText, 0f, 0f, SKTextAlign.Center, font, fontId, packet.Palette.AccentColor3, opacity: 0.26f, fonts: context.Fonts);
                }
                finally
                {
                    canvas.RestoreToCount(prevSave);
                }
            }
        }

        // Draw every word currently routed behind the turntable (Z >= 40) BEFORE platter draws!
        for (var i = 0; i < _wordSlots.Count; i++)
        {
            var slot = _wordSlots[i];
            if (!slot.IsFrontPass)
            {
                DrawProjectedWordSlot(in context, font, fontId, in slot, slice.WordProgress, fontSize, climaxGlow);
            }
        }
    }

    private void DrawFrontPlaneDepthPass(
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        ActiveLyricSlice slice,
        TurntableGeometry geom,
        float fontSize,
        float climaxGlow)
    {
        var canvas = context.Canvas;
        var packet = context.Packet;
        var line = slice.CurrentLine!;

        if (geom.IsFullStage && packet.PlaybackSnapshot?.Track is { IsValid: true } track)
        {
            var captionFont = context.Fonts.GetOrCreateFont(LyricFontId.Jost, Math.Clamp(fontSize * 0.32f, 11f, 17f), enforceMinOverlayFloor: false);
            var headerText = $"33\u2153 RPM \u00B7 {track.RawTitle.ToUpperInvariant()}";
            var measuredH = TextShaperService.MeasureWidth(headerText, captionFont, context.Fonts);
            var fitH = TextShaperService.ComputeAutoFitScale(measuredH, geom.LyricWidth, 0.94f);
            var headerSave = canvas.Save();
            try
            {
                canvas.Translate(geom.LyricMidX, Math.Max(22f, geom.Cy - (geom.PlatterRadius * 0.95f)));
                canvas.Scale(fitH, fitH);
                context.Legibility.DrawLegibleText(canvas, headerText, 0f, 0f, SKTextAlign.Center, captionFont, LyricFontId.Jost, packet.Palette.AccentColor3, opacity: 0.46f, fonts: context.Fonts);
            }
            finally
            {
                canvas.RestoreToCount(headerSave);
            }
        }

        // Line-level LRC fallback
        if (line.Words.IsEmpty)
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

            StyleLayoutHelper.DrawSecondarySubline(in context, line, geom.LyricMidX, geom.LyricCy + (fontSize * 1.1f), fontSize, opacity: 0.74f);
            return;
        }

        var dotX = geom.LyricMidX;
        var dotY = geom.LyricCy;
        var foundActive = false;

        // Draw every word in the Front Z plane AFTER platter draws so active words sit sharply IN FRONT of vinyl!
        for (var i = 0; i < _wordSlots.Count; i++)
        {
            var slot = _wordSlots[i];
            if (!slot.IsFrontPass)
            {
                continue;
            }

            if (slot.IsActive)
            {
                foundActive = true;
                dotX = slot.ScreenX;
                dotY = slot.ScreenY + (fontSize * 0.36f * slot.DepthScale);
            }

            DrawProjectedWordSlot(in context, font, fontId, in slot, slice.WordProgress, fontSize, climaxGlow);
        }

        if (foundActive && !packet.ReducedMotion)
        {
            _aesthetics.DrawFollowingDot(canvas, packet, dotX, dotY, Math.Clamp(fontSize * 0.11f, 3f, 7f), 0.92f);
        }

        StyleLayoutHelper.DrawSecondarySubline(in context, line, geom.LyricMidX, geom.LyricCy + (fontSize * 1.72f), fontSize, opacity: 0.74f);

        if (geom.IsFullStage && slice.NextLine is not null && !slice.NextLine.IsInstrumentalBreak)
        {
            var nextY = Math.Min(packet.ViewportHeightDip - 16f, geom.LyricCy + (fontSize * 2.30f));
            var nextSave = canvas.Save();
            try
            {
                canvas.Translate(geom.LyricMidX + (geom.LyricWidth * 0.06f), nextY);
                canvas.Scale(0.46f, 0.46f);
                context.Legibility.DrawLegibleText(canvas, slice.NextLine.RawText, 0f, 0f, SKTextAlign.Center, font, fontId, packet.Palette.AccentColor1, opacity: 0.24f, fonts: context.Fonts);
            }
            finally
            {
                canvas.RestoreToCount(nextSave);
            }
        }
    }

    private void DrawProjectedWordSlot(
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        in Projected3DWordSlot slot,
        double wordProgress,
        float fontSize,
        float climaxGlow)
    {
        var canvas = context.Canvas;
        var packet = context.Packet;
        var totalScale = Math.Clamp(slot.WordScale * slot.DepthScale, 0.38f, 1.85f);

        if (slot.IsActive && packet.ShowColorSweepLayer)
        {
            var wordBounds = new SKRect(
                slot.ScreenX - (slot.MeasuredWidth * totalScale * 0.50f),
                slot.ScreenY - (fontSize * 0.85f * totalScale),
                slot.ScreenX + (slot.MeasuredWidth * totalScale * 0.50f),
                slot.ScreenY + (fontSize * 0.18f));
            _aesthetics.DrawWordColorSweep(canvas, wordBounds, wordProgress, packet, 0.94f);
        }

        var save = canvas.Save();
        try
        {
            canvas.Translate(slot.ScreenX, slot.ScreenY);
            if (!packet.ReducedMotion && MathF.Abs(slot.SkewY) > 0.002f)
            {
                canvas.Skew(0f, slot.SkewY);
            }

            canvas.Scale(totalScale, totalScale);
            Draw3DExtrudedWord(in context, font, fontId, slot.Text, SKTextAlign.Center, slot.Color, slot.Opacity, slot.IsActive, climaxGlow);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
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

        // 11 concentric grooves cleanly surrounding the 0.54R circular album cover
        var grooveOuter = r * 0.95f;
        var grooveInner = r * 0.58f;
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

        // Oversized 0.54R Circularly-Cropped Center Song Album Cover Photo + Punched Center Spindle Cutout
        var coverR = r * 0.54f;
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

        // Crisp luminous vinyl cutout collar & punched-out center hole around album cover
        _strokePaint.StrokeWidth = Math.Max(1.6f, r * 0.020f);
        _strokePaint.Color = palette.AccentColor1.ToSkColorWithAlpha(0.65f + (climaxGlow * 0.30f));
        canvas.DrawCircle(geom.Cx, geom.Cy, coverR, _strokePaint);

        var holeR = Math.Max(2.8f, r * 0.044f);
        _fillPaint.Shader = null;
        _fillPaint.Color = new SKColor(0x07, 0x08, 0x0E, 255);
        canvas.DrawCircle(geom.Cx, geom.Cy, holeR, _fillPaint);
        _strokePaint.StrokeWidth = Math.Max(1.1f, r * 0.012f);
        _strokePaint.Color = new SKColor(0xE2, 0xE8, 0xF0, 210);
        canvas.DrawCircle(geom.Cx, geom.Cy, holeR, _strokePaint);
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

        var horizonY = geom.Cy + (coverR * 0.14f);
        _fillPaint.Color = palette.AccentColor1.ToSkColorWithAlpha(0.28f);
        canvas.DrawCircle(geom.Cx, geom.Cy - (coverR * 0.12f), coverR * 0.44f, _fillPaint);
        _fillPaint.Color = new SKColor(0x0A, 0x0C, 0x16, 175);
        canvas.DrawRect(new SKRect(coverBounds.Left, horizonY, coverBounds.Right, coverBounds.Bottom), _fillPaint);

        _strokePaint.StrokeWidth = Math.Max(0.9f, coverR * 0.018f);
        _strokePaint.Color = palette.AccentColor2.ToSkColorWithAlpha(0.60f);
        canvas.DrawLine(coverBounds.Left, horizonY, coverBounds.Right, horizonY, _strokePaint);

        if (packet.PlaybackSnapshot?.TrackMoodVisual is { } mood && coverR >= 16f)
        {
            var emblemSize = Math.Clamp(coverR * 0.46f, 12f, 44f);
            _moodIcons.DrawVisual(canvas, in context, mood, geom.Cx, geom.Cy - (coverR * 0.24f), emblemSize, 0.94f, isActive: true);
        }

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

        var grooveRadius = r * (0.94f - (0.35f * trackProgress));
        var landingAngle = (-34f + (22f * trackProgress)) * (MathF.PI / 180f);
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
        var stepPx = packet.IsCompactCard ? 0.9f : 1.65f;

        if (!packet.ReducedMotion)
        {
            for (var layer = depthSteps; layer >= 1; layer--)
            {
                var off = layer * stepPx;
                var layerAlpha = Math.Clamp((isActive ? 0.46f : 0.22f) * opacity * (1f - ((layer - 1) * 0.22f)), 0.04f, 0.65f);
                var extrudeColor = layer == depthSteps
                    ? new SKColor(0x09, 0x0A, 0x14, (byte)MathF.Round(255f * layerAlpha))
                    : packet.Palette.AccentColor3.ToSkColorWithAlpha(layerAlpha);

                _fillPaint.Shader = null;
                _fillPaint.Color = extrudeColor;
                canvas.DrawText(text, off, off, align, font, _fillPaint);
            }

            if (isActive && (_smoothedBeat > 0.06f || climaxGlow > 0.04f))
            {
                var splitDx = (2.1f * _smoothedBeat) + (3.6f * climaxGlow);
                context.Legibility.DrawLegibleText(canvas, text, -splitDx, -0.5f, align, font, fontId, packet.Palette.AccentColor1, opacity: 0.28f * opacity, fonts: context.Fonts);
                context.Legibility.DrawLegibleText(canvas, text, splitDx, 0.5f, align, font, fontId, packet.Palette.AccentColor2, opacity: 0.28f * opacity, fonts: context.Fonts);
            }
        }

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
            var lyricRight = Math.Max(lyricLeft + 120f, w - 34f);
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
            packet.IsCompactCard ? 28f : 78f,
            MathF.Min(w * (packet.IsCompactCard ? 0.20f : 0.21f), h * (packet.IsCompactCard ? 0.36f : 0.35f)));
        var cx = w * (packet.IsCompactCard ? 0.27f : 0.30f);
        var cy = h * 0.50f;
        var stageLyricLeft = cx + platterR + Math.Max(packet.IsCompactCard ? 16f : 28f, w * 0.042f);
        var stageLyricRight = w - (packet.IsCompactCard ? 14f : 42f);
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
