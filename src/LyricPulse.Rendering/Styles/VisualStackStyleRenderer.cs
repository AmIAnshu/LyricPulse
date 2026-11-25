using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Motion;
using LyricPulse.Rendering.Shaders;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #5 — Visual Stack: expressive centered vertical stack pairing semantically matched
/// lyric words with high-contrast dark obsidian Fluent Icon / Emoji badges (&gt;= 7:1 over #FFFFFF)
/// driven by tempo-compressed <c>Spring.IconPop</c> and vertical <c>DirectionalMotionBlur13.sksl</c>
/// (shutter 0.42). Layer 0 scrim renders on the main canvas (never clipped during blur).
/// </summary>
public sealed class VisualStackStyleRenderer : IKineticStyleRenderer
{
    public const float VerticalShutterFactor = 0.42f;
    public const float RowPitchRatio = 1.44f;
    public const float RippleDurationSeconds = 0.28f;

    private static readonly SKColor ActiveBadgeObsidian = new(0x0B, 0x0C, 0x16, 0xD9);   // #D90B0C16
    private static readonly SKColor InactiveBadgeObsidian = new(0x0B, 0x0C, 0x16, 0xB8); // #B80B0C16

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _badgeObsidianPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _badgeTintPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _badgeRingPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
    private readonly SKPaint _ripplePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f };
    private readonly List<VisualStackRow> _advanceRows = new(24);
    private readonly List<VisualStackRow> _renderRows = new(24);

    private SpringState _stackOffsetY = SpringState.AtRest(0f);
    private SpringState _activeRowScale = SpringState.AtRest(1.06f);
    private SpringState _iconPopScale = SpringState.AtRest(1.0f);
    private float _rippleElapsedSec = RippleDurationSeconds;
    private float _fadeIn = 1.0f;
    private float _lastWordDurationSec = 0.32f;

    private (int LineIndex, int RowIndex) _lastActiveRowKey = (-1, -1);
    private bool _disposed;

    internal bool AllowCpuShadersForTesting { get; set; }

    public OverlayStyleId StyleId => OverlayStyleId.VisualStack;

    public void ResetState()
    {
        _stackOffsetY = SpringState.AtRest(0f);
        _activeRowScale = SpringState.AtRest(1.06f);
        _iconPopScale = SpringState.AtRest(1.0f);
        _rippleElapsedSec = RippleDurationSeconds;
        _fadeIn = 1.0f;
        _lastWordDurationSec = 0.32f;
        _lastActiveRowKey = (-1, -1);
        _aesthetics.ResetDot();
    }

    public bool Advance(in FrameRenderPacket packet, in LifecycleFrameState lifecycle)
    {
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        if (slice.CurrentLine is null)
        {
            return false;
        }

        var activeRowIdx = BuildWindowedRows(slice, _advanceRows);
        if (_advanceRows.Count == 0 || activeRowIdx < 0)
        {
            return false;
        }

        var activeRow = _advanceRows[activeRowIdx];
        var rowKey = (slice.ActiveLineIndex, activeRow.StartWordIndex);

        var dt = Math.Clamp(packet.DeltaSeconds, 0f, SpringState.MaxDeltaSeconds);
        var fontSize = ResolveFontSize(packet);
        var rowPitch = fontSize * RowPitchRatio;

        if (!slice.CurrentLine.Words.IsEmpty && slice.ActiveWordIndex >= 0 && slice.ActiveWordIndex < slice.CurrentLine.Words.Length)
        {
            var activeWord = slice.CurrentLine.Words[slice.ActiveWordIndex];
            _lastWordDurationSec = Math.Clamp((float)(activeWord.EndTime - activeWord.StartTime).TotalSeconds, 0.08f, 2.5f);
        }

        if (rowKey != _lastActiveRowKey)
        {
            var wasInitialized = _lastActiveRowKey.LineIndex >= 0;
            _lastActiveRowKey = rowKey;

            if (packet.ReducedMotion)
            {
                _stackOffsetY = SpringState.AtRest(0f);
                _activeRowScale = SpringState.AtRest(1.00f);
                _iconPopScale = SpringState.AtRest(1.00f);
                _rippleElapsedSec = RippleDurationSeconds;
                _fadeIn = 0f;
            }
            else
            {
                if (wasInitialized)
                {
                    _stackOffsetY = new SpringState(Value: rowPitch, Velocity: _stackOffsetY.Velocity * 0.2f, Target: 0f);
                }

                var targetScale = 1.06f + ((packet.SafePartyEnergy - 1.0f) * 0.08f);
                _activeRowScale = new SpringState(Value: 0.84f, Velocity: 0f, Target: targetScale);
                if (activeRow.Visual is not null || packet.PlaybackSnapshot?.TrackMoodVisual is not null)
                {
                    _iconPopScale = new SpringState(Value: 0.35f, Velocity: 0f, Target: 1.00f);
                    _rippleElapsedSec = 0f;
                }
                else
                {
                    _iconPopScale = SpringState.AtRest(1.0f);
                }

                _fadeIn = 1f;
            }
        }

        if (packet.ReducedMotion)
        {
            _stackOffsetY = SpringState.AtRest(0f);
            _activeRowScale = SpringState.AtRest(1.00f);
            _iconPopScale = SpringState.AtRest(1.00f);
            _rippleElapsedSec = RippleDurationSeconds;
            _aesthetics.ResetDot();
            _fadeIn = Math.Min(1f, _fadeIn + (dt / 0.14f));
            return _fadeIn < 0.999f;
        }

        var lineSpring = SpringTokens.LineShift.WithTempoCompression(_lastWordDurationSec);
        var wordSpring = SpringTokens.WordSnap.WithTempoCompression(_lastWordDurationSec);
        var iconSpring = SpringTokens.IconPop.WithTempoCompression(_lastWordDurationSec);

        _stackOffsetY = _stackOffsetY.Step(lineSpring, dt);
        _activeRowScale = _activeRowScale.Step(wordSpring, dt);
        _iconPopScale = _iconPopScale.Step(iconSpring, dt);
        if (_rippleElapsedSec < RippleDurationSeconds)
        {
            _rippleElapsedSec = Math.Min(RippleDurationSeconds, _rippleElapsedSec + dt);
        }

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.52f);
        var dotProgress = (float)Math.Clamp(slice.WordProgress, 0.0, 1.0);
        var dotTargetX = anchorX + ((dotProgress - 0.5f) * Math.Min(packet.ViewportWidthDip * 0.28f, fontSize * 3.0f));
        var dotTargetY = anchorY + (fontSize * 0.62f);
        var dotActive = packet.ShowFollowingDot && _aesthetics.AdvanceDot(dotTargetX, dotTargetY, dt, reducedMotion: false, _lastWordDurationSec);

        return !_stackOffsetY.IsAtRest ||
               !_activeRowScale.IsAtRest ||
               !_iconPopScale.IsAtRest ||
               dotActive ||
               _rippleElapsedSec < RippleDurationSeconds;
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

        var activeRowIdx = BuildWindowedRows(slice, _renderRows);
        if (_renderRows.Count == 0 || activeRowIdx < 0)
        {
            return;
        }

        var fontId = packet.ActiveFontId;
        var fontSize = ResolveFontSize(packet);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var rowPitch = fontSize * RowPitchRatio;
        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.52f);

        var dpi = packet.RenderScaling;
        var velocityPxPerSec = new SKPoint(0f, _stackOffsetY.Velocity * dpi);
        var clampedVel = MotionBlurMath.ComputeClampedVelocity(velocityPxPerSec, VerticalShutterFactor, dpi, out var blurDeadZone);
        var (_, blurPadYPx) = blurDeadZone ? (0, 0) : MotionBlurMath.ComputeDirectionalPaddingPx(clampedVel);
        var blurPadYDip = blurPadYPx / Math.Max(dpi, 0.5f);

        var layerWidthDip = Math.Clamp(packet.ViewportWidthDip * (packet.IsCompactCard ? 0.94f : 0.80f), packet.IsCompactCard ? 140f : 320f, packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas ? 1600f : 1100f);
        var layerHeightDip = Math.Clamp((rowPitch * 5.0f) + (blurPadYDip * 2f), packet.IsCompactCard ? 110f : 240f, packet.ViewportHeightDip);

        var destRect = StyleLayoutHelper.ClampLayerRectToViewport(
            anchorX,
            anchorY,
            layerWidthDip,
            layerHeightDip,
            packet.ViewportWidthDip,
            packet.ViewportHeightDip);

        // Layer 0 radial scrim renders directly on main canvas (NEVER clipped or flashing when blur engages!)
        var scrimRect = new SKRect(
            destRect.MidX - (font.Size * 4.5f),
            destRect.MidY - (rowPitch * 2.0f),
            destRect.MidX + (font.Size * 4.5f),
            destRect.MidY + (rowPitch * 1.4f));

        context.Legibility.DrawAmbientScrim(
            context.Canvas,
            scrimRect,
            font.Size,
            packet.Palette.ScrimTint,
            packet.ScrimIntensity,
            packet.HighContrastScrim);

        var shouldBlur = !context.Shaders.ShouldBypassMotionBlur(
            context.IsGpuBackend,
            packet.ReducedMotion,
            velocityPxPerSec,
            VerticalShutterFactor,
            dpi,
            out _,
            AllowCpuShadersForTesting);

        if (!shouldBlur)
        {
            DrawStackGlyphs(context.Canvas, context, font, fontId, destRect.MidX, destRect.MidY, rowPitch, activeRowIdx, slice.WordProgress);
        }
        else
        {
            var physW = Math.Clamp((int)MathF.Ceiling(destRect.Width * dpi), 64, OffscreenSurfacePool.MaxSurfaceDimensionPx);
            var physH = Math.Clamp((int)MathF.Ceiling(destRect.Height * dpi), 64, OffscreenSurfacePool.MaxSurfaceDimensionPx);

            var scratchSurface = context.SurfacePool.RentSurface(OffscreenSurfaceSlot.Scratch, context.GrContext, physW, physH);
            scratchSurface.Canvas.Clear(SKColors.Transparent);

            var save = scratchSurface.Canvas.Save();
            try
            {
                scratchSurface.Canvas.Scale(dpi, dpi);
                DrawStackGlyphs(
                    scratchSurface.Canvas,
                    context,
                    font,
                    fontId,
                    destRect.Width * 0.5f,
                    destRect.Height * 0.5f,
                    rowPitch,
                    activeRowIdx,
                    slice.WordProgress);
            }
            finally
            {
                scratchSurface.Canvas.RestoreToCount(save);
            }

            context.Shaders.ApplyDirectionalMotionBlur(
                context.Canvas,
                scratchSurface,
                destRect,
                physW,
                physH,
                dpi,
                velocityPxPerSec,
                VerticalShutterFactor,
                context.IsGpuBackend,
                packet.ReducedMotion,
                AllowCpuShadersForTesting);
        }

        _aesthetics.DrawFollowingDot(
            context.Canvas,
            packet,
            destRect.MidX + (((float)Math.Clamp(slice.WordProgress, 0.0, 1.0) - 0.5f) * fontSize * 2.4f),
            destRect.MidY + (fontSize * 0.65f),
            Math.Clamp(fontSize * 0.09f, 3.0f, 7.5f));

        StyleLayoutHelper.DrawSecondarySubline(
            in context,
            slice.CurrentLine,
            destRect.MidX,
            destRect.Bottom + Math.Max(14f, fontSize * 0.25f),
            fontSize);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _aesthetics.Dispose();
        _badgeObsidianPaint.Dispose();
        _badgeTintPaint.Dispose();
        _badgeRingPaint.Dispose();
        _ripplePaint.Dispose();
    }

    /// <summary>
    /// Stitches trailing rows from <see cref="ActiveLyricSlice.PreviousLine"/> and the leading row from
    /// <see cref="ActiveLyricSlice.NextLine"/> around the active line so stack context never vanishes at line breaks.
    /// </summary>
    internal static int BuildWindowedRows(ActiveLyricSlice slice, List<VisualStackRow> destination)
    {
        destination.Clear();
        if (slice.CurrentLine is null)
        {
            return -1;
        }

        var temp = new List<VisualStackRow>(8);
        var prefixCount = 0;

        if (slice.PreviousLine is not null)
        {
            BuildGroupedRows(slice.PreviousLine, temp);
            var takeStart = Math.Max(0, temp.Count - 2);
            for (var i = takeStart; i < temp.Count; i++)
            {
                destination.Add(temp[i]);
                prefixCount++;
            }
        }

        BuildGroupedRows(slice.CurrentLine, temp);
        var localActiveIdx = FindActiveRowIndex(temp, slice.ActiveWordIndex);
        foreach (var row in temp)
        {
            destination.Add(row);
        }

        if (slice.NextLine is not null)
        {
            BuildGroupedRows(slice.NextLine, temp);
            if (temp.Count > 0)
            {
                destination.Add(temp[0]);
            }
        }

        return localActiveIdx >= 0 ? prefixCount + localActiveIdx : -1;
    }

    /// <summary>
    /// Groups up to 2 short leading stopwords onto the following semantically matched content word
    /// per MOTION_AND_STYLES.md section 6 (Style 5 VisualStack grouping rule).
    /// </summary>
    internal static void BuildGroupedRows(LyricLine line, List<VisualStackRow> destination)
    {
        destination.Clear();
        var words = line.Words;
        if (words.IsEmpty)
        {
            if (!string.IsNullOrWhiteSpace(line.RawText))
            {
                destination.Add(new VisualStackRow(0, 0, string.Empty, line.RawText, Visual: null));
            }

            return;
        }

        var i = 0;
        while (i < words.Length)
        {
            if (words[i].Visual is null && StyleLayoutHelper.IsConnectorWord(words[i].Text))
            {
                var startIdx = i;
                var prefixText = words[i].Text;
                var consumed = 1;

                while (i + consumed < words.Length &&
                       consumed < 2 &&
                       words[i + consumed].Visual is null &&
                       StyleLayoutHelper.IsConnectorWord(words[i + consumed].Text) &&
                       prefixText.Length + 1 + words[i + consumed].Text.Length <= 10)
                {
                    prefixText = $"{prefixText} {words[i + consumed].Text}";
                    consumed++;
                }

                var nextIdx = i + consumed;
                if (nextIdx < words.Length)
                {
                    var hero = words[nextIdx];
                    destination.Add(new VisualStackRow(startIdx, nextIdx, prefixText, hero.Text, hero.Visual));
                    i = nextIdx + 1;
                    continue;
                }

                destination.Add(new VisualStackRow(startIdx, i + consumed - 1, string.Empty, prefixText, Visual: null));
                i += consumed;
                continue;
            }

            var current = words[i];
            destination.Add(new VisualStackRow(i, i, string.Empty, current.Text, current.Visual));
            i++;
        }
    }

    private void DrawStackGlyphs(
        SKCanvas canvas,
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        float centerX,
        float centerY,
        float rowPitch,
        int activeRowIdx,
        double wordProgress)
    {
        var packet = context.Packet;
        var scrollY = packet.ReducedMotion ? 0f : _stackOffsetY.Value;

        for (var offset = -2; offset <= 1; offset++)
        {
            var idx = activeRowIdx + offset;
            if (idx < 0 || idx >= _renderRows.Count)
            {
                continue;
            }

            var row = _renderRows[idx];
            var isActive = offset == 0;
            var rowScale = offset switch
            {
                0 => packet.ReducedMotion ? 1.00f : _activeRowScale.Value,
                -1 => 0.82f,
                -2 => 0.68f,
                _ => 0.78f
            };

            // Under Reduced Motion keep active row opacity >= 0.90f (never dimmer than previous row 0.56f)
            var rowOpacity = offset switch
            {
                0 => packet.ReducedMotion ? (0.90f + (0.10f * Ease.Highlight(_fadeIn))) : 1.00f,
                -1 => 0.56f,
                -2 => 0.28f,
                _ => 0.42f
            };

            var y = centerY + (offset * rowPitch) + scrollY;
            DrawSingleVisualRow(canvas, context, font, fontId, row, centerX, y, rowScale, rowOpacity, isActive, wordProgress);
        }
    }

    private void DrawSingleVisualRow(
        SKCanvas canvas,
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        VisualStackRow row,
        float centerX,
        float centerY,
        float rowScale,
        float opacity,
        bool isActive,
        double wordProgress)
    {
        var packet = context.Packet;
        var combinedText = string.IsNullOrEmpty(row.LeadingConnectors)
            ? row.PrimaryWord
            : $"{row.LeadingConnectors} {row.PrimaryWord}";

        var effectiveVisual = row.Visual ?? (isActive && !StyleLayoutHelper.IsConnectorWord(row.PrimaryWord)
            ? packet.PlaybackSnapshot?.TrackMoodVisual
            : null);

        var textWidth = TextShaperService.MeasureWidth(combinedText, font, context.Fonts);
        var hasVisual = effectiveVisual is not null;
        var badgeDiameter = font.Size * 1.18f;
        var gap = font.Size * 0.34f;
        var totalWidth = textWidth + (hasVisual ? badgeDiameter + gap : 0f);

        var fitScale = TextShaperService.ComputeAutoFitScale(totalWidth * rowScale, packet.ViewportWidthDip, 0.76f);
        var finalScale = rowScale * fitScale;

        var save = canvas.Save();
        try
        {
            canvas.Translate(centerX, centerY);
            canvas.Scale(finalScale, finalScale);

            var startX = -totalWidth * 0.5f;
            var textStartX = startX;

            if (hasVisual && effectiveVisual is not null)
            {
                var badgeCenterX = startX + (badgeDiameter * 0.5f);
                var badgePop = isActive && !packet.ReducedMotion ? _iconPopScale.Value : 1.0f;

                DrawIconBadge(
                    canvas,
                    context,
                    effectiveVisual,
                    badgeCenterX,
                    0f,
                    badgeDiameter,
                    badgePop,
                    opacity,
                    isActive);

                textStartX = startX + badgeDiameter + gap;
            }

            font.GetFontMetrics(out var metrics);
            var baselineY = -((metrics.Ascent + metrics.Descent) * 0.5f);

            if (!string.IsNullOrEmpty(row.LeadingConnectors))
            {
                var prefixWithSpace = row.LeadingConnectors + " ";
                var prefixColor = isActive ? packet.Palette.AccentColor1 : packet.Palette.AccentColor3;
                context.Legibility.DrawLegibleText(
                    canvas,
                    prefixWithSpace,
                    textStartX,
                    baselineY,
                    SKTextAlign.Left,
                    font,
                    fontId,
                    prefixColor,
                    opacity: opacity * (isActive ? 0.80f : 0.85f),
                    fonts: context.Fonts);

                textStartX += TextShaperService.MeasureWidth(prefixWithSpace, font, context.Fonts);
            }

            if (isActive && packet.ShowColorSweepLayer)
            {
                var primaryW = TextShaperService.MeasureWidth(row.PrimaryWord, font, context.Fonts);
                var sweepRect = new SKRect(textStartX, -font.Size * 0.54f, textStartX + primaryW, font.Size * 0.54f);
                _aesthetics.DrawWordColorSweep(canvas, sweepRect, wordProgress, packet, opacity);
            }

            var primaryColor = isActive ? packet.Palette.AccentColor1 : packet.Palette.AccentColor3;
            context.Legibility.DrawLegibleText(
                canvas,
                row.PrimaryWord,
                textStartX,
                baselineY,
                SKTextAlign.Left,
                font,
                fontId,
                primaryColor,
                opacity: opacity,
                activeGlowColor: isActive ? packet.Palette.AccentColor2 : null,
                fonts: context.Fonts);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    private void DrawIconBadge(
        SKCanvas canvas,
        in StyleRenderContext context,
        WordVisualMatch visual,
        float cx,
        float cy,
        float diameter,
        float popScale,
        float opacity,
        bool isActive)
    {
        var packet = context.Packet;
        var radius = diameter * 0.5f;

        if (isActive && !packet.ReducedMotion && _rippleElapsedSec < RippleDurationSeconds)
        {
            var t = Ease.Enter(_rippleElapsedSec / RippleDurationSeconds);
            var rippleRadius = radius * (0.85f + (0.65f * t));
            var rippleAlpha = 0.45f * (1f - t) * opacity;
            _ripplePaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(rippleAlpha);
            canvas.DrawCircle(cx, cy, rippleRadius, _ripplePaint);
        }

        var badgeSave = canvas.Save();
        try
        {
            canvas.Translate(cx, cy);
            canvas.Scale(popScale, popScale);

            var rect = new SKRect(-radius, -radius, radius, radius);
            var corner = diameter * 0.34f;

            // High-contrast dark obsidian backing guarantees >= 7:1 glyph contrast over pure #FFFFFF webpages
            _badgeObsidianPaint.Color = LegibilityStyleSpec.ScaleAlpha(
                isActive ? ActiveBadgeObsidian : InactiveBadgeObsidian,
                opacity);
            canvas.DrawRoundRect(rect, corner, corner, _badgeObsidianPaint);

            // Subtle album-synced accent tint layered over obsidian backing
            _badgeTintPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha((isActive ? 0.18f : 0.10f) * opacity);
            canvas.DrawRoundRect(rect, corner, corner, _badgeTintPaint);

            _badgeRingPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha((isActive ? 0.72f : 0.44f) * opacity);
            canvas.DrawRoundRect(rect, corner, corner, _badgeRingPaint);

            var glyphTint = isActive ? packet.Palette.AccentColor1 : packet.Palette.AccentColor2;
            StyleLayoutHelper.DrawVisualGlyph(
                canvas,
                context.Fonts,
                context.Legibility,
                visual,
                centerX: 0f,
                centerY: 0f,
                iconSizeDip: diameter * 0.62f,
                tintColor: glyphTint,
                opacity: opacity);
        }
        finally
        {
            canvas.RestoreToCount(badgeSave);
        }
    }

    private static int FindActiveRowIndex(List<VisualStackRow> rows, int activeWordIndex)
    {
        if (rows.Count == 0)
        {
            return -1;
        }

        if (activeWordIndex <= 0)
        {
            return 0;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (activeWordIndex >= rows[i].StartWordIndex && activeWordIndex <= rows[i].EndWordIndex)
            {
                return i;
            }
        }

        return rows.Count - 1;
    }

    private static float ResolveFontSize(FrameRenderPacket packet) =>
        StyleLayoutHelper.ResolveStyleFontSize(packet, styleMultiplier: 0.92f);

    internal readonly record struct VisualStackRow(
        int StartWordIndex,
        int EndWordIndex,
        string LeadingConnectors,
        string PrimaryWord,
        WordVisualMatch? Visual);
}
