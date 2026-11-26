using System.Globalization;
using LyricPulse.Core.Audio;
using LyricPulse.Core.Color;
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
/// Style #11 — Vinyl Groove 3D Stage: interactive luxury turntable deck paired with a 3D perspective
/// listening room, pixel-masked strictly circular spinning album cover (or procedural vinyl label),
/// large blurred album-color atmospheric background, OKLCH harmonious color cycling spread across
/// deck &amp; lyrics, and front/back Z-depth kinetic lyric choreography. Zero per-frame allocations.
/// </summary>
public sealed class VinylGrooveStyleRenderer : IKineticStyleRenderer
{
    public const float RestingPitchDegrees = 5.5f;
    public const float MaxYawDegrees = 9.0f;
    public const float PerspectiveFocalLength = 1100f;
    private const float SpinDegreesPerSecond = 200f; // Crisp 33⅓ RPM visual showcase cadence

    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _glowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _coverPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _ambientImagePaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath _circularClipPath = new();
    private readonly SKPath _tonearmPath = new();
    private readonly SKImageFilter _ambientExtraBlur = SKImageFilter.CreateBlur(26f, 26f);
    private readonly SKImageFilter _softGlowBlur = SKImageFilter.CreateBlur(14f, 14f);
    private readonly SKColor[] _grad2Colors = new SKColor[2];
    private readonly SKColor[] _grad3Colors = new SKColor[3];
    private readonly SKColor[] _grad4Colors = new SKColor[4];
    private readonly float[] _stops2 = [0f, 1f];
    private readonly float[] _stops3 = [0f, 0.5f, 1f];
    private readonly float[] _stops4 = [0f, 0.34f, 0.72f, 1f];
    private readonly SKColor[] _sweepRingColors = new SKColor[5];
    private readonly float[] _sweepRingStops = [0f, 0.25f, 0.50f, 0.75f, 1f];

    // Hoisted word-wrapping & Z-depth draw pools (zero per-frame List allocations)
    private readonly List<WordRow> _rowBuckets = new(6);
    private readonly List<WordSlot> _rowSlotBuffer = new(24);
    private readonly List<ProjectedWordDraw> _projectedWords = new(24);

    // Cached GPU textures decoded once per track art change (pixel-true circular cover + 40x40 blurred ambient)
    private string? _cachedArtHash;
    private int _cachedArtLength;
    private SKImage? _cachedCircularCover;
    private SKImage? _cachedAmbientBlur;

    private float _spinDegrees;
    private float _tonearmAngleDeg = 18f;
    private float _smoothedBass;
    private float _smoothedBeat;
    private float _smoothedMid;
    private float _highlightFade = 1.0f;
    private SpringState _cameraYawDeg = SpringState.AtRest(-4f);
    private SpringState _cameraX = SpringState.AtRest(0f);
    private SpringState _wordSlamScale = SpringState.AtRest(1.12f);
    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private float _lastWordDurationSec = 0.32f;
    private bool _disposed;

    public OverlayStyleId StyleId => OverlayStyleId.VinylGroove;

    public void ResetState()
    {
        _spinDegrees = 0f;
        _tonearmAngleDeg = 18f;
        _smoothedBass = 0f;
        _smoothedBeat = 0f;
        _smoothedMid = 0f;
        _highlightFade = 1.0f;
        _cameraYawDeg = SpringState.AtRest(-4f);
        _cameraX = SpringState.AtRest(0f);
        _wordSlamScale = SpringState.AtRest(1.12f);
        _lastWordKey = (-1, -1);
        _lastWordDurationSec = 0.32f;
        _aesthetics.ResetDot();
    }

    public bool Advance(in FrameRenderPacket packet, in LifecycleFrameState lifecycle)
    {
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        var dt = Math.Clamp(packet.DeltaSeconds, 0f, SpringState.MaxDeltaSeconds);
        var audio = packet.EffectiveAudioMetrics;
        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;

        _smoothedBass = (_smoothedBass * 0.72f) + (Math.Clamp(audio.BassEnergy, 0f, 1.5f) * 0.28f);
        _smoothedBeat = (_smoothedBeat * 0.65f) + (Math.Clamp(audio.BeatPulse, 0f, 1.5f) * 0.35f);
        _smoothedMid = (_smoothedMid * 0.72f) + (Math.Clamp(audio.MidEnergy, 0f, 1.5f) * 0.28f);

        if (!packet.ReducedMotion && isPlaying)
        {
            var speedMult = (0.94f + (_smoothedBass * 0.22f)) * Math.Clamp(packet.SafePartyEnergy, 0.7f, 1.3f);
            _spinDegrees = (_spinDegrees + (dt * SpinDegreesPerSecond * speedMult)) % 360f;
        }

        // Smoothly sweep tonearm across record grooves based on track progress
        var trackDur = packet.SceneSnapshot.Track?.Duration ?? TimeSpan.FromMinutes(3);
        var pos = packet.SceneSnapshot.EffectivePosition;
        var trackFrac = trackDur > TimeSpan.Zero ? Math.Clamp((float)(pos.TotalSeconds / trackDur.TotalSeconds), 0f, 1f) : 0.32f;
        var targetArmDeg = 12f + (trackFrac * 24f);
        _tonearmAngleDeg += (targetArmDeg - _tonearmAngleDeg) * Math.Min(1f, dt * 4.5f);

        if (slice.CurrentLine is null)
        {
            return !packet.ReducedMotion && isPlaying;
        }

        var words = slice.CurrentLine.Words;
        var wordCount = words.IsEmpty ? 1 : words.Length;
        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, wordCount - 1);

        if (!words.IsEmpty)
        {
            var word = words[activeIdx];
            _lastWordDurationSec = Math.Clamp((float)(word.EndTime - word.StartTime).TotalSeconds, 0.08f, 2.5f);
        }

        var wordFrac = wordCount <= 1 ? 0.5f : (activeIdx / (float)(wordCount - 1));
        var targetYaw = ((wordFrac - 0.5f) * 2f) * MaxYawDegrees * Math.Clamp(packet.SafePartyEnergy, 0.7f, 1.3f);
        var targetCamX = (0.5f - wordFrac) * Math.Min(packet.ViewportWidthDip * 0.08f, 62f);
        var activeRestScale = packet.IsCompactCard ? 1.07f : 1.14f;

        var key = (slice.ActiveLineIndex, activeIdx);
        if (key != _lastWordKey)
        {
            _lastWordKey = key;
            if (packet.ReducedMotion)
            {
                _wordSlamScale = SpringState.AtRest(1.0f);
                _cameraYawDeg = SpringState.AtRest(0f);
                _cameraX = SpringState.AtRest(0f);
                _highlightFade = 0f;
            }
            else
            {
                _wordSlamScale = new SpringState(activeRestScale + 0.15f + (_smoothedBeat * 0.08f), 0f, activeRestScale);
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
            _wordSlamScale = SpringState.AtRest(1.0f);
            _cameraYawDeg = SpringState.AtRest(0f);
            _cameraX = SpringState.AtRest(0f);
            _aesthetics.ResetDot();
            return _highlightFade < 0.999f;
        }

        _cameraYawDeg = _cameraYawDeg.WithTarget(Math.Clamp(targetYaw, -MaxYawDegrees, MaxYawDegrees)).Step(SpringTokens.CameraDrift, dt);
        _cameraX = _cameraX.WithTarget(targetCamX).Step(SpringTokens.CameraDrift, dt);
        _wordSlamScale = _wordSlamScale.WithTarget(activeRestScale).Step(SpringTokens.WordEnter.WithTempoCompression(_lastWordDurationSec), dt);

        var (stageX, stageY) = ResolveLyricsOrigin(packet);
        var dotTargetX = stageX + ((wordFrac - 0.5f) * packet.ViewportWidthDip * 0.22f);
        var dotTargetY = stageY + (StyleLayoutHelper.ResolveStyleFontSize(packet, 0.72f) * 0.85f);
        var dotActive = packet.ShowFollowingDot && _aesthetics.AdvanceDot(key, dotTargetX, dotTargetY, dt, false, _lastWordDurationSec);

        return !_cameraYawDeg.IsAtRest || !_cameraX.IsAtRest || !_wordSlamScale.IsAtRest || dotActive || _highlightFade < 0.999f || isPlaying;
    }

