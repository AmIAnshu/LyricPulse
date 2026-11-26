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
/// Style #11 — Glass Stage (stored under <see cref="OverlayStyleId.VinylGroove"/> for settings compat):
/// full-bleed lightly-blurred playing album cover background synced to song color vibe,
/// sleek medium Apple GlassOS rounded album cover card with hover transport slot alignment,
/// harmonious OKLCH color cycling, and front/back Z-depth kinetic 3D lyrics. Zero per-frame allocs.
/// </summary>
public sealed class VinylGrooveStyleRenderer : IKineticStyleRenderer
{
    public const float RestingPitchDegrees = 5.5f;
    public const float MaxYawDegrees = 9.0f;
    public const float PerspectiveFocalLength = 1100f;

    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _glowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _coverPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _ambientImagePaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath _cardClipPath = new();
    private readonly SKImageFilter _backdropLightBlur = SKImageFilter.CreateBlur(11f, 11f);
    private readonly SKImageFilter _cardGlowBlur = SKImageFilter.CreateBlur(20f, 20f);

    private readonly SKColor[] _grad2Colors = new SKColor[2];
    private readonly SKColor[] _grad3Colors = new SKColor[3];
    private readonly SKColor[] _grad4Colors = new SKColor[4];
    private readonly float[] _stops2 = [0f, 1f];
    private readonly float[] _stops3 = [0f, 0.5f, 1f];
    private readonly float[] _stops4 = [0f, 0.34f, 0.72f, 1f];

    private readonly List<WordRow> _rowBuckets = new(6);
    private readonly List<WordSlot> _rowSlotBuffer = new(24);
    private readonly List<ProjectedWordDraw> _projectedWords = new(24);

    // Cached GPU textures decoded once per track art change (crisp square cover + lightly blurred backdrop)
    private string? _cachedArtHash;
    private int _cachedArtLength;
    private SKImage? _cachedCoverArt;
    private SKImage? _cachedBackdropBlur;

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

    /// <summary>
    /// Computes the exact medium rounded album cover card rectangle in DIPs so Avalonia hover controls
    /// align pixel-accurately with the Skia-rendered album cover across window sizes and fullscreen transitions.
    /// </summary>
    public static SKRect ResolveCoverCardBounds(
        float viewportWidthDip,
        float viewportHeightDip,
        StageRenderSurfaceMode surfaceMode,
        OverlayBoundsConfig? boundsConfig = null,
        float renderScaling = 1.0f)
    {
        var w = Math.Max(1f, viewportWidthDip);
        var h = Math.Max(1f, viewportHeightDip);

        if (surfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            var cfg = boundsConfig ?? OverlayBoundsConfig.FromAnchorZone(AnchorZone.BottomThird, AppSettings.Default.SafeMarginPx);
            var safeMargin = Math.Clamp(cfg.SafeMarginPx / Math.Max(renderScaling, 0.5f), 16f, 120f);
            var minX = Math.Min(safeMargin + 40f, w * 0.45f);
            var maxX = Math.Max(minX, w - minX);
            var minY = Math.Min(safeMargin + 40f, h * 0.45f);
            var maxY = Math.Max(minY, h - minY);
            var anchorX = Math.Clamp((float)(w * cfg.AnchorX), minX, maxX);
            var anchorY = Math.Clamp((float)(h * cfg.AnchorY), minY, maxY);

            var side = Math.Clamp(MathF.Min(w * 0.13f, h * 0.22f), 54f, 124f);
            var cx = Math.Clamp(anchorX - (side * 1.45f), side * 0.5f + 24f, w * 0.42f);
            var cy = Math.Clamp(anchorY, side * 0.5f + 24f, h - (side * 0.5f) - 24f);
            return new SKRect(cx - (side * 0.5f), cy - (side * 0.5f), cx + (side * 0.5f), cy + (side * 0.5f));
        }

        var isCompact = surfaceMode == StageRenderSurfaceMode.GalleryCard;
        var cardSide = isCompact
            ? Math.Clamp(MathF.Min(w * 0.26f, h * 0.48f), 54f, 118f)
            : Math.Clamp(MathF.Min(w * 0.23f, h * 0.44f), 112f, 236f);
        var centerX = isCompact ? w * 0.28f : w * 0.29f;
        var centerY = h * 0.50f;
        return new SKRect(
            centerX - (cardSide * 0.5f),
            centerY - (cardSide * 0.5f),
            centerX + (cardSide * 0.5f),
            centerY + (cardSide * 0.5f));
    }

