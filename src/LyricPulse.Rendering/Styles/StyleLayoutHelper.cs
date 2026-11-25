using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Shared immutable word-stream, safe-zone anchor, viewport clamping, grapheme-safe slicing,
/// and contrast-guarded Fluent/Emoji visual drawing utilities reused across kinetic lyric renderers.
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

    /// <summary>
    /// Clamps a centered offscreen layer rectangle strictly within the overlay viewport
    /// so high DPI (150%-200%) + 160% FontScale + BottomThird never clip glyphs off-screen.
    /// </summary>
    public static SKRect ClampLayerRectToViewport(
        float desiredCenterX,
        float desiredCenterY,
        float layerWidthDip,
        float layerHeightDip,
        float viewportWidthDip,
        float viewportHeightDip)
    {
        var clampedW = Math.Clamp(layerWidthDip, 1f, Math.Max(1f, viewportWidthDip));
        var clampedH = Math.Clamp(layerHeightDip, 1f, Math.Max(1f, viewportHeightDip));

        var left = Math.Clamp(desiredCenterX - (clampedW * 0.5f), 0f, Math.Max(0f, viewportWidthDip - clampedW));
        var top = Math.Clamp(desiredCenterY - (clampedH * 0.5f), 0f, Math.Max(0f, viewportHeightDip - clampedH));
        return new SKRect(left, top, left + clampedW, top + clampedH);
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

    public static (LyricWord? Previous, LyricWord? Active, LyricWord? Next, (int LineIndex, int WordIndex) WordKey, float WordDurationSec)
        ResolveWordTriad(ActiveLyricSlice slice)
    {
        if (slice.CurrentLine is null || slice.CurrentLine.Words.IsEmpty)
        {
            return (null, null, null, (-1, -1), 0.32f);
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
        return (previous, active, next, (slice.ActiveLineIndex, idx), durationSec);
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
    /// Grapheme-cluster-safe prefix slice preventing mid-surrogate splits on emoji or combining sequences.
    /// </summary>
    public static string SliceByGraphemes(string text, int maxGraphemes)
    {
        if (string.IsNullOrEmpty(text) || maxGraphemes <= 0)
        {
            return string.Empty;
        }

        var info = new StringInfo(text);
        var total = info.LengthInTextElements;
        if (maxGraphemes >= total)
        {
            return text;
        }

        return info.SubstringByTextElements(0, maxGraphemes);
    }

    public static int CountGraphemes(string text) =>
        string.IsNullOrEmpty(text) ? 0 : new StringInfo(text).LengthInTextElements;

    public static RgbColor BlendSrgb(RgbColor from, RgbColor to, float t)
    {
        var clamped = Math.Clamp(float.IsFinite(t) ? t : 1f, 0f, 1f);
        var r = (byte)Math.Clamp(MathF.Round(from.R + ((to.R - from.R) * clamped)), 0f, 255f);
        var g = (byte)Math.Clamp(MathF.Round(from.G + ((to.G - from.G) * clamped)), 0f, 255f);
        var b = (byte)Math.Clamp(MathF.Round(from.B + ((to.B - from.B) * clamped)), 0f, 255f);
        return new RgbColor(r, g, b);
    }

    /// <summary>
    /// Draws a resolved <see cref="WordVisualMatch"/> centered at (<paramref name="centerX"/>, <paramref name="centerY"/>)
    /// routed through <see cref="LegibilityPipeline"/> so both Fluent Icons (BMP and Supplementary PUA-A &gt; U+FFFF)
    /// and Emoji fallbacks maintain &gt;= 7:1 effective legibility over pure #FFFFFF web pages.
    /// </summary>
    public static void DrawVisualGlyph(
        SKCanvas canvas,
        FontAssetRegistry fonts,
        LegibilityPipeline legibility,
        WordVisualMatch visual,
        float centerX,
        float centerY,
        float iconSizeDip,
        RgbColor tintColor,
        float opacity)
    {
        var clampedOpacity = Math.Clamp(opacity, 0f, 1f);
        if (clampedOpacity <= 0.004f || string.IsNullOrEmpty(visual.Value))
        {
            return;
        }

        var hasFluentRune = visual.Kind == VisualKind.FluentIcon &&
                            TryDecodeFirstScalar(visual.Value, out var scalar) &&
                            fonts.GetOrCreateFont(LyricFontId.FluentIcons, iconSizeDip).ContainsGlyph(scalar);

        if (hasFluentRune)
        {
            var fluentFont = fonts.GetOrCreateFont(LyricFontId.FluentIcons, iconSizeDip);
            fluentFont.GetFontMetrics(out var metrics);
            var baselineY = centerY - ((metrics.Ascent + metrics.Descent) * 0.5f);

            legibility.DrawLegibleText(
                canvas,
                visual.Value,
                centerX,
                baselineY,
                SKTextAlign.Center,
                fluentFont,
                LyricFontId.FluentIcons,
                tintColor,
                opacity: clampedOpacity,
                activeGlowColor: null,
                includeStrokeHalo: true,
                fonts: null);
            return;
        }

        var emojiText = !string.IsNullOrEmpty(visual.EmojiFallback) ? visual.EmojiFallback : visual.Value;
        var emojiSize = Math.Max(14f, iconSizeDip * 0.92f);
        var emojiFont = fonts.GetOrCreateFont(LyricFontId.Emoji, emojiSize);
        emojiFont.GetFontMetrics(out var emojiMetrics);
        var emojiBaseline = centerY - ((emojiMetrics.Ascent + emojiMetrics.Descent) * 0.5f);

        // Contact shadow only (no dark stroke around multicolor emoji glyphs per MOTION_AND_STYLES.md section 3.1)
        legibility.DrawLegibleText(
            canvas,
            emojiText,
            centerX,
            emojiBaseline,
            SKTextAlign.Center,
            emojiFont,
            LyricFontId.Emoji,
            new RgbColor(255, 255, 255),
            opacity: clampedOpacity,
            activeGlowColor: null,
            includeStrokeHalo: false,
            fonts: null,
            includeAmbientShadow: false);
    }

    internal static bool TryDecodeFirstScalar(string text, out int scalarValue)
    {
        scalarValue = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var status = Rune.DecodeFromUtf16(text.AsSpan(), out var rune, out _);
        if (status != System.Buffers.OperationStatus.Done)
        {
            return false;
        }

        scalarValue = rune.Value;
        return true;
    }
}
