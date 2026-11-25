using System.Globalization;
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
/// Style #4 — Codestyle Visual: live-typed monospace lyrics under a blinking <c>#EF4444</c> cursor
/// enhanced with inline IDE inlay-hint visual icon pill badges (<c>Spring.IconPop</c> at 65% word progress)
/// pre-allocated in width so lines never jitter. Zero post-process shaders.
/// </summary>
public sealed class CodestyleVisualStyleRenderer : IKineticStyleRenderer
{
    public const double CursorBlinkPeriodSeconds = 1.06;
    public const double TypingRevealFraction = 0.78;
    public const double IconPopThresholdProgress = 0.65;

    private static readonly SKPoint[] CardCornerRadii = [new(14f, 14f), new(14f, 14f), new(14f, 14f), new(14f, 14f)];

    private readonly AestheticsOverlayRenderer _aesthetics = new();
    private readonly SKPaint _cardBgPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _cardBorderPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.0f };
    private readonly SKPaint _chipFillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _chipBorderPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.0f };
    private readonly SKPaint _cursorPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _cursorGlowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _underlinePaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKRoundRect _clipRoundRect = new();
    private readonly SKImageFilter _cursorBlur = SKImageFilter.CreateBlur(6.0f, 6.0f);

    private SpringState _lineScrollOffsetY = SpringState.AtRest(0f);
    private SpringState _activeChipPop = SpringState.AtRest(1.0f);
    private float _lineFadeIn = 1.0f;
    private double _blinkClockSec;
    private int _lastLineIndex = -1;
    private (int LineIndex, int WordIndex) _lastChipPoppedKey = (-1, -1);
    private bool _disposed;

    public OverlayStyleId StyleId => OverlayStyleId.CodestyleVisual;

    public void ResetState()
    {
        _lineScrollOffsetY = SpringState.AtRest(0f);
        _activeChipPop = SpringState.AtRest(1.0f);
        _lineFadeIn = 1.0f;
        _blinkClockSec = 0.0;
        _lastLineIndex = -1;
        _lastChipPoppedKey = (-1, -1);
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
        var fontSize = ResolveMonoFontSize(packet);
        var linePitch = fontSize * 1.42f;

        if (slice.ActiveLineIndex != _lastLineIndex)
        {
            var wasInitialized = _lastLineIndex >= 0;
            _lastLineIndex = slice.ActiveLineIndex;

            if (packet.ReducedMotion)
            {
                _lineScrollOffsetY = SpringState.AtRest(0f);
                _lineFadeIn = 0f;
            }
            else
            {
                if (wasInitialized)
                {
                    _lineScrollOffsetY = new SpringState(Value: linePitch, Velocity: 0f, Target: 0f);
                }

                _lineFadeIn = 1f;
            }
        }

        var activeWordKey = (slice.ActiveLineIndex, slice.ActiveWordIndex);
        if (!packet.ReducedMotion &&
            slice.ActiveWordIndex >= 0 &&
            slice.ActiveWordIndex < slice.CurrentLine.Words.Length &&
            slice.CurrentLine.Words[slice.ActiveWordIndex].Visual is not null &&
            slice.WordProgress >= IconPopThresholdProgress &&
            _lastChipPoppedKey != activeWordKey)
        {
            _lastChipPoppedKey = activeWordKey;
            _activeChipPop = new SpringState(Value: 0.30f, Velocity: 0f, Target: 1.00f);
        }

        if (!packet.ReducedMotion)
        {
            _blinkClockSec = (_blinkClockSec + dt) % CursorBlinkPeriodSeconds;
            _lineScrollOffsetY = _lineScrollOffsetY.Step(SpringTokens.LineShift, dt);
            _activeChipPop = _activeChipPop.Step(SpringTokens.IconPop, dt);
        }
        else
        {
            _lineScrollOffsetY = SpringState.AtRest(0f);
            _activeChipPop = SpringState.AtRest(1.0f);
            _lineFadeIn = Math.Min(1f, _lineFadeIn + (dt / 0.14f));
        }

        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? (packet.SceneSnapshot.PlaybackState == MediaPlaybackState.Playing);
        return !_lineScrollOffsetY.IsAtRest ||
               !_activeChipPop.IsAtRest ||
               _lineFadeIn < 0.999f ||
               (isPlaying && !packet.ReducedMotion);
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

        var fontSize = ResolveMonoFontSize(packet);
        var monoFont = context.Fonts.GetOrCreateFont(LyricFontId.SystemMono, fontSize, enforceMinOverlayFloor: !packet.IsCompactCard);
        var linePitch = fontSize * 1.42f;
        var maxBlockWidth = Math.Min(packet.ViewportWidthDip * (packet.IsCompactCard ? 0.92f : 0.80f), packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas ? 1360f : 960f);

        var gutterStr = FormatLineNumber(slice.ActiveLineIndex + 1);
        const string prefixStr = "> ";
        var activeMeasure = MeasureLineWithChips(gutterStr + "  " + prefixStr, slice.CurrentLine, monoFont, context.Fonts);
        var prevMeasure = slice.PreviousLine is not null
            ? MeasureLineWithChips("// ", slice.PreviousLine, monoFont, context.Fonts)
            : 0f;
        var showNext = slice.NextLine is not null &&
                       (slice.LineProgress >= 0.68 || (slice.TimeUntilNextLine?.TotalMilliseconds ?? 9999) <= 500);
        var nextText = showNext && slice.NextLine is not null ? $"   {slice.NextLine.RawText}" : string.Empty;
        var nextMeasure = string.IsNullOrEmpty(nextText) ? 0f : TextShaperService.MeasureWidth(nextText, monoFont, context.Fonts);

        var maxContentMeasure = Math.Max(activeMeasure, Math.Max(prevMeasure, nextMeasure));
        var fitScale = TextShaperService.ComputeAutoFitScale(maxContentMeasure + 56f, maxBlockWidth, 0.96f);
        var scaledFontSize = fontSize * fitScale;
        var scaledLinePitch = linePitch * fitScale;

        var padX = packet.IsCompactCard ? 12f : 20f;
        var padY = packet.IsCompactCard ? 10f : 16f;
        var blockWidth = Math.Clamp((maxContentMeasure * fitScale) + (padX * 2f) + 24f, packet.IsCompactCard ? 170f : 320f, maxBlockWidth);
        var blockHeight = (3f * scaledLinePitch) + (padY * 2f);

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.52f);
        var margin = packet.IsCompactCard ? 8f : 16f;
        var left = Math.Clamp(anchorX - (blockWidth * 0.5f), margin, Math.Max(margin, packet.ViewportWidthDip - blockWidth - margin));
        var top = Math.Clamp(anchorY - (blockHeight * 0.5f), margin, Math.Max(margin, packet.ViewportHeightDip - blockHeight - margin));
        var cardRect = new SKRect(left, top, left + blockWidth, top + blockHeight);

        DrawCodeCardBackdrop(context.Canvas, cardRect, packet);

        var clipSave = context.Canvas.Save();
        try
        {
            _clipRoundRect.SetRectRadii(cardRect, CardCornerRadii);
            context.Canvas.ClipRoundRect(_clipRoundRect, antialias: true);

            var scrollY = packet.ReducedMotion ? 0f : _lineScrollOffsetY.Value * fitScale;
            var contentLeft = cardRect.Left + padX;
            var activeRowTopY = cardRect.Top + padY + scaledLinePitch + scrollY;
            var lineAlpha = packet.ReducedMotion ? (0.90f + (0.10f * Ease.Highlight(_lineFadeIn))) : 1.0f;

            // Row 0: Previous commented line with dimmed inline icon chips
            if (slice.PreviousLine is not null)
            {
                var prevBaseline = cardRect.Top + padY + (scaledFontSize * 0.88f) + scrollY;
                DrawCommentedVisualLine(context, monoFont, slice.PreviousLine, contentLeft, prevBaseline, fitScale, 0.46f * lineAlpha);
            }

            // Row 1: Active typed line with inline IDE visual inlay-hint badges
            var activeBaseline = activeRowTopY + (scaledFontSize * 0.88f);
            DrawActiveTypedVisualLine(context, monoFont, slice, contentLeft, activeBaseline, fitScale, lineAlpha, packet.ReducedMotion);

            // Row 2: Faint upcoming autocomplete ghost hint
            if (!string.IsNullOrEmpty(nextText))
            {
                var nextBaseline = activeRowTopY + scaledLinePitch + (scaledFontSize * 0.88f);
                DrawScaledMonoText(context, monoFont, nextText, contentLeft, nextBaseline, fitScale, packet.Palette.AccentColor3, 0.28f * lineAlpha);
            }
        }
        finally
        {
            context.Canvas.RestoreToCount(clipSave);
        }

        StyleLayoutHelper.DrawSecondarySubline(
            in context,
            slice.CurrentLine,
            cardRect.MidX,
            cardRect.Bottom + Math.Max(16f, scaledFontSize * 0.72f),
            scaledFontSize * 1.4f);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _aesthetics.Dispose();
        _clipRoundRect.Dispose();
        _cursorBlur.Dispose();
        _cardBgPaint.Dispose();
        _cardBorderPaint.Dispose();
        _chipFillPaint.Dispose();
        _chipBorderPaint.Dispose();
        _cursorPaint.Dispose();
        _cursorGlowPaint.Dispose();
        _underlinePaint.Dispose();
    }

    private void DrawCommentedVisualLine(
        in StyleRenderContext context,
        SKFont monoFont,
        LyricLine line,
        float startX,
        float baselineY,
        float scale,
        float alpha)
    {
        var packet = context.Packet;
        var currentX = startX;
        DrawScaledMonoText(context, monoFont, "// ", currentX, baselineY, scale, packet.Palette.AccentColor3, alpha);
        currentX += TextShaperService.MeasureWidth("// ", monoFont, context.Fonts) * scale;

        if (line.Words.IsEmpty)
        {
            DrawScaledMonoText(context, monoFont, line.RawText, currentX, baselineY, scale, packet.Palette.AccentColor3, alpha);
            return;
        }

        var spaceW = TextShaperService.MeasureWidth(" ", monoFont, context.Fonts) * scale;
        var scaledFont = monoFont.Size * scale;

        for (var i = 0; i < line.Words.Length; i++)
        {
            var word = line.Words[i];
            if (i > 0 && word.IsSpaceSeparated)
            {
                currentX += spaceW;
            }

            DrawScaledMonoText(context, monoFont, word.Text, currentX, baselineY, scale, packet.Palette.AccentColor3, alpha);
            currentX += TextShaperService.MeasureWidth(word.Text, monoFont, context.Fonts) * scale;

            if (word.Visual is { } vis)
            {
                currentX += scaledFont * 0.22f;
                var chipW = scaledFont * 1.24f;
                DrawInlineIconChip(context, vis, currentX, baselineY, chipW, scaledFont * 1.05f, popScale: 1.0f, alpha * 0.85f, isActive: false);
                currentX += chipW;
            }
        }
    }

    private void DrawActiveTypedVisualLine(
        in StyleRenderContext context,
        SKFont monoFont,
        ActiveLyricSlice slice,
        float startX,
        float baselineY,
        float scale,
        float lineAlpha,
        bool reducedMotion)
    {
        var packet = context.Packet;
        var currentX = startX;
        var scaledFont = monoFont.Size * scale;

        var gutter = FormatLineNumber(slice.ActiveLineIndex + 1) + "  ";
        DrawScaledMonoText(context, monoFont, gutter, currentX, baselineY, scale, packet.Palette.AccentColor3, 0.48f * lineAlpha);
        currentX += TextShaperService.MeasureWidth(gutter, monoFont, context.Fonts) * scale;

        const string prefix = "> ";
        DrawScaledMonoText(context, monoFont, prefix, currentX, baselineY, scale, packet.Palette.AccentColor2, 0.80f * lineAlpha);
        currentX += TextShaperService.MeasureWidth(prefix, monoFont, context.Fonts) * scale;

        var words = slice.CurrentLine!.Words;
        if (words.IsEmpty)
        {
            DrawScaledMonoText(context, monoFont, slice.CurrentLine.RawText, currentX, baselineY, scale, packet.Palette.AccentColor1, lineAlpha);
            currentX += TextShaperService.MeasureWidth(slice.CurrentLine.RawText, monoFont, context.Fonts) * scale;
            DrawCursor(context.Canvas, currentX + 3f, baselineY, scaledFont, isTypingActively: false, reducedMotion, lineAlpha);
            return;
        }

        var activeIdx = slice.ActiveWordIndex >= 0 && slice.ActiveWordIndex < words.Length ? slice.ActiveWordIndex : 0;
        var spaceW = TextShaperService.MeasureWidth(" ", monoFont, context.Fonts) * scale;
        var isTypingActively = false;

        for (var i = 0; i <= activeIdx; i++)
        {
            var word = words[i];
            if (i > 0 && word.IsSpaceSeparated)
            {
                currentX += spaceW;
            }

            if (i < activeIdx)
            {
                var isKeyword = StyleLayoutHelper.IsConnectorWord(word.Text);
                var tokenColor = isKeyword ? packet.Palette.AccentColor2 : packet.Palette.AccentColor1;
                DrawScaledMonoText(context, monoFont, word.Text, currentX, baselineY, scale, tokenColor, (isKeyword ? 0.84f : 0.88f) * lineAlpha);
                currentX += TextShaperService.MeasureWidth(word.Text, monoFont, context.Fonts) * scale;

                if (word.Visual is { } vis)
                {
                    currentX += scaledFont * 0.22f;
                    var chipW = scaledFont * 1.24f;
                    DrawInlineIconChip(context, vis, currentX, baselineY, chipW, scaledFont * 1.05f, popScale: 1.0f, lineAlpha, isActive: false);
                    currentX += chipW;
                }
            }
            else
            {
                string revealedText;
                if (reducedMotion)
                {
                    revealedText = word.Text;
                }
                else
                {
                    var normalizedTyping = Math.Clamp(slice.WordProgress / TypingRevealFraction, 0.0, 1.0);
                    isTypingActively = normalizedTyping < 0.999 && (packet.PlaybackSnapshot?.IsPlaying ?? true);
                    var graphemeCount = StyleLayoutHelper.CountGraphemes(word.Text);
                    var revealedElements = Math.Clamp((int)Math.Ceiling(normalizedTyping * graphemeCount), 1, Math.Max(1, graphemeCount));
                    revealedText = revealedElements >= graphemeCount
                        ? word.Text
                        : StyleLayoutHelper.SliceByGraphemes(word.Text, revealedElements);
                }

                var wordW = TextShaperService.MeasureWidth(revealedText, monoFont, context.Fonts) * scale;
                if (packet.ShowColorSweepLayer)
                {
                    var sweepRect = new SKRect(currentX, baselineY - (scaledFont * 0.84f), currentX + Math.Max(wordW, 6f), baselineY + (scaledFont * 0.14f));
                    _aesthetics.DrawWordColorSweep(context.Canvas, sweepRect, slice.WordProgress, packet, lineAlpha * 0.85f);
                }

                DrawScaledMonoText(context, monoFont, revealedText, currentX, baselineY, scale, packet.Palette.AccentColor1, lineAlpha);

                _underlinePaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.55f * lineAlpha);
                context.Canvas.DrawRect(currentX, baselineY + 4f, Math.Max(wordW, 2f), 2f, _underlinePaint);
                currentX += wordW;

                var activeVisual = StyleLayoutHelper.ResolveWordVisual(word, packet, allowTrackMoodFallback: false);
                if (activeVisual is not null && (reducedMotion || slice.WordProgress >= IconPopThresholdProgress))
                {
                    currentX += scaledFont * 0.22f;
                    var chipW = scaledFont * 1.24f;
                    var popScale = reducedMotion ? 1.0f : _activeChipPop.Value;
                    DrawInlineIconChip(context, activeVisual, currentX, baselineY, chipW, scaledFont * 1.05f, popScale, lineAlpha, isActive: true);
                    currentX += chipW;
                }
            }
        }

        DrawCursor(context.Canvas, currentX + 3f, baselineY, scaledFont, isTypingActively, reducedMotion, lineAlpha);
        _aesthetics.DrawFollowingDot(context.Canvas, packet, currentX + 5f, baselineY - (scaledFont * 0.96f), Math.Clamp(scaledFont * 0.11f, 2.5f, 5.5f), lineAlpha);
    }

    private void DrawInlineIconChip(
        in StyleRenderContext context,
        WordVisualMatch visual,
        float leftX,
        float baselineY,
        float chipW,
        float chipH,
        float popScale,
        float opacity,
        bool isActive)
    {
        var packet = context.Packet;
        var centerX = leftX + (chipW * 0.5f);
        var centerY = baselineY - (chipH * 0.34f);

        var save = context.Canvas.Save();
        try
        {
            context.Canvas.Translate(centerX, centerY);
            context.Canvas.Scale(popScale, popScale);

            var rect = new SKRect(-chipW * 0.5f, -chipH * 0.5f, chipW * 0.5f, chipH * 0.5f);
            var corner = Math.Clamp(chipH * 0.28f, 4f, 12f);

            _chipFillPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha((isActive ? 0.24f : 0.16f) * opacity);
            context.Canvas.DrawRoundRect(rect, corner, corner, _chipFillPaint);

            _chipBorderPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha((isActive ? 0.62f : 0.40f) * opacity);
            context.Canvas.DrawRoundRect(rect, corner, corner, _chipBorderPaint);

            var tint = isActive ? packet.Palette.AccentColor1 : packet.Palette.AccentColor2;
            StyleLayoutHelper.DrawVisualGlyph(
                context.Canvas,
                context.Fonts,
                context.Legibility,
                visual,
                centerX: 0f,
                centerY: 0f,
                iconSizeDip: chipH * 0.70f,
                tintColor: tint,
                opacity: opacity);
        }
        finally
        {
            context.Canvas.RestoreToCount(save);
        }
    }

    private void DrawCodeCardBackdrop(SKCanvas canvas, SKRect rect, FrameRenderPacket packet)
    {
        if (packet.HighContrastScrim)
        {
            _cardBgPaint.Color = LegibilityStyleSpec.HighContrastScrimColor;
        }
        else
        {
            var alpha = packet.ScrimIntensity switch
            {
                ScrimIntensity.Off => 0.0f,
                ScrimIntensity.Subtle => 0.42f,
                ScrimIntensity.Balanced => 0.64f,
                ScrimIntensity.Strong => 0.78f,
                _ => 0.64f
            };

            if (alpha <= 0.001f)
            {
                return;
            }

            _cardBgPaint.Color = new SKColor(0x0B, 0x0C, 0x16, (byte)Math.Clamp(MathF.Round(alpha * 255f), 0f, 255f));
        }

        canvas.DrawRoundRect(rect, 14f, 14f, _cardBgPaint);
        _cardBorderPaint.Color = new SKColor(0xFF, 0xFF, 0xFF, packet.HighContrastScrim ? (byte)0x26 : (byte)28);
        canvas.DrawRoundRect(rect, 14f, 14f, _cardBorderPaint);
    }

    private void DrawCursor(
        SKCanvas canvas,
        float x,
        float baselineY,
        float scaledFontSize,
        bool isTypingActively,
        bool reducedMotion,
        float opacity)
    {
        var cursorVisible = reducedMotion || isTypingActively || _blinkClockSec < (CursorBlinkPeriodSeconds * 0.5);
        if (!cursorVisible)
        {
            return;
        }

        var caretW = Math.Max(2.5f, scaledFontSize * 0.12f);
        var caretH = scaledFontSize * 1.06f;
        var caretTop = baselineY - (scaledFontSize * 0.86f);
        var rect = new SKRect(x, caretTop, x + caretW, caretTop + caretH);

        _cursorGlowPaint.Color = CodestyleStyleRenderer.CursorRed.WithAlpha((byte)Math.Clamp(MathF.Round(140f * opacity), 0f, 255f));
        _cursorGlowPaint.ImageFilter = _cursorBlur;
        try
        {
            canvas.DrawRoundRect(rect, 1.5f, 1.5f, _cursorGlowPaint);
        }
        finally
        {
            _cursorGlowPaint.ImageFilter = null;
        }

        _cursorPaint.Color = CodestyleStyleRenderer.CursorRed.WithAlpha((byte)Math.Clamp(MathF.Round(255f * opacity), 0f, 255f));
        canvas.DrawRoundRect(rect, 1.5f, 1.5f, _cursorPaint);
    }

    private static void DrawScaledMonoText(
        in StyleRenderContext context,
        SKFont monoFont,
        string text,
        float x,
        float baselineY,
        float scale,
        RgbColor fillColor,
        float opacity)
    {
        if (string.IsNullOrEmpty(text) || opacity <= 0.004f)
        {
            return;
        }

        var save = context.Canvas.Save();
        try
        {
            context.Canvas.Translate(x, baselineY);
            context.Canvas.Scale(scale, scale);
            context.Legibility.DrawLegibleText(
                context.Canvas,
                text,
                0f,
                0f,
                SKTextAlign.Left,
                monoFont,
                LyricFontId.SystemMono,
                fillColor,
                opacity: opacity,
                fonts: context.Fonts);
        }
        finally
        {
            context.Canvas.RestoreToCount(save);
        }
    }

    private static float MeasureLineWithChips(string prefix, LyricLine line, SKFont monoFont, FontAssetRegistry fonts)
    {
        var total = TextShaperService.MeasureWidth(prefix, monoFont, fonts);
        if (line.Words.IsEmpty)
        {
            return total + TextShaperService.MeasureWidth(line.RawText, monoFont, fonts);
        }

        var spaceW = TextShaperService.MeasureWidth(" ", monoFont, fonts);
        var chipW = monoFont.Size * 1.46f;
        for (var i = 0; i < line.Words.Length; i++)
        {
            if (i > 0 && line.Words[i].IsSpaceSeparated)
            {
                total += spaceW;
            }

            total += TextShaperService.MeasureWidth(line.Words[i].Text, monoFont, fonts);
            if (line.Words[i].Visual is not null)
            {
                total += chipW;
            }
        }

        return total;
    }

    private static float ResolveMonoFontSize(FrameRenderPacket packet) =>
        packet.IsCompactCard
            ? Math.Clamp(packet.EffectiveHeroFontSizeDip * 0.62f, 10f, 26f)
            : Math.Clamp(packet.EffectiveHeroFontSizeDip * 0.62f, 18f, 68f);

    private static string FormatLineNumber(int oneBasedIndex) =>
        Math.Max(1, oneBasedIndex).ToString("00", CultureInfo.InvariantCulture);
}
