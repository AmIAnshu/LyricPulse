using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #6 — Constellation (Celestial Observatory Overhaul):
/// Full-sky 2D scattered constellations across 6 astronomical archetypes (Corona Arc,
/// Dipper Loop, Golden Spiral, Cygnus Cross, Orion Hourglass, Scorpius Sweep),
/// multi-lobe cosmic nebula clouds &amp; Milky Way band, 3-tier parallax twinkling starfield,
/// shooting meteors, gently arched quadratic-Bezier light filaments with comet sparks,
/// 8-point JWST diffraction starbursts with sleek horizontal anamorphic lens streaks,
/// rotating astrolabe orbital reticles, supernova ignition shockwaves &amp; exact star-locked dots.
/// </summary>
public sealed class ConstellationStyleRenderer : IKineticStyleRenderer
{
    private const float IgnitionDurationSec = 0.72f;
    private const float LineFadeDurationSec = 1.15f;
    private const float MaxCameraParallaxDip = 22f;
    private const int BackgroundStarCountStage = 76;
    private const int BackgroundStarCountCard = 24;
    private const int StardustSparkCount = 12;

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _threadPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _threadGlowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _dashedGuidePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPathEffect _guideDashEffect = SKPathEffect.CreateDash([2.5f, 7.0f], 0f);
    private readonly SKPaint _starPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _ringPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _bgStarPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _nebulaPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _meteorPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _anamorphicPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath _diamondStarPath = new();
    private readonly SKPath _threadPath = new();
    private readonly SKImageFilter _glowBlur = SKImageFilter.CreateBlur(7.5f, 7.5f);
    private readonly SKImageFilter _wideGlowBlur = SKImageFilter.CreateBlur(16.0f, 16.0f);
    private readonly SKColor[] _grad2Colors = new SKColor[2];
    private readonly float[] _grad2Stops = [0f, 1f];
    private readonly SKColor[] _nebulaColors = new SKColor[3];
    private readonly float[] _nebulaStops = [0f, 0.5f, 1f];
    private readonly SKColor[] _streakColors = new SKColor[3];
    private readonly float[] _streakStops = [0f, 0.5f, 1f];
    private readonly List<StarNode> _nodes = new(16);
    private readonly List<StarNode> _ghostNodes = new(16);
    private readonly List<SKPoint> _renderedTargets = new(16);

    private SpringState _flareScale = SpringState.AtRest(1.0f);
    private SpringState _labelLift = SpringState.AtRest(0f);
    private SpringState _camX = SpringState.AtRest(0f);
    private SpringState _camY = SpringState.AtRest(0f);
    private float _highlightFade = 1.0f;
    private float _ignitionElapsedSec = IgnitionDurationSec;
    private float _lineElapsedSec = LineFadeDurationSec;
    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private int _lastLineIndex = -1;
    private int _renderedLineIndex = -1;
    private SKRect _renderedRegion;
    private float _lastWordDurationSec = 0.32f;
    private bool _disposed;

    public ConstellationStyleRenderer()
    {
        _dashedGuidePaint.PathEffect = _guideDashEffect;
    }

    public OverlayStyleId StyleId => OverlayStyleId.Constellation;

    public void ResetState()
    {
        _flareScale = SpringState.AtRest(1.0f);
        _labelLift = SpringState.AtRest(0f);
        _camX = SpringState.AtRest(0f);
        _camY = SpringState.AtRest(0f);
        _highlightFade = 1.0f;
        _ignitionElapsedSec = IgnitionDurationSec;
        _lineElapsedSec = LineFadeDurationSec;
        _lastWordKey = (-1, -1);
        _lastLineIndex = -1;
        _renderedLineIndex = -1;
        _renderedRegion = SKRect.Empty;
        _renderedTargets.Clear();
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
        var activeWordIdx = slice.CurrentLine.Words.IsEmpty
            ? 0
            : Math.Clamp(slice.ActiveWordIndex, 0, slice.CurrentLine.Words.Length - 1);

        if (!slice.CurrentLine.Words.IsEmpty)
        {
            var word = slice.CurrentLine.Words[activeWordIdx];
            _lastWordDurationSec = Math.Clamp((float)(word.EndTime - word.StartTime).TotalSeconds, 0.08f, 2.5f);
        }

        var key = (slice.ActiveLineIndex, activeWordIdx);
        if (slice.ActiveLineIndex != _lastLineIndex)
        {
            _lastLineIndex = slice.ActiveLineIndex;
            _lineElapsedSec = packet.ReducedMotion ? LineFadeDurationSec : 0f;
        }

        if (key != _lastWordKey)
        {
            _lastWordKey = key;
            if (packet.ReducedMotion)
            {
                _flareScale = SpringState.AtRest(1.0f);
                _labelLift = SpringState.AtRest(0f);
                _highlightFade = 0f;
                _ignitionElapsedSec = IgnitionDurationSec;
            }
            else
            {
                _flareScale = new SpringState(Value: 0.30f, Velocity: 0f, Target: 1.14f + ((packet.SafePartyEnergy - 1.0f) * 0.28f));
                _labelLift = new SpringState(Value: 9.0f, Velocity: 0f, Target: 0f);
                _highlightFade = 0f;
                _ignitionElapsedSec = 0f;
            }
        }

        var fadeRate = packet.ReducedMotion ? (1f / 0.14f) : (1f / 0.16f);
        if (_highlightFade < 1f)
        {
            _highlightFade = Math.Min(1f, _highlightFade + (dt * fadeRate));
        }

        if (packet.ReducedMotion)
        {
            _flareScale = SpringState.AtRest(1.0f);
            _labelLift = SpringState.AtRest(0f);
            _camX = SpringState.AtRest(0f);
            _camY = SpringState.AtRest(0f);
            _ignitionElapsedSec = IgnitionDurationSec;
            _lineElapsedSec = LineFadeDurationSec;
            _aesthetics.ResetDot();
            return _highlightFade < 0.999f;
        }

        if (_ignitionElapsedSec < IgnitionDurationSec)
        {
            _ignitionElapsedSec = Math.Min(IgnitionDurationSec, _ignitionElapsedSec + dt);
        }

        if (_lineElapsedSec < LineFadeDurationSec)
        {
            _lineElapsedSec = Math.Min(LineFadeDurationSec, _lineElapsedSec + dt);
        }

        var iconSpring = SpringTokens.IconPop.WithTempoCompression(_lastWordDurationSec);
        var wordSpring = SpringTokens.WordEnter.WithTempoCompression(_lastWordDurationSec);
        _flareScale = _flareScale.Step(iconSpring, dt);
        _labelLift = _labelLift.Step(wordSpring, dt);

        var region = ComputeSkyRegion(packet);
        float targetX;
        float targetY;
        if (_renderedLineIndex == slice.CurrentLine.Index &&
            MathF.Abs(_renderedRegion.Width - region.Width) < 2.0f &&
            MathF.Abs(_renderedRegion.Height - region.Height) < 2.0f &&
            activeWordIdx >= 0 &&
            activeWordIdx < _renderedTargets.Count)
        {
            targetX = _renderedTargets[activeWordIdx].X;
            targetY = _renderedTargets[activeWordIdx].Y;
        }
        else
        {
            var count = slice.CurrentLine.Words.IsEmpty ? 1 : slice.CurrentLine.Words.Length;
            (targetX, targetY) = EstimateStarAnchor(slice.CurrentLine, activeWordIdx, count, region);
        }

        var parallaxFactor = packet.IsCompactCard ? 0.04f : 0.095f;
        var targetCamX = Math.Clamp((region.MidX - targetX) * parallaxFactor, -MaxCameraParallaxDip, MaxCameraParallaxDip);
        var targetCamY = Math.Clamp((region.MidY - targetY) * parallaxFactor, -MaxCameraParallaxDip, MaxCameraParallaxDip);
        _camX = _camX.WithTarget(targetCamX).Step(SpringTokens.LineShift, dt);
        _camY = _camY.WithTarget(targetCamY).Step(SpringTokens.LineShift, dt);

        // Local sky coordinates (Render draws DrawFollowingDot inside foreground camera transform -> zero double parallax!)
        var dotActive = packet.ShowFollowingDot && _aesthetics.AdvanceDot(
            key,
            targetX,
            targetY,
            dt,
            reducedMotion: false,
            _lastWordDurationSec);

        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        return !_flareScale.IsAtRest ||
               !_labelLift.IsAtRest ||
               !_camX.IsAtRest ||
               !_camY.IsAtRest ||
               _ignitionElapsedSec < IgnitionDurationSec ||
               _lineElapsedSec < LineFadeDurationSec ||
               dotActive ||
               _highlightFade < 0.999f ||
               isPlaying;
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
        var fontSize = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 0.60f);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var region = ComputeSkyRegion(packet);

