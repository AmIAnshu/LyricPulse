using System.Globalization;
using System.Text;
using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #3 — Codestyle: live-typed monospace lyrics (<c>Cascadia Mono</c> -&gt; <c>Consolas</c>)
/// with tabular gutter line numbers (<c>01</c>, <c>02</c>), syntax token coloring,
/// character-by-character typing reveal under a blinking <c>#EF4444</c> cursor (530ms cadence),
/// and zero post-process distortion shaders.
/// </summary>
public sealed class CodestyleStyleRenderer : IKineticStyleRenderer
{
    public const double CursorBlinkPeriodSeconds = 1.06; // 530ms on / 530ms off
    public const double TypingRevealFraction = 0.78;     // Rest complete for final 22% of beat

    public static readonly SKColor CursorRed = new(0xEF, 0x44, 0x44, 0xFF); // #EF4444

    private readonly SKPaint _cardBgPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _cardBorderPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.0f };
    private readonly SKPaint _cursorPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _cursorGlowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _underlinePaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly StringBuilder _lineScratch = new(128);

    private SpringState _lineScrollOffsetY = SpringState.AtRest(0f);
    private float _lineFadeIn = 1.0f;
    private double _blinkClockSec;
    private int _lastLineIndex = -1;
    private bool _disposed;

    public OverlayStyleId StyleId => OverlayStyleId.Codestyle;

    public void ResetState()
    {
        _lineScrollOffsetY = SpringState.AtRest(0f);
        _lineFadeIn = 1.0f;
        _blinkClockSec = 0.0;
        _lastLineIndex = -1;
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
        var linePitch = fontSize * 1.38f;

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

        if (!packet.ReducedMotion)
        {
            _blinkClockSec = (_blinkClockSec + dt) % CursorBlinkPeriodSeconds;
            _lineScrollOffsetY = _lineScrollOffsetY.Step(SpringTokens.LineShift, dt);
        }
        else
        {
            _lineScrollOffsetY = SpringState.AtRest(0f);
            _lineFadeIn = Math.Min(1f, _lineFadeIn + (dt / 0.14f));
        }

        var isPlaying = packet.PlaybackSnapshot?.IsPlaying ?? (packet.SceneSnapshot.PlaybackState == MediaPlaybackState.Playing);
        return !_lineScrollOffsetY.IsAtRest ||
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
        var monoFont = context.Fonts.GetOrCreateFont(LyricFontId.SystemMono, fontSize);
        var linePitch = fontSize * 1.38f;
        var maxBlockWidth = Math.Min(packet.ViewportWidthDip * 0.72f, 920f);

        var gutterStr = FormatLineNumber(slice.ActiveLineIndex + 1);
        var prefixStr = "> ";
        var activeFullLine = BuildFullLineText(slice.CurrentLine);
        var fullMeasure = TextShaperService.MeasureWidth(gutterStr + "  " + prefixStr + activeFullLine, monoFont);
        var fitScale = TextShaperService.ComputeAutoFitScale(fullMeasure + 44f, maxBlockWidth, 0.96f);

        var scaledFontSize = fontSize * fitScale;
        var scaledLinePitch = linePitch * fitScale;
        var blockWidth = Math.Clamp((fullMeasure * fitScale) + 54f, 280f, maxBlockWidth);

        var showPrev = slice.PreviousLine is not null;
        var showNext = slice.NextLine is not null && (slice.LineProgress >= 0.68 || (slice.TimeUntilNextLine?.TotalMilliseconds ?? 9999) <= 500);
        var visibleRows = 1 + (showPrev ? 1 : 0) + (showNext ? 1 : 0);

        const float padX = 20f;
        const float padY = 16f;
        var blockHeight = (visibleRows * scaledLinePitch) + (padY * 2f);

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet);
        var left = Math.Clamp(anchorX - (blockWidth * 0.5f), 16f, Math.Max(16f, packet.ViewportWidthDip - blockWidth - 16f));
        var top = Math.Clamp(anchorY - (blockHeight * 0.5f), 16f, Math.Max(16f, packet.ViewportHeightDip - blockHeight - 16f));
        var cardRect = new SKRect(left, top, left + blockWidth, top + blockHeight);

        DrawCodeCardBackdrop(context.Canvas, cardRect, packet);

        var clipSave = context.Canvas.Save();
        try
        {
            context.Canvas.ClipRoundRect(new SKRoundRect(cardRect, 14f, 14f), antialias: true);

            var scrollY = packet.ReducedMotion ? 0f : _lineScrollOffsetY.Value * fitScale;
            var contentLeft = cardRect.Left + padX;
            var cursorRowY = cardRect.Top + padY + (showPrev ? scaledLinePitch : 0f) + scrollY;
            var lineAlpha = packet.ReducedMotion ? Ease.Highlight(_lineFadeIn) : 1.0f;

            // Past line: dim comment syntax "// <previous line>"
            if (showPrev && slice.PreviousLine is not null)
            {
                var prevBaseline = cardRect.Top + padY + (scaledFontSize * 0.88f) + scrollY;
                DrawScaledMonoText(
                    context,
                    monoFont,
                    $"// {slice.PreviousLine.RawText}",
                    contentLeft,
                    prevBaseline,
                    fitScale,
                    packet.Palette.AccentColor3,
                    opacity: 0.48f * lineAlpha);
            }

            // Active line: tabular gutter + syntax prefix + typed words + blinking #EF4444 cursor
            var activeBaseline = cursorRowY + (scaledFontSize * 0.88f);
            DrawActiveTypedLine(
                context,
                monoFont,
                slice,
                contentLeft,
                activeBaseline,
                fitScale,
                lineAlpha,
                packet.ReducedMotion);

            // Upcoming line: faint autocomplete ghost hint
            if (showNext && slice.NextLine is not null)
            {
                var nextBaseline = cursorRowY + scaledLinePitch + (scaledFontSize * 0.88f);
                DrawScaledMonoText(
                    context,
                    monoFont,
                    $"   {slice.NextLine.RawText}",
                    contentLeft,
                    nextBaseline,
                    fitScale,
                    packet.Palette.AccentColor3,
                    opacity: 0.24f * lineAlpha);
            }
        }
        finally
        {
            context.Canvas.RestoreToCount(clipSave);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cardBgPaint.Dispose();
        _cardBorderPaint.Dispose();
        _cursorPaint.Dispose();
        _cursorGlowPaint.Dispose();
        _underlinePaint.Dispose();
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

        _cardBorderPaint.Color = new SKColor(0xFF, 0xFF, 0xFF, 28);
        canvas.DrawRoundRect(rect, 14f, 14f, _cardBorderPaint);
    }

    private void DrawActiveTypedLine(
        in StyleRenderContext context,
        SKFont monoFont,
        Core.Runtime.ActiveLyricSlice slice,
        float startX,
        float baselineY,
        float scale,
        float lineAlpha,
        bool reducedMotion)
    {
        var packet = context.Packet;
        var currentX = startX;

        var gutter = FormatLineNumber(slice.ActiveLineIndex + 1) + "  ";
        DrawScaledMonoText(context, monoFont, gutter, currentX, baselineY, scale, packet.Palette.AccentColor3, 0.48f * lineAlpha);
        currentX += TextShaperService.MeasureWidth(gutter, monoFont) * scale;

        const string prefix = "> ";
        DrawScaledMonoText(context, monoFont, prefix, currentX, baselineY, scale, packet.Palette.AccentColor2, 0.80f * lineAlpha);
        currentX += TextShaperService.MeasureWidth(prefix, monoFont) * scale;

        var words = slice.CurrentLine!.Words;
        if (words.IsEmpty)
        {
            DrawScaledMonoText(context, monoFont, slice.CurrentLine.RawText, currentX, baselineY, scale, packet.Palette.AccentColor1, lineAlpha);
            currentX += TextShaperService.MeasureWidth(slice.CurrentLine.RawText, monoFont) * scale;
            DrawCursor(context.Canvas, currentX + 3f, baselineY, monoFont.Size * scale, isTypingActively: false, reducedMotion, lineAlpha);
            return;
        }

        var activeIdx = slice.ActiveWordIndex >= 0 && slice.ActiveWordIndex < words.Length
            ? slice.ActiveWordIndex
            : 0;

        var spaceWidth = TextShaperService.MeasureWidth(" ", monoFont) * scale;
        var isTypingActively = false;

        for (var i = 0; i <= activeIdx; i++)
        {
            var word = words[i];
            if (i > 0 && word.IsSpaceSeparated)
            {
                currentX += spaceWidth;
            }

            if (i < activeIdx)
            {
                var isKeyword = StyleLayoutHelper.IsConnectorWord(word.Text);
                var tokenColor = isKeyword ? packet.Palette.AccentColor2 : packet.Palette.AccentColor1;
                var tokenOpacity = (isKeyword ? 0.84f : 0.88f) * lineAlpha;
                DrawScaledMonoText(context, monoFont, word.Text, currentX, baselineY, scale, tokenColor, tokenOpacity);
                currentX += TextShaperService.MeasureWidth(word.Text, monoFont) * scale;
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
                    isTypingActively = normalizedTyping < 0.999 &&
                                       (packet.PlaybackSnapshot?.IsPlaying ?? true);

                    var revealedChars = Math.Clamp((int)Math.Ceiling(normalizedTyping * word.Text.Length), 1, word.Text.Length);
                    revealedText = revealedChars >= word.Text.Length ? word.Text : word.Text[..revealedChars];
                }

                var wordWidth = TextShaperService.MeasureWidth(revealedText, monoFont) * scale;
                DrawScaledMonoText(context, monoFont, revealedText, currentX, baselineY, scale, packet.Palette.AccentColor1, lineAlpha);

                // Subtle 2px underline beneath active typed token
                _underlinePaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.55f * lineAlpha);
                context.Canvas.DrawRect(currentX, baselineY + 4f, Math.Max(wordWidth, 2f), 2f, _underlinePaint);

                currentX += wordWidth;
            }
        }

        DrawCursor(context.Canvas, currentX + 3f, baselineY, monoFont.Size * scale, isTypingActively, reducedMotion, lineAlpha);
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

        using (var blur = SKImageFilter.CreateBlur(5f, 5f))
        {
            _cursorGlowPaint.Color = CursorRed.WithAlpha((byte)Math.Clamp(MathF.Round(140f * opacity), 0f, 255f));
            _cursorGlowPaint.ImageFilter = blur;
            try
            {
                canvas.DrawRoundRect(rect, 1.5f, 1.5f, _cursorGlowPaint);
            }
            finally
            {
                _cursorGlowPaint.ImageFilter = null;
            }
        }

        _cursorPaint.Color = CursorRed.WithAlpha((byte)Math.Clamp(MathF.Round(255f * opacity), 0f, 255f));
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
                opacity: opacity);
        }
        finally
        {
            context.Canvas.RestoreToCount(save);
        }
    }

    private string BuildFullLineText(LyricLine line)
    {
        if (line.Words.IsEmpty)
        {
            return line.RawText;
        }

        _lineScratch.Clear();
        for (var i = 0; i < line.Words.Length; i++)
        {
            if (i > 0 && line.Words[i].IsSpaceSeparated)
            {
                _lineScratch.Append(' ');
            }

            _lineScratch.Append(line.Words[i].Text);
        }

        return _lineScratch.ToString();
    }

    private static float ResolveMonoFontSize(FrameRenderPacket packet) =>
        Math.Clamp(packet.BaseFontSizeDip * 0.62f, 18f, 44f);

    private static string FormatLineNumber(int oneBasedIndex) =>
        Math.Max(1, oneBasedIndex).ToString("00", CultureInfo.InvariantCulture);
}
