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
    private readonly SKPath _cardClipPath = new();
    private readonly SKImageFilter _backdropLightBlur = SKImageFilter.CreateBlur(11f, 11f);
    private readonly SKImageFilter _cardShadowBlur = SKImageFilter.CreateBlur(22f, 22f);

    private readonly SKColor[] _grad2Colors = new SKColor[2];
    private readonly SKColor[] _grad3Colors = new SKColor[3];
    private readonly float[] _stops2 = [0f, 1f];
    private readonly float[] _stops3 = [0f, 0.5f, 1f];
    private readonly List<string> _wrapLineBuffer = new(6);

    private readonly List<WordRow> _rowBuckets = new(6);
    private readonly List<WordSlot> _rowSlotBuffer = new(24);
    private readonly List<ProjectedWordDraw> _projectedWords = new(24);

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
    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private float _lastWordDurationSec = 0.32f;
    private bool _disposed;

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
        var activeRestScale = packet.IsCompactCard ? 1.06f : 1.12f;

        var key = (slice.ActiveLineIndex, activeIdx);
        if (key != _lastWordKey)
        {
            _lastWordKey = key;
            if (packet.ReducedMotion)
            {
                _wordSlamScale = SpringState.AtRest(1.0f);
                _cameraYawDeg = SpringState.AtRest(0f);
                _highlightFade = 0f;
            }
            else
            {
                _wordSlamScale = new SpringState(activeRestScale + 0.16f + (_smoothedBeat * 0.08f), 0f, activeRestScale);
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
            _aesthetics.ResetDot();
            return _highlightFade < 0.999f;
        }

        _cameraYawDeg = _cameraYawDeg.WithTarget(Math.Clamp(targetYaw, -MaxYawDegrees, MaxYawDegrees)).Step(SpringTokens.CameraDrift, dt);
        _wordSlamScale = _wordSlamScale.WithTarget(activeRestScale).Step(SpringTokens.WordEnter.WithTempoCompression(_lastWordDurationSec), dt);

        var (lyricsLeftX, colW, centerY) = ResolveLyricsColumnGeometry(packet);
        var dotTargetX = lyricsLeftX + (wordFrac * colW * 0.75f);
        var dotTargetY = centerY + (ResolveGlassStageFontSize(packet) * 1.1f);
        var dotActive = packet.ShowFollowingDot && _aesthetics.AdvanceDot(key, dotTargetX, dotTargetY, dt, false, _lastWordDurationSec);

        return !_cameraYawDeg.IsAtRest || !_wordSlamScale.IsAtRest || dotActive || _highlightFade < 0.999f || isPlaying;
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

        // Layer 1: Left-Column Medium Album Cover + Apple Music Scrubber Bar + Title/Artist Meta
        DrawMediumGlassCoverCard(in themedContext, theme, cardRect);
        DrawLeftScrubberAndTrackMeta(in themedContext, theme, cardRect);

        var lyricAlpha = applyLifecycleToLyricsOnly ? Math.Clamp(lifecycle.ContentOpacity, 0f, 1f) : 1.0f;
        if (lyricAlpha <= 0.004f)
        {
            return;
        }

        // Layer 2 & 3: Right-Column Left-Aligned Multi-Line 3D Lyric Stack (back-plane context + popping active 3D words)
        var hasWords = BuildLeftAlignedActiveWordSlots(in themedContext, slice, lyricsLeftX, lyricsColW, lyricsCenterY, out var activeBlockTopY, out var activeBlockBottomY);
        var lyricSave = lyricAlpha < 0.999f ? context.Legibility.BeginGlobalOpacityLayer(canvas, lyricAlpha) : canvas.Save();
        try
        {
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
                DrawProjectedActiveLineWords(in themedContext, theme, lyricsLeftX, lyricsColW, lyricsCenterY);
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
        _cardClipPath.Dispose();
        _backdropLightBlur.Dispose();
        _cardShadowBlur.Dispose();
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

    private void DrawMediumGlassCoverCard(
        in StyleRenderContext context,
        in AlbumCycleTheme theme,
        SKRect cardRect)
    {
        var canvas = context.Canvas;
        var packet = context.Packet;
        var side = MathF.Min(cardRect.Width, cardRect.Height);
        var radius = Math.Clamp(side * 0.068f, 6f, 16f);
        var beatExpand = packet.ReducedMotion ? 0f : (_smoothedBeat * 2.5f);
        var drawRect = new SKRect(
            cardRect.Left - (beatExpand * 0.5f),
            cardRect.Top - (beatExpand * 0.5f),
            cardRect.Right + (beatExpand * 0.5f),
            cardRect.Bottom + (beatExpand * 0.5f));

        // 1. Soft elevation shadow behind cover card
        var shadowRect = new SKRect(
            drawRect.Left - 6f,
            drawRect.Top + 8f,
            drawRect.Right + 6f,
            drawRect.Bottom + 16f);
        _glowPaint.Color = SKColors.Black.WithAlpha(138);
        _glowPaint.ImageFilter = _cardShadowBlur;
        try
        {
            canvas.DrawRoundRect(shadowRect, radius + 3f, radius + 3f, _glowPaint);
        }
        finally
        {
            _glowPaint.ImageFilter = null;
        }

        // 2. Clipped album cover artwork or rich GlassOS procedural card
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
        }
        finally
        {
            canvas.RestoreToCount(clipSave);
        }

        // 3. Subtle Apple Glass hairline border
        _grad2Colors[0] = SKColors.White.WithAlpha(92);
        _grad2Colors[1] = theme.Primary.ToSkColorWithAlpha(0.24f);
        using var rimShader = SKShader.CreateLinearGradient(
            new SKPoint(drawRect.Left, drawRect.Top),
            new SKPoint(drawRect.Right, drawRect.Bottom),
            _grad2Colors,
            _stops2,
            SKShaderTileMode.Clamp);
        _strokePaint.Shader = rimShader;
        _strokePaint.Color = SKColors.White;
        _strokePaint.StrokeWidth = packet.IsCompactCard ? 0.9f : 1.15f;
        try
        {
            canvas.DrawRoundRect(drawRect, radius, radius, _strokePaint);
        }
        finally
        {
            _strokePaint.Shader = null;
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
        _fillPaint.Color = new SKColor(255, 255, 255, 68);
        context.Canvas.DrawRoundRect(new SKRect(barLeft, barY - 1.5f, barRight, barY + 1.5f), 1.5f, 1.5f, _fillPaint);

        _fillPaint.Color = new SKColor(255, 255, 255, 232);
        context.Canvas.DrawRoundRect(new SKRect(barLeft, barY - 1.5f, Math.Max(barLeft + 2f, fillEndX), barY + 1.5f), 1.5f, 1.5f, _fillPaint);

        _fillPaint.Color = SKColors.White;
        context.Canvas.DrawCircle(fillEndX, barY, 3.2f, _fillPaint);

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

        var pitchDeg = packet.ReducedMotion ? 0f : RestingPitchDegrees + (_smoothedBass * 1.8f);
        var yawDeg = packet.ReducedMotion ? 0f : _cameraYawDeg.Value;
        var focal = PerspectiveFocalLength * Math.Max(packet.RenderScaling, 1f);
        var rowHeight = fontSize * 1.28f;
        var startRowY = centerY - ((_rowBuckets.Count - 1) * rowHeight * 0.5f);
        var pivotX = leftX + (maxColWidth * 0.25f);

        blockTopY = startRowY - (fontSize * 0.62f);
        blockBottomY = startRowY + ((_rowBuckets.Count - 1) * rowHeight) + (fontSize * 0.62f);

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

                // Active word bursts forward in 3D Z (-46..-74); upcoming waves oscillate smoothly; past stays crisp near-focal
                float worldZ;
                if (packet.ReducedMotion)
                {
                    worldZ = 0f;
                }
                else if (isActive)
                {
                    worldZ = -44f - (_smoothedBeat * 24f);
                }
                else if (isPast)
                {
                    worldZ = -6f + (MathF.Sin((t * 1.0f) + (wordIdx * 0.8f)) * 8f);
                }
                else
                {
                    worldZ = 14f + (MathF.Sin((t * 1.35f) + (wordIdx * 1.2f) + (line.Index * 0.7f)) * 18f);
                }

                var (projPt, depthScale) = packet.ReducedMotion
                    ? (new SKPoint(wordCenterX, rowY), 1.0f)
                    : KineticWallStyleRenderer.Project3DPoint(wordCenterX, rowY, worldZ, pitchDeg, yawDeg, 0f, focal, pivotX, centerY);

                if (packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay)
                {
                    projPt = new SKPoint(
                        Math.Clamp(projPt.X, 42f, Math.Max(42f, packet.ViewportWidthDip - 42f)),
                        Math.Clamp(projPt.Y, 36f, Math.Max(36f, packet.ViewportHeightDip - 36f)));
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

        _projectedWords.Sort(static (a, b) => b.WorldZ.CompareTo(a.WorldZ));
        return _projectedWords.Count > 0;
    }

    private void DrawProjectedActiveLineWords(
        in StyleRenderContext context,
        in AlbumCycleTheme theme,
        float leftX,
        float colW,
        float centerY)
    {
        var packet = context.Packet;
        var fontSize = ResolveGlassStageFontSize(packet);
        var font = context.Fonts.GetOrCreateFont(packet.ActiveFontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var highlightEased = Ease.Highlight(_highlightFade);
        font.GetFontMetrics(out var metrics);
        var baselineOffset = -((metrics.Ascent + metrics.Descent) * 0.5f);

        SKPoint? activeDotPt = null;
        for (var i = 0; i < _projectedWords.Count; i++)
        {
            var item = _projectedWords[i];
            var activeScale = item.IsActive ? _wordSlamScale.Value : 1.0f;
            var finalScale = Math.Clamp(item.DepthScale * activeScale, 0.55f, 1.38f);
            var opacity = item.IsActive ? 1.0f : (item.IsPast ? 0.94f : 0.78f);

            // Multi-layer 3D extruded chromatic depth behind protruding active word
            if (item.IsActive && !packet.ReducedMotion)
            {
                var echoX = Math.Clamp(3.6f + (_smoothedBeat * 3.8f), 2.5f, 8.0f);
                var backSave = context.Canvas.Save();
                try
                {
                    context.Canvas.Translate(item.ScreenPos.X + echoX, item.ScreenPos.Y + (echoX * 0.70f));
                    context.Canvas.Scale(finalScale * 0.98f, finalScale * 0.98f);
                    context.Legibility.DrawLegibleText(context.Canvas, item.Word.Text, 0f, baselineOffset, SKTextAlign.Center, font, packet.ActiveFontId, theme.Secondary, 0.30f, fonts: context.Fonts);
                }
                finally
                {
                    context.Canvas.RestoreToCount(backSave);
                }
            }

            var fillColor = item.IsActive
                ? StyleLayoutHelper.BlendSrgb(theme.Primary, new RgbColor(255, 255, 255), 0.76f * highlightEased)
                : item.IsPast
                    ? new RgbColor(255, 255, 255)
                    : StyleLayoutHelper.BlendSrgb(theme.Tertiary, new RgbColor(245, 248, 255), 0.65f);

            var wordSave = context.Canvas.Save();
            try
            {
                context.Canvas.Translate(item.ScreenPos.X, item.ScreenPos.Y);
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

                if (item.IsActive &&
                    StyleLayoutHelper.ResolveWordVisual(item.Word, packet, allowTrackMoodFallback: false) is { } visual)
                {
                    _aesthetics.DrawReactiveVisual(context.Canvas, in context, visual, 0f, -font.Size * 0.76f, Math.Clamp(font.Size * 0.44f, 13f, 24f), 0.92f, isActive: true);
                }
            }
            finally
            {
                context.Canvas.RestoreToCount(wordSave);
            }

            if (item.IsActive)
            {
                activeDotPt = new SKPoint(item.ScreenPos.X, item.ScreenPos.Y + (fontSize * 0.70f));
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

        // 1. Walk UPWARD drawing up to 2 past lines receding into 3D space (matches top rows in reference screenshot)
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

            var worldZ = 34f * delta;
            var opacity = delta == 1 ? 0.42f : 0.25f;
            for (var r = 0; r < rowCount; r++)
            {
                var rowY = blockTop + (r * lineH) + (lineH * 0.5f);
                var (pt, scale) = packet.ReducedMotion
                    ? (new SKPoint(leftX, rowY), 1.0f)
                    : KineticWallStyleRenderer.Project3DPoint(leftX, rowY, worldZ, pitchDeg, yawDeg, 0f, focal, pivotX, centerY);
                DrawLeftRow3D(in context, contextFont, _wrapLineBuffer[r], pt, scale, colWidth, tintedWhite, opacity);
            }

            cursorUpY = blockTop - lineGap;
        }

        // 2. Walk DOWNWARD drawing up to 3 upcoming lines fading smoothly deeper in 3D space
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

            var worldZ = 28f * delta;
            var opacity = delta == 1 ? 0.44f : (delta == 2 ? 0.28f : 0.16f);
            for (var r = 0; r < rowCount; r++)
            {
                var rowY = cursorDownY + (r * lineH) + (lineH * 0.5f);
                if (rowY > maxBottom)
                {
                    break;
                }

                var (pt, scale) = packet.ReducedMotion
                    ? (new SKPoint(leftX, rowY), 1.0f)
                    : KineticWallStyleRenderer.Project3DPoint(leftX, rowY, worldZ, pitchDeg, yawDeg, 0f, focal, pivotX, centerY);
                DrawLeftRow3D(in context, contextFont, _wrapLineBuffer[r], pt, scale, colWidth, tintedWhite, opacity);
            }

            cursorDownY += (rowCount * lineH) + lineGap;
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
        float opacity)
    {
        var measured = TextShaperService.MeasureWidth(text, font, context.Fonts);
        var fit = TextShaperService.ComputeAutoFitScale(measured * scale, maxWidth, 0.96f);
        var finalScale = scale * fit;

        font.GetFontMetrics(out var metrics);
        var baselineOffset = -((metrics.Ascent + metrics.Descent) * 0.5f);

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