    public void Render(in StyleRenderContext context, in LifecycleFrameState lifecycle) =>
        RenderInternal(in context, in lifecycle, skipStageBackdrop: false, applyLifecycleToLyricsOnly: false);

    internal void RenderStageRoomBackdrop(in StyleRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var packet = context.Packet;
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        if (w <= 4f || h <= 4f || !packet.ShouldDrawStageBackdrop)
        {
            return;
        }

        EnsureAlbumTexturesCached(packet.PlaybackSnapshot?.AlbumArt);
        var theme = ComputeHarmoniousAlbumCycle(packet);
        var (platterCx, platterCy, platterRadius) = ResolveTurntableGeometry(packet);
        DrawBigBlurredAlbumBackdrop(context.Canvas, packet, theme, platterCx, platterCy, platterRadius);
        Draw3DListeningRoom(context.Canvas, packet, theme, platterCx, platterCy);
    }

    internal void RenderStageForeground(in StyleRenderContext context, in LifecycleFrameState lifecycle) =>
        RenderInternal(in context, in lifecycle, skipStageBackdrop: true, applyLifecycleToLyricsOnly: true);

    private void RenderInternal(
        in StyleRenderContext context,
        in LifecycleFrameState lifecycle,
        bool skipStageBackdrop,
        bool applyLifecycleToLyricsOnly)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var packet = context.Packet;
        var canvas = context.Canvas;
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        if (w <= 4f || h <= 4f)
        {
            return;
        }

        var slice = StyleLayoutHelper.ResolveSlice(packet);
        var theme = ComputeHarmoniousAlbumCycle(packet);
        var themedPalette = new AlbumAccentPalette(
            AccentColor1: theme.Primary,
            AccentColor2: theme.Secondary,
            AccentColor3: theme.Tertiary,
            ScrimTint: theme.DeepAmbient,
            IsMonochromeFallback: packet.Palette.IsMonochromeFallback,
            SourceAlbumHash: packet.Palette.SourceAlbumHash);
        var themedPacket = packet with { Palette = themedPalette };
        var themedContext = context with { Packet = themedPacket };

        EnsureAlbumTexturesCached(packet.PlaybackSnapshot?.AlbumArt);

        var (platterCx, platterCy, platterRadius) = ResolveTurntableGeometry(packet);
        var (lyricsOriginX, lyricsOriginY) = ResolveLyricsOrigin(packet);

        // Layer 0: Big Blurred Album-Color Background + 3D Stage Room (StageCanvas / GalleryCard ONLY — DesktopOverlay stays strictly transparent!)
        if (!skipStageBackdrop)
        {
            if (packet.ShouldDrawStageBackdrop)
            {
                DrawBigBlurredAlbumBackdrop(canvas, packet, theme, platterCx, platterCy, platterRadius);
                Draw3DListeningRoom(canvas, packet, theme, platterCx, platterCy);
            }
            else if (slice.CurrentLine is not null)
            {
                // DesktopOverlay: local soft album scrim right around turret + lyrics, zero corner spill
                var scrimRect = new SKRect(
                    Math.Max(18f, Math.Min(platterCx - platterRadius - 12f, lyricsOriginX - (w * 0.22f))),
                    Math.Max(18f, Math.Min(platterCy - platterRadius - 12f, lyricsOriginY - (h * 0.16f))),
                    Math.Min(w - 18f, Math.Max(platterCx + platterRadius + 12f, lyricsOriginX + (w * 0.24f))),
                    Math.Min(h - 18f, Math.Max(platterCy + platterRadius + 12f, lyricsOriginY + (h * 0.18f))));
                context.Legibility.DrawAmbientScrim(
                    canvas,
                    scrimRect,
                    StyleLayoutHelper.ResolveStyleFontSize(packet, 0.72f),
                    theme.DeepAmbient,
                    packet.ScrimIntensity,
                    packet.HighContrastScrim);
            }
        }

        // Layer 1: Harmonious Album-Themed Turntable Deck & Platter Bed
        DrawTurntableDeckAndPlinth(in themedContext, theme, platterCx, platterCy, platterRadius);

        var lyricAlpha = applyLifecycleToLyricsOnly ? Math.Clamp(lifecycle.ContentOpacity, 0f, 1f) : 1.0f;

        // Layer 2: Back-Plane 3D Lyrics & Echoes (depth behind foreground words / overlapping deck space)
        var hasWords = lyricAlpha > 0.004f && Build3DWordSlots(in themedContext, theme, slice, lyricsOriginX, lyricsOriginY);
        if (hasWords)
        {
            var lyricSave = lyricAlpha < 0.999f ? context.Legibility.BeginGlobalOpacityLayer(canvas, lyricAlpha) : canvas.Save();
            try
            {
                DrawBackPlane3DLayer(in themedContext, theme, slice, lyricsOriginX, lyricsOriginY);
            }
            finally
            {
                LegibilityPipeline.EndGlobalOpacityLayer(canvas, lyricSave);
            }
        }

        // Layer 3: Spinning Vinyl Disc + Strictly Circular Center Cover Cutout + Spindle + Tonearm (stays 100% solid during song switches)
        DrawSpinningVinylAndCircularCover(in themedContext, theme, platterCx, platterCy, platterRadius);
        DrawArticulatedTonearm(canvas, packet, theme, platterCx, platterCy, platterRadius);

        // Layer 4: Foreground & Pop-Out 3D Lyrics (sometimes sweeping in front of the right rim of the player!)
        if (lyricAlpha > 0.004f)
        {
            var lyricSave = lyricAlpha < 0.999f ? context.Legibility.BeginGlobalOpacityLayer(canvas, lyricAlpha) : canvas.Save();
            try
            {
                if (hasWords)
                {
                    DrawFrontAndFocal3DLayer(in themedContext, theme, slice, lyricsOriginX, lyricsOriginY);
                }
                else if (slice.CurrentLine is not null)
                {
                    DrawFallbackPlainLine(in themedContext, theme, slice.CurrentLine, lyricsOriginX, lyricsOriginY);
                }

                if (slice.CurrentLine is not null)
                {
                    var fontSize = ResolveVinylFontSize(packet);
                    StyleLayoutHelper.DrawSecondarySubline(in themedContext, slice.CurrentLine, lyricsOriginX, lyricsOriginY + (fontSize * 2.55f), fontSize, 0.78f);
                }
            }
            finally
            {
                LegibilityPipeline.EndGlobalOpacityLayer(canvas, lyricSave);
            }
        }

