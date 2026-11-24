using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #5 — Visual Stack: expressive centered vertical stack pairing semantically matched
/// lyric words with glowing Fluent Icon / Emoji badges driven by <c>Spring.IconPop</c>
/// (<c>k=480, c=22, m=0.9</c>) and vertical <c>DirectionalMotionBlur13.sksl</c> (shutter 0.42).
/// </summary>
public sealed class VisualStackStyleRenderer : IKineticStyleRenderer
{
    public const float VerticalShutterFactor = 0.42f;
    public const float RowPitchRatio = 1.44f;
    public const float RippleDurationSeconds = 0.28f;

    private readonly SKPaint _badgeBgPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _badgeRingPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f };
    private readonly SKPaint _ripplePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f };
    private readonly SKPaint _glyphPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly List<VisualStackRow> _rowScratch = new(16);

    private SpringState _stackOffsetY = SpringState.AtRest(0f);
    private SpringState _activeRowScale = SpringState.AtRest(1.06f);
    private SpringState _iconPopScale = SpringState.AtRest(1.0f);
    private float _rippleElapsedSec = RippleDurationSeconds;
    private float _fadeIn = 1.0f;

    private int _lastActiveRowKey = -1;
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
        _lastActiveRowKey = -1;
    }

    public bool Advance(in FrameRenderPacket packet, in LifecycleFrameState lifecycle)
    {
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        if (slice.CurrentLine is null)
        {
            return false;
        }

        BuildGroupedRows(slice.CurrentLine, _rowScratch);
        if (_rowScratch.Count == 0)
        {
            return false;
        }

        var activeRowIdx = FindActiveRowIndex(_rowScratch, slice.ActiveWordIndex);
        var activeRow = _rowScratch[activeRowIdx];
        var rowKey = (slice.ActiveLineIndex * 100) + activeRowIdx;

        var dt = Math.Clamp(packet.DeltaSeconds, 0f, SpringState.MaxDeltaSeconds);
        var fontSize = ResolveFontSize(packet);
        var rowPitch = fontSize * RowPitchRatio;

        if (rowKey != _lastActiveRowKey)
        {
            var wasInitialized = _lastActiveRowKey >= 0;
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

                _activeRowScale = new SpringState(Value: 0.84f, Velocity: 0f, Target: 1.06f);
                if (activeRow.Visual is not null)
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
            _fadeIn = Math.Min(1f, _fadeIn + (dt / 0.14f));
            return _fadeIn < 0.999f;
        }

        _stackOffsetY = _stackOffsetY.Step(SpringTokens.LineShift, dt);
        _activeRowScale = _activeRowScale.Step(SpringTokens.WordSnap, dt);
        _iconPopScale = _iconPopScale.Step(SpringTokens.IconPop, dt);
        if (_rippleElapsedSec < RippleDurationSeconds)
        {
            _rippleElapsedSec = Math.Min(RippleDurationSeconds, _rippleElapsedSec + dt);
        }

        return !_stackOffsetY.IsAtRest ||
               !_activeRowScale.IsAtRest ||
               !_iconPopScale.IsAtRest ||
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

        BuildGroupedRows(slice.CurrentLine, _rowScratch);
        if (_rowScratch.Count == 0)
        {
            return;
        }

        var activeRowIdx = FindActiveRowIndex(_rowScratch, slice.ActiveWordIndex);
        var fontId = packet.ActiveFontId;
        var fontSize = ResolveFontSize(packet);
        var font = context.Fonts.GetOrCreateFont(fontId, fontSize);
        var rowPitch = fontSize * RowPitchRatio;
        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet);

        var dpi = packet.RenderScaling;
        var velocityPxPerSec = new SKPoint(0f, _stackOffsetY.Velocity * dpi);
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
            DrawStackContent(context.Canvas, context, font, fontId, anchorX, anchorY, rowPitch, activeRowIdx);
            return;
        }

        var layerWidthDip = Math.Clamp(packet.ViewportWidthDip * 0.80f, 320f, 1100f);
        var layerHeightDip = Math.Clamp(rowPitch * 4.8f, 240f, packet.ViewportHeightDip);
        var physW = Math.Clamp((int)MathF.Ceiling(layerWidthDip * dpi), 64, OffscreenSurfacePool.MaxSurfaceDimensionPx);
        var physH = Math.Clamp((int)MathF.Ceiling(layerHeightDip * dpi), 64, OffscreenSurfacePool.MaxSurfaceDimensionPx);

        var scratchSurface = context.SurfacePool.RentSurface(OffscreenSurfaceSlot.Scratch, context.GrContext, physW, physH);
        scratchSurface.Canvas.Clear(SKColors.Transparent);

        var save = scratchSurface.Canvas.Save();
        try
        {
            scratchSurface.Canvas.Scale(dpi, dpi);
            DrawStackContent(
                scratchSurface.Canvas,
                context,
                font,
                fontId,
                layerWidthDip * 0.5f,
                layerHeightDip * 0.5f,
                rowPitch,
                activeRowIdx);
        }
        finally
        {
            scratchSurface.Canvas.RestoreToCount(save);
        }

        var destRect = new SKRect(
            anchorX - (layerWidthDip * 0.5f),
            anchorY - (layerHeightDip * 0.5f),
            anchorX + (layerWidthDip * 0.5f),
            anchorY + (layerHeightDip * 0.5f));

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _badgeBgPaint.Dispose();
        _badgeRingPaint.Dispose();
        _ripplePaint.Dispose();
        _glyphPaint.Dispose();
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

    private void DrawStackContent(
        SKCanvas canvas,
        in StyleRenderContext context,
        SKFont font,
        LyricFontId fontId,
        float centerX,
        float centerY,
        float rowPitch,
        int activeRowIdx)
    {
        var packet = context.Packet;
        var scrollY = packet.ReducedMotion ? 0f : _stackOffsetY.Value;

        var scrimRect = new SKRect(
            centerX - (font.Size * 4.5f),
            centerY - (rowPitch * 2.1f),
            centerX + (font.Size * 4.5f),
            centerY + (rowPitch * 1.4f));

        context.Legibility.DrawAmbientScrim(
            canvas,
            scrimRect,
            font.Size,
            packet.Palette.ScrimTint,
            packet.ScrimIntensity,
            packet.HighContrastScrim);

        // Render up to 4 rows: -2, -1 (past), 0 (active hero), +1 (upcoming)
        for (var offset = -2; offset <= 1; offset++)
        {
            var idx = activeRowIdx + offset;
            if (idx < 0 || idx >= _rowScratch.Count)
            {
                continue;
            }

            var row = _rowScratch[idx];
            var isActive = offset == 0;
            var rowScale = offset switch
            {
                0 => packet.ReducedMotion ? 1.00f : _activeRowScale.Value,
                -1 => 0.82f,
                -2 => 0.68f,
                _ => 0.78f
            };

            var rowOpacity = offset switch
            {
                0 => packet.ReducedMotion ? (0.45f + (0.55f * Ease.Highlight(_fadeIn))) : 1.00f,
                -1 => 0.56f,
                -2 => 0.28f,
                _ => 0.42f
            };

            var y = centerY + (offset * rowPitch) + scrollY;
            DrawSingleVisualRow(canvas, context, font, fontId, row, centerX, y, rowScale, rowOpacity, isActive);
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
        bool isActive)
    {
        var packet = context.Packet;
        var combinedText = string.IsNullOrEmpty(row.LeadingConnectors)
            ? row.PrimaryWord
            : $"{row.LeadingConnectors} {row.PrimaryWord}";

        var textWidth = TextShaperService.MeasureWidth(combinedText, font);
        var hasVisual = row.Visual is not null;
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

            if (hasVisual && row.Visual is not null)
            {
                var badgeCenterX = startX + (badgeDiameter * 0.5f);
                var badgePop = isActive && !packet.ReducedMotion ? _iconPopScale.Value : 1.0f;

                DrawIconBadge(
                    canvas,
                    context,
                    row.Visual,
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
                    opacity: opacity * (isActive ? 0.78f : 0.85f));

                textStartX += TextShaperService.MeasureWidth(prefixWithSpace, font);
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
                activeGlowColor: isActive ? packet.Palette.AccentColor2 : null);
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

        // Expanding accent ripple ring on active icon onset (disabled under Reduced Motion)
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

            _badgeBgPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha((isActive ? 0.22f : 0.14f) * opacity);
            canvas.DrawRoundRect(rect, corner, corner, _badgeBgPaint);

            _badgeRingPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha((isActive ? 0.56f : 0.34f) * opacity);
            canvas.DrawRoundRect(rect, corner, corner, _badgeRingPaint);

            var glyphTint = isActive ? packet.Palette.AccentColor1 : packet.Palette.AccentColor2;
            StyleLayoutHelper.DrawVisualGlyph(
                canvas,
                context.Fonts,
                visual,
                centerX: 0f,
                centerY: 0f,
                iconSizeDip: diameter * 0.62f,
                tintColor: glyphTint,
                opacity: opacity,
                scratchPaint: _glyphPaint);
        }
        finally
        {
            canvas.RestoreToCount(badgeSave);
        }
    }

    private static int FindActiveRowIndex(List<VisualStackRow> rows, int activeWordIndex)
    {
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

    private static float ResolveFontSize(FrameRenderPacket packet)
    {
        var metrics = FontAssetRegistry.GetOpticalMetrics(packet.ActiveFontId);
        return Math.Max(packet.BaseFontSizeDip * 0.92f, metrics.MinOverlaySizeDip);
    }

    internal readonly record struct VisualStackRow(
        int StartWordIndex,
        int EndWordIndex,
        string LeadingConnectors,
        string PrimaryWord,
        WordVisualMatch? Visual);
}
