using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #7 — Spider's Web Nocturnal Silk Constellation: 6 catenary dew-lined orbital silk rings
/// with harmonic moonlight shimmer, elastic plucked-strand vibration without blur-filter stalls,
/// 8 curved 3-joint articulated orb-weaver legs following a taut luminous dragline, specular glass
/// dewdrop pearls, semantic mood badges, and previous/next context whispers.
/// </summary>
public sealed class SpidersWebStyleRenderer : IKineticStyleRenderer
{
    private static readonly float[] RingRatios = [0.16f, 0.30f, 0.44f, 0.60f, 0.76f, 0.92f];

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _ringPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _spokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _spokeGlowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _dewPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _dewRingPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f };
    private readonly SKPaint _spiderBodyPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _spiderLegPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPath _silkPath = new();
    private readonly SKPath _legPath = new();

    private SpringState _wordScale = SpringState.AtRest(1.14f);
    private SpringState _spiderX = SpringState.AtRest(0f);
    private SpringState _spiderY = SpringState.AtRest(0f);
    private float _twangElapsedSec = 0.40f;
    private float _highlightFade = 1.0f;
    private float _smoothedBass;
    private float _smoothedBeat;
    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private float _lastWordDurationSec = 0.32f;
    private bool _hasSpiderPos;
    private bool _disposed;

    public OverlayStyleId StyleId => OverlayStyleId.SpidersWeb;

    public void ResetState()
    {
        _wordScale = SpringState.AtRest(1.14f);
        _spiderX = SpringState.AtRest(0f);
        _spiderY = SpringState.AtRest(0f);
        _hasSpiderPos = false;
        _twangElapsedSec = 0.40f;
        _highlightFade = 1.0f;
        _smoothedBass = 0f;
        _smoothedBeat = 0f;
        _lastWordKey = (-1, -1);
        _lastWordDurationSec = 0.32f;
        _aesthetics.ResetDot();
    }

    public bool Advance(in FrameRenderPacket packet, in LifecycleFrameState lifecycle)
    {
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        if (slice.CurrentLine is null)
        {
            return false;
        }

        var dt = Math.Clamp(packet.DeltaSeconds, 0f, SpringState.MaxDeltaSeconds);
        var audio = packet.EffectiveAudioMetrics;
        _smoothedBass = (_smoothedBass * 0.72f) + (Math.Clamp(audio.BassEnergy, 0f, 1.5f) * 0.28f);
        _smoothedBeat = (_smoothedBeat * 0.65f) + (Math.Clamp(audio.BeatPulse, 0f, 1.5f) * 0.35f);

        var words = slice.CurrentLine.Words;
        var wordCount = words.IsEmpty ? 1 : words.Length;
        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, wordCount - 1);

        if (!words.IsEmpty)
        {
            var word = words[activeIdx];
            _lastWordDurationSec = Math.Clamp((float)(word.EndTime - word.StartTime).TotalSeconds, 0.08f, 2.5f);
        }

        var restScale = packet.IsCompactCard ? 1.06f : 1.15f;
        var key = (slice.ActiveLineIndex, activeIdx);
        if (key != _lastWordKey)
        {
            _lastWordKey = key;
            if (packet.ReducedMotion)
            {
                _wordScale = SpringState.AtRest(1.00f);
                _twangElapsedSec = 0.40f;
                _highlightFade = 0f;
            }
            else
            {
                _wordScale = new SpringState(Value: restScale - 0.24f, Velocity: 0f, Target: restScale + ((packet.SafePartyEnergy - 1f) * 0.08f));
                _twangElapsedSec = 0f;
                _highlightFade = 0f;
            }
        }

        var fadeRate = packet.ReducedMotion ? (1f / 0.14f) : (1f / 0.15f);
        if (_highlightFade < 1f)
        {
            _highlightFade = Math.Min(1f, _highlightFade + (dt * fadeRate));
        }

        if (packet.ReducedMotion)
        {
            _wordScale = SpringState.AtRest(1.00f);
            _twangElapsedSec = 0.40f;
            _hasSpiderPos = false;
            _aesthetics.ResetDot();
            return _highlightFade < 0.999f;
        }

        var (cx, cy, radius) = ComputeWebMetrics(packet);
        var spokeCount = Math.Max(wordCount, 8);
        var seed = StyleLayoutHelper.DeterministicHash(slice.CurrentLine.Index, slice.CurrentLine.RawText);
        var angle = ComputeSpokeAngle(activeIdx, spokeCount, seed);
        var tierRatio = (activeIdx % 2 == 0) ? 0.56f : 0.82f;
        var nx = cx + (MathF.Cos(angle) * radius * tierRatio);
        var ny = cy + (MathF.Sin(angle) * radius * tierRatio);

        // Spider patrols purposefully closer to the newly spun active dewdrop
        var spiderTargetX = cx + ((nx - cx) * 0.48f);
        var spiderTargetY = cy + ((ny - cy) * 0.48f);
        if (!_hasSpiderPos)
        {
            _spiderX = SpringState.AtRest(spiderTargetX);
            _spiderY = SpringState.AtRest(spiderTargetY);
            _hasSpiderPos = true;
        }
        else
        {
            _spiderX = _spiderX.WithTarget(spiderTargetX).Step(SpringTokens.WordEnter.WithTempoCompression(_lastWordDurationSec), dt);
            _spiderY = _spiderY.WithTarget(spiderTargetY).Step(SpringTokens.WordEnter.WithTempoCompression(_lastWordDurationSec), dt);
        }

        var spring = SpringTokens.WordSnap.WithTempoCompression(_lastWordDurationSec);
        _wordScale = _wordScale.WithTarget(restScale + ((packet.SafePartyEnergy - 1f) * 0.06f)).Step(spring, dt);
        var dotActive = packet.ShowFollowingDot && _aesthetics.AdvanceDot(key, nx, ny, dt, reducedMotion: false, _lastWordDurationSec);

        if (_twangElapsedSec < 0.40f)
        {
            _twangElapsedSec = Math.Min(0.40f, _twangElapsedSec + dt);
        }

        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        return !_wordScale.IsAtRest || !_spiderX.IsAtRest || dotActive || _twangElapsedSec < 0.40f || _highlightFade < 0.999f || isPlaying;
    }

    public void Render(in StyleRenderContext context, in LifecycleFrameState lifecycle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var packet = context.Packet;
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        if (slice.CurrentLine is null)
        {
            return;
        }

        var fontId = packet.ActiveFontId;
        var fontSize = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 0.62f);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);

        var (cx, cy, radius) = ComputeWebMetrics(packet);
        var isFullStage = packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas && !packet.IsCompactCard;
        var scrimRadius = radius * (isFullStage ? 1.0f : 0.72f);
        var scrimRect = new SKRect(cx - scrimRadius, cy - scrimRadius, cx + scrimRadius, cy + scrimRadius);
        context.Legibility.DrawAmbientScrim(
            context.Canvas,
            scrimRect,
            fontSize,
            packet.Palette.ScrimTint,
            packet.ScrimIntensity,
            packet.HighContrastScrim);

        var words = slice.CurrentLine.Words;
        var wordCount = words.IsEmpty ? (string.IsNullOrWhiteSpace(slice.CurrentLine.RawText) ? 0 : 1) : words.Length;
        if (wordCount == 0)
        {
            return;
        }

        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, wordCount - 1);
        var spokeCount = Math.Max(wordCount, 8);
        var seed = StyleLayoutHelper.DeterministicHash(slice.CurrentLine.Index, slice.CurrentLine.RawText);

        Span<float> angles = spokeCount <= 32 ? stackalloc float[spokeCount] : new float[spokeCount];
        for (var i = 0; i < spokeCount; i++)
        {
            angles[i] = ComputeSpokeAngle(i, spokeCount, seed);
        }

        var twangProgress = packet.ReducedMotion ? 1.0f : Ease.Enter(_twangElapsedSec / 0.40f);
        var twangAmp = packet.ReducedMotion
            ? 0f
            : (5.2f + (_smoothedBeat * 3.0f)) * MathF.Exp(-8.5f * _twangElapsedSec) * MathF.Sin(42f * _twangElapsedSec) * packet.SafePartyEnergy;

        // 1. Moonlight-tinted catenary rings + elastic radial bow + intersection dew glints
        DrawCatenaryWeb(context.Canvas, cx, cy, radius, angles, angles[activeIdx], twangAmp, packet, isFullStage);

        // 2. Spokes (blur-free 3-layer luminous silk glow on active strand)
        var spinFraction = packet.ReducedMotion ? 1.0f : (float)Math.Clamp(slice.WordProgress / 0.45, 0.25, 1.0);
        for (var i = 0; i < spokeCount; i++)
        {
            var isWordSpoke = i < wordCount;
            var isActive = isWordSpoke && i == activeIdx;
            var isPast = isWordSpoke && i < activeIdx;

            var angle = angles[i];
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);
            var reach = isActive ? (radius * spinFraction) : radius;
            var ex = cx + (cos * reach);
            var ey = cy + (sin * reach);

            if (isActive && MathF.Abs(twangAmp) > 0.06f)
            {
                var mx = cx + (cos * reach * 0.5f) - (sin * twangAmp);
                var my = cy + (sin * reach * 0.5f) + (cos * twangAmp);
                _silkPath.Rewind();
                _silkPath.MoveTo(cx, cy);
                _silkPath.QuadTo(mx, my, ex, ey);

                _spokeGlowPaint.StrokeWidth = 9.0f + (_smoothedBeat * 4.0f);
                _spokeGlowPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.16f + (_smoothedBeat * 0.10f));
                context.Canvas.DrawPath(_silkPath, _spokeGlowPaint);

                _spokeGlowPaint.StrokeWidth = 4.2f;
                _spokeGlowPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.45f);
                context.Canvas.DrawPath(_silkPath, _spokeGlowPaint);

                _spokePaint.StrokeWidth = 1.9f;
                _spokePaint.Color = SKColors.White.WithAlpha(242);
                context.Canvas.DrawPath(_silkPath, _spokePaint);
            }
            else
            {
                if (isActive)
                {
                    _spokeGlowPaint.StrokeWidth = 7.5f;
                    _spokeGlowPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.28f);
                    context.Canvas.DrawLine(cx, cy, ex, ey, _spokeGlowPaint);
                }

                _spokePaint.StrokeWidth = isActive ? 1.9f : (isPast ? 1.25f : 0.85f);
                _spokePaint.Color = isActive
                    ? SKColors.White.WithAlpha(242)
                    : isPast
                        ? packet.Palette.AccentColor1.ToSkColorWithAlpha(0.56f)
                        : packet.Palette.AccentColor3.ToSkColorWithAlpha(0.20f);
                context.Canvas.DrawLine(cx, cy, ex, ey, _spokePaint);
            }
        }

        // 3. Taut luminous silk dragline & articulated orb-weaver spider gliding between hub and active word
        var sx = !packet.ReducedMotion && _hasSpiderPos ? _spiderX.Value : cx;
        var sy = !packet.ReducedMotion && _hasSpiderPos ? _spiderY.Value : cy;

        var activeAngle = angles[activeIdx];
        var activeTierRatio = (activeIdx % 2 == 0) ? 0.56f : 0.82f;
        var targetDewX = cx + (MathF.Cos(activeAngle) * radius * activeTierRatio);
        var targetDewY = cy + (MathF.Sin(activeAngle) * radius * activeTierRatio);

        if (!packet.ReducedMotion && !packet.IsCompactCard)
        {
            _spokePaint.StrokeWidth = 1.1f;
            _spokePaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(0.48f);
            context.Canvas.DrawLine(cx, cy, sx, sy, _spokePaint);

            _spokePaint.Color = SKColors.White.WithAlpha(155);
            context.Canvas.DrawLine(sx, sy, targetDewX, targetDewY, _spokePaint);
        }

        DrawStylizedSpider(
            context.Canvas,
            sx,
            sy,
            activeAngle,
            packet.IsCompactCard ? 0.56f : 1.05f,
            (float)packet.RenderTimeSeconds,
            packet.ReducedMotion,
            packet.Palette);

        // 4. Context whispers above/below on StageCanvas
        if (isFullStage)
        {
            DrawContextWhispers(in context, font, fontId, slice, cx, cy, radius, fontSize);
        }

        // 5. Specular dew pearl nodes + plucked shockwave ripple + semantic icons + upright lyrics
        var highlightEased = Ease.Highlight(_highlightFade);
        for (var i = 0; i < wordCount; i++)
        {
            var text = words.IsEmpty ? slice.CurrentLine.RawText : words[i].Text;
            var wordVisual = words.IsEmpty ? null : words[i].Visual;
            var angle = angles[i];
            var tierRatio = (i % 2 == 0) ? 0.56f : 0.82f;
            var nx = cx + (MathF.Cos(angle) * radius * tierRatio);
            var ny = cy + (MathF.Sin(angle) * radius * tierRatio);
            var isActive = i == activeIdx;
            var isPast = i < activeIdx;

            DrawDewPearlNode(context.Canvas, nx, ny, isActive, isPast, twangProgress, packet);

            var labelOpacity = isActive
                ? (packet.ReducedMotion ? (0.92f + (0.08f * highlightEased)) : 1.0f)
                : isPast ? 0.76f : 0.34f;
            var labelColor = isActive
                ? StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor1, SKColors.White.ToRgb(), 0.48f * highlightEased)
                : isPast ? packet.Palette.AccentColor1 : packet.Palette.AccentColor3;
            var labelScale = isActive ? (packet.ReducedMotion ? 1.0f : _wordScale.Value) : 0.88f;

            var measuredW = TextShaperService.MeasureWidth(text, font, context.Fonts) * labelScale;
            var clampedX = Math.Clamp(nx, (measuredW * 0.5f) + 12f, Math.Max((measuredW * 0.5f) + 12f, packet.ViewportWidthDip - (measuredW * 0.5f) - 12f));
            var clampedY = Math.Clamp(ny - (font.Size * 0.48f), font.Size + 8f, Math.Max(font.Size + 8f, packet.ViewportHeightDip - 14f));

            if (isActive && packet.ShowColorSweepLayer)
            {
                var sweepRect = new SKRect(clampedX - (measuredW * 0.5f), clampedY - (font.Size * 0.85f * labelScale), clampedX + (measuredW * 0.5f), clampedY + (font.Size * 0.16f * labelScale));
                _aesthetics.DrawWordColorSweep(context.Canvas, sweepRect, slice.WordProgress, packet, labelOpacity);
            }

            if (isActive && wordVisual is { } matchedVisual)
            {
                var dirSign = clampedX > cx ? 1f : -1f;
                var badgeX = Math.Clamp(clampedX + (dirSign * ((measuredW * 0.5f) + (font.Size * 0.52f))), 32f, packet.ViewportWidthDip - 32f);
                var badgeY = Math.Clamp(clampedY - (font.Size * 0.25f), 32f, packet.ViewportHeightDip - 32f);
                var badgeSize = Math.Clamp(font.Size * (packet.IsCompactCard ? 0.56f : 0.70f), 18f, 36f);
                _aesthetics.DrawReactiveVisual(context.Canvas, in context, matchedVisual, badgeX, badgeY, badgeSize, labelOpacity, isActive: true);
            }

            var save = context.Canvas.Save();
            try
            {
                context.Canvas.Translate(clampedX, clampedY);
                context.Canvas.Scale(labelScale, labelScale);
                context.Legibility.DrawLegibleText(
                    context.Canvas,
                    text,
                    0f,
                    0f,
                    SKTextAlign.Center,
                    font,
                    fontId,
                    labelColor,
                    opacity: labelOpacity,
                    activeGlowColor: isActive ? packet.Palette.AccentColor2 : null,
                    fonts: context.Fonts);
            }
            finally
            {
                context.Canvas.RestoreToCount(save);
            }
        }

        _aesthetics.DrawFollowingDot(context.Canvas, packet, targetDewX, targetDewY, Math.Clamp(fontSize * 0.14f, 3.0f, 6.2f));

        StyleLayoutHelper.DrawSecondarySubline(
            in context,
            slice.CurrentLine,
            cx,
            cy + radius + Math.Max(16f, fontSize * 0.65f),
            fontSize * 1.15f);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _aesthetics.Dispose();
        _ringPaint.Dispose();
        _spokePaint.Dispose();
        _spokeGlowPaint.Dispose();
        _dewPaint.Dispose();
        _dewRingPaint.Dispose();
        _spiderBodyPaint.Dispose();
        _spiderLegPaint.Dispose();
        _silkPath.Dispose();
        _legPath.Dispose();
    }

    private void DrawStylizedSpider(
        SKCanvas canvas,
        float sx,
        float sy,
        float facingRad,
        float scale,
        float timeSec,
        bool reducedMotion,
        AlbumAccentPalette palette)
    {
        var save = canvas.Save();
        try
        {
            canvas.Translate(sx, sy);
            canvas.RotateDegrees((facingRad * (180f / MathF.PI)) + 90f);
            canvas.Scale(scale, scale);

            var legJitter = reducedMotion ? 0f : MathF.Sin(timeSec * 15f) * 2.2f;

            // 8 graceful 3-joint curved orb-weaver silk legs (QuadTo femur -> tibia arc)
            _spiderLegPaint.StrokeWidth = 1.55f;
            _spiderLegPaint.Color = StyleLayoutHelper.BlendSrgb(palette.AccentColor1, SKColors.White.ToRgb(), 0.72f).ToSkColorWithAlpha(0.88f);
            for (var side = -1; side <= 1; side += 2)
            {
                for (var leg = 0; leg < 4; leg++)
                {
                    var yAnchor = -4.2f + (leg * 3.4f);
                    var spreadY = -19f + (leg * 12.5f) + ((leg % 2 == 0 ? 1 : -1) * legJitter);
                    var kneeX = side * (15.5f + (leg == 1 || leg == 2 ? 4.5f : 0f));
                    var kneeY = spreadY * 0.48f - 3.5f;
                    var tipX = side * (29f + (leg == 1 ? 6f : 0f));

                    _legPath.Rewind();
                    _legPath.MoveTo(side * 3.2f, yAnchor);
                    _legPath.QuadTo(kneeX, kneeY, tipX, spreadY);
                    canvas.DrawPath(_legPath, _spiderLegPaint);
                }
            }

            // Soft moonlight body halo
            _spiderBodyPaint.Color = palette.AccentColor2.ToSkColorWithAlpha(0.24f);
            canvas.DrawCircle(0f, 1.5f, 12f, _spiderBodyPaint);

            // Translucent silver-white cephalothorax & tapered abdomen + album iridescent dorsal marking
            _spiderBodyPaint.Color = new SKColor(0xFF, 0xFF, 0xFF, 238);
            canvas.DrawOval(0f, -4.4f, 4.1f, 5.0f, _spiderBodyPaint);
            canvas.DrawOval(0f, 6.8f, 5.8f, 8.2f, _spiderBodyPaint);

            _spiderBodyPaint.Color = palette.AccentColor2.ToSkColorWithAlpha(0.76f);
            canvas.DrawOval(0f, 6.8f, 2.2f, 4.2f, _spiderBodyPaint);

            // Tiny glistening front eyes
            _spiderBodyPaint.Color = palette.AccentColor1.ToSkColorWithAlpha(0.95f);
            canvas.DrawCircle(-1.6f, -8.2f, 1.1f, _spiderBodyPaint);
            canvas.DrawCircle(1.6f, -8.2f, 1.1f, _spiderBodyPaint);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    private void DrawCatenaryWeb(
        SKCanvas canvas,
        float cx,
        float cy,
        float radius,
        ReadOnlySpan<float> angles,
        float activeAngleRad,
        float twangAmp,
        FrameRenderPacket packet,
        bool isFullStage)
    {
        var timeSec = (float)packet.RenderTimeSeconds;
        var shimmerPhase = packet.ReducedMotion ? 0f : (timeSec * 0.45f) % MathF.Tau;
        var bassExpand = 1f + (_smoothedBass * 0.035f);

        for (var ringIdx = 0; ringIdx < RingRatios.Length; ringIdx++)
        {
            var ratio = RingRatios[ringIdx];
            var r = radius * ratio * bassExpand;
            var ringTone = StyleLayoutHelper.BlendSrgb(
                SKColors.White.ToRgb(),
                (ringIdx % 2 == 0) ? packet.Palette.AccentColor3 : packet.Palette.AccentColor1,
                0.22f + (0.28f * ratio));

            _ringPaint.StrokeWidth = ringIdx == RingRatios.Length - 1 ? 1.15f : 0.85f;
            _ringPaint.Color = ringTone.ToSkColorWithAlpha(0.14f + (0.12f * (1f - ratio)) + (_smoothedBass * 0.08f));

            _silkPath.Rewind();
            for (var i = 0; i < angles.Length; i++)
            {
                var a1 = angles[i];
                var a2 = angles[(i + 1) % angles.Length];
                var x1 = cx + (MathF.Cos(a1) * r);
                var y1 = cy + (MathF.Sin(a1) * r);
                var x2 = cx + (MathF.Cos(a2) * r);
                var y2 = cy + (MathF.Sin(a2) * r);

                var bow = (MathF.Cos(a1 - activeAngleRad) * 0.5f + 0.5f) * (twangAmp * 0.45f);
                var sag = 2.6f * ratio;
                var midX = ((x1 + x2) * 0.5f) * 0.93f + (cx * 0.07f);
                var midY = ((y1 + y2) * 0.5f) * 0.93f + (cy * 0.07f) + sag + bow;

                if (i == 0)
                {
                    _silkPath.MoveTo(x1, y1);
                }

                _silkPath.QuadTo(midX, midY, x2, y2);
            }

            _silkPath.Close();
            canvas.DrawPath(_silkPath, _ringPaint);

            // Micro-dew beads sparkling delicately along alternating ring/spoke intersections on full StageCanvas
            if (isFullStage && ringIdx is 1 or 3 or 4)
            {
                for (var s = 0; s < angles.Length; s += 2)
                {
                    var a = angles[s];
                    var glint = 0.5f + (0.5f * MathF.Sin(shimmerPhase + s + ringIdx));
                    _dewPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(42f + (95f * glint), 25f, 155f));
                    canvas.DrawCircle(cx + (MathF.Cos(a) * r), cy + (MathF.Sin(a) * r), 1.35f + (0.6f * glint), _dewPaint);
                }
            }
        }
    }

    private void DrawDewPearlNode(
        SKCanvas canvas,
        float nx,
        float ny,
        bool isActive,
        bool isPast,
        float twangProgress,
        FrameRenderPacket packet)
    {
        if (isActive)
        {
            if (!packet.ReducedMotion && twangProgress < 0.999f)
            {
                var ringR = (packet.IsCompactCard ? 6f : 9f) + ((packet.IsCompactCard ? 12f : 22f) * twangProgress * packet.SafePartyEnergy);
                _dewRingPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.70f * (1f - twangProgress));
                canvas.DrawCircle(nx, ny, ringR, _dewRingPaint);
            }

            var haloR = (packet.IsCompactCard ? 6.5f : 10.5f) + (_smoothedBeat * 4.5f);
            _dewPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.26f + (_smoothedBeat * 0.16f));
            canvas.DrawCircle(nx, ny, haloR, _dewPaint);

            var r = packet.IsCompactCard ? 3.5f : 5.2f;
            _dewPaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(0.92f);
            canvas.DrawCircle(nx, ny, r, _dewPaint);

            _dewPaint.Color = SKColors.White;
            canvas.DrawCircle(nx - (r * 0.25f), ny - (r * 0.25f), r * 0.45f, _dewPaint);
        }
        else
        {
            var r = packet.IsCompactCard ? 2.0f : 3.0f;
            _dewPaint.Color = (isPast ? packet.Palette.AccentColor1 : packet.Palette.AccentColor3).ToSkColorWithAlpha(isPast ? 0.80f : 0.38f);
            canvas.DrawCircle(nx, ny, r, _dewPaint);
            if (isPast)
            {
                _dewPaint.Color = SKColors.White.WithAlpha(185);
                canvas.DrawCircle(nx - (r * 0.26f), ny - (r * 0.26f), r * 0.36f, _dewPaint);
            }
        }
    }

    private static void DrawContextWhispers(
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        Core.Runtime.ActiveLyricSlice slice,
        float cx,
        float cy,
        float radius,
        float fontSize)
    {
        var packet = context.Packet;
        var canvas = context.Canvas;

        if (slice.PreviousLine is not null)
        {
            var prevY = Math.Max(fontSize * 0.9f, cy - radius - (fontSize * 0.45f));
            var prevSave = canvas.Save();
            try
            {
                canvas.Translate(cx, prevY);
                canvas.Scale(0.48f, 0.48f);
                context.Legibility.DrawLegibleText(canvas, slice.PreviousLine.RawText, 0f, 0f, SKTextAlign.Center, font, fontId, packet.Palette.AccentColor3, opacity: 0.22f, fonts: context.Fonts);
            }
            finally
            {
                canvas.RestoreToCount(prevSave);
            }
        }

        if (slice.NextLine is not null && !slice.NextLine.IsInstrumentalBreak)
        {
            var nextY = Math.Min(packet.ViewportHeightDip - 16f, cy + radius + (fontSize * 0.95f));
            var nextSave = canvas.Save();
            try
            {
                canvas.Translate(cx, nextY);
                canvas.Scale(0.46f, 0.46f);
                context.Legibility.DrawLegibleText(canvas, slice.NextLine.RawText, 0f, 0f, SKTextAlign.Center, font, fontId, packet.Palette.AccentColor1, opacity: 0.22f, fonts: context.Fonts);
            }
            finally
            {
                canvas.RestoreToCount(nextSave);
            }
        }
    }

    private static (float Cx, float Cy, float Radius) ComputeWebMetrics(FrameRenderPacket packet)
    {
        var (cx, cy) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.50f);
        var radius = MathF.Min(
            packet.ViewportWidthDip * (packet.IsCompactCard ? 0.42f : 0.37f),
            packet.ViewportHeightDip * (packet.IsCompactCard ? 0.44f : 0.39f));
        return (cx, cy, Math.Max(radius, 42f));
    }

    private static float ComputeSpokeAngle(int index, int spokeCount, uint seed)
    {
        const float startAngleRad = -115f * (MathF.PI / 180f);
        var stepRad = (2f * MathF.PI) / spokeCount;
        var jitterDeg = ((((seed >> (index % 16)) & 0xF) / 15f) - 0.5f) * 8f;
        return startAngleRad + (index * stepRad) + (jitterDeg * (MathF.PI / 180f));
    }
}