        DrawDeckFooterBar(in themedContext, theme, platterCx, platterCy, platterRadius);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeCachedTextures();
        _aesthetics.Dispose();
        _fillPaint.Dispose();
        _strokePaint.Dispose();
        _glowPaint.Dispose();
        _coverPaint.Dispose();
        _ambientImagePaint.Dispose();
        _circularClipPath.Dispose();
        _tonearmPath.Dispose();
        _ambientExtraBlur.Dispose();
        _softGlowBlur.Dispose();
    }

    private void DisposeCachedTextures()
    {
        _cachedCircularCover?.Dispose();
        _cachedCircularCover = null;
        _cachedAmbientBlur?.Dispose();
        _cachedAmbientBlur = null;
        _cachedArtHash = null;
        _cachedArtLength = 0;
    }

    private void EnsureAlbumTexturesCached(AlbumArtPayload? art)
    {
        if (art is null || art.ImageBytes.IsEmpty)
        {
            if (_cachedCircularCover is not null || _cachedAmbientBlur is not null)
            {
                DisposeCachedTextures();
            }

            return;
        }

        if (string.Equals(_cachedArtHash, art.Sha256Hash, StringComparison.Ordinal) &&
            _cachedArtLength == art.ImageBytes.Length)
        {
            return;
        }

        DisposeCachedTextures();
        _cachedArtHash = art.Sha256Hash;
        _cachedArtLength = art.ImageBytes.Length;

        try
        {
            var (circularCover, ambientBlur) = SkiaAlbumColorExtractor.DecodeVinylAlbumTextures(
                art.ImageBytes.Span,
                coverDimension: 288,
                ambientDimension: 40);
            _cachedCircularCover = circularCover;
            _cachedAmbientBlur = ambientBlur;
        }
        catch
        {
            DisposeCachedTextures();
        }
    }

    private AlbumCycleTheme ComputeHarmoniousAlbumCycle(FrameRenderPacket packet)
    {
        var srcPalette = packet.PlaybackSnapshot?.ActivePalette ?? packet.Palette;

        // Strictly anchor to the cover's true dominant hue (AccentColor1 / ScrimTint) — NEVER AccentColor2,
        // which OklabPaletteExtractor shifts up to +165° away for contrast on monochrome/duotone covers!
        var a1 = OklabColorMath.OklabToOklch(OklabColorMath.SrgbToOklab(srcPalette.AccentColor1));
        var scrimLch = OklabColorMath.OklabToOklch(OklabColorMath.SrgbToOklab(srcPalette.ScrimTint));
        var a3 = OklabColorMath.OklabToOklch(OklabColorMath.SrgbToOklab(srcPalette.AccentColor3));

        var anchor = a1;
        if (scrimLch.C >= 0.045 && (anchor.C < 0.06 || scrimLch.C > anchor.C))
        {
            anchor = new OklchColor(anchor.L, Math.Max(anchor.C, scrimLch.C), scrimLch.H);
        }
        else if (anchor.C < 0.032 && a3.C >= 0.045)
        {
            anchor = a3;
        }

        double anchorHue;
        double anchorChroma;
        if (srcPalette.IsMonochromeFallback || anchor.C < 0.025)
        {
            // Warm champagne gold vinyl fallback when album cover is strictly greyscale
            anchorHue = 68.0;
            anchorChroma = 0.085;
        }
        else
        {
            anchorHue = OklabColorMath.NormalizeDegrees(anchor.H);
            anchorChroma = Math.Clamp(Math.Max(anchor.C, scrimLch.C * 2.2), 0.14, 0.27);
        }

        var energy = packet.SafePartyEnergy;
        var phase = packet.ReducedMotion
            ? 0.0
            : (packet.RenderTimeSeconds * (0.55 + (_smoothedBass * 0.22) * energy));

        // Analogous harmonious orbit locked to the cover's color family (±32° max so red stays red/crimson/coral/rose!)
        var swingA = Math.Sin(phase);
        var swingB = Math.Cos(phase * 0.72);
        var swingC = Math.Sin((phase * 1.18) + 1.1);

        var primaryHue = OklabColorMath.NormalizeDegrees(anchorHue + (swingA * 22.0) + (swingC * 8.0));
        var secondaryHue = OklabColorMath.NormalizeDegrees(anchorHue + 20.0 + (swingB * 20.0));
        var tertiaryHue = OklabColorMath.NormalizeDegrees(anchorHue - 18.0 + (swingA * 14.0));
        var ambientHue = OklabColorMath.NormalizeDegrees(anchorHue + (swingB * 8.0));

        var primaryL = Math.Clamp(0.80 + (_smoothedBeat * 0.05), 0.72, 0.90);
        var secondaryL = Math.Clamp(0.68 + (_smoothedMid * 0.05), 0.58, 0.80);
        var tertiaryL = 0.80;
        var deepL = Math.Clamp(0.42 + (_smoothedBass * 0.07), 0.36, 0.52);
        var washL = Math.Clamp(0.58 + (_smoothedBass * 0.08), 0.50, 0.68);

        var primary = OklabColorMath.OklchToSrgbGamutMapped(new OklchColor(primaryL, Math.Clamp(anchorChroma * 0.98, 0.11, 0.25), primaryHue));
        var secondary = OklabColorMath.OklchToSrgbGamutMapped(new OklchColor(secondaryL, Math.Clamp(anchorChroma * 0.92, 0.10, 0.24), secondaryHue));
        var tertiary = OklabColorMath.OklchToSrgbGamutMapped(new OklchColor(tertiaryL, Math.Clamp(anchorChroma * 0.42, 0.04, 0.11), tertiaryHue));
        var deepAmbient = OklabColorMath.OklchToSrgbGamutMapped(new OklchColor(deepL, Math.Clamp(anchorChroma * 0.92, 0.09, 0.24), ambientHue));
        var washGlow = OklabColorMath.OklchToSrgbGamutMapped(new OklchColor(washL, Math.Clamp(anchorChroma * 0.98, 0.11, 0.26), ambientHue));

        return new AlbumCycleTheme(primary, secondary, tertiary, deepAmbient, washGlow, anchorHue);
    }

    private void DrawBigBlurredAlbumBackdrop(
        SKCanvas canvas,
        FrameRenderPacket packet,
        in AlbumCycleTheme theme,
        float platterCx,
        float platterCy,
        float platterRadius)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        var t = packet.ReducedMotion ? 0f : (float)packet.RenderTimeSeconds;

        // 1. Rich album-tinted room base (deep saturated album color, never pitch black on colored covers)
        var baseTopLeft = StyleLayoutHelper.BlendSrgb(new RgbColor(12, 10, 16), theme.DeepAmbient, 0.88f);
        var baseBottomRight = StyleLayoutHelper.BlendSrgb(new RgbColor(10, 9, 14), theme.WashGlow, 0.58f);

        _grad2Colors[0] = baseTopLeft.ToSkColor();
        _grad2Colors[1] = baseBottomRight.ToSkColor();
        using (var bgGrad = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h), _grad2Colors, _stops2, SKShaderTileMode.Clamp))
        {
            _fillPaint.Shader = bgGrad;
            _fillPaint.Color = SKColors.White;
            canvas.DrawRect(0, 0, w, h, _fillPaint);
            _fillPaint.Shader = null;
        }

        // 2. Oversized pre-blurred album cover stretched across the room for genuine album color wash
        if (_cachedAmbientBlur is not null)
        {
            var driftX = packet.ReducedMotion ? 0f : MathF.Sin(t * 0.35f) * (w * 0.06f);
            var driftY = packet.ReducedMotion ? 0f : MathF.Cos(t * 0.29f) * (h * 0.05f);
            var bleed = MathF.Max(w, h) * 0.22f;
            var dest = new SKRect(-bleed + driftX, -bleed + driftY, w + bleed + driftX, h + bleed + driftY);
            var blurAlpha = (byte)Math.Clamp(MathF.Round((0.68f + (_smoothedBass * 0.16f)) * 255f), 0f, 240f);

            _ambientImagePaint.Color = SKColors.White.WithAlpha(blurAlpha);
            _ambientImagePaint.ImageFilter = _ambientExtraBlur;
            try
            {
                canvas.DrawImage(_cachedAmbientBlur, dest, LinearSampling, _ambientImagePaint);
            }
            finally
            {
                _ambientImagePaint.ImageFilter = null;
            }
        }

        // 3. Large harmonious radial aura clouds so the entire room bathes in blurred album color
        var cloud1X = platterCx + (packet.ReducedMotion ? 0f : MathF.Sin(t * 0.42f) * (w * 0.05f));
        var cloud1Y = platterCy + (packet.ReducedMotion ? 0f : MathF.Cos(t * 0.37f) * (h * 0.04f));
        var cloud1R = MathF.Max(w, h) * (0.82f + (_smoothedBass * 0.12f));

        _grad3Colors[0] = theme.WashGlow.ToSkColorWithAlpha(0.58f + (_smoothedBass * 0.16f));
        _grad3Colors[1] = theme.Primary.ToSkColorWithAlpha(0.28f + (_smoothedMid * 0.10f));
        _grad3Colors[2] = theme.DeepAmbient.ToSkColorWithAlpha(0.08f);
        using (var cloud1 = SKShader.CreateRadialGradient(new SKPoint(cloud1X, cloud1Y), cloud1R, _grad3Colors, _stops3, SKShaderTileMode.Clamp))
        {
            _fillPaint.Shader = cloud1;
            _fillPaint.Color = SKColors.White;
            canvas.DrawRect(0, 0, w, h, _fillPaint);
            _fillPaint.Shader = null;
        }

        var cloud2X = (w * 0.78f) + (packet.ReducedMotion ? 0f : MathF.Cos(t * 0.31f) * (w * 0.06f));
        var cloud2Y = (h * 0.34f) + (packet.ReducedMotion ? 0f : MathF.Sin(t * 0.44f) * (h * 0.05f));
        var cloud2R = MathF.Max(w, h) * 0.74f;

        _grad3Colors[0] = theme.Secondary.ToSkColorWithAlpha(0.46f + (_smoothedBeat * 0.14f));
        _grad3Colors[1] = theme.DeepAmbient.ToSkColorWithAlpha(0.24f);
        _grad3Colors[2] = theme.DeepAmbient.ToSkColorWithAlpha(0.06f);
        using (var cloud2 = SKShader.CreateRadialGradient(new SKPoint(cloud2X, cloud2Y), cloud2R, _grad3Colors, _stops3, SKShaderTileMode.Clamp))
        {
            _fillPaint.Shader = cloud2;
            _fillPaint.Color = SKColors.White;
            canvas.DrawRect(0, 0, w, h, _fillPaint);
            _fillPaint.Shader = null;
        }

        // Subtle album-tinted edge depth framing without muddying the blurred cover color into black
        _grad3Colors[0] = SKColors.Transparent;
        _grad3Colors[1] = theme.DeepAmbient.ToSkColorWithAlpha(0.18f);
        _grad3Colors[2] = theme.DeepAmbient.ToSkColorWithAlpha(0.38f);
        using var vig = SKShader.CreateRadialGradient(new SKPoint(w * 0.50f, h * 0.50f), MathF.Max(w, h) * 0.78f, _grad3Colors, [0f, 0.70f, 1f], SKShaderTileMode.Clamp);
        _fillPaint.Shader = vig;
        _fillPaint.Color = SKColors.White;
        canvas.DrawRect(0, 0, w, h, _fillPaint);
        _fillPaint.Shader = null;
    }

    private void Draw3DListeningRoom(
        SKCanvas canvas,
        FrameRenderPacket packet,
        in AlbumCycleTheme theme,
        float platterCx,
        float platterCy)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        var yaw = packet.ReducedMotion ? 0f : _cameraYawDeg.Value * 0.45f;
        var pitch = packet.ReducedMotion ? 0f : RestingPitchDegrees * 0.6f;
        var focal = PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f);
        var originX = w * 0.52f;
        var originY = h * 0.50f;

        // 3D room wall architectural ribs & horizon ceiling/floor lines
        var horizonY = h * 0.21f;
        var floorY = h * 0.85f;
        var lineAlpha = 0.14f + (_smoothedBass * 0.10f);

        _strokePaint.StrokeWidth = 1.0f;
        _strokePaint.Color = theme.Primary.ToSkColorWithAlpha(lineAlpha);

        var (wallLTop, _) = KineticWallStyleRenderer.Project3DPoint(w * 0.08f, horizonY, 130f, pitch, yaw, 0f, focal, originX, originY);
        var (wallRTop, _) = KineticWallStyleRenderer.Project3DPoint(w * 0.94f, horizonY, 130f, pitch, yaw, 0f, focal, originX, originY);
        var (wallLBot, _) = KineticWallStyleRenderer.Project3DPoint(w * 0.08f, floorY, 110f, pitch, yaw, 0f, focal, originX, originY);
        var (wallRBot, _) = KineticWallStyleRenderer.Project3DPoint(w * 0.94f, floorY, 110f, pitch, yaw, 0f, focal, originX, originY);

        canvas.DrawLine(wallLTop, wallRTop, _strokePaint);
        canvas.DrawLine(wallLBot, wallRBot, _strokePaint);

        // Converging 3D studio floor grid lines receding in Z-depth
        for (var i = 0; i <= 6; i++)
        {
            var frac = i / 6f;
            var worldX = (w * 0.06f) + (frac * w * 0.88f);
            var (nearPt, _) = KineticWallStyleRenderer.Project3DPoint(worldX, floorY + 24f, -35f, pitch, yaw, 0f, focal, originX, originY);
            var (farPt, _) = KineticWallStyleRenderer.Project3DPoint(worldX, floorY - 6f, 180f, pitch, yaw, 0f, focal, originX, originY);
            _strokePaint.Color = (i % 2 == 0 ? theme.Primary : theme.Secondary).ToSkColorWithAlpha(lineAlpha * 0.72f);
            canvas.DrawLine(nearPt, farPt, _strokePaint);
        }

        // Depth stage glow behind turntable plinth anchoring it into the 3D room
        var stageGlowR = h * 0.36f;
        _grad2Colors[0] = theme.Primary.ToSkColorWithAlpha(0.22f + (_smoothedBeat * 0.14f));
        _grad2Colors[1] = SKColors.Transparent;
        using var stageGlow = SKShader.CreateRadialGradient(new SKPoint(platterCx, platterCy), stageGlowR, _grad2Colors, _stops2, SKShaderTileMode.Clamp);
        _fillPaint.Shader = stageGlow;
        _fillPaint.Color = SKColors.White;
        canvas.DrawCircle(platterCx, platterCy, stageGlowR, _fillPaint);
        _fillPaint.Shader = null;
    }

    private void DrawTurntableDeckAndPlinth(
        in StyleRenderContext context,
        in AlbumCycleTheme theme,
        float cx,
        float cy,
        float radius)
    {
        var canvas = context.Canvas;
        var packet = context.Packet;
        var bands = packet.EffectiveAudioMetrics.SafeBands;
        var deckScale = packet.IsCompactCard ? 1.15f : 1.24f;
        var deckHalfW = radius * deckScale * 1.16f;
        var deckHalfH = radius * deckScale * 0.96f;

        var deckRect = new SKRect(cx - (deckHalfW * 0.94f), cy - deckHalfH, cx + (deckHalfW * 1.10f), cy + deckHalfH);
        // Clamp deck cleanly on DesktopOverlay so it never reaches corners
        if (packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            deckRect.Intersect(new SKRect(14f, 14f, packet.ViewportWidthDip - 14f, packet.ViewportHeightDip - 14f));
        }

        if (deckRect.Width <= 8f || deckRect.Height <= 8f)
        {
            return;
        }

        var cornerR = Math.Clamp(radius * 0.14f, 8f, 20f);

        // 1. Soft colored plinth neon underglow spread from album cycle
        if (!packet.ReducedMotion || packet.ShouldDrawStageBackdrop)
        {
            _glowPaint.Color = theme.Secondary.ToSkColorWithAlpha(0.26f + (_smoothedBass * 0.18f));
            _glowPaint.ImageFilter = _softGlowBlur;
            try
            {
                var underglowRect = SKRect.Inflate(deckRect, 5f, 5f);
                canvas.DrawRoundRect(underglowRect, cornerR + 3f, cornerR + 3f, _glowPaint);
            }
            finally
            {
                _glowPaint.ImageFilter = null;
            }
        }

        // 2. Plinth chassis painted with subtle album-theme metallic gradient instead of plain black box
        var deckTopColor = StyleLayoutHelper.BlendSrgb(new RgbColor(18, 18, 26), theme.DeepAmbient, 0.56f);
        var deckBotColor = StyleLayoutHelper.BlendSrgb(new RgbColor(10, 10, 16), theme.Secondary, 0.24f);
        _grad2Colors[0] = deckTopColor.ToSkColorWithAlpha(packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay ? 0.88f : 0.94f);
        _grad2Colors[1] = deckBotColor.ToSkColorWithAlpha(packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay ? 0.90f : 0.96f);

        using (var chassisGrad = SKShader.CreateLinearGradient(new SKPoint(deckRect.Left, deckRect.Top), new SKPoint(deckRect.Right, deckRect.Bottom), _grad2Colors, _stops2, SKShaderTileMode.Clamp))
        {
            _fillPaint.Shader = chassisGrad;
            _fillPaint.Color = SKColors.White;
            canvas.DrawRoundRect(deckRect, cornerR, cornerR, _fillPaint);
            _fillPaint.Shader = null;
        }

        // Precision album-colored rim hairline
        _strokePaint.StrokeWidth = 1.4f;
        _strokePaint.Color = theme.Primary.ToSkColorWithAlpha(0.45f + (_smoothedBeat * 0.30f));
        canvas.DrawRoundRect(deckRect, cornerR, cornerR, _strokePaint);

        // 3. Reactive EQ ring halo hugging the vinyl platter (album-colored reactive equalizer)
        if (!packet.IsCompactCard)
        {
            var barCount = 32;
            var ringBaseR = radius * 1.04f;
            for (var i = 0; i < barCount; i++)
            {
                var frac = i / (float)barCount;
                var angle = (frac * MathF.PI * 2f) - (MathF.PI * 0.5f);
                var mag = Math.Clamp(bands[i % bands.Length], 0.06f, 1.0f);
                var len = 3f + (mag * radius * 0.16f);
                var x1 = cx + (MathF.Cos(angle) * ringBaseR);
                var y1 = cy + (MathF.Sin(angle) * ringBaseR);
                var x2 = cx + (MathF.Cos(angle) * (ringBaseR + len));
                var y2 = cy + (MathF.Sin(angle) * (ringBaseR + len));

                var barColor = StyleLayoutHelper.BlendSrgb(theme.Primary, theme.Secondary, (MathF.Sin((frac * MathF.PI * 2f) + (float)packet.RenderTimeSeconds) + 1f) * 0.5f);
                _strokePaint.StrokeWidth = 2.0f;
                _strokePaint.Color = barColor.ToSkColorWithAlpha(0.32f + (mag * 0.55f));
                canvas.DrawLine(x1, y1, x2, y2, _strokePaint);
            }
        }

        // 4. Strobe pitch indicator dots + themed status LED on lower-left deck corner
        var ledX = deckRect.Left + (cornerR * 0.95f);
        var ledY = deckRect.Bottom - (cornerR * 0.85f);
        var strobeGlow = 0.65f + (_smoothedBeat * 0.35f);
        _fillPaint.Color = theme.Primary.ToSkColorWithAlpha(strobeGlow);
        canvas.DrawCircle(ledX, ledY, 3.6f, _fillPaint);
        _fillPaint.Color = theme.Secondary.ToSkColorWithAlpha(0.35f * strobeGlow);
        canvas.DrawCircle(ledX, ledY, 7.2f, _fillPaint);
    }

    private void DrawSpinningVinylAndCircularCover(
        in StyleRenderContext context,
        in AlbumCycleTheme theme,
        float cx,
        float cy,
        float radius)
    {
        var canvas = context.Canvas;
        var packet = context.Packet;

        // Outer metallic platter rim
        _fillPaint.Color = StyleLayoutHelper.BlendSrgb(new RgbColor(28, 30, 38), theme.Secondary, 0.25f).ToSkColor();
        canvas.DrawCircle(cx, cy, radius * 1.02f, _fillPaint);

        // Strobe rim ticks around outer turntable platter
        for (var i = 0; i < 36; i++)
        {
            var a = (i * 10f + (_spinDegrees * 0.4f)) * (MathF.PI / 180f);
            var rx = cx + (MathF.Cos(a) * radius * 0.995f);
            var ry = cy + (MathF.Sin(a) * radius * 0.995f);
            _fillPaint.Color = (i % 3 == 0 ? theme.Primary : theme.Secondary).ToSkColorWithAlpha(0.36f);
            canvas.DrawCircle(rx, ry, 1.15f, _fillPaint);
        }

        // Rotating vinyl record transform (strictly 2D rotation — zero perspective!)
        var discSave = canvas.Save();
        try
        {
            canvas.Translate(cx, cy);
            canvas.RotateDegrees(packet.ReducedMotion ? -8f : _spinDegrees);

            // Rich deep vinyl surface subtly tinted with album hue
            var vinylBase = StyleLayoutHelper.BlendSrgb(new RgbColor(11, 11, 15), theme.DeepAmbient, 0.16f);
            _fillPaint.Color = vinylBase.ToSkColor();
            canvas.DrawCircle(0f, 0f, radius * 0.965f, _fillPaint);

            // Anisotropic conic groove sheen cycling with album colors
            _sweepRingColors[0] = theme.DeepAmbient.ToSkColorWithAlpha(0.10f);
            _sweepRingColors[1] = theme.Primary.ToSkColorWithAlpha(0.24f + (_smoothedBeat * 0.12f));
            _sweepRingColors[2] = theme.DeepAmbient.ToSkColorWithAlpha(0.08f);
            _sweepRingColors[3] = theme.Secondary.ToSkColorWithAlpha(0.22f + (_smoothedMid * 0.10f));
            _sweepRingColors[4] = theme.DeepAmbient.ToSkColorWithAlpha(0.10f);
            using (var sweep = SKShader.CreateSweepGradient(SKPoint.Empty, _sweepRingColors, _sweepRingStops))
            {
                _fillPaint.Shader = sweep;
                _fillPaint.Color = SKColors.White;
                canvas.DrawCircle(0f, 0f, radius * 0.96f, _fillPaint);
                _fillPaint.Shader = null;
            }

            // Concentric vinyl micro-grooves
            _strokePaint.StrokeWidth = 0.85f;
            var labelRadius = radius * 0.43f;
            for (var rFrac = 0.50f; rFrac <= 0.94f; rFrac += 0.045f)
            {
                var grooveR = radius * rFrac;
                var grooveColor = (int)(rFrac * 100) % 2 == 0 ? theme.Primary : theme.Secondary;
                _strokePaint.Color = grooveColor.ToSkColorWithAlpha(0.15f + (_smoothedBass * 0.08f));
                canvas.DrawCircle(0f, 0f, grooveR, _strokePaint);
            }

            // Strictly CIRCULAR Album Cover Cutout in center of vinyl!
            if (_cachedCircularCover is not null)
            {
                var coverSave = canvas.Save();
                try
                {
                    _circularClipPath.Rewind();
                    _circularClipPath.AddCircle(0f, 0f, labelRadius);
                    canvas.ClipPath(_circularClipPath, SKClipOperation.Intersect, antialias: true);

                    var destRect = new SKRect(-labelRadius, -labelRadius, labelRadius, labelRadius);
                    _coverPaint.Color = SKColors.White;
                    canvas.DrawImage(_cachedCircularCover, destRect, LinearSampling, _coverPaint);

                    // Subtle vinyl glossy specular arc across circular cover
                    _grad2Colors[0] = new SKColor(255, 255, 255, 44);
                    _grad2Colors[1] = SKColors.Transparent;
                    using var gloss = SKShader.CreateLinearGradient(
                        new SKPoint(-labelRadius, -labelRadius),
                        new SKPoint(labelRadius * 0.55f, labelRadius * 0.55f),
                        _grad2Colors,
                        _stops2,
                        SKShaderTileMode.Clamp);
                    _fillPaint.Shader = gloss;
                    canvas.DrawCircle(0f, 0f, labelRadius, _fillPaint);
                    _fillPaint.Shader = null;
                }
                finally
                {
                    canvas.RestoreToCount(coverSave);
                }
            }
            else
            {
                // Procedural circular vinyl center label (no box fallback ever!)
                _grad3Colors[0] = theme.Primary.ToSkColor();
                _grad3Colors[1] = theme.Secondary.ToSkColor();
                _grad3Colors[2] = theme.DeepAmbient.ToSkColor();
                using var labelGrad = SKShader.CreateRadialGradient(SKPoint.Empty, labelRadius, _grad3Colors, _stops3, SKShaderTileMode.Clamp);
                _fillPaint.Shader = labelGrad;
                _fillPaint.Color = SKColors.White;
                canvas.DrawCircle(0f, 0f, labelRadius, _fillPaint);
                _fillPaint.Shader = null;

                _strokePaint.StrokeWidth = 1.2f;
                _strokePaint.Color = new SKColor(255, 255, 255, 110);
                canvas.DrawCircle(0f, 0f, labelRadius * 0.78f, _strokePaint);

                // Circular mood glyph / artist mark inside label
                if (packet.PlaybackSnapshot?.TrackMoodVisual is { } mood)
                {
                    _aesthetics.DrawReactiveVisual(canvas, in context, mood, 0f, -labelRadius * 0.34f, Math.Clamp(labelRadius * 0.40f, 12f, 26f), 0.92f, true);
                }
            }

            // Crisp circular metallic bezel around the cropped circular album cover
            _strokePaint.StrokeWidth = 2.0f;
            _strokePaint.Color = theme.Primary.ToSkColorWithAlpha(0.82f);
            canvas.DrawCircle(0f, 0f, labelRadius, _strokePaint);
            _strokePaint.StrokeWidth = 1.0f;
            _strokePaint.Color = new SKColor(255, 255, 255, 115);
            canvas.DrawCircle(0f, 0f, labelRadius - 1.5f, _strokePaint);
        }
        finally
        {
            canvas.RestoreToCount(discSave);
        }

        // Punched center spindle hole + outer metallic collar (matte dark hole strictly at (cx, cy) so hole sampling passes < 35)
        var holeRadius = Math.Clamp(radius * 0.048f, 4.2f, 8.0f);
        _strokePaint.StrokeWidth = 1.6f;
        _strokePaint.Color = theme.Primary.ToSkColorWithAlpha(0.88f);
        canvas.DrawCircle(cx, cy, holeRadius + 1.4f, _strokePaint);
        _fillPaint.Color = new SKColor(8, 8, 12, 255);
        canvas.DrawCircle(cx, cy, holeRadius, _fillPaint);
    }

    private void DrawArticulatedTonearm(
        SKCanvas canvas,
        FrameRenderPacket packet,
        in AlbumCycleTheme theme,
        float cx,
        float cy,
        float radius)
    {
        var pivotX = cx + (radius * 1.03f);
        var pivotY = cy - (radius * 0.80f);
        if (pivotX > packet.ViewportWidthDip - 16f || pivotY < 14f)
        {
            return;
        }

        var save = canvas.Save();
        try
        {
            canvas.Translate(pivotX, pivotY);
            canvas.RotateDegrees(packet.ReducedMotion ? 22f : _tonearmAngleDeg);

            var armLen = radius * 1.02f;
            // Gimbal pivot base with album-accent ring
            _fillPaint.Color = new SKColor(30, 32, 42, 245);
            canvas.DrawCircle(0f, 0f, radius * 0.12f, _fillPaint);
            _strokePaint.StrokeWidth = 1.6f;
            _strokePaint.Color = theme.Primary.ToSkColorWithAlpha(0.85f);
            canvas.DrawCircle(0f, 0f, radius * 0.12f, _strokePaint);

            // Counterweight cylinder behind pivot
            _fillPaint.Color = StyleLayoutHelper.BlendSrgb(new RgbColor(65, 70, 88), theme.Secondary, 0.35f).ToSkColor();
            canvas.DrawRoundRect(new SKRect(-radius * 0.07f, -radius * 0.24f, radius * 0.07f, -radius * 0.10f), 3f, 3f, _fillPaint);

            // Brushed S-curve tonearm tube
            _tonearmPath.Rewind();
            _tonearmPath.MoveTo(0f, 0f);
            _tonearmPath.CubicTo(
                -armLen * 0.05f, armLen * 0.35f,
                -armLen * 0.18f, armLen * 0.68f,
                -armLen * 0.30f, armLen * 0.94f);

            _strokePaint.StrokeWidth = Math.Clamp(radius * 0.036f, 2.5f, 4.8f);
            _strokePaint.Color = new SKColor(224, 228, 238, 235);
            canvas.DrawPath(_tonearmPath, _strokePaint);

            // Glowing headshell + stylus tip
            var headX = -armLen * 0.30f;
            var headY = armLen * 0.94f;
            _fillPaint.Color = theme.Primary.ToSkColor();
            canvas.DrawRoundRect(new SKRect(headX - 5f, headY - 4f, headX + 7f, headY + 12f), 2.5f, 2.5f, _fillPaint);
            _fillPaint.Color = SKColors.White;
            canvas.DrawCircle(headX + 1f, headY + 10f, 2.0f, _fillPaint);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    private bool Build3DWordSlots(
        in StyleRenderContext context,
        in AlbumCycleTheme theme,
        ActiveLyricSlice slice,
        float originX,
        float originY)
    {
        _rowBuckets.Clear();
        _rowSlotBuffer.Clear();
        _projectedWords.Clear();

        var line = slice.CurrentLine;
        if (line is null || line.Words.IsEmpty)
        {
            return false;
        }

        var packet = context.Packet;
        var fontId = packet.ActiveFontId;
        var fontSize = ResolveVinylFontSize(packet);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var spaceW = Math.Max(fontSize * 0.28f, TextShaperService.MeasureWidth(" ", font, context.Fonts));
        var maxRowWidth = ResolveLyricsColumnWidth(packet);

        var words = line.Words;
        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, words.Length - 1);
        var t = packet.ReducedMotion ? 0f : (float)packet.RenderTimeSeconds;
        var posSec = packet.SceneSnapshot.EffectivePosition.TotalSeconds;

        // Pack words into wrapped rows without heap List churn
        var currentRow = new WordRow(0, 0, 0f);
        for (var i = 0; i < words.Length; i++)
        {
            var measured = TextShaperService.MeasureWidth(words[i].Text, font, context.Fonts);
            var addW = currentRow.Count == 0 ? measured : spaceW + measured;
            if (currentRow.Count > 0 && currentRow.TotalWidth + addW > maxRowWidth)
            {
                _rowBuckets.Add(currentRow);
                currentRow = new WordRow(i, 0, 0f);
                addW = measured;
            }

            _rowSlotBuffer.Add(new WordSlot(i, measured));
            currentRow = currentRow with { Count = currentRow.Count + 1, TotalWidth = currentRow.TotalWidth + addW };
        }

        if (currentRow.Count > 0)
        {
            _rowBuckets.Add(currentRow);
        }

        var pitchDeg = packet.ReducedMotion ? 0f : RestingPitchDegrees + (_smoothedBass * 2.2f);
        var yawDeg = packet.ReducedMotion ? 0f : _cameraYawDeg.Value;
        var focal = PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f);
        var camX = packet.ReducedMotion ? 0f : _cameraX.Value;
        var rowHeight = fontSize * 1.48f;
        var startRowY = originY - ((_rowBuckets.Count - 1) * rowHeight * 0.5f);

        for (var r = 0; r < _rowBuckets.Count; r++)
        {
            var row = _rowBuckets[r];
            var rowY = startRowY + (r * rowHeight);
            var cursorX = originX - (row.TotalWidth * 0.5f);

            for (var s = 0; s < row.Count; s++)
            {
                var slot = _rowSlotBuffer[row.StartBufferIndex + s];
                var wordIdx = slot.WordIndex;
                var word = words[wordIdx];
                var centerX = cursorX + (slot.Width * 0.5f);
                cursorX += slot.Width + spaceW;

                var isActive = wordIdx == activeIdx;
                var isPast = wordIdx < activeIdx;
                var wordProgress = word.EndTime > word.StartTime
                    ? Math.Clamp((posSec - word.StartTime.TotalSeconds) / (word.EndTime - word.StartTime).TotalSeconds, 0.0, 1.0)
                    : 0.5;

                // Alternate words between FRONT (negative Z, crisp pop in front) and BACK (positive Z, receding into room)
                float worldZ;
                if (packet.ReducedMotion)
                {
                    worldZ = 0f;
                }
                else if (isActive)
                {
                    // Active word pops subtly forward toward viewer in Z (-34) while breathing with beat
                    worldZ = -34f - (_smoothedBeat * 22f);
                }
                else if (isPast)
                {
                    // Sung words recede smoothly behind into the 3D listening room (+28..+82)
                    var driftWave = MathF.Sin((t * 1.1f) + (wordIdx * 0.9f)) * 18f;
                    worldZ = 42f + driftWave;
                }
                else
                {
                    // Upcoming words oscillate gently front/back so text dances in 3D space ("sometimes in front sometimes in back")
                    var oscZ = MathF.Sin((t * 1.35f) + (wordIdx * 1.25f) + (line.Index * 0.7f));
                    worldZ = oscZ >= 0f ? (-18f * oscZ) : (52f * -oscZ);
                }

                var (projPt, depthScale) = packet.ReducedMotion
                    ? (new SKPoint(centerX, rowY), 1.0f)
                    : KineticWallStyleRenderer.Project3DPoint(centerX + camX, rowY, worldZ, pitchDeg, yawDeg, 0f, focal, originX, originY);

                // Clamp strictly inside safe bounds for DesktopOverlay
                if (packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
                {
                    projPt = new SKPoint(
                        Math.Clamp(projPt.X, 42f, Math.Max(42f, packet.ViewportWidthDip - 42f)),
                        Math.Clamp(projPt.Y, 36f, Math.Max(36f, packet.ViewportHeightDip - 36f)));
                }

                _projectedWords.Add(new ProjectedWordDraw(
                    wordIdx,
                    word,
                    slot.Width,
                    projPt,
                    depthScale,
                    worldZ,
                    isActive,
                    isPast,
                    wordProgress));
            }
        }

        // Draw back-to-front (largest worldZ first, frontmost negative Z last)
        _projectedWords.Sort(static (a, b) => b.WorldZ.CompareTo(a.WorldZ));
        return _projectedWords.Count > 0;
    }

    private void DrawBackPlane3DLayer(
        in StyleRenderContext context,
        in AlbumCycleTheme theme,
        ActiveLyricSlice slice,
        float originX,
        float originY)
    {
        var packet = context.Packet;
        var fontSize = ResolveVinylFontSize(packet);
        var font = context.Fonts.GetOrCreateFont(packet.ActiveFontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var pitchDeg = packet.ReducedMotion ? 0f : RestingPitchDegrees;
        var yawDeg = packet.ReducedMotion ? 0f : _cameraYawDeg.Value;
        var focal = PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f);

        // Previous line receding far back in Z
        if (!packet.IsCompactCard && slice.PreviousLine is not null)
        {
            var (prevPt, prevScale) = packet.ReducedMotion
                ? (new SKPoint(originX, originY - (fontSize * 2.15f)), 0.72f)
                : KineticWallStyleRenderer.Project3DPoint(originX, originY - (fontSize * 2.15f), 115f, pitchDeg, yawDeg, 0f, focal, originX, originY);
            DrawScaledLineText(in context, font, slice.PreviousLine.RawText, prevPt, 0.56f * prevScale, theme.Tertiary, 0.44f);
        }

        // Next line entering from back-plane room depth
        if (!packet.IsCompactCard && slice.NextLine is not null)
        {
            var (nextPt, nextScale) = packet.ReducedMotion
                ? (new SKPoint(originX, originY + (fontSize * 2.15f)), 0.72f)
                : KineticWallStyleRenderer.Project3DPoint(originX, originY + (fontSize * 2.15f), 96f, pitchDeg, yawDeg, 0f, focal, originX, originY);
            DrawScaledLineText(in context, font, slice.NextLine.RawText, nextPt, 0.58f * nextScale, theme.Secondary, 0.48f);
        }

        // Render words that are currently behind the vinyl/focal plane (WorldZ > 8f)
        for (var i = 0; i < _projectedWords.Count; i++)
        {
            var item = _projectedWords[i];
            if (item.WorldZ > 8f)
            {
                DrawProjectedWord(in context, theme, font, item);
            }
        }
    }

    private void DrawFrontAndFocal3DLayer(
        in StyleRenderContext context,
        in AlbumCycleTheme theme,
        ActiveLyricSlice slice,
        float originX,
        float originY)
    {
        var packet = context.Packet;
        var fontSize = ResolveVinylFontSize(packet);
        var font = context.Fonts.GetOrCreateFont(packet.ActiveFontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);

        SKPoint? activeDotPt = null;
        for (var i = 0; i < _projectedWords.Count; i++)
        {
            var item = _projectedWords[i];
            if (item.WorldZ <= 8f)
            {
                DrawProjectedWord(in context, theme, font, item);
            }

            if (item.IsActive)
            {
                activeDotPt = new SKPoint(item.ScreenPos.X, item.ScreenPos.Y + (fontSize * 0.72f));
            }
        }

        if (activeDotPt is { } dot)
        {
            _aesthetics.DrawFollowingDot(context.Canvas, packet, dot.X, dot.Y, Math.Clamp(fontSize * 0.11f, 2.8f, 6.2f));
        }
    }

    private void DrawProjectedWord(
        in StyleRenderContext context,
        in AlbumCycleTheme theme,
        SKFont font,
        in ProjectedWordDraw item)
    {
        var canvas = context.Canvas;
        var packet = context.Packet;
        var fontId = packet.ActiveFontId;
        var highlightEased = Ease.Highlight(_highlightFade);

        font.GetFontMetrics(out var metrics);
        var baselineOffset = -((metrics.Ascent + metrics.Descent) * 0.5f);

        var activeScale = item.IsActive ? _wordSlamScale.Value : 1.0f;
        var finalScale = Math.Clamp(item.DepthScale * activeScale, 0.55f, 1.48f);

        var depthDim = item.WorldZ > 8f ? Math.Clamp(1f - ((item.WorldZ - 8f) / 140f), 0.42f, 0.88f) : 1.0f;
        var opacity = item.IsActive ? 1.0f : (item.IsPast ? 0.68f : 0.84f) * depthDim;

        // Chromatic depth shadow behind front-plane active/pop words
        if (item.IsActive && !packet.ReducedMotion)
        {
            var echoOffset = Math.Clamp(4.5f + (_smoothedBeat * 5.0f), 3f, 10f);
            var echoSave = canvas.Save();
            try
            {
                canvas.Translate(item.ScreenPos.X + echoOffset, item.ScreenPos.Y + (echoOffset * 0.65f));
                canvas.Scale(finalScale * 0.98f, finalScale * 0.98f);
                context.Legibility.DrawLegibleText(canvas, item.Word.Text, 0f, baselineOffset, SKTextAlign.Center, font, fontId, theme.Secondary, 0.28f, fonts: context.Fonts);
            }
            finally
            {
                canvas.RestoreToCount(echoSave);
            }
        }

        var fillColor = item.IsActive
            ? StyleLayoutHelper.BlendSrgb(theme.Primary, new RgbColor(255, 255, 255), 0.56f * highlightEased)
            : item.IsPast
                ? StyleLayoutHelper.BlendSrgb(theme.Primary, theme.Tertiary, 0.45f)
                : StyleLayoutHelper.BlendSrgb(theme.Secondary, new RgbColor(245, 248, 255), 0.35f);

        var wordSave = canvas.Save();
        try
        {
            canvas.Translate(item.ScreenPos.X, item.ScreenPos.Y);
            canvas.Scale(finalScale, finalScale);

            var wordRect = new SKRect(-item.MeasuredWidth * 0.5f, -font.Size * 0.54f, item.MeasuredWidth * 0.5f, font.Size * 0.48f);
            if (item.IsActive)
            {
                _aesthetics.DrawWordColorSweep(canvas, wordRect, item.WordProgress, packet, opacity);
            }

            context.Legibility.DrawLegibleText(
                canvas,
                item.Word.Text,
                0f,
                baselineOffset,
                SKTextAlign.Center,
                font,
                fontId,
                fillColor,
                opacity: opacity,
                activeGlowColor: item.IsActive ? theme.Primary : null,
                fonts: context.Fonts);

            // Small semantic illustration above key front-plane words
            if ((item.IsActive || item.WorldZ < -8f) &&
                StyleLayoutHelper.ResolveWordVisual(item.Word, packet, allowTrackMoodFallback: false) is { } visual)
            {
                _aesthetics.DrawReactiveVisual(canvas, in context, visual, 0f, -font.Size * 0.78f, Math.Clamp(font.Size * 0.48f, 14f, 28f), opacity * 0.92f, item.IsActive);
            }
        }
        finally
        {
            canvas.RestoreToCount(wordSave);
        }
    }

    private static void DrawScaledLineText(
        in StyleRenderContext context,
        SKFont font,
        string text,
        SKPoint pos,
        float scale,
        RgbColor color,
        float opacity)
    {
        var maxW = ResolveLyricsColumnWidth(context.Packet);
        var measured = TextShaperService.MeasureWidth(text, font, context.Fonts);
        var fit = TextShaperService.ComputeAutoFitScale(measured * scale, maxW, 0.94f);
        var finalScale = scale * fit;

        font.GetFontMetrics(out var metrics);
        var baselineOffset = -((metrics.Ascent + metrics.Descent) * 0.5f);

        var save = context.Canvas.Save();
        try
        {
            context.Canvas.Translate(pos.X, pos.Y);
            context.Canvas.Scale(finalScale, finalScale);
            context.Legibility.DrawLegibleText(context.Canvas, text, 0f, baselineOffset, SKTextAlign.Center, font, context.Packet.ActiveFontId, color, opacity, fonts: context.Fonts);
        }
        finally
        {
            context.Canvas.RestoreToCount(save);
        }
    }

    private static void DrawFallbackPlainLine(
        in StyleRenderContext context,
        in AlbumCycleTheme theme,
        LyricLine line,
        float originX,
        float originY)
    {
        var fontSize = ResolveVinylFontSize(context.Packet);
        var font = context.Fonts.GetOrCreateFont(context.Packet.ActiveFontId, fontSize, enforceMinOverlayFloor: !context.Packet.IsCompactCard);
        DrawScaledLineText(in context, font, line.RawText, new SKPoint(originX, originY), 1.0f, StyleLayoutHelper.BlendSrgb(theme.Primary, new RgbColor(255, 255, 255), 0.5f), 0.96f);
    }

    private static void DrawDeckFooterBar(
        in StyleRenderContext context,
        in AlbumCycleTheme theme,
        float platterCx,
        float platterCy,
        float platterRadius)
    {
        var packet = context.Packet;
        if (packet.IsCompactCard || packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            return;
        }

        var track = packet.SceneSnapshot.Track;
        if (track is null || string.IsNullOrWhiteSpace(track.RawTitle))
        {
            return;
        }

        var footerY = Math.Min(packet.ViewportHeightDip - 24f, platterCy + platterRadius + 46f);
        var captionFont = context.Fonts.GetOrCreateFont(LyricFontId.SystemMono, 12f, enforceMinOverlayFloor: false);
        var caption = string.IsNullOrWhiteSpace(track.RawArtist)
            ? track.RawTitle
            : $"{track.RawArtist}  •  {track.RawTitle}";

        context.Legibility.DrawLegibleText(
            context.Canvas,
            caption,
            platterCx,
            footerY,
            SKTextAlign.Center,
            captionFont,
            LyricFontId.SystemMono,
            theme.Primary,
            opacity: 0.78f,
            fonts: context.Fonts);
    }

    private static (float PlatterCx, float PlatterCy, float PlatterRadius) ResolveTurntableGeometry(FrameRenderPacket packet)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;

        if (packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.50f);
            var r = Math.Clamp(MathF.Min(w * 0.08f, h * 0.14f), 32f, 76f);
            var cx = Math.Clamp(anchorX - (r * 2.1f), r + 32f, w * 0.42f);
            var cy = Math.Clamp(anchorY, r + 32f, h - r - 32f);
            return (cx, cy, r);
        }

        var platterR = packet.IsCompactCard
            ? MathF.Min(w * 0.18f, h * 0.34f)
            : MathF.Min(w * 0.22f, h * 0.38f);
        var platterCx = packet.IsCompactCard ? w * 0.28f : w * 0.30f;
        var platterCy = h * 0.50f;
        return (platterCx, platterCy, platterR);
    }

    private static (float OriginX, float OriginY) ResolveLyricsOrigin(FrameRenderPacket packet)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;

        if (packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.50f);
            return (Math.Clamp(anchorX + (w * 0.08f), w * 0.44f, w - 120f), anchorY);
        }

        return (packet.IsCompactCard ? w * 0.68f : w * 0.70f, h * 0.50f);
    }

    private static float ResolveLyricsColumnWidth(FrameRenderPacket packet) =>
        packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay
            ? Math.Clamp(packet.ViewportWidthDip * 0.44f, 180f, 640f)
            : Math.Clamp(packet.ViewportWidthDip * (packet.IsCompactCard ? 0.40f : 0.43f), 140f, 700f);

    private static float ResolveVinylFontSize(FrameRenderPacket packet)
    {
        var baseScaled = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: packet.IsCompactCard ? 0.66f : 0.62f);
        if (packet.IsCompactCard || packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            return baseScaled;
        }

        // Prevent oversized StageScale on smaller windows/surfaces from overlapping the turntable spindle center
        var viewportCap = Math.Max(22f, packet.ViewportHeightDip * 0.115f);
        return Math.Min(baseScaled, viewportCap);
    }

    private readonly record struct AlbumCycleTheme(
        RgbColor Primary,
        RgbColor Secondary,
        RgbColor Tertiary,
        RgbColor DeepAmbient,
        RgbColor WashGlow,
        double AnchorHueDegrees);

    private readonly record struct WordRow(int StartBufferIndex, int Count, float TotalWidth);

    private readonly record struct WordSlot(int WordIndex, float Width);

    private readonly record struct ProjectedWordDraw(
        int WordIndex,
        LyricWord Word,
        float MeasuredWidth,
        SKPoint ScreenPos,
        float DepthScale,
        float WorldZ,
        bool IsActive,
        bool IsPast,
        double WordProgress);
}
