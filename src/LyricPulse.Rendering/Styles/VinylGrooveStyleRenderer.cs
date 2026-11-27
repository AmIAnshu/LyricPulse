using System.Globalization;
using LyricPulse.Core.Audio;
using LyricPulse.Core.Color;
using LyricPulse.Core.Lyrics;
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
/// Apple-Music-caliber split stage pairing a left-column square album cover card + live scrubber
/// bar + centered track metadata (with Apple GlassOS hover transport overlay aligned to cover bounds)
/// alongside a right-column left-aligned multi-line scrolling lyric stack with enhanced 3D word pop
/// and Z-depth room perspective over a lightly blurred full-bleed cover backdrop.
/// Zero per-frame heap allocations.
/// </summary>
public sealed class VinylGrooveStyleRenderer : IKineticStyleRenderer
{
    public const float RestingPitchDegrees = 4.5f;
    public const float MaxYawDegrees = 7.5f;
    public const float PerspectiveFocalLength = 1150f;

    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _glowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _coverPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _ambientImagePaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _tracePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPath _cardClipPath = new();
    private readonly SKPath _tracePath = new();
    private readonly SKPath _starPath = new();
    private readonly SKImageFilter _backdropLightBlur = SKImageFilter.CreateBlur(11f, 11f);
    private readonly SKImageFilter _cardShadowBlur = SKImageFilter.CreateBlur(22f, 22f);
    private readonly SKImageFilter _moteBlur = SKImageFilter.CreateBlur(4.5f, 4.5f);
    private readonly SKImageFilter _orbBadgeShadow = SKImageFilter.CreateBlur(5.5f, 5.5f);
    private readonly SKImageFilter _dofNearBlur = SKImageFilter.CreateBlur(0.75f, 0.75f);
    private readonly SKImageFilter _dofFarBlur = SKImageFilter.CreateBlur(1.55f, 1.55f);
    private readonly SKPaint _dofNearPaint;
    private readonly SKPaint _dofFarPaint;

    private static readonly BokehMote[] SpatialMotes = BuildSpatialMotes();

    private readonly SKColor[] _grad2Colors = new SKColor[2];
    private readonly SKColor[] _grad3Colors = new SKColor[3];
    private readonly SKColor[] _grad4Colors = new SKColor[4];
    private readonly float[] _stops2 = [0f, 1f];
    private readonly float[] _stops3 = [0f, 0.5f, 1f];
    private readonly float[] _stops4 = [0f, 0.36f, 0.70f, 1f];
    private readonly List<string> _wrapLineBuffer = new(6);

    private readonly List<WordRow> _rowBuckets = new(6);
    private readonly List<WordSlot> _rowSlotBuffer = new(24);
    private readonly List<ProjectedWordDraw> _projectedWords = new(24);
    private readonly SKPoint[] _traceAnchors = new SKPoint[32];
    private int _traceAnchorCount;
    private int _traceActiveIndex;

    // Cached GPU textures decoded once per track art change
    private string? _cachedArtHash;
    private int _cachedArtLength;
    private SKImage? _cachedCoverArt;
    private SKImage? _cachedBackdropBlur;

    private float _smoothedBass;
    private float _smoothedBeat;
    private float _smoothedMid;
    private float _highlightFade = 1.0f;
    private SpringState _cameraYawDeg = SpringState.AtRest(-3.5f);
    private SpringState _wordSlamScale = SpringState.AtRest(1.12f);
    private SpringState _lineScrollOffsetY = SpringState.AtRest(0f);
    private SpringState _wordLiftY = SpringState.AtRest(0f);
    private int _lastLineKey = -1;
    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private float _lastWordDurationSec = 0.32f;
    private bool _disposed;

    public VinylGrooveStyleRenderer()
    {
        _dofNearPaint = new SKPaint { IsAntialias = true, ImageFilter = _dofNearBlur };
        _dofFarPaint = new SKPaint { IsAntialias = true, ImageFilter = _dofFarBlur };
    }

    public OverlayStyleId StyleId => OverlayStyleId.VinylGroove;

    /// <summary>
    /// Computes the exact album cover square card rectangle in DIPs so Avalonia GlassOS hover controls
    /// align pixel-accurately with the Skia-rendered cover card across window sizes and fullscreen transitions.
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