        BuildDeterministicStarfield(slice.CurrentLine, region, font, context.Fonts, _nodes, spreadScale: 1.0f);
        if (_nodes.Count == 0)
        {
            return;
        }

        // Cache exact relaxed coordinates so next Advance() locks parallax & dot cleanly onto rendered stars
        _renderedLineIndex = slice.CurrentLine.Index;
        _renderedRegion = region;
        _renderedTargets.Clear();
        for (var i = 0; i < _nodes.Count; i++)
        {
            _renderedTargets.Add(new SKPoint(_nodes[i].X, _nodes[i].Y));
        }

        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, _nodes.Count - 1);
        var camX = packet.ReducedMotion ? 0f : _camX.Value;
        var camY = packet.ReducedMotion ? 0f : _camY.Value;

        // Layer 0: Multi-lobe cosmic nebula clouds, 3-tier parallax starfield & meteors (Stage & Cards only — never DesktopOverlay)
        if (packet.SurfaceMode != StageRenderSurfaceMode.DesktopOverlay)
        {
            DrawCosmicNebulaField(context.Canvas, packet, _nodes[activeIdx], camX, camY);
            DrawBackgroundStarfield(context.Canvas, packet, camX, camY);
            if (!packet.IsCompactCard && !packet.ReducedMotion)
            {
                DrawShootingStars(context.Canvas, packet);
            }
        }
        else
        {
            context.Legibility.DrawAmbientScrim(
                context.Canvas,
                region,
                fontSize,
                packet.Palette.ScrimTint,
                packet.ScrimIntensity,
                packet.HighContrastScrim);
        }

        // Layer 1: Ghosted previous-line constellation drifting gently into deep parallax space
        if (!packet.IsCompactCard && slice.PreviousLine is not null)
        {
            var ghostRegion = new SKRect(
                region.Left + (region.Width * 0.04f),
                region.Top + (region.Height * 0.04f),
                region.Right - (region.Width * 0.04f),
                region.Bottom - (region.Height * 0.04f));
            BuildDeterministicStarfield(slice.PreviousLine, ghostRegion, font, context.Fonts, _ghostNodes, spreadScale: 0.90f);
            DrawGhostConstellation(context.Canvas, packet, camX * 0.55f, camY * 0.55f);
        }

        // Layer 2: Foreground active constellation (with smooth camera parallax)
        var fgSave = context.Canvas.Save();
        try
        {
            if (MathF.Abs(camX) > 0.05f || MathF.Abs(camY) > 0.05f)
            {
                context.Canvas.Translate(camX, camY);
            }

            // Secondary star-atlas triangulation cross-braces
            DrawTriangulationGuides(context.Canvas, packet);

            // Curved gossamer connecting threads + comet spark head & ember trail
            var rawProgress = packet.ReducedMotion ? 1.0f : (float)Math.Clamp(slice.WordProgress / 0.52, 0.0, 1.0);
            var traceProgress = packet.ReducedMotion ? 1.0f : Ease.Enter(rawProgress);
            var lineSeed = StyleLayoutHelper.DeterministicHash(slice.CurrentLine.Index, slice.CurrentLine.RawText);
            DrawConstellationThreads(context.Canvas, packet, lineSeed, activeIdx, traceProgress);

            // Supernova ignition shockwave ring & radial stardust burst at active star
            var activeNode = _nodes[activeIdx];
            if (!packet.ReducedMotion && _ignitionElapsedSec < IgnitionDurationSec)
            {
                DrawIgnitionSupernova(context.Canvas, packet, activeNode, slice.CurrentLine.Index, activeIdx);
            }

            var highlightEased = Ease.Highlight(_highlightFade);
            for (var i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                var isPast = i < activeIdx;
                var isActive = i == activeIdx;
                DrawStarAndLabel(in context, font, fontId, node, i, lineSeed, isPast, isActive, highlightEased, slice.WordProgress, camX, camY);
            }

            if (!packet.ReducedMotion && packet.ShowFollowingDot)
            {
                _aesthetics.DrawFollowingDot(
                    context.Canvas,
                    packet,
                    activeNode.X,
                    activeNode.Y,
                    Math.Clamp(fontSize * 0.13f, 2.8f, 6.0f));
            }
        }
        finally
        {
            context.Canvas.RestoreToCount(fgSave);
        }

        StyleLayoutHelper.DrawSecondarySubline(
            in context,
            slice.CurrentLine,
            region.MidX,
            Math.Min(packet.ViewportHeightDip - 16f, region.Bottom + Math.Max(14f, fontSize * 0.50f)),
            fontSize * 1.12f);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _aesthetics.Dispose();
        _threadPaint.Dispose();
        _threadGlowPaint.Dispose();
        _dashedGuidePaint.Dispose();
        _guideDashEffect.Dispose();
        _starPaint.Dispose();
        _ringPaint.Dispose();
        _bgStarPaint.Dispose();
        _nebulaPaint.Dispose();
        _meteorPaint.Dispose();
        _anamorphicPaint.Dispose();
        _diamondStarPath.Dispose();
        _threadPath.Dispose();
        _glowBlur.Dispose();
        _wideGlowBlur.Dispose();
    }

    internal static SKRect ComputeSkyRegion(FrameRenderPacket packet)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;

        if (packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.48f);
            var rw = Math.Clamp(w * 0.78f, 140f, Math.Max(140f, w - 32f));
            var rh = Math.Clamp(h * 0.56f, 96f, Math.Max(96f, h - 32f));
            return StyleLayoutHelper.ClampLayerRectToViewport(anchorX, anchorY, rw, rh, w, h);
        }

        if (packet.IsCompactCard)
        {
            return new SKRect(w * 0.06f, h * 0.10f, w * 0.94f, h * 0.90f);
        }

        // Full-sky observatory bounds keeping TopGlassBar (~86px) & lower hero editorial caption cleanly clear
        var left = w * 0.055f;
        var right = w * 0.945f;
        var top = packet.BottomFadeToBlack ? Math.Max(h * 0.16f, 88f) : h * 0.10f;
        var bottom = packet.BottomFadeToBlack ? Math.Max(top + 120f, h * 0.77f) : h * 0.89f;
        return new SKRect(left, top, Math.Max(left + 120f, right), Math.Max(top + 96f, bottom));
    }

    internal static void BuildDeterministicStarfield(
        LyricLine line,
        SKRect region,
        SKFont font,
        FontAssetRegistry fonts,
        List<StarNode> destination,
        float spreadScale)
    {
        destination.Clear();
        var words = line.Words;
        var count = words.IsEmpty ? (string.IsNullOrWhiteSpace(line.RawText) ? 0 : 1) : words.Length;
        if (count == 0)
        {
            return;
        }

        var seed = StyleLayoutHelper.DeterministicHash(line.Index, line.RawText);
        var archetype = (int)(seed % 6u);
        var flipX = ((seed >> 3) & 1u) != 0;
        var flipY = ((seed >> 4) & 1u) != 0;

        var padX = Math.Clamp(region.Width * 0.07f, 14f, 52f);
        var padY = Math.Clamp(region.Height * 0.10f, 12f, 44f);
        var usableW = Math.Max(48f, region.Width - (padX * 2f));
        var usableH = Math.Max(40f, region.Height - (padY * 2f));
        var minStarDist = Math.Max(font.Size * 2.35f * spreadScale, 44f);

        for (var i = 0; i < count; i++)
        {
            var text = words.IsEmpty ? line.RawText : words[i].Text;
            var visual = words.IsEmpty ? null : words[i].Visual;
            var (nx, ny) = GenerateArchetypeNormalizedPoint(archetype, i, count, seed, flipX, flipY);

            var measured = TextShaperService.MeasureWidth(text, font, fonts);
            var safeHalfW = Math.Min((measured * 0.54f) + 8f, usableW * 0.42f);
            var minX = region.Left + Math.Max(padX, safeHalfW);
            var maxX = Math.Max(minX, region.Right - Math.Max(padX, safeHalfW));
            var minY = region.Top + padY;
            var maxY = Math.Max(minY, region.Bottom - padY);

            var x = Math.Clamp(region.MidX + (nx * usableW * spreadScale), minX, maxX);
            var y = Math.Clamp(region.MidY + (ny * usableH * spreadScale), minY, maxY);
            destination.Add(new StarNode(text, x, y, measured, visual, LabelAbove: false));
        }

        // Label-box-aware 2D relaxation: keeps both star icons & wide word labels cleanly separated
        var labelBandH = font.Size * 2.05f;
        for (var iter = 0; iter < 8; iter++)
        {
            for (var i = 0; i < destination.Count; i++)
            {
                for (var j = i + 1; j < destination.Count; j++)
                {
                    var a = destination[i];
                    var b = destination[j];
                    var dx = b.X - a.X;
                    var dy = b.Y - a.Y;
                    var dist = MathF.Sqrt((dx * dx) + (dy * dy));

                    var reqX = ((a.MeasuredWidth + b.MeasuredWidth) * 0.52f) + 20f;
                    var horizOverlap = MathF.Abs(dx) < reqX && MathF.Abs(dy) < labelBandH;

                    if (dist < minStarDist || horizOverlap)
                    {
                        var safeDist = Math.Max(dist, 0.001f);
                        var nx = dist > 0.001f ? dx / safeDist : (((i + j) % 2 == 0) ? 0.6f : -0.6f);
                        var ny = dist > 0.001f ? dy / safeDist : (((i % 2) == 0) ? -0.8f : 0.8f);

                        var radialPush = dist < minStarDist ? (minStarDist - dist) * 0.36f : 0f;
                        var verticalPush = horizOverlap ? (labelBandH - MathF.Abs(dy)) * 0.36f * (dy >= 0f ? 1f : -1f) : 0f;
                        var horizontalPush = horizOverlap ? (reqX - MathF.Abs(dx)) * 0.14f * (dx >= 0f ? 1f : -1f) : 0f;

                        var shiftX = (nx * radialPush * 0.65f) + horizontalPush;
                        var shiftY = (ny * radialPush) + verticalPush;

                        var aHalfW = Math.Min((a.MeasuredWidth * 0.54f) + 8f, usableW * 0.42f);
                        var bHalfW = Math.Min((b.MeasuredWidth * 0.54f) + 8f, usableW * 0.42f);

                        var aMinX = region.Left + Math.Max(padX, aHalfW);
                        var aMaxX = Math.Max(aMinX, region.Right - Math.Max(padX, aHalfW));
                        var bMinX = region.Left + Math.Max(padX, bHalfW);
                        var bMaxX = Math.Max(bMinX, region.Right - Math.Max(padX, bHalfW));
                        var minY = region.Top + padY;
                        var maxY = Math.Max(minY, region.Bottom - padY);

                        destination[i] = a with
                        {
                            X = Math.Clamp(a.X - shiftX, aMinX, aMaxX),
                            Y = Math.Clamp(a.Y - shiftY, minY, maxY)
                        };
                        destination[j] = b with
                        {
                            X = Math.Clamp(b.X + shiftX, bMinX, bMaxX),
                            Y = Math.Clamp(b.Y + shiftY, minY, maxY)
                        };
                    }
                }
            }
        }

        // Smart atlas label placement: flip above if near bottom edge or above a lower close neighbor
        for (var i = 0; i < destination.Count; i++)
        {
            var node = destination[i];
            var nearBottom = node.Y > region.Top + (region.Height * 0.72f);
            var neighborDirectlyBelow = false;
            for (var j = 0; j < destination.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                var other = destination[j];
                if (other.Y > node.Y && other.Y - node.Y < font.Size * 2.65f && MathF.Abs(other.X - node.X) < (node.MeasuredWidth * 0.6f) + 30f)
                {
                    neighborDirectlyBelow = true;
                    break;
                }
            }

            var nearTop = node.Y < region.Top + (region.Height * 0.16f);
            destination[i] = node with { LabelAbove = (nearBottom || neighborDirectlyBelow) && !nearTop };
        }
    }

    private void DrawCosmicNebulaField(
        SKCanvas canvas,
        FrameRenderPacket packet,
        StarNode activeStar,
        float camX,
        float camY)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        var bass = packet.ReducedMotion ? 0f : packet.EffectiveAudioMetrics.BassEnergy;
        var baseAlpha = (packet.IsCompactCard ? 0.08f : 0.12f) + (bass * 0.08f);

        // 1. Diagonal Milky Way dust veil
        _nebulaColors[0] = SKColors.Transparent;
        _nebulaColors[1] = StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor1, packet.Palette.AccentColor2, 0.50f).ToSkColorWithAlpha(baseAlpha);
        _nebulaColors[2] = SKColors.Transparent;

        var driftX = camX * 0.16f;
        var driftY = camY * 0.16f;
        using (var bandShader = SKShader.CreateLinearGradient(
            new SKPoint((w * 0.12f) + driftX, (h * 0.10f) + driftY),
            new SKPoint((w * 0.88f) + driftX, (h * 0.88f) + driftY),
            _nebulaColors,
            _nebulaStops,
            SKShaderTileMode.Clamp))
        {
            _nebulaPaint.Shader = bandShader;
            _nebulaPaint.Color = SKColors.White;
            try
            {
                canvas.DrawRect(0f, 0f, w, h, _nebulaPaint);
            }
            finally
            {
                _nebulaPaint.Shader = null;
            }
        }

        // 2. Soft chromatic gas cloud bloom following active star quadrant
        if (!packet.IsCompactCard)
        {
            var bloomR = MathF.Max(w, h) * (0.28f + (bass * 0.08f));
            var bx = activeStar.X + (camX * 0.55f);
            var by = activeStar.Y + (camY * 0.55f);
            _grad2Colors[0] = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.16f + (bass * 0.11f));
            _grad2Colors[1] = SKColors.Transparent;
            using var bloomShader = SKShader.CreateRadialGradient(
                new SKPoint(bx, by),
                Math.Max(bloomR, 20f),
                _grad2Colors,
                _grad2Stops,
                SKShaderTileMode.Clamp);

            _nebulaPaint.Shader = bloomShader;
            try
            {
                canvas.DrawCircle(bx, by, bloomR, _nebulaPaint);
            }
            finally
            {
                _nebulaPaint.Shader = null;
            }
        }
    }

    private void DrawBackgroundStarfield(SKCanvas canvas, FrameRenderPacket packet, float camX, float camY)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        if (w <= 4f || h <= 4f)
        {
            return;
        }

        var count = packet.IsCompactCard ? BackgroundStarCountCard : BackgroundStarCountStage;
        var time = packet.ReducedMotion ? 0f : (float)packet.RenderTimeSeconds;
        var treble = packet.ReducedMotion ? 0f : packet.EffectiveAudioMetrics.TrebleEnergy;
        var fadeBottomY = packet.BottomFadeToBlack ? h * 0.84f : h;

        const float phi1 = 0.61803398875f;
        const float phi2 = 0.32471795724f;

        for (var i = 0; i < count; i++)
        {
            var ux = ((i * phi1) + 0.137f) % 1.0f;
            var uy = ((i * phi2) + 0.293f) % 1.0f;

            var tier = i % 3;
            var depth = tier switch
            {
                0 => 0.18f,
                1 => 0.42f,
                _ => 0.78f
            };

            var sx = WrapPositive((ux * w) + (camX * depth), w);
            var sy = WrapPositive((uy * fadeBottomY) + (camY * depth), fadeBottomY);

            var twinkleSpeed = 1.0f + ((i % 7) * 0.42f);
            var twinklePhase = i * 1.91f;
            var twinkle = packet.ReducedMotion
                ? 0.70f
                : 0.42f + (0.46f * (0.5f + (0.5f * MathF.Sin((time * twinkleSpeed) + twinklePhase)))) + (treble * 0.24f);

            var edgeFade = packet.BottomFadeToBlack && sy > h * 0.66f
                ? Math.Clamp(1f - ((sy - (h * 0.66f)) / (h * 0.20f)), 0f, 1f)
                : 1f;

            var alpha = Math.Clamp((tier == 2 ? 0.72f : tier == 1 ? 0.48f : 0.30f) * twinkle * edgeFade, 0f, 0.94f);
            if (alpha <= 0.01f)
            {
                continue;
            }

            // Astronomical color temperature mix (cool blue-white, warm gold-white, and track accents)
            var tint = (i % 6) switch
            {
                0 => packet.Palette.AccentColor1,
                1 => new RgbColor(196, 224, 255), // spectral B/A blue-white
                2 => packet.Palette.AccentColor2,
                3 => new RgbColor(255, 232, 198), // spectral G/K warm gold-white
                4 => packet.Palette.AccentColor3,
                _ => new RgbColor(248, 250, 255)
            };

            var radius = (tier == 2 ? 2.15f : tier == 1 ? 1.45f : 0.95f) * (packet.IsCompactCard ? 0.75f : 1.0f);
            if (i % 7 == 0 && !packet.IsCompactCard)
            {
                DrawPinchedStar(canvas, sx, sy, radius * 2.25f, tint.ToSkColorWithAlpha(alpha));
            }
            else
            {
                _bgStarPaint.Color = tint.ToSkColorWithAlpha(alpha);
                canvas.DrawCircle(sx, sy, radius, _bgStarPaint);
            }
        }
    }

    private void DrawShootingStars(SKCanvas canvas, FrameRenderPacket packet)
    {
        const double periodSec = 4.5;
        const double streakDurationSec = 0.78;

        var t = packet.RenderTimeSeconds;
        var cycleIndex = (long)Math.Floor(t / periodSec);
        var phaseSec = t - (cycleIndex * periodSec);
        if (phaseSec < 0.0 || phaseSec > streakDurationSec)
        {
            return;
        }

        var progress = (float)(phaseSec / streakDurationSec);
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        var hash = StyleLayoutHelper.DeterministicHash((int)(cycleIndex & 0x7FFFFFFF), "meteor");

        var startX = w * (0.12f + (((hash & 0xFFu) / 255f) * 0.72f));
        var startY = h * (0.06f + ((((hash >> 8) & 0xFFu) / 255f) * 0.25f));
        var dirX = ((hash >> 16) & 1u) == 0 ? 1f : -1f;
        var travelX = dirX * w * (0.20f + ((((hash >> 17) & 0x3Fu) / 63f) * 0.16f));
        var travelY = h * (0.14f + ((((hash >> 23) & 0x1Fu) / 31f) * 0.11f));

        var eased = Ease.Enter(progress);
        var headX = startX + (travelX * eased);
        var headY = startY + (travelY * eased);

        var tailLag = Math.Max(0f, eased - 0.30f);
        var tailX = startX + (travelX * tailLag);
        var tailY = startY + (travelY * tailLag);

        var envelope = MathF.Sin(progress * MathF.PI);
        var alpha = Math.Clamp(envelope * 0.88f, 0f, 1f);
        if (alpha <= 0.01f)
        {
            return;
        }

        _grad2Colors[0] = SKColors.White.WithAlpha((byte)(alpha * 250f));
        _grad2Colors[1] = packet.Palette.AccentColor1.ToSkColorWithAlpha(0f);

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(headX, headY),
            new SKPoint(tailX, tailY),
            _grad2Colors,
            _grad2Stops,
            SKShaderTileMode.Clamp);

        _meteorPaint.Shader = shader;
        _meteorPaint.StrokeWidth = 2.1f;
        _meteorPaint.Color = SKColors.White;
        try
        {
            canvas.DrawLine(headX, headY, tailX, tailY, _meteorPaint);
        }
        finally
        {
            _meteorPaint.Shader = null;
        }

        DrawPinchedStar(canvas, headX, headY, 5.2f * envelope, SKColors.White.WithAlpha((byte)(alpha * 255f)));
    }

    private void DrawGhostConstellation(SKCanvas canvas, FrameRenderPacket packet, float parallaxX, float parallaxY)
    {
        if (_ghostNodes.Count < 2)
        {
            return;
        }

        var lineT = packet.ReducedMotion ? 1f : Math.Clamp(_lineElapsedSec / LineFadeDurationSec, 0f, 1f);
        var fadeFactor = 1.0f - (0.44f * Ease.Enter(lineT));
        var drift = packet.ReducedMotion ? 0f : -12f * Ease.Enter(lineT);

        var save = canvas.Save();
        try
        {
            canvas.Translate(parallaxX + (drift * 0.6f), parallaxY + drift);

            _threadPaint.StrokeWidth = 0.80f;
            _threadPaint.Color = packet.Palette.AccentColor3.ToSkColorWithAlpha(0.16f * fadeFactor);
            for (var i = 1; i < _ghostNodes.Count; i++)
            {
                canvas.DrawLine(_ghostNodes[i - 1].X, _ghostNodes[i - 1].Y, _ghostNodes[i].X, _ghostNodes[i].Y, _threadPaint);
            }

            foreach (var node in _ghostNodes)
            {
                DrawPinchedStar(canvas, node.X, node.Y, 3.6f, packet.Palette.AccentColor3.ToSkColorWithAlpha(0.24f * fadeFactor));
            }
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    private void DrawTriangulationGuides(SKCanvas canvas, FrameRenderPacket packet)
    {
        if (_nodes.Count < 3)
        {
            return;
        }

        var maxDist = packet.ViewportWidthDip * (packet.IsCompactCard ? 0.44f : 0.34f);
        var maxDistSq = maxDist * maxDist;
        _dashedGuidePaint.StrokeWidth = packet.IsCompactCard ? 0.65f : 0.85f;
        _dashedGuidePaint.Color = packet.Palette.AccentColor3.ToSkColorWithAlpha(packet.ReducedMotion ? 0.13f : 0.16f);

        var drawn = 0;
        for (var i = 0; i < _nodes.Count && drawn < 11; i++)
        {
            for (var j = i + 2; j < _nodes.Count && drawn < 11; j++)
            {
                var dx = _nodes[j].X - _nodes[i].X;
                var dy = _nodes[j].Y - _nodes[i].Y;
                var d2 = (dx * dx) + (dy * dy);
                if (d2 > 1400f && d2 <= maxDistSq)
                {
                    canvas.DrawLine(_nodes[i].X, _nodes[i].Y, _nodes[j].X, _nodes[j].Y, _dashedGuidePaint);
                    drawn++;
                }
            }
        }
    }

    private void DrawConstellationThreads(
        SKCanvas canvas,
        FrameRenderPacket packet,
        uint lineSeed,
        int activeIdx,
        float traceProgress)
    {
        if (_nodes.Count < 2)
        {
            return;
        }

        var energy = packet.SafePartyEnergy;
        for (var i = 1; i < _nodes.Count; i++)
        {
            var a = _nodes[i - 1];
            var b = _nodes[i];
            var (ctrlX, ctrlY) = ComputeThreadControlPoint(a.X, a.Y, b.X, b.Y, lineSeed, i);

            if (packet.ReducedMotion)
            {
                var lit = i <= activeIdx;
                _threadPaint.StrokeWidth = lit ? 1.6f : 0.95f;
                _threadPaint.Color = (lit ? packet.Palette.AccentColor2 : packet.Palette.AccentColor3).ToSkColorWithAlpha(lit ? 0.74f : 0.24f);
                _threadPath.Rewind();
                _threadPath.MoveTo(a.X, a.Y);
                _threadPath.QuadTo(ctrlX, ctrlY, b.X, b.Y);
                canvas.DrawPath(_threadPath, _threadPaint);
                continue;
            }

            if (i < activeIdx)
            {
                DrawGlowingCurvedSegment(canvas, a.X, a.Y, ctrlX, ctrlY, b.X, b.Y, 1.0f, packet.Palette.AccentColor1, packet.Palette.AccentColor2, 0.64f, 1.45f);
            }
            else if (i == activeIdx)
            {
                DrawGlowingCurvedSegment(
                    canvas,
                    a.X,
                    a.Y,
                    ctrlX,
                    ctrlY,
                    b.X,
                    b.Y,
                    traceProgress,
                    packet.Palette.AccentColor1,
                    packet.Palette.AccentColor2,
                    0.96f,
                    2.0f * Math.Clamp(energy, 0.85f, 1.32f));

                if (traceProgress is > 0.04f and < 0.98f)
                {
                    var head = EvalQuadBezier(a.X, a.Y, ctrlX, ctrlY, b.X, b.Y, traceProgress);
                    DrawPinchedStar(canvas, head.X, head.Y, packet.IsCompactCard ? 5.2f : 8.8f, SKColors.White);
                    for (var m = 1; m <= 4; m++)
                    {
                        var mt = Math.Max(0f, traceProgress - (m * 0.055f));
                        var mote = EvalQuadBezier(a.X, a.Y, ctrlX, ctrlY, b.X, b.Y, mt);
                        _starPaint.Color = (m % 2 == 0 ? packet.Palette.AccentColor2 : packet.Palette.AccentColor1).ToSkColorWithAlpha(0.70f - (m * 0.14f));
                        canvas.DrawCircle(mote.X, mote.Y, 2.5f - (m * 0.40f), _starPaint);
                    }
                }
            }
            else
            {
                _threadPaint.StrokeWidth = 0.85f;
                _threadPaint.Color = packet.Palette.AccentColor3.ToSkColorWithAlpha(0.18f);
                _threadPath.Rewind();
                _threadPath.MoveTo(a.X, a.Y);
                _threadPath.QuadTo(ctrlX, ctrlY, b.X, b.Y);
                canvas.DrawPath(_threadPath, _threadPaint);
            }
        }
    }

    private void DrawGlowingCurvedSegment(
        SKCanvas canvas,
        float x0,
        float y0,
        float cx,
        float cy,
        float x1,
        float y1,
        float tMax,
        RgbColor coreColor,
        RgbColor auraColor,
        float alpha,
        float strokeW)
    {
        var clampedT = Math.Clamp(tMax, 0f, 1f);
        if (clampedT <= 0.005f)
        {
            return;
        }

        _threadPath.Rewind();
        _threadPath.MoveTo(x0, y0);
        if (clampedT >= 0.999f)
        {
            _threadPath.QuadTo(cx, cy, x1, y1);
        }
        else
        {
            // De Casteljau quadratic Bezier prefix [0, clampedT]
            var subCx = x0 + ((cx - x0) * clampedT);
            var subCy = y0 + ((cy - y0) * clampedT);
            var end = EvalQuadBezier(x0, y0, cx, cy, x1, y1, clampedT);
            _threadPath.QuadTo(subCx, subCy, end.X, end.Y);
        }

        _threadGlowPaint.StrokeWidth = strokeW * 3.8f;
        _threadGlowPaint.Color = auraColor.ToSkColorWithAlpha(alpha * 0.38f);
        _threadGlowPaint.ImageFilter = _glowBlur;
        try
        {
            canvas.DrawPath(_threadPath, _threadGlowPaint);
        }
        finally
        {
            _threadGlowPaint.ImageFilter = null;
        }

        _threadPaint.StrokeWidth = strokeW;
        _threadPaint.Color = StyleLayoutHelper.BlendSrgb(coreColor, new RgbColor(255, 255, 255), 0.38f).ToSkColorWithAlpha(alpha);
        canvas.DrawPath(_threadPath, _threadPaint);
    }

    private void DrawIgnitionSupernova(
        SKCanvas canvas,
        FrameRenderPacket packet,
        StarNode star,
        int lineIndex,
        int wordIndex)
    {
        var t = Math.Clamp(_ignitionElapsedSec / IgnitionDurationSec, 0f, 1f);
        var eased = 1f - MathF.Pow(1f - t, 3f);
        var fade = (1f - t) * (1f - t);
        if (fade <= 0.01f)
        {
            return;
        }

        var energy = packet.SafePartyEnergy;
        var scale = packet.IsCompactCard ? 0.55f : 1.0f;

        // 1. Dual supernova shockwave rings
        var ringR = (9f + (48f * eased * Math.Clamp(energy, 0.8f, 1.4f))) * scale;
        _ringPaint.StrokeWidth = Math.Max(0.75f, (2.4f * (1f - t)) * scale);
        _ringPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(fade * 0.64f);
        canvas.DrawCircle(star.X, star.Y, ringR, _ringPaint);

        if (t > 0.14f)
        {
            var innerR = ringR * 0.62f;
            _ringPaint.StrokeWidth = Math.Max(0.6f, 1.3f * (1f - t) * scale);
            _ringPaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(fade * 0.42f);
            canvas.DrawCircle(star.X, star.Y, innerR, _ringPaint);
        }

        // 2. Deterministic radial stardust sparks
        var sparkSeed = StyleLayoutHelper.DeterministicHash(lineIndex * 31 + wordIndex, star.Text);
        var sparkCount = packet.IsCompactCard ? 6 : StardustSparkCount;
        for (var i = 0; i < sparkCount; i++)
        {
            var angleJitter = ((((sparkSeed >> ((i * 3) % 20)) & 0xFu) / 15f) - 0.5f) * 0.45f;
            var angle = ((i / (float)sparkCount) * MathF.Tau) + angleJitter;
            var reach = (14f + (((sparkSeed >> ((i * 5) % 20)) & 0x1Fu) * 1.25f)) * scale * eased * Math.Clamp(energy, 0.85f, 1.35f);
            var sx = star.X + (MathF.Cos(angle) * reach);
            var sy = star.Y + (MathF.Sin(angle) * reach);
            var sparkSize = (2.8f - (1.4f * t)) * scale;

            var color = (i & 1) == 0 ? SKColors.White : packet.Palette.AccentColor1.ToSkColor();
            _starPaint.Color = color.WithAlpha((byte)Math.Clamp(fade * 240f, 0f, 255f));
            canvas.DrawCircle(sx, sy, Math.Max(0.7f, sparkSize), _starPaint);
        }
    }

    private void DrawStarAndLabel(
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        StarNode node,
        int nodeIndex,
        uint lineSeed,
        bool isPast,
        bool isActive,
        float highlightEased,
        double wordProgress,
        float camX,
        float camY)
    {
        var canvas = context.Canvas;
        var packet = context.Packet;
        var audio = packet.EffectiveAudioMetrics;
        var audioLift = (audio.BeatPulse * 0.28f) + (audio.BassEnergy * 0.16f);
        var flare = (packet.ReducedMotion ? 1.0f : _flareScale.Value) * (isActive ? (1.0f + audioLift) : 1.0f);
        var time = packet.ReducedMotion ? 0f : (float)packet.RenderTimeSeconds;

        // Subtle Pleiades companion micro-star near hero nouns/stars on StageCanvas
        if (!packet.IsCompactCard && (node.Visual is not null || (nodeIndex % 2 == 0)))
        {
            var compAngle = (((lineSeed >> ((nodeIndex * 4) % 24)) & 0xFu) / 15f) * MathF.Tau;
            var compDist = (isActive ? 28f : 18f) * (packet.ReducedMotion ? 1f : 0.94f + (0.06f * MathF.Sin(time * 2f + nodeIndex)));
            var mx = node.X + (MathF.Cos(compAngle) * compDist);
            var my = node.Y + (MathF.Sin(compAngle) * compDist);
            DrawPinchedStar(canvas, mx, my, isActive ? 3.8f : 2.3f, packet.Palette.AccentColor2.ToSkColorWithAlpha(isActive ? 0.80f : 0.36f));
        }

        if (isActive)
        {
            var starRadius = (packet.IsCompactCard ? 10.5f : 18.0f) * flare;

            // Layer A: Wide chromatic nebula corona behind active star
            _starPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.48f);
            _starPaint.ImageFilter = _wideGlowBlur;
            try
            {
                canvas.DrawCircle(node.X, node.Y, starRadius * 1.40f, _starPaint);
            }
            finally
            {
                _starPaint.ImageFilter = null;
            }

            // Layer B: Sleek horizontal anamorphic optical streak (StageCanvas only so DesktopOverlay edges remain clean)
            if (packet.SurfaceMode != StageRenderSurfaceMode.DesktopOverlay && !packet.IsCompactCard)
            {
                var streakHalfW = Math.Clamp((starRadius * 3.4f) + (audio.BeatPulse * 24f), 28f, 95f);
                var streakHalfH = 2.0f;
                var streakRect = new SKRect(node.X - streakHalfW, node.Y - streakHalfH, node.X + streakHalfW, node.Y + streakHalfH);
                _streakColors[0] = SKColors.Transparent;
                _streakColors[1] = StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor1, new RgbColor(255, 255, 255), 0.55f).ToSkColorWithAlpha(0.75f);
                _streakColors[2] = SKColors.Transparent;

                using var streakShader = SKShader.CreateLinearGradient(
                    new SKPoint(streakRect.Left, node.Y),
                    new SKPoint(streakRect.Right, node.Y),
                    _streakColors,
                    _streakStops,
                    SKShaderTileMode.Clamp);
                _anamorphicPaint.Shader = streakShader;
                try
                {
                    canvas.DrawRoundRect(streakRect, streakHalfH, streakHalfH, _anamorphicPaint);
                }
                finally
                {
                    _anamorphicPaint.Shader = null;
                }
            }

            // Layer C: Astrolabe orbital ring + counter-orbiting satellite glints
            if (!packet.ReducedMotion)
            {
                var orbitR = starRadius * 1.46f;
                _ringPaint.StrokeWidth = packet.IsCompactCard ? 0.75f : 1.05f;
                _ringPaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(0.38f);
                canvas.DrawCircle(node.X, node.Y, orbitR, _ringPaint);

                var orbitAngle = (time * 1.55f) + (nodeIndex * 0.9f);
                for (var sat = 0; sat < 2; sat++)
                {
                    var a = orbitAngle + (sat * MathF.PI);
                    var ox = node.X + (MathF.Cos(a) * orbitR);
                    var oy = node.Y + (MathF.Sin(a) * orbitR);
                    _starPaint.Color = (sat == 0 ? SKColors.White : packet.Palette.AccentColor2.ToSkColor()).WithAlpha(225);
                    canvas.DrawCircle(ox, oy, packet.IsCompactCard ? 1.4f : 2.3f, _starPaint);
                }
            }

            // Layer D: 45-degree diffraction spikes (8-point JWST starburst)
            var diagSave = canvas.Save();
            try
            {
                canvas.Translate(node.X, node.Y);
                canvas.RotateDegrees(45f);
                DrawPinchedStar(canvas, 0f, 0f, starRadius * 0.64f, packet.Palette.AccentColor1.ToSkColorWithAlpha(0.88f));
            }
            finally
            {
                canvas.RestoreToCount(diagSave);
            }

            // Layer E: Incandescent primary 4-point diamond star + white-hot core
            DrawPinchedStar(canvas, node.X, node.Y, starRadius, SKColors.White);
            _starPaint.Color = SKColors.White;
            canvas.DrawCircle(node.X, node.Y, Math.Max(2.0f, starRadius * 0.22f), _starPaint);
        }
        else if (isPast)
        {
            var twinkle = packet.ReducedMotion ? 1.0f : (0.86f + (0.14f * MathF.Sin((time * 2.4f) + nodeIndex + (lineSeed & 7u))));
            var pastRadius = (packet.IsCompactCard ? 5.6f : 9.0f) * twinkle;
            DrawPinchedStar(canvas, node.X, node.Y, pastRadius, packet.Palette.AccentColor1.ToSkColorWithAlpha(0.92f));
            _starPaint.Color = SKColors.White.WithAlpha(205);
            canvas.DrawCircle(node.X, node.Y, Math.Max(1.2f, pastRadius * 0.20f), _starPaint);
        }
        else
        {
            var beatBreath = packet.ReducedMotion ? 1.0f : (1.0f + (audio.TrebleEnergy * 0.22f));
            var upcomingR = (packet.IsCompactCard ? 3.2f : 4.9f) * beatBreath;
            DrawPinchedStar(canvas, node.X, node.Y, upcomingR, packet.Palette.AccentColor3.ToSkColorWithAlpha(0.55f));
        }

        // Smart star-atlas word label positioning (above or below star, strictly viewport-clamped)
        var liftY = isActive && !packet.ReducedMotion ? _labelLift.Value : 0f;
        var labelOffset = (font.Size * (node.LabelAbove ? -0.94f : 1.18f)) + (node.LabelAbove ? -liftY : liftY);
        var labelY = node.Y + labelOffset;
        var halfW = node.MeasuredWidth * 0.5f;
        var clampedX = Math.Clamp(node.X, halfW + 14f - camX, Math.Max(halfW + 14f - camX, packet.ViewportWidthDip - halfW - 14f - camX));
        var clampedY = Math.Clamp(labelY, font.Size + 10f - camY, Math.Max(font.Size + 10f - camY, packet.ViewportHeightDip - 14f - camY));

        var opacity = isActive
            ? (packet.ReducedMotion ? (0.90f + (0.10f * highlightEased)) : 1.0f)
            : isPast ? 0.78f : 0.40f;
        var color = isActive
            ? StyleLayoutHelper.BlendSrgb(packet.Palette.AccentColor1, SKColors.White.ToRgb(), 0.54f * highlightEased)
            : packet.Palette.AccentColor3;

        if (isActive && packet.ShowColorSweepLayer)
        {
            var sweepRect = new SKRect(clampedX - halfW, clampedY - (font.Size * 0.85f), clampedX + halfW, clampedY + (font.Size * 0.16f));
            _aesthetics.DrawWordColorSweep(canvas, sweepRect, wordProgress, packet, opacity);
        }

        var scale = isActive ? 1.08f : 0.90f;
        var save = canvas.Save();
        try
        {
            canvas.Translate(clampedX, clampedY);
            canvas.Scale(scale, scale);
            context.Legibility.DrawLegibleText(
                canvas,
                node.Text,
                0f,
                0f,
                SKTextAlign.Center,
                font,
                fontId,
                color,
                opacity: opacity,
                activeGlowColor: isActive ? packet.Palette.AccentColor2 : null,
                fonts: context.Fonts);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }

        // Semantic custom vector illustration companion beside active matched star-word
        if (isActive && node.Visual is { } vis && !packet.IsCompactCard)
        {
            var iconSize = Math.Clamp(font.Size * 0.95f, 18f, 44f);
            var iconX = clampedX + (halfW * scale) + (iconSize * 0.65f);
            if (iconX + (iconSize * 0.5f) > packet.ViewportWidthDip - 12f - camX)
            {
                iconX = clampedX - (halfW * scale) - (iconSize * 0.65f);
            }

            _aesthetics.DrawReactiveVisual(
                canvas,
                in context,
                vis,
                iconX,
                clampedY - (font.Size * 0.30f),
                iconSize,
                opacity,
                isActive: true);
        }
    }

    private void DrawPinchedStar(SKCanvas canvas, float cx, float cy, float radius, SKColor color)
    {
        var inner = radius * 0.21f;
        _diamondStarPath.Rewind();
        _diamondStarPath.MoveTo(cx, cy - radius);
        _diamondStarPath.QuadTo(cx + inner, cy - inner, cx + radius, cy);
        _diamondStarPath.QuadTo(cx + inner, cy + inner, cx, cy + radius);
        _diamondStarPath.QuadTo(cx - inner, cy + inner, cx - radius, cy);
        _diamondStarPath.QuadTo(cx - inner, cy - inner, cx, cy - radius);
        _diamondStarPath.Close();

        _starPaint.Color = color;
        canvas.DrawPath(_diamondStarPath, _starPaint);
    }

    private static (float Cx, float Cy) ComputeThreadControlPoint(float x0, float y0, float x1, float y1, uint lineSeed, int segIdx)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var dist = MathF.Sqrt((dx * dx) + (dy * dy));
        if (dist < 1f)
        {
            return (x0, y0);
        }

        var sign = ((lineSeed >> (segIdx % 16)) & 1u) == 0 ? 1f : -1f;
        var bow = MathF.Min(dist * 0.14f, 26f) * sign;
        var nx = -dy / dist;
        var ny = dx / dist;
        return (((x0 + x1) * 0.5f) + (nx * bow), ((y0 + y1) * 0.5f) + (ny * bow));
    }

    private static (float X, float Y) EvalQuadBezier(float x0, float y0, float cx, float cy, float x1, float y1, float t)
    {
        var u = 1f - t;
        var x = (u * u * x0) + (2f * u * t * cx) + (t * t * x1);
        var y = (u * u * y0) + (2f * u * t * cy) + (t * t * y1);
        return (x, y);
    }

    private static (float X, float Y) EstimateStarAnchor(LyricLine line, int idx, int count, SKRect region)
    {
        var seed = StyleLayoutHelper.DeterministicHash(line.Index, line.RawText);
        var archetype = (int)(seed % 6u);
        var flipX = ((seed >> 3) & 1u) != 0;
        var flipY = ((seed >> 4) & 1u) != 0;
        var (nx, ny) = GenerateArchetypeNormalizedPoint(archetype, idx, count, seed, flipX, flipY);
        var x = Math.Clamp(region.MidX + (nx * region.Width * 0.86f), region.Left + 20f, region.Right - 20f);
        var y = Math.Clamp(region.MidY + (ny * region.Height * 0.82f), region.Top + 20f, region.Bottom - 20f);
        return (x, y);
    }

    private static (float Nx, float Ny) GenerateArchetypeNormalizedPoint(
        int archetype,
        int index,
        int count,
        uint seed,
        bool flipX,
        bool flipY)
    {
        if (count <= 1)
        {
            return (0f, -0.04f);
        }

        var t = index / (float)(count - 1);
        var h = (seed >> ((index * 5) % 24)) & 0xFFu;
        var jx = (((h & 0xFu) / 15f) - 0.5f) * 0.14f;
        var jy = ((((h >> 4) & 0xFu) / 15f) - 0.5f) * 0.14f;

        float nx;
        float ny;
        switch (archetype)
        {
            case 0:
                // Corona Borealis / Cassiopeia Horseshoe Arc across wide 2D sky
                var arcAngle = MathF.PI * (1.12f - (t * 1.24f));
                var rMod = (index & 1) == 0 ? 0.42f : 0.30f;
                nx = (MathF.Cos(arcAngle) * rMod * 1.12f) + jx;
                ny = (MathF.Sin(arcAngle) * rMod * 0.92f) - 0.04f + jy;
                break;

            case 1:
                // Ursa Major Dipper Loop: curving tail opening into a wide 2D bowl
                if (t < 0.48f)
                {
                    var ht = t / 0.48f;
                    nx = -0.46f + (ht * 0.44f) + (jx * 0.6f);
                    ny = -0.36f + (MathF.Sin(ht * MathF.PI * 0.72f) * 0.24f) + jy;
                }
                else
                {
                    var bt = (t - 0.48f) / 0.52f;
                    var bowlAngle = -0.40f + (bt * MathF.PI * 1.45f);
                    nx = 0.16f + (MathF.Cos(bowlAngle) * 0.30f) + (jx * 0.5f);
                    ny = 0.08f + (MathF.Sin(bowlAngle) * 0.35f) + (jy * 0.5f);
                }
                break;

            case 2:
                // Golden-Angle Galaxy Spiral filling all 4 sky quadrants
                var radius = 0.10f + (MathF.Sqrt(t) * 0.39f);
                var angle = (((seed & 0x1Fu) / 31f) * MathF.Tau) + (index * 1.38f);
                nx = MathF.Cos(angle) * radius * 1.15f;
                ny = MathF.Sin(angle) * radius * 0.96f;
                break;

            case 3:
                // Cygnus Winged Cross / Diagonal Sweep with deep perpendicular offsets
                var wing = (index % 3) == 1 ? -0.32f : (index % 3) == 2 ? 0.30f : 0.0f;
                nx = (-0.43f + (t * 0.86f)) + (wing * 0.42f) + jx;
                ny = (0.38f - (t * 0.76f)) + (wing * 0.86f) + jy;
                break;

            case 4:
                // Orion Open Hourglass spanning all four sky corners + belt center
                var q = index % 4;
                var beltBias = count >= 5 && index == count / 2 ? 0.18f : 1.0f;
                nx = ((q is 0 or 2 ? -0.39f : 0.39f) * beltBias) + ((t - 0.5f) * 0.20f) + jx;
                ny = ((q < 2 ? -0.38f : 0.38f) * beltBias) + jy;
                break;

            default:
                // Scorpius S-Hook Sweeping from top-left star cluster down into a curled tail
                var hookT = t > 0.65f ? (t - 0.65f) / 0.35f : 0f;
                nx = -0.45f + (t * 0.88f) - (MathF.Sin(hookT * MathF.PI) * 0.22f) + jx;
                ny = (MathF.Sin((t * MathF.Tau) + 0.35f) * 0.36f) + (hookT * 0.18f) + jy;
                break;
        }

        if (flipX)
        {
            nx = -nx;
        }

        if (flipY)
        {
            ny = -ny;
        }

        return (Math.Clamp(nx, -0.48f, 0.48f), Math.Clamp(ny, -0.46f, 0.46f));
    }

    private static float WrapPositive(float value, float period)
    {
        if (period <= 0f)
        {
            return 0f;
        }

        var rem = value % period;
        return rem < 0f ? rem + period : rem;
    }

    internal readonly record struct StarNode(
        string Text,
        float X,
        float Y,
        float MeasuredWidth,
        WordVisualMatch? Visual,
        bool LabelAbove);
}
