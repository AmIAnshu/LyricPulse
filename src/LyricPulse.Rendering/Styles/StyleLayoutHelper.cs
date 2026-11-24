using System.Collections.Frozen;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Shared immutable word-stream, safe-zone anchor, keyword classification, and Fluent/Emoji visual
/// drawing utilities reused across kinetic lyric style renderers.
/// </summary>
internal static class StyleLayoutHelper
{
    public static readonly FrozenSet<string> ConnectorStopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "of", "in", "on", "at", "to", "for", "and", "or", "but",
        "my", "your", "our", "his", "her", "so", "as", "by", "this", "that", "with", "into", "from"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static (float AnchorX, float AnchorY) ComputeAnchorPoint(FrameRenderPacket packet)
    {
        var safeMargin = Math.Clamp(packet.BoundsConfig.SafeMarginPx / Math.Max(packet.RenderScaling, 0.5f), 16f, 120f);
        var minX = Math.Min(safeMargin + 40f, packet.ViewportWidthDip * 0.45f);
        var maxX = Math.Max(minX, packet.ViewportWidthDip - minX);
        var minY = Math.Min(safeMargin + 40f, packet.ViewportHeightDip * 0.45f);
        var maxY = Math.Max(minY, packet.ViewportHeightDip - minY);

        var x = Math.Clamp((float)(packet.ViewportWidthDip * packet.BoundsConfig.AnchorX), minX, maxX);
        var y = Math.Clamp((float)(packet.ViewportHeightDip * packet.BoundsConfig.AnchorY), minY, maxY);
        return (x, y);
    }

    public static ActiveLyricSlice ResolveSlice(FrameRenderPacket packet)
    {
        if (packet.PlaybackSnapshot is { } snap)
        {
            return snap.ActiveSlice;
        }

        if (packet.SceneSnapshot.Document is { } doc)
        {
            return ActiveLyricSlice.Resolve(doc, packet.SceneSnapshot.EffectivePosition);
        }

        return ActiveLyricSlice.Empty;
    }

    public static (LyricWord? Previous, LyricWord? Active, LyricWord? Next, int GlobalWordIndex, float WordDurationSec)
        ResolveWordTriad(ActiveLyricSlice slice)
    {
        if (slice.CurrentLine is null || slice.CurrentLine.Words.IsEmpty)
        {
            return (null, null, null, -1, 0.32f);
        }

        var words = slice.CurrentLine.Words;
        var idx = slice.ActiveWordIndex >= 0 && slice.ActiveWordIndex < words.Length
            ? slice.ActiveWordIndex
            : 0;

        var active = words[idx];
        var previous = idx > 0
            ? words[idx - 1]
            : slice.PreviousLine is { Words.IsEmpty: false } prevLine
                ? prevLine.Words[^1]
                : null;

        var next = idx + 1 < words.Length
            ? words[idx + 1]
            : slice.NextLine is { Words.IsEmpty: false } nextLine
                ? nextLine.Words[0]
                : null;

        var durationSec = Math.Clamp((float)(active.EndTime - active.StartTime).TotalSeconds, 0.08f, 2.5f);
        var globalIdx = (slice.ActiveLineIndex * 1000) + idx;
        return (previous, active, next, globalIdx, durationSec);
    }

    public static bool IsConnectorWord(string text)
    {
        var cleaned = TrimPunctuation(text);
        return cleaned.Length > 0 && ConnectorStopwords.Contains(cleaned);
    }

    public static string TrimPunctuation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var span = text.AsSpan().Trim();
        var start = 0;
        var end = span.Length;
        while (start < end && !char.IsLetterOrDigit(span[start]))
        {
            start++;
        }

        while (end > start && !char.IsLetterOrDigit(span[end - 1]))
        {
            end--;
        }

        return start < end ? span[start..end].ToString() : string.Empty;
    }

    /// <summary>
    /// Draws a resolved <see cref="WordVisualMatch"/> centered at (<paramref name="centerX"/>, <paramref name="centerY"/>).
    /// Automatically checks glyph existence on FluentSystemIcons-Filled.ttf and falls back to Emoji if missing
    /// so tofu boxes never appear on the desktop overlay.
    /// </summary>
    public static void DrawVisualGlyph(
        SKCanvas canvas,
        FontAssetRegistry fonts,
        WordVisualMatch visual,
        float centerX,
        float centerY,
        float iconSizeDip,
        RgbColor tintColor,
        float opacity,
        SKPaint scratchPaint)
    {
        var clampedOpacity = Math.Clamp(opacity, 0f, 1f);
        if (clampedOpacity <= 0.004f || string.IsNullOrEmpty(visual.Value))
        {
            return;
        }

        var useFluent = visual.Kind == VisualKind.FluentIcon &&
                        TryGetFirstCodepoint(visual.Value, out var cp) &&
                        fonts.GetOrCreateFont(LyricFontId.FluentIcons, iconSizeDip).ContainsGlyph(cp);

        if (useFluent)
        {
            var fluentFont = fonts.GetOrCreateFont(LyricFontId.FluentIcons, iconSizeDip);
            fluentFont.GetFontMetrics(out var metrics);
            var baselineY = centerY - ((metrics.Ascent + metrics.Descent) * 0.5f);

            scratchPaint.Style = SKPaintStyle.Fill;
            scratchPaint.Color = tintColor.ToSkColorWithAlpha(clampedOpacity);
            canvas.DrawText(visual.Value, centerX, baselineY, SKTextAlign.Center, fluentFont, scratchPaint);
            return;
        }

        var emojiText = !string.IsNullOrEmpty(visual.EmojiFallback) ? visual.EmojiFallback : visual.Value;
        var emojiSize = Math.Max(14f, iconSizeDip * 0.88f);
        var emojiFont = fonts.GetOrCreateFont(LyricFontId.Emoji, emojiSize);
        emojiFont.GetFontMetrics(out var emojiMetrics);
        var emojiBaseline = centerY - ((emojiMetrics.Ascent + emojiMetrics.Descent) * 0.5f);

        scratchPaint.Style = SKPaintStyle.Fill;
        scratchPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(MathF.Round(clampedOpacity * 255f), 0f, 255f));
        canvas.DrawText(emojiText, centerX, emojiBaseline, SKTextAlign.Center, emojiFont, scratchPaint);
    }

    private static bool TryGetFirstCodepoint(string text, out int codepoint)
    {
        codepoint = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (char.IsHighSurrogate(text[0]) && text.Length > 1 && char.IsLowSurrogate(text[1]))
        {
            codepoint = char.ConvertToUtf32(text[0], text[1]);
            return true;
        }

        codepoint = text[0];
        return true;
    }
}