            var side = Math.Clamp(MathF.Min(w * 0.14f, h * 0.24f), 58f, 136f);
            var cx = Math.Clamp(anchorX - (side * 1.55f), side * 0.5f + 24f, w * 0.40f);
            var cy = Math.Clamp(anchorY, side * 0.5f + 24f, h - (side * 0.5f) - 24f);
            return new SKRect(cx - (side * 0.5f), cy - (side * 0.5f), cx + (side * 0.5f), cy + (side * 0.5f));
        }

        var isCompact = surfaceMode == StageRenderSurfaceMode.GalleryCard;
        var cardSide = isCompact
            ? Math.Clamp(MathF.Min(w * 0.27f, h * 0.48f), 52f, 118f)
            : Math.Clamp(MathF.Min(w * 0.275f, h * 0.48f), 112f, 256f);
        var centerX = w * 0.26f;
        var centerY = isCompact ? h * 0.46f : h * 0.43f;
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
        _cameraYawDeg = SpringState.AtRest(-3.5f);
        _wordSlamScale = SpringState.AtRest(1.12f);
        _lineScrollOffsetY = SpringState.AtRest(0f);
        _wordLiftY = SpringState.AtRest(0f);
        _lastLineKey = -1;
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

        if (slice.ActiveLineIndex != _lastLineKey)
        {
            var prevLine = _lastLineKey;
            _lastLineKey = slice.ActiveLineIndex;
            if (!packet.ReducedMotion && prevLine >= 0)
            {
                var stepAmt = Math.Clamp(ResolveGlassStageFontSize(packet) * 1.15f, 14f, 44f);
                _lineScrollOffsetY = new SpringState(stepAmt, 0f, 0f);
            }
            else
            {
                _lineScrollOffsetY = SpringState.AtRest(0f);
            }
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
        var activeRestScale = packet.IsCompactCard ? 1.07f : 1.14f;

        var key = (slice.ActiveLineIndex, activeIdx);
        if (key != _lastWordKey)
        {
            _lastWordKey = key;
            if (packet.ReducedMotion)
            {
                _wordSlamScale = SpringState.AtRest(1.0f);
                _wordLiftY = SpringState.AtRest(0f);
                _cameraYawDeg = SpringState.AtRest(0f);
                _highlightFade = 0f;
            }
            else
            {
                _wordSlamScale = new SpringState(activeRestScale + 0.19f + (_smoothedBeat * 0.09f), 0f, activeRestScale);
                _wordLiftY = new SpringState(-Math.Clamp(ResolveGlassStageFontSize(packet) * 0.16f, 3.0f, 7.0f), 0f, 0f);
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
            _wordLiftY = SpringState.AtRest(0f);
            _lineScrollOffsetY = SpringState.AtRest(0f);
            _cameraYawDeg = SpringState.AtRest(0f);
            _aesthetics.ResetDot();
            return _highlightFade < 0.999f;
        }

        _cameraYawDeg = _cameraYawDeg.WithTarget(Math.Clamp(targetYaw, -MaxYawDegrees, MaxYawDegrees)).Step(SpringTokens.CameraDrift, dt);
        _wordSlamScale = _wordSlamScale.WithTarget(activeRestScale).Step(SpringTokens.WordEnter.WithTempoCompression(_lastWordDurationSec), dt);
        _wordLiftY = _wordLiftY.WithTarget(0f).Step(SpringTokens.WordEnter.WithTempoCompression(_lastWordDurationSec), dt);
        _lineScrollOffsetY = _lineScrollOffsetY.WithTarget(0f).Step(SpringTokens.CameraDrift, dt);

        var (lyricsLeftX, colW, centerY) = ResolveLyricsColumnGeometry(packet);
        var dotTargetX = lyricsLeftX + (wordFrac * colW * 0.75f);
        var dotTargetY = centerY + (ResolveGlassStageFontSize(packet) * 1.1f);
        var dotActive = packet.ShowFollowingDot && _aesthetics.AdvanceDot(key, dotTargetX, dotTargetY, dt, false, _lastWordDurationSec);

        return !_cameraYawDeg.IsAtRest || !_wordSlamScale.IsAtRest || !_lineScrollOffsetY.IsAtRest || !_wordLiftY.IsAtRest || dotActive || _highlightFade < 0.999f || isPlaying;
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
        var (lyricsLeftX, lyricsColW, lyricsCenterY) = ResolveLyricsColumnGeometry(packet);

        // Layer 0: Lightly-blurred full-bleed cover room (StageCanvas / GalleryCard only; DesktopOverlay stays transparent)
        if (!skipStageBackdrop)
        {
            if (packet.ShouldDrawStageBackdrop)
            {
                DrawLightlyBlurredCoverBackdrop(canvas, packet, theme);
            }
            else if (slice.CurrentLine is not null)
            {
                var scrimRect = new SKRect(
                    Math.Max(18f, Math.Min(cardRect.Left - 14f, lyricsLeftX - 18f)),
                    Math.Max(18f, Math.Min(cardRect.Top - 14f, lyricsCenterY - (h * 0.20f))),
                    Math.Min(w - 18f, Math.Max(cardRect.Right + 14f, lyricsLeftX + lyricsColW + 14f)),
                    Math.Min(h - 18f, Math.Max(cardRect.Bottom + 14f, lyricsCenterY + (h * 0.22f))));
                context.Legibility.DrawAmbientScrim(
                    canvas,
                    scrimRect,
                    StyleLayoutHelper.ResolveStyleFontSize(packet, 0.68f),
                    theme.DeepAmbient,
                    packet.ScrimIntensity,
                    packet.HighContrastScrim);
            }
        }

        // Layer 0.5: Spatial 3D Room Atmosphere (perspective floor plane + 3D parallax bokeh motes on StageCanvas)
        if (packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas && !packet.IsCompactCard)
        {
            DrawSpatial3DRoomAtmosphere(canvas, packet, theme);
        }

        // Layer 1: Left-Column Spatial 3D Album Cover Showcase + Luminous Scrubber Bar + Track Meta
        DrawMediumGlassCoverCard(in themedContext, theme, cardRect);
        DrawLeftScrubberAndTrackMeta(in themedContext, theme, cardRect);

        var lyricAlpha = applyLifecycleToLyricsOnly ? Math.Clamp(lifecycle.ContentOpacity, 0f, 1f) : 1.0f;
        if (lyricAlpha <= 0.004f)
        {
            return;
        }

        // Layer 2 & 3: Right-Column Kinetic Editorial Backplane + Multi-Line 3D Lyric Stack + Extruded Words
        var hasWords = BuildLeftAlignedActiveWordSlots(in themedContext, slice, lyricsLeftX, lyricsColW, lyricsCenterY, out var activeBlockTopY, out var activeBlockBottomY);
        var lyricSave = lyricAlpha < 0.999f ? context.Legibility.BeginGlobalOpacityLayer(canvas, lyricAlpha) : canvas.Save();
        try
        {
            if (!packet.IsCompactCard && packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas && slice.CurrentLine is not null)
            {
                DrawRightColumnKineticWatermarkAndRail(
                    in themedContext,
                    theme,
                    slice,
                    cardRect,
                    lyricsLeftX,
                    lyricsColW,
                    lyricsCenterY,
                    activeBlockTopY,
                    activeBlockBottomY);
            }

            if (!packet.IsCompactCard)
            {
                DrawContextualHistoryAndUpcomingLines(
                    in themedContext,
                    theme,
                    slice,
                    lyricsLeftX,
                    lyricsColW,
                    lyricsCenterY,
                    activeBlockTopY,
                    activeBlockBottomY);
            }

            if (hasWords)
            {
                DrawProjectedActiveLineWords(in themedContext, theme, lyricsLeftX, lyricsColW, lyricsCenterY, activeBlockTopY, activeBlockBottomY);
            }
            else if (slice.CurrentLine is not null)
            {
                DrawLeftFallbackPlainLine(in themedContext, slice.CurrentLine, lyricsLeftX, lyricsColW, lyricsCenterY);
            }

            if (slice.CurrentLine is not null)
            {
                var fontSize = ResolveGlassStageFontSize(packet);
                var subText = StyleLayoutHelper.ResolveSecondaryLineText(slice.CurrentLine, packet.SecondaryLineMode);
                if (!string.IsNullOrWhiteSpace(subText))
                {
                    var subSize = Math.Clamp(fontSize * 0.48f, packet.IsCompactCard ? 9f : 13f, 26f);
                    var subFont = context.Fonts.GetOrCreateFont(LyricFontId.Mulish, subSize, enforceMinOverlayFloor: false);
                    var subY = Math.Min(h - 14f, (hasWords ? activeBlockBottomY : lyricsCenterY + (fontSize * 0.8f)) + (fontSize * 0.55f));
                    DrawFittedLeftText(in themedContext, subFont, subText, lyricsLeftX, subY, lyricsColW, theme.Primary, 0.82f);
                }
            }
        }
        finally
        {
            LegibilityPipeline.EndGlobalOpacityLayer(canvas, lyricSave);
        }
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
        _tracePaint.Dispose();
        _cardClipPath.Dispose();
        _tracePath.Dispose();
        _starPath.Dispose();
        _backdropLightBlur.Dispose();
        _cardShadowBlur.Dispose();
        _moteBlur.Dispose();
        _orbBadgeShadow.Dispose();
        _dofNearPaint.Dispose();
        _dofFarPaint.Dispose();
        _dofNearBlur.Dispose();
        _dofFarBlur.Dispose();
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
            anchorHue = 34.0;
            anchorChroma = 0.11;
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
        var deepL = Math.Clamp(0.38 + (_smoothedBass * 0.07), 0.32, 0.48);
        var washL = Math.Clamp(0.54 + (_smoothedBass * 0.08), 0.46, 0.64);

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

        var baseTopLeft = StyleLayoutHelper.BlendSrgb(new RgbColor(16, 10, 10), theme.DeepAmbient, 0.90f);
        var baseBottomRight = StyleLayoutHelper.BlendSrgb(new RgbColor(10, 9, 14), theme.WashGlow, 0.62f);

        _grad2Colors[0] = baseTopLeft.ToSkColor();
        _grad2Colors[1] = baseBottomRight.ToSkColor();
        using (var bgGrad = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h), _grad2Colors, _stops2, SKShaderTileMode.Clamp))
        {
            _fillPaint.Shader = bgGrad;
            _fillPaint.Color = SKColors.White;
            canvas.DrawRect(0, 0, w, h, _fillPaint);
            _fillPaint.Shader = null;
        }

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

            var coverAlpha = (byte)Math.Clamp(MathF.Round((0.82f + (_smoothedBass * 0.10f)) * 255f), 0f, 246f);
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
            var cloud1X = (w * 0.28f) + (packet.ReducedMotion ? 0f : MathF.Sin(t * 0.38f) * (w * 0.05f));
            var cloud1Y = (h * 0.48f) + (packet.ReducedMotion ? 0f : MathF.Cos(t * 0.32f) * (h * 0.04f));
            var cloudR = MathF.Max(w, h) * 0.78f;

            _grad3Colors[0] = theme.WashGlow.ToSkColorWithAlpha(0.60f + (_smoothedBass * 0.14f));
            _grad3Colors[1] = theme.Primary.ToSkColorWithAlpha(0.28f);
            _grad3Colors[2] = theme.DeepAmbient.ToSkColorWithAlpha(0.08f);
            using (var cloud = SKShader.CreateRadialGradient(new SKPoint(cloud1X, cloud1Y), cloudR, _grad3Colors, _stops3, SKShaderTileMode.Clamp))
            {
                _fillPaint.Shader = cloud;
                _fillPaint.Color = SKColors.White;
                canvas.DrawRect(0, 0, w, h, _fillPaint);
                _fillPaint.Shader = null;
            }
        }

        // Apple-Music style depth scrim: slightly richer darkness across the right lyric column for high legibility
        _grad3Colors[0] = theme.DeepAmbient.ToSkColorWithAlpha(0.20f);
        _grad3Colors[1] = new SKColor(0, 0, 0, 74);
        _grad3Colors[2] = new SKColor(0, 0, 0, 108);
        using (var scrim = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, 0), _grad3Colors, _stops3, SKShaderTileMode.Clamp))
        {
            _fillPaint.Shader = scrim;
            _fillPaint.Color = SKColors.White;
            canvas.DrawRect(0, 0, w, h, _fillPaint);
            _fillPaint.Shader = null;
        }
    }

    private void DrawSpatial3DRoomAtmosphere(
        SKCanvas canvas,
        FrameRenderPacket packet,
        in AlbumCycleTheme theme)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;
        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        var t = packet.ReducedMotion || !isPlaying ? 0f : (float)packet.RenderTimeSeconds;

        // 1. Subtle 3D perspective glass floor horizon grounding the floating album slab & lyrics
        var floorTopY = h * 0.77f;
        _grad3Colors[0] = SKColors.Transparent;
        _grad3Colors[1] = theme.Primary.ToSkColorWithAlpha(0.055f + (_smoothedBass * 0.045f));
        _grad3Colors[2] = theme.DeepAmbient.ToSkColorWithAlpha(0.16f);
        using (var floorGrad = SKShader.CreateLinearGradient(
                   new SKPoint(w * 0.5f, floorTopY),
                   new SKPoint(w * 0.5f, h),
                   _grad3Colors,
                   _stops3,
                   SKShaderTileMode.Clamp))
        {
            _fillPaint.Shader = floorGrad;
            _fillPaint.Color = SKColors.White;
            canvas.DrawRect(0f, floorTopY, w, h - floorTopY, _fillPaint);
            _fillPaint.Shader = null;
        }

        if (!packet.ReducedMotion)
        {
            var vanishX = (w * 0.44f) + (_cameraYawDeg.Value * 3.5f);
            var vanishY = h * 0.60f;
            _strokePaint.Shader = null;
            _strokePaint.StrokeWidth = 0.85f;
            _strokePaint.Color = theme.Primary.ToSkColorWithAlpha(0.055f + (_smoothedBass * 0.04f));
            for (var i = -2; i <= 2; i++)
            {
                var bottomX = (w * 0.5f) + (i * w * 0.28f);
                var startX = vanishX + ((bottomX - vanishX) * 0.44f);
                var startY = vanishY + ((h - vanishY) * 0.44f);
                canvas.DrawLine(startX, startY, bottomX, h, _strokePaint);
            }

            // 2. 18 deterministic 3D depth-parallax floating bokeh light motes
            var focal = PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f);
            var yawDeg = _cameraYawDeg.Value * 1.15f;
            var pitchDeg = RestingPitchDegrees + (_smoothedBass * 1.8f);
            for (var i = 0; i < SpatialMotes.Length; i++)
            {
                var mote = SpatialMotes[i];
                var v = (mote.BaseV - (t * mote.DriftSpeed)) % 1.0f;
                if (v < 0f)
                {
                    v += 1.0f;
                }

                var swayU = mote.BaseU + (MathF.Sin((t * 0.65f) + mote.Phase) * 0.018f);
                var wx = swayU * w;
                var wy = v * h;
                var wz = mote.DepthZ + (MathF.Cos((t * 0.5f) + mote.Phase) * 22f);

                var (pt, depthScale) = KineticWallStyleRenderer.Project3DPoint(
                    wx, wy, wz, pitchDeg, yawDeg, 0f, focal, w * 0.5f, h * 0.5f);
                if (pt.X < -24f || pt.X > w + 24f || pt.Y < -24f || pt.Y > h + 24f)
                {
                    continue;
                }

                var radius = Math.Clamp(mote.Radius * depthScale * (1f + (_smoothedBeat * 0.24f)), 1.2f, 11f);
                var twinkle = 0.55f + (0.45f * MathF.Sin((t * 1.9f) + mote.Phase));
                var alpha = Math.Clamp((mote.DepthZ < 25f ? 0.22f : 0.14f) * twinkle * (0.85f + (_smoothedBeat * 0.35f)), 0.03f, 0.36f);
                var col = StyleLayoutHelper.BlendSrgb(theme.Primary, theme.Secondary, mote.TintMix);

                _glowPaint.Color = col.ToSkColorWithAlpha(alpha);
                _glowPaint.ImageFilter = mote.DepthZ < 40f ? _moteBlur : null;
                try
                {
                    canvas.DrawCircle(pt.X, pt.Y, radius, _glowPaint);
                }
                finally
                {
                    _glowPaint.ImageFilter = null;
                }
            }
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
        var radius = Math.Clamp(side * 0.068f, 6f, 16f);
        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? true;
        var t = packet.ReducedMotion || !isPlaying ? 0f : (float)packet.RenderTimeSeconds;
        var beatExpand = packet.ReducedMotion ? 0f : (_smoothedBeat * 3.2f);
        var drawRect = new SKRect(
            cardRect.Left - (beatExpand * 0.5f),
            cardRect.Top - (beatExpand * 0.5f),
            cardRect.Right + (beatExpand * 0.5f),
            cardRect.Bottom + (beatExpand * 0.5f));

        // 0. Volumetric Backlit Album Aura + Dual 3D Tilted Orbital Glass Rings (StageCanvas / GalleryCard only)
        if (packet.SurfaceMode != StageRenderSurfaceMode.DesktopOverlay)
        {
            var auraR = (side * 0.88f) + (_smoothedBass * 18f);
            _grad3Colors[0] = theme.Primary.ToSkColorWithAlpha(0.28f + (_smoothedBeat * 0.18f));
            _grad3Colors[1] = theme.Secondary.ToSkColorWithAlpha(0.11f + (_smoothedBass * 0.08f));
            _grad3Colors[2] = SKColors.Transparent;
            using (var auraGrad = SKShader.CreateRadialGradient(
                       new SKPoint(drawRect.MidX, drawRect.MidY),
                       auraR,
                       _grad3Colors,
                       _stops3,
                       SKShaderTileMode.Clamp))
            {
                _fillPaint.Shader = auraGrad;
                _fillPaint.Color = SKColors.White;
                canvas.DrawCircle(drawRect.MidX, drawRect.MidY, auraR, _fillPaint);
                _fillPaint.Shader = null;
            }

            if (!packet.IsCompactCard && !packet.ReducedMotion)
            {
                Draw3DOrbitalCoverRings(canvas, drawRect, theme, t, packet.RenderScaling);
            }
        }

        // Compute subtle live spatial 3D tilt for the floating glass album slab on StageCanvas
        var use3DCard = packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas && !packet.ReducedMotion;
        var cardPitch = use3DCard ? 2.6f + (MathF.Sin(t * 0.65f) * 1.8f) + (_smoothedBass * 2.5f) : 0f;
        var cardYaw = use3DCard ? 4.2f + (MathF.Cos(t * 0.52f) * 2.2f) + (_cameraYawDeg.Value * 0.24f) : 0f;
        var cardRoll = use3DCard ? MathF.Sin(t * 0.42f) * 0.85f : 0f;

        var cardSave = canvas.Save();
        try
        {
            if (use3DCard)
            {
                var cardMatrix = KineticWallStyleRenderer.Create3DPerspectiveMatrix(
                    cardPitch,
                    cardYaw,
                    cardRoll,
                    PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f),
                    drawRect.MidX,
                    drawRect.MidY);
                canvas.Concat(in cardMatrix);
            }

            // 1. Soft elevation shadow behind cover card (tracks 3D tilt angle)
            var shadowOffX = use3DCard ? -cardYaw * 0.75f : 0f;
            var shadowRect = new SKRect(
                drawRect.Left - 6f + shadowOffX,
                drawRect.Top + 9f,
                drawRect.Right + 6f + shadowOffX,
                drawRect.Bottom + 18f);
            _glowPaint.Color = SKColors.Black.WithAlpha(148);
            _glowPaint.ImageFilter = _cardShadowBlur;
            try
            {
                canvas.DrawRoundRect(shadowRect, radius + 4f, radius + 4f, _glowPaint);
            }
            finally
            {
                _glowPaint.ImageFilter = null;
            }

            // 1.5 Extruded 3D Frosted Glass Slab Side-Edge (visible thickness behind tilted cover)
            if (use3DCard)
            {
                var slabStepX = Math.Clamp(-cardYaw * 0.36f, -3.4f, 3.4f);
                var slabStepY = Math.Clamp(cardPitch * 0.24f + 1.2f, 0.8f, 3.2f);
                for (var s = 3; s >= 1; s--)
                {
                    var slabRect = new SKRect(
                        drawRect.Left + (slabStepX * s * 0.65f),
                        drawRect.Top + (slabStepY * s * 0.65f),
                        drawRect.Right + (slabStepX * s * 0.65f),
                        drawRect.Bottom + (slabStepY * s * 0.65f));
                    _strokePaint.Shader = null;
                    _strokePaint.StrokeWidth = 1.6f;
                    _strokePaint.Color = StyleLayoutHelper.BlendSrgb(theme.Primary, new RgbColor(255, 255, 255), 0.35f)
                        .ToSkColorWithAlpha(0.18f + ((4 - s) * 0.08f));
                    canvas.DrawRoundRect(slabRect, radius, radius, _strokePaint);
                }
            }

            // 2. Clipped album cover artwork or rich GlassOS procedural card + live liquid specular sheen
            var clipSave = canvas.Save();
            try
            {
                _cardClipPath.Reset();
                _cardClipPath.AddRoundRect(drawRect, radius, radius);
                canvas.ClipPath(_cardClipPath, SKClipOperation.Intersect, antialias: true);

                if (_cachedCoverArt is not null)
                {
                    _fillPaint.Shader = null;
                    _fillPaint.Color = theme.DeepAmbient.ToSkColor();
                    canvas.DrawRect(drawRect, _fillPaint);

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
                    _grad3Colors[0] = StyleLayoutHelper.BlendSrgb(theme.DeepAmbient, theme.Primary, 0.54f).ToSkColor();
                    _grad3Colors[1] = StyleLayoutHelper.BlendSrgb(theme.WashGlow, theme.Secondary, 0.50f).ToSkColor();
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

                // Dynamic liquid-glass diagonal specular reflection sweeping gently across tilted glass face
                if (!packet.IsCompactCard)
                {
                    var sheenShift = packet.ReducedMotion ? 0f : (MathF.Sin(t * 0.72f) * side * 0.24f) + (cardYaw * 3.5f);
                    _grad4Colors[0] = SKColors.White.WithAlpha((byte)Math.Clamp((int)(42 + (_smoothedBeat * 28)), 30, 76));
                    _grad4Colors[1] = SKColors.White.WithAlpha(8);
                    _grad4Colors[2] = SKColors.Transparent;
                    _grad4Colors[3] = SKColors.Black.WithAlpha(34);
                    using var sheen = SKShader.CreateLinearGradient(
                        new SKPoint(drawRect.Left + sheenShift, drawRect.Top),
                        new SKPoint(drawRect.Right, drawRect.Bottom),
                        _grad4Colors,
                        _stops4,
                        SKShaderTileMode.Clamp);
                    _fillPaint.Shader = sheen;
                    _fillPaint.Color = SKColors.White;
                    canvas.DrawRect(drawRect, _fillPaint);
                    _fillPaint.Shader = null;
                }
            }
            finally
            {
                canvas.RestoreToCount(clipSave);
            }

            // 3. Crisp Apple Glass specular rim highlight
            _grad2Colors[0] = SKColors.White.WithAlpha((byte)Math.Clamp((int)(120 + (_smoothedBeat * 56)), 96, 196));
            _grad2Colors[1] = theme.Primary.ToSkColorWithAlpha(0.36f + (_smoothedBeat * 0.22f));
            using var rimShader = SKShader.CreateLinearGradient(
                new SKPoint(drawRect.Left, drawRect.Top),
                new SKPoint(drawRect.Right, drawRect.Bottom),
                _grad2Colors,
                _stops2,
                SKShaderTileMode.Clamp);
            _strokePaint.Shader = rimShader;
            _strokePaint.Color = SKColors.White;
            _strokePaint.StrokeWidth = packet.IsCompactCard ? 0.9f : 1.35f;
            try
            {
                canvas.DrawRoundRect(drawRect, radius, radius, _strokePaint);
            }
            finally
            {
                _strokePaint.Shader = null;
            }
        }
        finally
        {
            canvas.RestoreToCount(cardSave);
        }

        // 4. Whisper-soft glass floor mirror reflection tucked cleanly above scrubber bar (StageCanvas only)
        if (use3DCard && _cachedCoverArt is not null && packet.ViewportHeightDip > 420f)
        {
            var reflH = Math.Clamp(side * 0.055f, 6f, 12f);
            var reflRect = new SKRect(drawRect.Left + 6f, drawRect.Bottom + 2f, drawRect.Right - 6f, drawRect.Bottom + 2f + reflH);
            var rSave = canvas.Save();
            try
            {
                canvas.ClipRect(reflRect);
                canvas.Translate(0f, (drawRect.Bottom * 2f) + 4f);
                canvas.Scale(1f, -1f);
                _coverPaint.Color = SKColors.White.WithAlpha(30);
                canvas.DrawImage(
                    _cachedCoverArt,
                    new SKRect(0f, _cachedCoverArt.Height * 0.82f, _cachedCoverArt.Width, _cachedCoverArt.Height),
                    new SKRect(reflRect.Left, drawRect.Bottom - reflH, reflRect.Right, drawRect.Bottom),
                    LinearSampling,
                    _coverPaint);
                _coverPaint.Color = SKColors.White;
            }
            finally
            {
                canvas.RestoreToCount(rSave);
            }
        }
    }

    private void Draw3DOrbitalCoverRings(
        SKCanvas canvas,
        SKRect cardRect,
        in AlbumCycleTheme theme,
        float t,
        float renderScaling)
    {
        var side = MathF.Min(cardRect.Width, cardRect.Height);
        var cx = cardRect.MidX;
        var cy = cardRect.MidY;
        var focal = PerspectiveFocalLength * Math.Max(renderScaling, 1f);

        for (var ringIdx = 0; ringIdx < 2; ringIdx++)
        {
            var rX = (side * (ringIdx == 0 ? 0.72f : 0.80f)) + (_smoothedBass * (ringIdx == 0 ? 8f : 12f));
            var rY = rX * (ringIdx == 0 ? 0.94f : 0.88f);
            var pitch = ringIdx == 0 ? 64f + (MathF.Sin(t * 0.45f) * 6f) : 70f + (MathF.Cos(t * 0.38f) * 6f);
            var yaw = ringIdx == 0 ? -18f + (MathF.Cos(t * 0.32f) * 10f) : 24f + (MathF.Sin(t * 0.28f) * 10f);
            var roll = (ringIdx == 0 ? 18f : -22f) + (t * (ringIdx == 0 ? 12f : -9f));

            var save = canvas.Save();
            try
            {
                var mat = KineticWallStyleRenderer.Create3DPerspectiveMatrix(pitch, yaw, roll, focal, cx, cy);
                canvas.Concat(in mat);

                var ringRect = new SKRect(cx - rX, cy - rY, cx + rX, cy + rY);
                _grad3Colors[0] = (ringIdx == 0 ? theme.Primary : theme.Secondary).ToSkColorWithAlpha(0.32f + (_smoothedBeat * 0.24f));
                _grad3Colors[1] = SKColors.White.WithAlpha((byte)Math.Clamp((int)(46 + (_smoothedBeat * 40)), 32, 105));
                _grad3Colors[2] = (ringIdx == 0 ? theme.Secondary : theme.Tertiary).ToSkColorWithAlpha(0.08f);
                using var ringGrad = SKShader.CreateSweepGradient(new SKPoint(cx, cy), _grad3Colors, _stops3);

                _strokePaint.Shader = ringGrad;
                _strokePaint.Color = SKColors.White;
                _strokePaint.StrokeWidth = ringIdx == 0 ? 1.5f : 1.1f;
                try
                {
                    canvas.DrawOval(ringRect, _strokePaint);
                }
                finally
                {
                    _strokePaint.Shader = null;
                }

                // Orbiting luminous specular bead riding the tilted 3D glass ring
                var beadAngle = (t * (ringIdx == 0 ? 0.85f : -0.62f)) + (ringIdx * 2.4f);
                var bx = cx + (MathF.Cos(beadAngle) * rX);
                var by = cy + (MathF.Sin(beadAngle) * rY);
                _glowPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp((int)(145 + (_smoothedBeat * 85)), 120, 240));
                _glowPaint.ImageFilter = _moteBlur;
                try
                {
                    canvas.DrawCircle(bx, by, 2.8f + (_smoothedBeat * 1.4f), _glowPaint);
                }
                finally
                {
                    _glowPaint.ImageFilter = null;
                }
            }
            finally
            {
                canvas.RestoreToCount(save);
            }
        }
    }

    private void DrawLeftScrubberAndTrackMeta(
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
        var compactHeight = packet.ViewportHeightDip <= 400f;
        var barY = Math.Min(packet.ViewportHeightDip - (compactHeight ? 58f : 74f), cardRect.Bottom + (compactHeight ? 11f : 16f));
        var posSec = Math.Max(0.0, packet.SceneSnapshot.EffectivePosition.TotalSeconds);
        var durSec = track?.Duration.TotalSeconds > 0.5 ? track.Duration.TotalSeconds : DemoSongProvider.LoopDuration.TotalSeconds;
        var frac = Math.Clamp((float)(posSec / durSec), 0f, 1f);

        // 1. Minimalist Apple Music horizontal scrubber bar aligned exactly with cover edges
        var barLeft = cardRect.Left;
        var barRight = cardRect.Right;
        var barW = Math.Max(10f, barRight - barLeft);
        var fillEndX = barLeft + (barW * frac);

        _fillPaint.Shader = null;
        _fillPaint.Color = new SKColor(255, 255, 255, 62);
        context.Canvas.DrawRoundRect(new SKRect(barLeft, barY - 1.8f, barRight, barY + 1.8f), 1.8f, 1.8f, _fillPaint);

        var fillRight = Math.Max(barLeft + 3f, fillEndX);
        _grad2Colors[0] = theme.Primary.ToSkColorWithAlpha(0.95f);
        _grad2Colors[1] = SKColors.White;
        using (var scrubGrad = SKShader.CreateLinearGradient(
                   new SKPoint(barLeft, barY),
                   new SKPoint(fillRight, barY),
                   _grad2Colors,
                   _stops2,
                   SKShaderTileMode.Clamp))
        {
            _fillPaint.Shader = scrubGrad;
            _fillPaint.Color = SKColors.White;
            context.Canvas.DrawRoundRect(new SKRect(barLeft, barY - 1.8f, fillRight, barY + 1.8f), 1.8f, 1.8f, _fillPaint);
            _fillPaint.Shader = null;
        }

        if (!packet.ReducedMotion)
        {
            _glowPaint.Color = theme.Primary.ToSkColorWithAlpha(0.45f + (_smoothedBeat * 0.35f));
            _glowPaint.ImageFilter = _moteBlur;
            try
            {
                context.Canvas.DrawCircle(fillEndX, barY, 5.4f + (_smoothedBeat * 2.0f), _glowPaint);
            }
            finally
            {
                _glowPaint.ImageFilter = null;
            }
        }

        _fillPaint.Color = SKColors.White;
        context.Canvas.DrawCircle(fillEndX, barY, 3.6f, _fillPaint);

        // 2. Left & right elapsed/total timestamps directly below scrubber
        var timeY = barY + (compactHeight ? 12f : 14f);
        var monoFont = context.Fonts.GetOrCreateFont(LyricFontId.SystemMono, compactHeight ? 9.5f : 10.5f, enforceMinOverlayFloor: false);
        var elapsedStr = FormatClock(TimeSpan.FromSeconds(posSec));
        var durationStr = FormatClock(TimeSpan.FromSeconds(durSec));

        context.Legibility.DrawLegibleText(
            context.Canvas, elapsedStr, barLeft, timeY, SKTextAlign.Left, monoFont, LyricFontId.SystemMono,
            new RgbColor(235, 238, 245), opacity: 0.78f, includeStrokeHalo: false, fonts: context.Fonts);
        context.Legibility.DrawLegibleText(
            context.Canvas, durationStr, barRight, timeY, SKTextAlign.Right, monoFont, LyricFontId.SystemMono,
            new RgbColor(235, 238, 245), opacity: 0.78f, includeStrokeHalo: false, fonts: context.Fonts);

        if (track is null || string.IsNullOrWhiteSpace(track.RawTitle))
        {
            return;
        }

        // 3. Centered Bold Song Title + Subtitle directly underneath scrubber
        var titleSize = Math.Clamp(packet.ViewportHeightDip * 0.042f, compactHeight ? 14f : 16f, 21f);
        var titleFont = context.Fonts.GetOrCreateFont(LyricFontId.Jost, titleSize, enforceMinOverlayFloor: false);
        var titleY = timeY + (compactHeight ? 18f : 22f);
        DrawFittedCenterText(in context, titleFont, track.RawTitle, cardRect.MidX, titleY, barW * 1.15f, new RgbColor(255, 255, 255), 0.96f);

        var subParts = !string.IsNullOrWhiteSpace(track.AlbumTitle) && !string.Equals(track.AlbumTitle, track.RawTitle, StringComparison.OrdinalIgnoreCase)
            ? $"{track.RawArtist}  •  {track.AlbumTitle}"
            : track.RawArtist;
        if (!string.IsNullOrWhiteSpace(subParts))
        {
            var subSize = Math.Clamp(titleSize * 0.70f, 10.5f, 14f);
            var subFont = context.Fonts.GetOrCreateFont(LyricFontId.Mulish, subSize, enforceMinOverlayFloor: false);
            DrawFittedCenterText(
                in context, subFont, subParts, cardRect.MidX, titleY + (compactHeight ? 14f : 18f), barW * 1.15f,
                StyleLayoutHelper.BlendSrgb(theme.Primary, new RgbColor(240, 244, 250), 0.65f), 0.76f);
        }
    }

    private bool BuildLeftAlignedActiveWordSlots(
        in StyleRenderContext context,
        ActiveLyricSlice slice,
        float leftX,
        float maxColWidth,
        float centerY,
        out float blockTopY,
        out float blockBottomY)
    {
        _traceActiveIndex = 0;
        _traceAnchorCount = 0;
        _rowBuckets.Clear();
        _rowSlotBuffer.Clear();
        _projectedWords.Clear();

        var line = slice.CurrentLine;
        if (line is null || line.Words.IsEmpty)
        {
            blockTopY = centerY - 18f;
            blockBottomY = centerY + 18f;
            return false;
        }

        var packet = context.Packet;
        var fontId = packet.ActiveFontId;
        var fontSize = ResolveGlassStageFontSize(packet);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var spaceW = Math.Max(fontSize * 0.27f, TextShaperService.MeasureWidth(" ", font, context.Fonts));

        var words = line.Words;
        var activeIdx = Math.Clamp(slice.ActiveWordIndex, 0, words.Length - 1);
        var t = packet.ReducedMotion ? 0f : (float)packet.RenderTimeSeconds;
        var posSec = packet.SceneSnapshot.EffectivePosition.TotalSeconds;

        var currentRow = new WordRow(0, 0, 0f);
        for (var i = 0; i < words.Length; i++)
        {
            var measured = TextShaperService.MeasureWidth(words[i].Text, font, context.Fonts);
            var addW = currentRow.Count == 0 ? measured : spaceW + measured;
            if (currentRow.Count > 0 && currentRow.TotalWidth + addW > maxColWidth)
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
        var rollDeg = packet.ReducedMotion ? 0f : MathF.Sin(t * 0.52f) * 1.15f;
        var focal = PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f);
        var rowHeight = fontSize * 1.30f;
        var scrollY = packet.ReducedMotion ? 0f : _lineScrollOffsetY.Value;
        var startRowY = (centerY + scrollY) - ((_rowBuckets.Count - 1) * rowHeight * 0.5f);
        var pivotX = leftX + (maxColWidth * 0.25f);

        blockTopY = startRowY - (fontSize * 0.65f);
        blockBottomY = startRowY + ((_rowBuckets.Count - 1) * rowHeight) + (fontSize * 0.65f);

        for (var r = 0; r < _rowBuckets.Count; r++)
        {
            var row = _rowBuckets[r];
            var rowY = startRowY + (r * rowHeight);
            var fitScale = row.TotalWidth > maxColWidth ? maxColWidth / Math.Max(1f, row.TotalWidth) : 1.0f;
            var cursorX = leftX;

            for (var s = 0; s < row.Count; s++)
            {
                var slot = _rowSlotBuffer[row.StartBufferIndex + s];
                var wordIdx = slot.WordIndex;
                var word = words[wordIdx];
                var wordW = slot.Width * fitScale;
                var wordCenterX = cursorX + (wordW * 0.5f);
                cursorX += wordW + (spaceW * fitScale);

                var isActive = wordIdx == activeIdx;
                var isPast = wordIdx < activeIdx;
                var wordProgress = word.EndTime > word.StartTime
                    ? Math.Clamp((posSec - word.StartTime.TotalSeconds) / (word.EndTime - word.StartTime).TotalSeconds, 0.0, 1.0)
                    : 0.5;

                // Active word bursts forward in 3D Z (-74..-108); upcoming waves oscillate smoothly; past stays crisp near-focal
                var cadenceY = !packet.ReducedMotion && !packet.IsCompactCard
                    ? MathF.Sin((wordIdx * 1.15f) + (line.Index * 0.65f)) * (fontSize * 0.085f)
                    : 0f;
                var rowBaselineY = rowY + cadenceY;

                float worldZ;
                if (packet.ReducedMotion)
                {
                    worldZ = 0f;
                }
                else if (isActive)
                {
                    worldZ = -74f - (_smoothedBeat * 34f);
                }
                else if (isPast)
                {
                    worldZ = -10f + (MathF.Sin((t * 1.1f) + (wordIdx * 0.8f)) * 7f);
                }
                else
                {
                    worldZ = 18f + (MathF.Sin((t * 1.80f) + (wordIdx * 1.15f) + (line.Index * 0.7f)) * 18f);
                }

                var (projPt, depthScale) = packet.ReducedMotion
                    ? (new SKPoint(wordCenterX, rowBaselineY), 1.0f)
                    : KineticWallStyleRenderer.Project3DPoint(wordCenterX, rowBaselineY, worldZ, pitchDeg, yawDeg, rollDeg, focal, pivotX, centerY);

                if (packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
                {
                    projPt = new SKPoint(
                        Math.Clamp(projPt.X, 42f, Math.Max(42f, packet.ViewportWidthDip - 42f)),
                        Math.Clamp(projPt.Y, 36f, Math.Max(36f, packet.ViewportHeightDip - 36f)));
                }

                if (wordIdx < _traceAnchors.Length)
                {
                    _traceAnchors[wordIdx] = new SKPoint(projPt.X, projPt.Y + (fontSize * 0.44f));
                    _traceAnchorCount = Math.Max(_traceAnchorCount, wordIdx + 1);
                }

                _projectedWords.Add(new ProjectedWordDraw(
                    wordIdx,
                    word,
                    wordW,
                    projPt,
                    depthScale * fitScale,
                    worldZ,
                    isActive,
                    isPast,
                    wordProgress));
            }
        }

        _traceActiveIndex = activeIdx;
        _projectedWords.Sort(static (a, b) => b.WorldZ.CompareTo(a.WorldZ));
        return _projectedWords.Count > 0;
    }

    private void DrawRightColumnKineticWatermarkAndRail(
        in StyleRenderContext context,
        in AlbumCycleTheme theme,
        ActiveLyricSlice slice,
        SKRect cardRect,
        float leftX,
        float colW,
        float centerY,
        float activeBlockTopY,
        float activeBlockBottomY)
    {
        var packet = context.Packet;
        var canvas = context.Canvas;
        var line = slice.CurrentLine!;
        var fontSize = ResolveGlassStageFontSize(packet);
        var font = context.Fonts.GetOrCreateFont(packet.ActiveFontId, fontSize, enforceMinOverlayFloor: true);

        // 1. Architectural Glass Watermark shifted cleanly up-right so it frames the deck without muddying active glyphs
        var wmText = !line.Words.IsEmpty
            ? line.Words[Math.Clamp(slice.ActiveWordIndex, 0, line.Words.Length - 1)].Text.ToUpperInvariant()
            : line.RawText.ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(wmText))
        {
            var clipSave = canvas.Save();
            try
            {
                canvas.ClipRect(new SKRect(Math.Max(0f, leftX - 28f), 0f, packet.ViewportWidthDip, packet.ViewportHeightDip));
                var wmPivotX = leftX + (colW * 0.54f);
                var wmPivotY = centerY - (fontSize * 0.35f);
                var yaw = packet.ReducedMotion ? 0f : _cameraYawDeg.Value * 0.45f;
                var (wmPt, _) = packet.ReducedMotion
                    ? (new SKPoint(wmPivotX, wmPivotY), 1f)
                    : KineticWallStyleRenderer.Project3DPoint(wmPivotX, wmPivotY, 145f, RestingPitchDegrees * 0.6f, yaw, 0f, PerspectiveFocalLength, wmPivotX, wmPivotY);

                var wmScale = 2.35f + (_smoothedBeat * 0.22f);
                var wmMeasured = TextShaperService.MeasureWidth(wmText, font, context.Fonts);
                var wmFit = TextShaperService.ComputeAutoFitScale(wmMeasured * wmScale, colW * 1.06f, 1.0f);
                var finalScale = wmScale * wmFit;
                var wmAlpha = Math.Clamp(0.045f + (_smoothedBeat * 0.045f), 0.038f, 0.115f);

                font.GetFontMetrics(out var m);
                var baseline = -((m.Ascent + m.Descent) * 0.5f);
                var wmSave = canvas.Save();
                try
                {
                    canvas.Translate(wmPt.X, wmPt.Y);
                    canvas.Scale(finalScale, finalScale);
                    context.Legibility.DrawLegibleText(
                        canvas, wmText, 0f, baseline, SKTextAlign.Center, font, packet.ActiveFontId,
                        StyleLayoutHelper.BlendSrgb(theme.Secondary, theme.Tertiary, 0.45f),
                        opacity: wmAlpha, includeStrokeHalo: false, fonts: context.Fonts);
                }
                finally
                {
                    canvas.RestoreToCount(wmSave);
                }
            }
            finally
            {
                canvas.RestoreToCount(clipSave);
            }
        }

        // 2. Frosted Telemetry & Track Mood Capsule + Sculpted 3D Harmonic Spine
        var totalLines = packet.SceneSnapshot.Document?.Lines.Length ?? 0;
        if (totalLines > 0)
        {
            var railY = Math.Clamp(cardRect.Top - 22f, 54f, Math.Max(54f, packet.ViewportHeightDip * 0.22f));
            var moodVisual = packet.PlaybackSnapshot?.TrackMoodVisual;
            var capLeft = leftX - 18f;
            var capTop = railY - 12f;
            var hasMood = moodVisual is not null;
            var capW = hasMood ? 120f : 100f;
            var capRect = new SKRect(capLeft, capTop, capLeft + capW, capTop + 24f);

            _grad2Colors[0] = StyleLayoutHelper.BlendSrgb(theme.DeepAmbient, theme.Primary, 0.22f).ToSkColorWithAlpha(0.58f);
            _grad2Colors[1] = theme.DeepAmbient.ToSkColorWithAlpha(0.68f);
            using (var capFill = SKShader.CreateLinearGradient(new SKPoint(capRect.Left, capRect.Top), new SKPoint(capRect.Right, capRect.Bottom), _grad2Colors, _stops2, SKShaderTileMode.Clamp))
            {
                _fillPaint.Shader = capFill;
                _fillPaint.Color = SKColors.White;
                canvas.DrawRoundRect(capRect, 12f, 12f, _fillPaint);
                _fillPaint.Shader = null;
            }

            _grad2Colors[0] = SKColors.White.WithAlpha(95);
            _grad2Colors[1] = theme.Primary.ToSkColorWithAlpha(0.35f);
            using (var capRim = SKShader.CreateLinearGradient(new SKPoint(capRect.Left, capRect.Top), new SKPoint(capRect.Right, capRect.Bottom), _grad2Colors, _stops2, SKShaderTileMode.Clamp))
            {
                _strokePaint.Shader = capRim;
                _strokePaint.Color = SKColors.White;
                _strokePaint.StrokeWidth = 1.0f;
                canvas.DrawRoundRect(capRect, 12f, 12f, _strokePaint);
                _strokePaint.Shader = null;
            }

            var textStartX = capLeft + 11f;
            if (moodVisual is not null)
            {
                _aesthetics.DrawReactiveVisual(canvas, in context, moodVisual, capLeft + 15f, railY, 13.5f, 0.92f, isActive: true);
                textStartX = capLeft + 27f;
            }

            var monoFont = context.Fonts.GetOrCreateFont(LyricFontId.SystemMono, 10.5f, enforceMinOverlayFloor: false);
            var label = string.Create(CultureInfo.InvariantCulture, $"{Math.Clamp(slice.ActiveLineIndex + 1, 1, totalLines):D2}/{totalLines:D2}");
            context.Legibility.DrawLegibleText(
                canvas, label, textStartX, railY + 3.5f, SKTextAlign.Left, monoFont, LyricFontId.SystemMono,
                StyleLayoutHelper.BlendSrgb(theme.Primary, new RgbColor(255, 255, 255), 0.65f),
                opacity: 0.88f, includeStrokeHalo: false, fonts: context.Fonts, includeAmbientShadow: false);

            var bands = packet.EffectiveAudioMetrics.SafeBands;
            var barsStartX = textStartX + 42f;
            for (var b = 0; b < 6 && b < bands.Length; b++)
            {
                var mag = Math.Clamp(bands[(b * 3) % bands.Length], 0.12f, 1.0f);
                var barH = 3.5f + (mag * 9.5f);
                var bx = barsStartX + (b * 5.2f);
                _fillPaint.Shader = null;
                _fillPaint.Color = StyleLayoutHelper.BlendSrgb(theme.Primary, theme.Secondary, b / 5f).ToSkColorWithAlpha(0.45f + (mag * 0.45f));
                canvas.DrawRoundRect(new SKRect(bx, railY - (barH * 0.5f), bx + 2.8f, railY + (barH * 0.5f)), 1.4f, 1.4f, _fillPaint);
            }

            // 3. Sculpted 3D Harmonic Spine flanking the right lyric column + audio aura ticks + laser bridge
            var railX = leftX - 20f;
            var railTop = Math.Max(capRect.Bottom + 18f, centerY - (packet.ViewportHeightDip * 0.22f));
            var railBottom = Math.Min(packet.ViewportHeightDip - 36f, centerY + (packet.ViewportHeightDip * 0.22f));
            if (railBottom - railTop > 40f && railX > cardRect.Right + 14f)
            {
                var lineFrac = totalLines <= 1 ? 0.5f : Math.Clamp(slice.ActiveLineIndex / (float)(totalLines - 1), 0f, 1f);
                var beadY = railTop + ((railBottom - railTop) * lineFrac);

                _fillPaint.Shader = null;
                _fillPaint.Color = SKColors.White.WithAlpha(34);
                canvas.DrawRoundRect(new SKRect(railX - 1.2f, railTop, railX + 1.2f, railBottom), 1.2f, 1.2f, _fillPaint);

                _grad2Colors[0] = theme.Primary.ToSkColorWithAlpha(0.88f);
                _grad2Colors[1] = theme.Secondary.ToSkColorWithAlpha(0.95f);
                using (var railGrad = SKShader.CreateLinearGradient(new SKPoint(railX, railTop), new SKPoint(railX, beadY), _grad2Colors, _stops2, SKShaderTileMode.Clamp))
                {
                    _fillPaint.Shader = railGrad;
                    _fillPaint.Color = SKColors.White;
                    canvas.DrawRoundRect(new SKRect(railX - 1.4f, railTop, railX + 1.4f, Math.Max(railTop + 3f, beadY)), 1.4f, 1.4f, _fillPaint);
                    _fillPaint.Shader = null;
                }

                // 10 subtle outward-facing audio aura ticks bridging cover card and lyric spine
                for (var tick = 0; tick < 10; tick++)
                {
                    var tickFrac = tick / 9f;
                    var tickY = railTop + ((railBottom - railTop) * tickFrac);
                    var bandMag = Math.Clamp(bands[(tick * 3) % bands.Length], 0.08f, 1.0f);
                    var tickLen = 2f + (bandMag * 9f);
                    var tickCol = StyleLayoutHelper.BlendSrgb(theme.Primary, theme.Secondary, tickFrac);
                    _fillPaint.Shader = null;
                    _fillPaint.Color = tickCol.ToSkColorWithAlpha(0.18f + (bandMag * 0.27f));
                    canvas.DrawRoundRect(new SKRect(railX - 4f - tickLen, tickY - 0.9f, railX - 4f, tickY + 0.9f), 0.9f, 0.9f, _fillPaint);
                }

                _glowPaint.Color = theme.Primary.ToSkColorWithAlpha(0.55f + (_smoothedBeat * 0.35f));
                _glowPaint.ImageFilter = _moteBlur;
                try
                {
                    canvas.DrawCircle(railX, beadY, 4.4f + (_smoothedBeat * 1.5f), _glowPaint);
                }
                finally
                {
                    _glowPaint.ImageFilter = null;
                }

                _fillPaint.Color = SKColors.White;
                canvas.DrawCircle(railX, beadY, 2.5f, _fillPaint);

                // Starburst Prism Node locked to active lyric deck + Horizontal Laser Bridge Beam
                var activeFocusY = Math.Clamp((activeBlockTopY + activeBlockBottomY) * 0.5f, railTop, railBottom);
                var deckEdgeX = Math.Max(railX + 6f, leftX - 12f);
                _grad2Colors[0] = SKColors.White.WithAlpha(220);
                _grad2Colors[1] = theme.Primary.ToSkColorWithAlpha(0f);
                using (var bridgeGrad = SKShader.CreateLinearGradient(new SKPoint(railX + 3f, activeFocusY), new SKPoint(deckEdgeX + 6f, activeFocusY), _grad2Colors, _stops2, SKShaderTileMode.Clamp))
                {
                    _strokePaint.Shader = bridgeGrad;
                    _strokePaint.Color = SKColors.White;
                    _strokePaint.StrokeWidth = 1.6f;
                    canvas.DrawLine(railX + 3f, activeFocusY, deckEdgeX + 6f, activeFocusY, _strokePaint);
                    _strokePaint.Shader = null;
                }

                DrawFourPointStar(canvas, railX, activeFocusY, 6.4f + (_smoothedBeat * 2.5f), 2.0f, SKColors.White, withGlow: true);
            }
        }
    }

    private void DrawProjectedActiveLineWords(
        in StyleRenderContext context,
        in AlbumCycleTheme theme,
        float leftX,
        float colW,
        float centerY,
        float activeBlockTopY,
        float activeBlockBottomY)
    {
        var packet = context.Packet;
        var fontSize = ResolveGlassStageFontSize(packet);
        var font = context.Fonts.GetOrCreateFont(packet.ActiveFontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var highlightEased = Ease.Highlight(_highlightFade);
        font.GetFontMetrics(out var metrics);
        var baselineOffset = -((metrics.Ascent + metrics.Descent) * 0.5f);

        // Compute active line singing progress (0..1) from active word index & within-word progress
        var activeItemProgress = 0.5;
        for (var i = 0; i < _projectedWords.Count; i++)
        {
            if (_projectedWords[i].IsActive)
            {
                activeItemProgress = _projectedWords[i].WordProgress;
                break;
            }
        }
        var lineWordTotal = Math.Max(1, _traceAnchorCount);
        var lineProg = Math.Clamp((_traceActiveIndex + (float)activeItemProgress) / lineWordTotal, 0.05f, 0.98f);

        // 1. True Elliptical Volumetric Spotlight Aura + Open-Ended VisionOS Frosted Glass Focus Deck (StageCanvas only!)
        if (!packet.IsCompactCard && packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas)
        {
            var deckLeft = leftX - 12f;
            var deckRight = leftX + colW + 8f;
            var deckTop = activeBlockTopY - 8f;
            var deckBottom = activeBlockBottomY + 10f;
            var spotCx = leftX + (colW * 0.42f);
            var spotCy = (deckTop + deckBottom) * 0.5f;

            // True elliptical radial shader with localTransform so gradient smoothly reaches 0 alpha (fixes clipped oval edge!)
            var rx = Math.Clamp(colW * 0.58f, 90f, 340f);
            var ry = Math.Max(36f, ((activeBlockBottomY - activeBlockTopY) * 0.95f) + 24f);
            _grad3Colors[0] = theme.Primary.ToSkColorWithAlpha(0.16f + (_smoothedBeat * 0.10f));
            _grad3Colors[1] = theme.Secondary.ToSkColorWithAlpha(0.06f + (_smoothedBass * 0.04f));
            _grad3Colors[2] = SKColors.Transparent;
            var ellipseMat = SKMatrix.Concat(
                SKMatrix.CreateTranslation(spotCx, spotCy),
                SKMatrix.CreateScale(1f, ry / rx));
            using (var spotGrad = SKShader.CreateRadialGradient(SKPoint.Empty, rx, _grad3Colors, _stops3, SKShaderTileMode.Clamp, ellipseMat))
            {
                _fillPaint.Shader = spotGrad;
                _fillPaint.Color = SKColors.White;
                context.Canvas.DrawOval(spotCx, spotCy, rx, ry, _fillPaint);
                _fillPaint.Shader = null;
            }

            // Open-Ended VisionOS Frosted Glass Focus Deck (dissolves smoothly into transparent space on the right)
            var deckRect = new SKRect(deckLeft, deckTop, deckRight, deckBottom);
            _grad3Colors[0] = StyleLayoutHelper.BlendSrgb(theme.DeepAmbient, theme.Primary, 0.28f).ToSkColorWithAlpha(0.32f + (_smoothedBeat * 0.09f));
            _grad3Colors[1] = theme.DeepAmbient.ToSkColorWithAlpha(0.18f);
            _grad3Colors[2] = SKColors.Transparent;
            using (var deckFill = SKShader.CreateLinearGradient(new SKPoint(deckLeft, spotCy), new SKPoint(deckRight, spotCy), _grad3Colors, _stops3, SKShaderTileMode.Clamp))
            {
                _fillPaint.Shader = deckFill;
                _fillPaint.Color = SKColors.White;
                context.Canvas.DrawRoundRect(deckRect, 16f, 16f, _fillPaint);
                _fillPaint.Shader = null;
            }

            _grad3Colors[0] = SKColors.White.WithAlpha(88);
            _grad3Colors[1] = theme.Primary.ToSkColorWithAlpha(0.28f);
            _grad3Colors[2] = SKColors.Transparent;
            using (var deckRim = SKShader.CreateLinearGradient(new SKPoint(deckLeft, deckTop), new SKPoint(deckRight, deckTop), _grad3Colors, _stops3, SKShaderTileMode.Clamp))
            {
                _strokePaint.Shader = deckRim;
                _strokePaint.Color = SKColors.White;
                _strokePaint.StrokeWidth = 1.1f;
                context.Canvas.DrawRoundRect(deckRect, 16f, 16f, _strokePaint);
                _strokePaint.Shader = null;
            }

            // Sweeping Anamorphic Top Edge Flare gliding along top rim synced to vocal progress
            var flareX = leftX + (colW * 0.85f * lineProg);
            var flareHalfW = Math.Clamp(colW * 0.16f, 36f, 96f);
            _grad3Colors[0] = SKColors.Transparent;
            _grad3Colors[1] = SKColors.White.WithAlpha((byte)Math.Clamp((int)(145 + (_smoothedBeat * 80)), 120, 235));
            _grad3Colors[2] = SKColors.Transparent;
            using (var flareShader = SKShader.CreateLinearGradient(new SKPoint(flareX - flareHalfW, deckTop), new SKPoint(flareX + flareHalfW, deckTop), _grad3Colors, _stops3, SKShaderTileMode.Clamp))
            {
                _strokePaint.Shader = flareShader;
                _strokePaint.Color = SKColors.White;
                _strokePaint.StrokeWidth = 1.8f;
                context.Canvas.DrawLine(flareX - flareHalfW, deckTop, flareX + flareHalfW, deckTop, _strokePaint);
                _strokePaint.Shader = null;
            }

            if (!packet.ReducedMotion)
            {
                DrawFourPointStar(context.Canvas, flareX, deckTop, 5.0f + (_smoothedBeat * 1.8f), 1.4f, SKColors.White, withGlow: true);
            }
        }

        // Luminous Kinetic Wall-inspired Bezier trace curve weaving under completed words of the active line
        var completedCount = Math.Min(_traceAnchorCount - 1, _traceActiveIndex);
        if (!packet.IsCompactCard && !packet.ReducedMotion && completedCount > 0)
        {
            _tracePath.Rewind();
            _tracePath.MoveTo(_traceAnchors[0]);
            for (var c = 1; c <= completedCount; c++)
            {
                var p0 = _traceAnchors[c - 1];
                var p1 = _traceAnchors[c];
                var midX = (p0.X + p1.X) * 0.5f;
                var midY = ((p0.Y + p1.Y) * 0.5f) + ((c % 2 == 0 ? 1f : -1f) * 3.5f);
                _tracePath.QuadTo(midX, midY, p1.X, p1.Y);
            }

            _tracePaint.StrokeWidth = 1.8f + (_smoothedBeat * 1.4f);
            _tracePaint.Color = theme.Primary.ToSkColorWithAlpha(0.24f + (_smoothedBass * 0.22f));
            context.Canvas.DrawPath(_tracePath, _tracePaint);
        }

        var tSec = packet.ReducedMotion ? 0f : (float)packet.RenderTimeSeconds;
        SKPoint? activeDotPt = null;
        for (var i = 0; i < _projectedWords.Count; i++)
        {
            var item = _projectedWords[i];
            var activeScale = item.IsActive ? _wordSlamScale.Value : 1.0f;
            var finalScale = Math.Clamp(item.DepthScale * activeScale, 0.55f, 1.48f);
            var opacity = item.IsActive ? 1.0f : (item.IsPast ? 0.96f : 0.72f);
            var liftY = item.IsActive && !packet.ReducedMotion ? _wordLiftY.Value : 0f;
            var tiltDeg = item.IsActive && !packet.ReducedMotion
                ? Math.Clamp((_wordSlamScale.Value - 1.12f) * 16f, -3.2f, 3.2f) * (item.WordIndex % 2 == 0 ? 1f : -1f)
                : 0f;

            // Prism Vocal Spotlight: Soft volumetric radial aurora bloom (replaces boxy text-input border pill!)
            if (item.IsActive && !packet.IsCompactCard)
            {
                var aurSave = context.Canvas.Save();
                try
                {
                    context.Canvas.Translate(item.ScreenPos.X, item.ScreenPos.Y + liftY);
                    context.Canvas.RotateDegrees(tiltDeg);
                    context.Canvas.Scale(finalScale, finalScale);

                    var aurRx = Math.Max(28f, item.MeasuredWidth * 0.72f);
                    var aurRy = font.Size * 0.78f;
                    _grad3Colors[0] = theme.Primary.ToSkColorWithAlpha(0.28f + (_smoothedBeat * 0.16f));
                    _grad3Colors[1] = theme.Secondary.ToSkColorWithAlpha(0.10f);
                    _grad3Colors[2] = SKColors.Transparent;
                    var aurMat = SKMatrix.CreateScale(1f, aurRy / aurRx);
                    using var aurGrad = SKShader.CreateRadialGradient(SKPoint.Empty, aurRx, _grad3Colors, _stops3, SKShaderTileMode.Clamp, aurMat);
                    _fillPaint.Shader = aurGrad;
                    _fillPaint.Color = SKColors.White;
                    context.Canvas.DrawOval(0f, 0f, aurRx, aurRy, _fillPaint);
                    _fillPaint.Shader = null;
                }
                finally
                {
                    context.Canvas.RestoreToCount(aurSave);
                }
            }

            // Chromatic Optical RGB Prism Split on strong beat hits behind extruded word stack
            if (item.IsActive && !packet.ReducedMotion && _smoothedBeat > 0.18f)
            {
                var rgbShift = Math.Clamp(_smoothedBeat * (packet.IsCompactCard ? 2.0f : 4.2f), 1.2f, 4.4f);
                DrawTranslatedWordPass(in context, font, item.Word.Text, item.ScreenPos.X - rgbShift, item.ScreenPos.Y + liftY, tiltDeg, finalScale, baselineOffset, new RgbColor(0x22, 0xD3, 0xEE), 0.32f);
                DrawTranslatedWordPass(in context, font, item.Word.Text, item.ScreenPos.X + rgbShift, item.ScreenPos.Y + liftY, tiltDeg, finalScale, baselineOffset, new RgbColor(0xFF, 0x38, 0x78), 0.32f);
            }

            // 5-Layer Sculpted 3D Isometric Depth Extrusion + Dynamic Parallax Cast Shadow behind active word
            if (item.IsActive && !packet.ReducedMotion)
            {
                var parallaxSign = 1.0f + Math.Clamp(-_cameraYawDeg.Value * 0.065f, -0.45f, 0.45f);
                var baseStepX = Math.Clamp((1.35f + (_smoothedBeat * 0.85f)) * parallaxSign, 0.9f, 2.6f);
                var baseStepY = Math.Clamp(1.15f + (_smoothedBeat * 0.72f), 0.9f, 2.2f);

                DrawTranslatedWordPass(
                    in context,
                    font,
                    item.Word.Text,
                    item.ScreenPos.X + (baseStepX * 6.8f),
                    item.ScreenPos.Y + liftY + (baseStepY * 6.4f),
                    tiltDeg,
                    finalScale * 0.93f,
                    baselineOffset,
                    new RgbColor(6, 7, 14),
                    0.34f);

                for (var layer = 5; layer >= 1; layer--)
                {
                    var layerColor = layer switch
                    {
                        5 => theme.DeepAmbient,
                        4 => StyleLayoutHelper.BlendSrgb(theme.DeepAmbient, theme.Secondary, 0.48f),
                        3 => theme.Secondary,
                        2 => StyleLayoutHelper.BlendSrgb(theme.Secondary, theme.Primary, 0.55f),
                        _ => theme.Primary
                    };
                    var layerAlpha = layer switch
                    {
                        5 => 0.22f,
                        4 => 0.30f,
                        3 => 0.39f,
                        2 => 0.48f,
                        _ => 0.60f
                    };
                    var layerScale = finalScale * (1.0f - (layer * 0.011f));
                    DrawTranslatedWordPass(
                        in context,
                        font,
                        item.Word.Text,
                        item.ScreenPos.X + (layer * baseStepX),
                        item.ScreenPos.Y + liftY + (layer * baseStepY),
                        tiltDeg,
                        layerScale,
                        baselineOffset,
                        layerColor,
                        layerAlpha);
                }
            }

            var fillColor = item.IsActive
                ? StyleLayoutHelper.BlendSrgb(theme.Primary, new RgbColor(255, 255, 255), 0.78f * highlightEased)
                : item.IsPast
                    ? new RgbColor(255, 255, 255)
                    : StyleLayoutHelper.BlendSrgb(theme.Tertiary, new RgbColor(245, 248, 255), 0.72f);

            var wordSave = context.Canvas.Save();
            try
            {
                context.Canvas.Translate(item.ScreenPos.X, item.ScreenPos.Y + liftY);
                context.Canvas.RotateDegrees(tiltDeg);
                context.Canvas.Scale(finalScale, finalScale);

                var wordRect = new SKRect(-item.MeasuredWidth * 0.5f, -font.Size * 0.54f, item.MeasuredWidth * 0.5f, font.Size * 0.48f);
                if (item.IsActive)
                {
                    _aesthetics.DrawWordColorSweep(context.Canvas, wordRect, item.WordProgress, packet, opacity);
                }

                context.Legibility.DrawLegibleText(
                    context.Canvas,
                    item.Word.Text,
                    0f,
                    baselineOffset,
                    SKTextAlign.Center,
                    font,
                    packet.ActiveFontId,
                    fillColor,
                    opacity: opacity,
                    activeGlowColor: item.IsActive ? theme.Primary : null,
                    fonts: context.Fonts);

                if (item.IsActive && !packet.ReducedMotion)
                {
                    // Crisp upper-left 3D specular edge gleam
                    context.Legibility.DrawLegibleText(
                        context.Canvas,
                        item.Word.Text,
                        -0.65f,
                        baselineOffset - 0.75f,
                        SKTextAlign.Center,
                        font,
                        packet.ActiveFontId,
                        new RgbColor(255, 255, 255),
                        opacity: 0.34f,
                        includeStrokeHalo: false,
                        fonts: context.Fonts);

                    if (!packet.IsCompactCard)
                    {
                        // 6 deterministic Rising Vocal Embers ascending around the active singing word
                        for (var k = 0; k < 6; k++)
                        {
                            var life = ((tSec * (0.55f + ((k % 3) * 0.18f))) + (k * 0.37f)) % 1.0f;
                            var eAlpha = MathF.Sin(life * MathF.PI) * (0.45f + (_smoothedBeat * 0.35f));
                            var ex = (((k / 5f) - 0.5f) * item.MeasuredWidth * 1.15f) + (MathF.Sin((tSec * 2.1f) + k) * 4f);
                            var ey = (font.Size * 0.20f) - (life * font.Size * 1.15f);
                            var eRadius = 1.1f + ((k % 2) * 0.7f);
                            var eCol = k % 2 == 0 ? SKColors.White : theme.Primary.ToSkColor();
                            _fillPaint.Shader = null;
                            _fillPaint.Color = eCol.WithAlpha((byte)Math.Clamp(MathF.Round(eAlpha * 255f), 0f, 255f));
                            context.Canvas.DrawCircle(ex, ey, eRadius, _fillPaint);
                        }

                        // Tapered Laser Glint + 4-Point Star Singing Cursor (positioned cleanly below Devanagari matras)
                        var barW = Math.Max(10f, item.MeasuredWidth * 0.96f);
                        var barL = -barW * 0.5f;
                        var barY = font.Size * 0.58f;
                        _fillPaint.Shader = null;
                        _fillPaint.Color = theme.Primary.ToSkColorWithAlpha(0.18f);
                        context.Canvas.DrawRoundRect(new SKRect(barL, barY, barL + barW, barY + 2.0f), 1.0f, 1.0f, _fillPaint);

                        var fillW = Math.Max(4f, barW * (float)Math.Clamp(item.WordProgress, 0.10, 1.0));
                        var cursorTipX = barL + fillW;
                        _grad3Colors[0] = theme.Secondary.ToSkColorWithAlpha(0.65f);
                        _grad3Colors[1] = theme.Primary.ToSkColorWithAlpha(0.95f);
                        _grad3Colors[2] = SKColors.White;
                        using (var barGrad = SKShader.CreateLinearGradient(
                                   new SKPoint(barL, barY),
                                   new SKPoint(cursorTipX, barY),
                                   _grad3Colors,
                                   _stops3,
                                   SKShaderTileMode.Clamp))
                        {
                            _fillPaint.Shader = barGrad;
                            _fillPaint.Color = SKColors.White;
                            context.Canvas.DrawRoundRect(new SKRect(barL, barY, cursorTipX, barY + 2.2f), 1.1f, 1.1f, _fillPaint);
                            _fillPaint.Shader = null;
                        }

                        DrawFourPointStar(context.Canvas, cursorTipX, barY + 1.1f, 5.2f + (_smoothedBeat * 2.2f), 1.5f, SKColors.White, withGlow: true);
                    }
                }

                // Visual Orbs & Persistent Jewel Mini-Charms for matched English & Hindi/Hinglish words
                if (StyleLayoutHelper.ResolveWordVisual(item.Word, packet, allowTrackMoodFallback: false) is { } visual)
                {
                    if (item.IsActive)
                    {
                        var bobOffset = packet.ReducedMotion
                            ? 0f
                            : (-MathF.Sin((tSec * 4.4f) + item.WordIndex) * 2.4f) - (_smoothedBeat * 4.2f);
                        var orbY = -(font.Size * 0.94f) + bobOffset;
                        var orbR = Math.Clamp(font.Size * 0.42f, 12f, 22f);

                        if (!packet.IsCompactCard)
                        {
                            // Outer pulsing prism halo ring
                            _strokePaint.Shader = null;
                            _strokePaint.StrokeWidth = 1.0f;
                            _strokePaint.Color = theme.Primary.ToSkColorWithAlpha(0.32f + (_smoothedBeat * 0.25f));
                            context.Canvas.DrawCircle(0f, orbY, orbR + 3.0f + (_smoothedBeat * 2.0f), _strokePaint);

                            _glowPaint.Color = SKColors.Black.WithAlpha(112);
                            _glowPaint.ImageFilter = _orbBadgeShadow;
                            try
                            {
                                context.Canvas.DrawCircle(1.5f, orbY + 3.5f, orbR, _glowPaint);
                            }
                            finally
                            {
                                _glowPaint.ImageFilter = null;
                            }

                            _grad2Colors[0] = StyleLayoutHelper.BlendSrgb(theme.DeepAmbient, theme.Primary, 0.34f).ToSkColorWithAlpha(0.76f);
                            _grad2Colors[1] = theme.DeepAmbient.ToSkColorWithAlpha(0.84f);
                            using (var orbBg = SKShader.CreateLinearGradient(new SKPoint(0f, orbY - orbR), new SKPoint(0f, orbY + orbR), _grad2Colors, _stops2, SKShaderTileMode.Clamp))
                            {
                                _fillPaint.Shader = orbBg;
                                _fillPaint.Color = SKColors.White;
                                context.Canvas.DrawCircle(0f, orbY, orbR, _fillPaint);
                                _fillPaint.Shader = null;
                            }

                            _grad2Colors[0] = SKColors.White.WithAlpha((byte)Math.Clamp((int)(140 + (_smoothedBeat * 70)), 110, 225));
                            _grad2Colors[1] = theme.Primary.ToSkColorWithAlpha(0.45f);
                            using (var orbRim = SKShader.CreateLinearGradient(new SKPoint(-orbR, orbY - orbR), new SKPoint(orbR, orbY + orbR), _grad2Colors, _stops2, SKShaderTileMode.Clamp))
                            {
                                _strokePaint.Shader = orbRim;
                                _strokePaint.Color = SKColors.White;
                                _strokePaint.StrokeWidth = 1.25f;
                                context.Canvas.DrawCircle(0f, orbY, orbR, _strokePaint);
                                _strokePaint.Shader = null;
                            }
                        }

                        _aesthetics.DrawReactiveVisual(context.Canvas, in context, visual, 0f, orbY, Math.Clamp(font.Size * 0.50f, 14f, 26f), 0.98f, isActive: true);
                    }
                    else if (item.IsPast && !packet.IsCompactCard)
                    {
                        // Persistent Jewel Mini-Charm so previously sung matched words stay visibly crowned!
                        var charmY = -(font.Size * 0.80f);
                        var charmR = Math.Clamp(font.Size * 0.28f, 8.5f, 14.5f);
                        _fillPaint.Shader = null;
                        _fillPaint.Color = theme.DeepAmbient.ToSkColorWithAlpha(0.68f);
                        context.Canvas.DrawCircle(0f, charmY, charmR, _fillPaint);

                        _strokePaint.Shader = null;
                        _strokePaint.StrokeWidth = 0.9f;
                        _strokePaint.Color = SKColors.White.WithAlpha(118);
                        context.Canvas.DrawCircle(0f, charmY, charmR, _strokePaint);

                        _aesthetics.DrawReactiveVisual(context.Canvas, in context, visual, 0f, charmY, Math.Clamp(font.Size * 0.34f, 10f, 17f), 0.82f, isActive: false);
                    }
                }
            }
            finally
            {
                context.Canvas.RestoreToCount(wordSave);
            }

            if (item.IsActive)
            {
                activeDotPt = new SKPoint(item.ScreenPos.X, item.ScreenPos.Y + liftY + (fontSize * 0.76f));
            }
        }

        if (activeDotPt is { } dot)
        {
            _aesthetics.DrawFollowingDot(context.Canvas, packet, dot.X, dot.Y, Math.Clamp(fontSize * 0.11f, 2.8f, 5.8f));
        }
    }

    private void DrawContextualHistoryAndUpcomingLines(
        in StyleRenderContext context,
        in AlbumCycleTheme theme,
        ActiveLyricSlice slice,
        float leftX,
        float colWidth,
        float centerY,
        float activeTopY,
        float activeBottomY)
    {
        var packet = context.Packet;
        var doc = packet.SceneSnapshot.Document;
        var lines = doc is not null && !doc.Lines.IsEmpty ? doc.Lines : default;
        var activeIdx = slice.ActiveLineIndex;

        var contextSize = ResolveGlassStageFontSize(packet) * 0.86f;
        var contextFont = context.Fonts.GetOrCreateFont(packet.ActiveFontId, contextSize, enforceMinOverlayFloor: false);
        var lineH = contextSize * 1.18f;
        var lineGap = contextSize * 0.42f;
        var pitchDeg = packet.ReducedMotion ? 0f : RestingPitchDegrees;
        var yawDeg = packet.ReducedMotion ? 0f : _cameraYawDeg.Value;
        var focal = PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f);
        var pivotX = leftX + (colWidth * 0.25f);
        var tintedWhite = StyleLayoutHelper.BlendSrgb(theme.Tertiary, new RgbColor(248, 250, 252), 0.78f);

        // 1. Walk UPWARD drawing up to 2 past lines receding into curved 3D cylindrical drum arc
        var cursorUpY = activeTopY - lineGap;
        for (var delta = 1; delta <= 2; delta++)
        {
            var lineObj = GetLineByOffset(lines, activeIdx, -delta, delta == 1 ? slice.PreviousLine : null);
            if (lineObj is null || string.IsNullOrWhiteSpace(lineObj.RawText))
            {
                continue;
            }

            WrapTextIntoBuffer(lineObj.RawText, contextFont, context.Fonts, colWidth, maxRows: 2);
            var rowCount = _wrapLineBuffer.Count;
            if (rowCount == 0)
            {
                continue;
            }

            var blockTop = cursorUpY - (rowCount * lineH);
            if (blockTop < 16f)
            {
                break;
            }

            var worldZ = 38f * delta;
            var arcIndent = Math.Min(colWidth * 0.22f, (delta * delta) * 7.5f);
            var availW = Math.Max(80f, colWidth - arcIndent);
            var opacity = delta == 1 ? 0.52f : 0.26f;
            var dofPaint = delta >= 2 && !packet.ReducedMotion ? _dofNearPaint : null;
            for (var r = 0; r < rowCount; r++)
            {
                var rowY = blockTop + (r * lineH) + (lineH * 0.5f);
                var (pt, scale) = packet.ReducedMotion
                    ? (new SKPoint(leftX + arcIndent, rowY), 1.0f)
                    : KineticWallStyleRenderer.Project3DPoint(leftX + arcIndent + (worldZ * 0.04f), rowY, worldZ, pitchDeg, yawDeg, 0f, focal, pivotX, centerY);
                DrawLeftRow3D(in context, contextFont, _wrapLineBuffer[r], pt, scale, availW, tintedWhite, opacity, dofPaint);
            }

            cursorUpY = blockTop - lineGap;
        }

        // 2. Walk DOWNWARD drawing up to 3 upcoming lines curving backward along 3D drum arc with optical DOF blur
        var cursorDownY = activeBottomY + lineGap;
        var maxBottom = packet.ViewportHeightDip - 16f;
        for (var delta = 1; delta <= 3; delta++)
        {
            var lineObj = GetLineByOffset(lines, activeIdx, delta, delta == 1 ? slice.NextLine : null);
            if (lineObj is null || string.IsNullOrWhiteSpace(lineObj.RawText))
            {
                continue;
            }

            WrapTextIntoBuffer(lineObj.RawText, contextFont, context.Fonts, colWidth, maxRows: 2);
            var rowCount = _wrapLineBuffer.Count;
            if (rowCount == 0)
            {
                continue;
            }

            if (cursorDownY + lineH > maxBottom)
            {
                break;
            }

            var worldZ = 38f * delta;
            var arcIndent = Math.Min(colWidth * 0.24f, (delta * delta) * 7.5f);
            var availW = Math.Max(80f, colWidth - arcIndent);
            var opacity = delta == 1 ? 0.60f : (delta == 2 ? 0.34f : 0.17f);
            var dofPaint = packet.ReducedMotion ? null : (delta == 2 ? _dofNearPaint : (delta >= 3 ? _dofFarPaint : null));
            for (var r = 0; r < rowCount; r++)
            {
                var rowY = cursorDownY + (r * lineH) + (lineH * 0.5f);
                if (rowY > maxBottom)
                {
                    break;
                }

                var (pt, scale) = packet.ReducedMotion
                    ? (new SKPoint(leftX + arcIndent, rowY), 1.0f)
                    : KineticWallStyleRenderer.Project3DPoint(leftX + arcIndent + (worldZ * 0.04f), rowY, worldZ, pitchDeg, yawDeg, 0f, focal, pivotX, centerY);
                DrawLeftRow3D(in context, contextFont, _wrapLineBuffer[r], pt, scale, availW, tintedWhite, opacity, dofPaint);
            }

            cursorDownY += (rowCount * lineH) + lineGap;
        }
    }

    private void DrawFourPointStar(
        SKCanvas canvas,
        float cx,
        float cy,
        float outerR,
        float innerR,
        SKColor color,
        bool withGlow = false)
    {
        if (outerR <= 0.2f || innerR <= 0.1f)
        {
            return;
        }

        _starPath.Rewind();
        _starPath.MoveTo(cx, cy - outerR);
        _starPath.LineTo(cx + innerR, cy - innerR);
        _starPath.LineTo(cx + outerR, cy);
        _starPath.LineTo(cx + innerR, cy + innerR);
        _starPath.LineTo(cx, cy + outerR);
        _starPath.LineTo(cx - innerR, cy + innerR);
        _starPath.LineTo(cx - outerR, cy);
        _starPath.LineTo(cx - innerR, cy - innerR);
        _starPath.Close();

        if (withGlow)
        {
            _glowPaint.Color = color.WithAlpha(180);
            _glowPaint.ImageFilter = _moteBlur;
            try
            {
                canvas.DrawPath(_starPath, _glowPaint);
            }
            finally
            {
                _glowPaint.ImageFilter = null;
            }
        }

        _fillPaint.Shader = null;
        _fillPaint.Color = color;
        canvas.DrawPath(_starPath, _fillPaint);
    }

    private static void DrawTranslatedWordPass(
        in StyleRenderContext context,
        SKFont font,
        string text,
        float x,
        float y,
        float tiltDeg,
        float scale,
        float baselineOffset,
        RgbColor color,
        float alpha)
    {
        var save = context.Canvas.Save();
        try
        {
            context.Canvas.Translate(x, y);
            context.Canvas.RotateDegrees(tiltDeg);
            context.Canvas.Scale(scale, scale);
            context.Legibility.DrawLegibleText(
                context.Canvas,
                text,
                0f,
                baselineOffset,
                SKTextAlign.Center,
                font,
                context.Packet.ActiveFontId,
                color,
                alpha,
                includeStrokeHalo: false,
                fonts: context.Fonts);
        }
        finally
        {
            context.Canvas.RestoreToCount(save);
        }
    }

    private static LyricLine? GetLineByOffset(
        System.Collections.Immutable.ImmutableArray<LyricLine> lines,
        int activeIndex,
        int delta,
        LyricLine? singleFallback)
    {
        if (!lines.IsDefaultOrEmpty && activeIndex >= 0)
        {
            var idx = activeIndex + delta;
            if (idx >= 0 && idx < lines.Length)
            {
                return lines[idx];
            }

            return null;
        }

        return singleFallback;
    }

    private void WrapTextIntoBuffer(string text, SKFont font, FontAssetRegistry fonts, float maxWidth, int maxRows)
    {
        _wrapLineBuffer.Clear();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;
        for (var i = 0; i < words.Length; i++)
        {
            var candidate = current.Length == 0 ? words[i] : $"{current} {words[i]}";
            if (current.Length > 0 && TextShaperService.MeasureWidth(candidate, font, fonts) > maxWidth)
            {
                _wrapLineBuffer.Add(current);
                if (_wrapLineBuffer.Count >= maxRows - 1)
                {
                    _wrapLineBuffer.Add(string.Join(' ', words, i, words.Length - i));
                    return;
                }

                current = words[i];
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
        {
            _wrapLineBuffer.Add(current);
        }
    }

    private static void DrawLeftRow3D(
        in StyleRenderContext context,
        SKFont font,
        string text,
        SKPoint pos,
        float scale,
        float maxWidth,
        RgbColor color,
        float opacity,
        SKPaint? dofLayerPaint = null)
    {
        var measured = TextShaperService.MeasureWidth(text, font, context.Fonts);
        var fit = TextShaperService.ComputeAutoFitScale(measured * scale, maxWidth, 0.96f);
        var finalScale = scale * fit;

        font.GetFontMetrics(out var metrics);
        var baselineOffset = -((metrics.Ascent + metrics.Descent) * 0.5f);

        var layerSave = dofLayerPaint is not null ? context.Canvas.SaveLayer(dofLayerPaint) : -1;
        var save = context.Canvas.Save();
        try
        {
            context.Canvas.Translate(pos.X, pos.Y);
            context.Canvas.Scale(finalScale, finalScale);
            context.Legibility.DrawLegibleText(context.Canvas, text, 0f, baselineOffset, SKTextAlign.Left, font, context.Packet.ActiveFontId, color, opacity, includeStrokeHalo: false, fonts: context.Fonts);
        }
        finally
        {
            context.Canvas.RestoreToCount(save);
            if (layerSave >= 0)
            {
                context.Canvas.RestoreToCount(layerSave);
            }
        }
    }

    private static void DrawLeftFallbackPlainLine(
        in StyleRenderContext context,
        LyricLine line,
        float leftX,
        float colWidth,
        float originY)
    {
        var fontSize = ResolveGlassStageFontSize(context.Packet);
        var font = context.Fonts.GetOrCreateFont(context.Packet.ActiveFontId, fontSize, enforceMinOverlayFloor: !context.Packet.IsCompactCard);
        DrawFittedLeftText(in context, font, line.RawText, leftX, originY, colWidth, new RgbColor(255, 255, 255), 0.96f);
    }

    private static void DrawFittedLeftText(
        in StyleRenderContext context,
        SKFont font,
        string text,
        float leftX,
        float baselineCenterY,
        float maxWidth,
        RgbColor color,
        float opacity)
    {
        var measured = TextShaperService.MeasureWidth(text, font, context.Fonts);
        var fit = TextShaperService.ComputeAutoFitScale(measured, maxWidth, 0.96f);
        font.GetFontMetrics(out var metrics);
        var baselineOffset = -((metrics.Ascent + metrics.Descent) * 0.5f);

        var save = context.Canvas.Save();
        try
        {
            context.Canvas.Translate(leftX, baselineCenterY);
            context.Canvas.Scale(fit, fit);
            context.Legibility.DrawLegibleText(context.Canvas, text, 0f, baselineOffset, SKTextAlign.Left, font, context.Packet.ActiveFontId, color, opacity, fonts: context.Fonts);
        }
        finally
        {
            context.Canvas.RestoreToCount(save);
        }
    }

    private static void DrawFittedCenterText(
        in StyleRenderContext context,
        SKFont font,
        string text,
        float centerX,
        float baselineCenterY,
        float maxWidth,
        RgbColor color,
        float opacity)
    {
        var measured = TextShaperService.MeasureWidth(text, font, context.Fonts);
        var fit = TextShaperService.ComputeAutoFitScale(measured, maxWidth, 0.96f);
        font.GetFontMetrics(out var metrics);
        var baselineOffset = -((metrics.Ascent + metrics.Descent) * 0.5f);

        var save = context.Canvas.Save();
        try
        {
            context.Canvas.Translate(centerX, baselineCenterY);
            context.Canvas.Scale(fit, fit);
            context.Legibility.DrawLegibleText(context.Canvas, text, 0f, baselineOffset, SKTextAlign.Center, font, LyricFontId.Jost, color, opacity, includeStrokeHalo: false, fonts: context.Fonts);
        }
        finally
        {
            context.Canvas.RestoreToCount(save);
        }
    }

    private static (float LeftX, float ColWidth, float CenterY) ResolveLyricsColumnGeometry(FrameRenderPacket packet)
    {
        var w = packet.ViewportWidthDip;
        var h = packet.ViewportHeightDip;

        if (packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.50f);
            var colW = Math.Clamp(w * 0.44f, 180f, 640f);
            var leftX = Math.Clamp(anchorX - (colW * 0.25f), w * 0.36f, w - colW - 24f);
            return (leftX, colW, anchorY);
        }

        var stageLeftX = w * (packet.IsCompactCard ? 0.44f : 0.475f);
        var stageColW = Math.Max(100f, w - stageLeftX - (w * 0.055f));
        return (stageLeftX, stageColW, h * 0.48f);
    }

    private static float ResolveGlassStageFontSize(FrameRenderPacket packet)
    {
        var baseScaled = StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: packet.IsCompactCard ? 0.56f : 0.54f);
        if (packet.IsCompactCard || packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
        {
            return baseScaled;
        }

        var viewportCap = Math.Clamp(packet.ViewportHeightDip * 0.078f, 20f, 44f);
        return Math.Min(baseScaled, viewportCap);
    }

    private static string FormatClock(TimeSpan ts)
    {
        var total = Math.Max(0, (int)Math.Floor(ts.TotalSeconds));
        var mins = total / 60;
        var secs = total % 60;
        return string.Create(CultureInfo.InvariantCulture, $"{mins}:{secs:D2}");
    }

    private static BokehMote[] BuildSpatialMotes()
    {
        var motes = new BokehMote[18];
        for (var i = 0; i < motes.Length; i++)
        {
            var u = ((i * 37 + 13) % 97) / 97f;
            var v = ((i * 53 + 29) % 89) / 89f;
            var z = -50f + ((i * 19) % 290);
            var radius = 2.4f + ((i * 7) % 5) * 1.15f;
            var speed = 0.014f + ((i % 4) * 0.008f);
            var phase = i * 1.13f;
            var tintMix = (i % 5) / 4f;
            motes[i] = new BokehMote(u, v, z, radius, speed, phase, tintMix);
        }

        return motes;
    }

    private readonly record struct BokehMote(
        float BaseU,
        float BaseV,
        float DepthZ,
        float Radius,
        float DriftSpeed,
        float Phase,
        float TintMix);

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