    public void ResetState()
    {
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

        var fadeRate = 1f / 0.15f;
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
        if (packet.ViewportWidthDip <= 4f || packet.ViewportHeightDip <= 4f || !packet.ShouldDrawStageBackdrop)
        {
            return;
        }

        EnsureAlbumTexturesCached(packet.PlaybackSnapshot?.AlbumArt);
        var theme = ComputeHarmoniousAlbumCycle(packet);
        DrawLightlyBlurredCoverBackdrop(context.Canvas, packet, theme);
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

        var cardRect = ResolveCoverCardBounds(w, h, packet.SurfaceMode, packet.BoundsConfig, packet.RenderScaling);
        var (lyricsOriginX, lyricsOriginY) = ResolveLyricsOrigin(packet);

        // Layer 0: Lightly blurred full-bleed playing album cover background (StageCanvas / GalleryCard only; DesktopOverlay stays transparent)
        if (!skipStageBackdrop)
        {
            if (packet.ShouldDrawStageBackdrop)
            {
                DrawLightlyBlurredCoverBackdrop(canvas, packet, theme);
            }
            else if (slice.CurrentLine is not null)
            {
                var scrimRect = new SKRect(
                    Math.Max(18f, Math.Min(cardRect.Left - 14f, lyricsOriginX - (w * 0.22f))),
                    Math.Max(18f, Math.Min(cardRect.Top - 14f, lyricsOriginY - (h * 0.16f))),
                    Math.Min(w - 18f, Math.Max(cardRect.Right + 14f, lyricsOriginX + (w * 0.24f))),
                    Math.Min(h - 18f, Math.Max(cardRect.Bottom + 14f, lyricsOriginY + (h * 0.18f))));
                context.Legibility.DrawAmbientScrim(
                    canvas,
                    scrimRect,
                    StyleLayoutHelper.ResolveStyleFontSize(packet, 0.72f),
                    theme.DeepAmbient,
                    packet.ScrimIntensity,
                    packet.HighContrastScrim);
            }
        }

        var lyricAlpha = applyLifecycleToLyricsOnly ? Math.Clamp(lifecycle.ContentOpacity, 0f, 1f) : 1.0f;

        // Layer 1: Back-Plane 3D Lyrics & Echoes
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

        // Layer 2: Medium Apple GlassOS Album Cover Card (sharp square art with smooth rounded glass bevel — ZERO vinyl disc or spindle hole)
        DrawMediumGlassCoverCard(in themedContext, theme, cardRect);

        // Layer 3: Foreground & Pop-Out 3D Lyrics
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
                    var fontSize = ResolveGlassStageFontSize(packet);
                    StyleLayoutHelper.DrawSecondarySubline(in themedContext, slice.CurrentLine, lyricsOriginX, lyricsOriginY + (fontSize * 2.55f), fontSize, 0.78f);
                }
            }
            finally
            {
                LegibilityPipeline.EndGlobalOpacityLayer(canvas, lyricSave);
            }
        }

        DrawCardFooterBar(in themedContext, theme, cardRect);
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
        _cardClipPath.Dispose();
        _backdropLightBlur.Dispose();
        _cardGlowBlur.Dispose();
    }

    private void DisposeCachedTextures()
    {
        _cachedCoverArt?.Dispose();
        _cachedCoverArt = null;
        _cachedBackdropBlur?.Dispose();
        _cachedBackdropBlur = null;
        _cachedArtHash = null;
        _cachedArtLength = 0;
    }

    private void EnsureAlbumTexturesCached(AlbumArtPayload? art)
    {
        if (art is null || art.ImageBytes.IsEmpty)
        {
            if (_cachedCoverArt is not null || _cachedBackdropBlur is not null)
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
            var (coverArt, lightBlur) = SkiaAlbumColorExtractor.DecodeGlassStageTextures(
                art.ImageBytes.Span,
                coverDimension: 360,
                backdropDimension: 320);
            _cachedCoverArt = coverArt;
            _cachedBackdropBlur = lightBlur;
        }
        catch
        {
            DisposeCachedTextures();
        }
    }

    private AlbumCycleTheme ComputeHarmoniousAlbumCycle(FrameRenderPacket packet)
    {
        var srcPalette = packet.PlaybackSnapshot?.ActivePalette ?? packet.Palette;

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
            anchorHue = 224.0;
            anchorChroma = 0.09;
        }
        else
        {
            anchorHue = OklabColorMath.NormalizeDegrees(anchor.H);
            anchorChroma = Math.Clamp(Math.Max(anchor.C, scrimLch.C * 2.2), 0.14, 0.27);
        }

        var energy = packet.SafePartyEnergy;
        var phase = packet.ReducedMotion
            ? 0.0
            : (packet.RenderTimeSeconds * (0.50 + (_smoothedBass * 0.20) * energy));

        var swingA = Math.Sin(phase);
        var swingB = Math.Cos(phase * 0.72);
        var swingC = Math.Sin((phase * 1.18) + 1.1);

        var primaryHue = OklabColorMath.NormalizeDegrees(anchorHue + (swingA * 20.0) + (swingC * 8.0));
        var secondaryHue = OklabColorMath.NormalizeDegrees(anchorHue + 18.0 + (swingB * 18.0));
        var tertiaryHue = OklabColorMath.NormalizeDegrees(anchorHue - 16.0 + (swingA * 12.0));
        var ambientHue = OklabColorMath.NormalizeDegrees(anchorHue + (swingB * 8.0));

        var primaryL = Math.Clamp(0.82 + (_smoothedBeat * 0.04), 0.74, 0.92);
        var secondaryL = Math.Clamp(0.70 + (_smoothedMid * 0.05), 0.60, 0.82);
        var tertiaryL = 0.80;
        var deepL = Math.Clamp(0.40 + (_smoothedBass * 0.07), 0.34, 0.50);
        var washL = Math.Clamp(0.56 + (_smoothedBass * 0.08), 0.48, 0.66);

        var primary = OklabColorMath.OklchToSrgbGamutMapped(new OklchColor(primaryL, Math.Clamp(anchorChroma * 0.98, 0.11, 0.25), primaryHue));
        var secondary = OklabColorMath.OklchToSrgbGamutMapped(new OklchColor(secondaryL, Math.Clamp(anchorChroma * 0.92, 0.10, 0.24), secondaryHue));
        var tertiary = OklabColorMath.OklchToSrgbGamutMapped(new OklchColor(tertiaryL, Math.Clamp(anchorChroma * 0.42, 0.04, 0.11), tertiaryHue));
        var deepAmbient = OklabColorMath.OklchToSrgbGamutMapped(new OklchColor(deepL, Math.Clamp(anchorChroma * 0.90, 0.09, 0.23), ambientHue));
        var washGlow = OklabColorMath.OklchToSrgbGamutMapped(new OklchColor(washL, Math.Clamp(anchorChroma * 0.96, 0.11, 0.25), ambientHue));

        return new AlbumCycleTheme(primary, secondary, tertiary, deepAmbient, washGlow);
    }

    private void DrawLightlyBlurredCoverBackdrop(
        SKCanvas canvas,
        FrameRenderPacket packet,
        in AlbumCycleTheme theme)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        var t = packet.ReducedMotion || !isPlaying ? 0f : (float)packet.RenderTimeSeconds;

        // 1. Harmonious album-vibe gradient base
        var baseTopLeft = StyleLayoutHelper.BlendSrgb(new RgbColor(12, 11, 18), theme.DeepAmbient, 0.90f);
        var baseBottomRight = StyleLayoutHelper.BlendSrgb(new RgbColor(10, 9, 15), theme.WashGlow, 0.62f);

        _grad2Colors[0] = baseTopLeft.ToSkColor();
        _grad2Colors[1] = baseBottomRight.ToSkColor();
        using (var bgGrad = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h), _grad2Colors, _stops2, SKShaderTileMode.Clamp))
        {
            _fillPaint.Shader = bgGrad;
            _fillPaint.Color = SKColors.White;
            canvas.DrawRect(0, 0, w, h, _fillPaint);
            _fillPaint.Shader = null;
        }

        // 2. Full-bleed lightly-blurred playing album cover (aspect-fill so artwork remains recognizable without distortion)
        var coverSource = _cachedBackdropBlur ?? _cachedCoverArt;
        if (coverSource is not null)
        {
            var breathe = packet.ReducedMotion || !isPlaying ? 1.10f : 1.10f + (_smoothedBass * 0.03f) + (0.018f * MathF.Sin(t * 0.28f));
            var scale = MathF.Max(w / Math.Max(1f, coverSource.Width), h / Math.Max(1f, coverSource.Height)) * breathe;
            var drawW = coverSource.Width * scale;
            var drawH = coverSource.Height * scale;
            var driftX = packet.ReducedMotion || !isPlaying ? 0f : MathF.Sin(t * 0.22f) * (w * 0.024f);
            var driftY = packet.ReducedMotion || !isPlaying ? 0f : MathF.Cos(t * 0.19f) * (h * 0.022f);
            var dest = new SKRect(
                ((w - drawW) * 0.5f) + driftX,
                ((h - drawH) * 0.5f) + driftY,
                ((w + drawW) * 0.5f) + driftX,
                ((h + drawH) * 0.5f) + driftY);

            var coverAlpha = (byte)Math.Clamp(MathF.Round((0.80f + (_smoothedBass * 0.10f)) * 255f), 0f, 245f);
            _ambientImagePaint.Color = SKColors.White.WithAlpha(coverAlpha);
            _ambientImagePaint.ImageFilter = _backdropLightBlur;
            try
            {
                canvas.DrawImage(coverSource, dest, LinearSampling, _ambientImagePaint);
            }
            finally
            {
                _ambientImagePaint.ImageFilter = null;
            }
        }
        else
        {
            // No album cover yet (Demo / fallback): smooth atmospheric aura matching song mood color vibe
            var cloud1X = (w * 0.30f) + (packet.ReducedMotion ? 0f : MathF.Sin(t * 0.38f) * (w * 0.05f));
            var cloud1Y = (h * 0.50f) + (packet.ReducedMotion ? 0f : MathF.Cos(t * 0.32f) * (h * 0.04f));
            var cloudR = MathF.Max(w, h) * 0.78f;

            _grad3Colors[0] = theme.WashGlow.ToSkColorWithAlpha(0.62f + (_smoothedBass * 0.14f));
            _grad3Colors[1] = theme.Primary.ToSkColorWithAlpha(0.30f);
            _grad3Colors[2] = theme.DeepAmbient.ToSkColorWithAlpha(0.08f);
            using (var cloud = SKShader.CreateRadialGradient(new SKPoint(cloud1X, cloud1Y), cloudR, _grad3Colors, _stops3, SKShaderTileMode.Clamp))
            {
                _fillPaint.Shader = cloud;
                _fillPaint.Color = SKColors.White;
                canvas.DrawRect(0, 0, w, h, _fillPaint);
                _fillPaint.Shader = null;
            }
        }

        // 3. Soft GlassOS tonal scrim keeping lyrics legible on top of bright album art
        _grad3Colors[0] = theme.DeepAmbient.ToSkColorWithAlpha(0.22f);
        _grad3Colors[1] = new SKColor(0, 0, 0, 72);
        _grad3Colors[2] = theme.DeepAmbient.ToSkColorWithAlpha(0.42f);
        using (var scrim = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h), _grad3Colors, _stops3, SKShaderTileMode.Clamp))
        {
            _fillPaint.Shader = scrim;
            _fillPaint.Color = SKColors.White;
            canvas.DrawRect(0, 0, w, h, _fillPaint);
            _fillPaint.Shader = null;
        }
    }

    private void DrawMediumGlassCoverCard(
        in StyleRenderContext context,
        in AlbumCycleTheme theme,
        SKRect cardRect)
    {
        var canvas = context.Canvas;
        var packet = context.Packet;
        var side = MathF.Min(cardRect.Width, cardRect.Height);
        var radius = Math.Clamp(side * 0.115f, 10f, 24f);
        var beatExpand = packet.ReducedMotion ? 0f : (_smoothedBeat * 3.5f);
        var drawRect = new SKRect(
            cardRect.Left - (beatExpand * 0.5f),
            cardRect.Top - (beatExpand * 0.5f),
            cardRect.Right + (beatExpand * 0.5f),
            cardRect.Bottom + (beatExpand * 0.5f));

        // 1. Soft colored GlassOS ambient glow & deep elevation shadow behind medium cover card
        var haloRect = new SKRect(
            drawRect.Left - 8f,
            drawRect.Top + 6f,
            drawRect.Right + 8f,
            drawRect.Bottom + 16f);
        _glowPaint.Color = SKColors.Black.WithAlpha(128);
        _glowPaint.MaskFilter = null;
        _glowPaint.ImageFilter = _cardGlowBlur;
        try
        {
            canvas.DrawRoundRect(haloRect, radius + 4f, radius + 4f, _glowPaint);
        }
        finally
        {
            _glowPaint.ImageFilter = null;
        }

        // 2. Clipped album cover artwork or frosted procedural GlassOS placeholder card
        var clipSave = canvas.Save();
        try
        {
            _cardClipPath.Reset();
            _cardClipPath.AddRoundRect(drawRect, radius, radius);
            canvas.ClipPath(_cardClipPath, SKClipOperation.Intersect, antialias: true);

            if (_cachedCoverArt is not null)
            {
                _coverPaint.Color = SKColors.White;
                canvas.DrawImage(
                    _cachedCoverArt,
                    new SKRect(0f, 0f, _cachedCoverArt.Width, _cachedCoverArt.Height),
                    drawRect,
                    LinearSampling,
                    _coverPaint);
            }
            else
            {
                _grad3Colors[0] = StyleLayoutHelper.BlendSrgb(theme.DeepAmbient, theme.Primary, 0.52f).ToSkColor();
                _grad3Colors[1] = StyleLayoutHelper.BlendSrgb(theme.WashGlow, theme.Secondary, 0.48f).ToSkColor();
                _grad3Colors[2] = theme.DeepAmbient.ToSkColor();
                using var artGrad = SKShader.CreateLinearGradient(
                    new SKPoint(drawRect.Left, drawRect.Top),
                    new SKPoint(drawRect.Right, drawRect.Bottom),
                    _grad3Colors,
                    _stops3,
                    SKShaderTileMode.Clamp);
                _fillPaint.Shader = artGrad;
                _fillPaint.Color = SKColors.White;
                canvas.DrawRect(drawRect, _fillPaint);
                _fillPaint.Shader = null;

                // Subtle centered GlassOS note/mood emblem when track has no artwork image bytes
                var moodVisual = packet.PlaybackSnapshot?.TrackMoodVisual;
                if (moodVisual is not null)
                {
                    _aesthetics.DrawReactiveVisual(
                        canvas,
                        in context,
                        moodVisual,
                        drawRect.MidX,
                        drawRect.MidY,
                        Math.Clamp(side * 0.34f, 18f, 64f),
                        0.92f,
                        isActive: true);
                }
            }

            // Delicate top GlassOS reflection glaze
            _grad4Colors[0] = SKColors.White.WithAlpha(46);
            _grad4Colors[1] = SKColors.White.WithAlpha(12);
            _grad4Colors[2] = SKColors.Transparent;
            _grad4Colors[3] = SKColors.Black.WithAlpha(24);
            using (var glassSheen = SKShader.CreateLinearGradient(
                new SKPoint(drawRect.Left, drawRect.Top),
                new SKPoint(drawRect.Left + (drawRect.Width * 0.35f), drawRect.Bottom),
                _grad4Colors,
                _stops4,
                SKShaderTileMode.Clamp))
            {
                _fillPaint.Shader = glassSheen;
                _fillPaint.Color = SKColors.White;
                canvas.DrawRect(drawRect, _fillPaint);
                _fillPaint.Shader = null;
            }
        }
        finally
        {
            canvas.RestoreToCount(clipSave);
        }

        // 3. Crisp specular Apple Liquid Glass rim border
        _grad2Colors[0] = SKColors.White.WithAlpha(138);
        _grad2Colors[1] = theme.Primary.ToSkColorWithAlpha(0.28f);
        using var rimShader = SKShader.CreateLinearGradient(
            new SKPoint(drawRect.Left, drawRect.Top),
            new SKPoint(drawRect.Right, drawRect.Bottom),
            _grad2Colors,
            _stops2,
            SKShaderTileMode.Clamp);
        _strokePaint.Shader = rimShader;
        _strokePaint.Color = SKColors.White;
        _strokePaint.StrokeWidth = packet.IsCompactCard ? 1.0f : 1.4f;
        try
        {
            canvas.DrawRoundRect(drawRect, radius, radius, _strokePaint);
        }
        finally
        {
            _strokePaint.Shader = null;
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
        var fontSize = ResolveGlassStageFontSize(packet);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var spaceW = Math.Max(fontSize * 0.28f, TextShaperService.MeasureWidth(" ", font, context.Fonts));
        var maxRowWidth = ResolveLyricsColumnWidth(packet);

        var words = line.Words;
        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, words.Length - 1);
        var t = packet.ReducedMotion ? 0f : (float)packet.RenderTimeSeconds;
        var posSec = packet.SceneSnapshot.EffectivePosition.TotalSeconds;

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

        var pitchDeg = packet.ReducedMotion ? 0f : RestingPitchDegrees + (_smoothedBass * 2.0f);
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

                float worldZ;
                if (packet.ReducedMotion)
                {
                    worldZ = 0f;
                }
                else if (isActive)
                {
                    worldZ = -34f - (_smoothedBeat * 22f);
                }
                else if (isPast)
                {
                    var driftWave = MathF.Sin((t * 1.1f) + (wordIdx * 0.9f)) * 18f;
                    worldZ = 42f + driftWave;
                }
                else
                {
                    var oscZ = MathF.Sin((t * 1.35f) + (wordIdx * 1.25f) + (line.Index * 0.7f));
                    worldZ = oscZ >= 0f ? (-18f * oscZ) : (52f * -oscZ);
                }

                var (projPt, depthScale) = packet.ReducedMotion
                    ? (new SKPoint(centerX, rowY), 1.0f)
                    : KineticWallStyleRenderer.Project3DPoint(centerX + camX, rowY, worldZ, pitchDeg, yawDeg, 0f, focal, originX, originY);

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
        var fontSize = ResolveGlassStageFontSize(packet);
        var font = context.Fonts.GetOrCreateFont(packet.ActiveFontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var pitchDeg = packet.ReducedMotion ? 0f : RestingPitchDegrees;
        var yawDeg = packet.ReducedMotion ? 0f : _cameraYawDeg.Value;
        var focal = PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f);

        if (!packet.IsCompactCard && slice.PreviousLine is not null)
        {
            var (prevPt, prevScale) = packet.ReducedMotion
                ? (new SKPoint(originX, originY - (fontSize * 2.15f)), 0.72f)
                : KineticWallStyleRenderer.Project3DPoint(originX, originY - (fontSize * 2.15f), 115f, pitchDeg, yawDeg, 0f, focal, originX, originY);
            DrawScaledLineText(in context, font, slice.PreviousLine.RawText, prevPt, 0.56f * prevScale, theme.Tertiary, 0.48f);
        }

        if (!packet.IsCompactCard && slice.NextLine is not null)
        {
            var (nextPt, nextScale) = packet.ReducedMotion
                ? (new SKPoint(originX, originY + (fontSize * 2.15f)), 0.72f)
                : KineticWallStyleRenderer.Project3DPoint(originX, originY + (fontSize * 2.15f), 96f, pitchDeg, yawDeg, 0f, focal, originX, originY);
            DrawScaledLineText(in context, font, slice.NextLine.RawText, nextPt, 0.58f * nextScale, theme.Secondary, 0.52f);
        }

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
        var fontSize = ResolveGlassStageFontSize(packet);
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

        var depthDim = item.WorldZ > 8f ? Math.Clamp(1f - ((item.WorldZ - 8f) / 140f), 0.44f, 0.88f) : 1.0f;
        var opacity = item.IsActive ? 1.0f : (item.IsPast ? 0.70f : 0.86f) * depthDim;

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
            ? StyleLayoutHelper.BlendSrgb(theme.Primary, new RgbColor(255, 255, 255), 0.58f * highlightEased)
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
        var fontSize = ResolveGlassStageFontSize(context.Packet);
        var font = context.Fonts.GetOrCreateFont(context.Packet.ActiveFontId, fontSize, enforceMinOverlayFloor: !context.Packet.IsCompactCard);
        DrawScaledLineText(in context, font, line.RawText, new SKPoint(originX, originY), 1.0f, StyleLayoutHelper.BlendSrgb(theme.Primary, new RgbColor(255, 255, 255), 0.5f), 0.96f);
    }

    private static void DrawCardFooterBar(
        in StyleRenderContext context,
        in AlbumCycleTheme theme,
        SKRect cardRect)
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

        var footerY = Math.Min(packet.ViewportHeightDip - 24f, cardRect.Bottom + 28f);
        var captionFont = context.Fonts.GetOrCreateFont(LyricFontId.SystemMono, 12f, enforceMinOverlayFloor: false);
        var caption = string.IsNullOrWhiteSpace(track.RawArtist)
            ? track.RawTitle
            : $"{track.RawArtist}  •  {track.RawTitle}";

        context.Legibility.DrawLegibleText(
            context.Canvas,
            caption,
            cardRect.MidX,
            footerY,
            SKTextAlign.Center,
            captionFont,
            LyricFontId.SystemMono,
            StyleLayoutHelper.BlendSrgb(theme.Primary, new RgbColor(255, 255, 255), 0.45f),
            opacity: 0.84f,
            fonts: context.Fonts);
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

        return (packet.IsCompactCard ? w * 0.68f : w * 0.67f, h * 0.50f);
    }

    private static float ResolveLyricsColumnWidth(FrameRenderPacket packet) =>
        packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay
            ? Math.Clamp(packet.ViewportWidthDip * 0.44f, 180f, 640f)
            : Math.Clamp(packet.ViewportWidthDip * (packet.IsCompactCard ? 0.40f : 0.44f), 140f, 700f);

    private static float ResolveGlassStageFontSize(FrameRenderPacket packet)
    {
        var baseScaled = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: packet.IsCompactCard ? 0.66f : 0.62f);
        if (packet.IsCompactCard || packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            return baseScaled;
        }

        var viewportCap = Math.Max(22f, packet.ViewportHeightDip * 0.115f);
        return Math.Min(baseScaled, viewportCap);
    }

    private readonly record struct AlbumCycleTheme(
        RgbColor Primary,
        RgbColor Secondary,
        RgbColor Tertiary,
        RgbColor DeepAmbient,
        RgbColor WashGlow);

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
