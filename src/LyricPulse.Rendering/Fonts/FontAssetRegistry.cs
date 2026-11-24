using System.Collections.Frozen;
using Avalonia;
using Avalonia.Platform;
using SkiaSharp;

namespace LyricPulse.Rendering.Fonts;

/// <summary>
/// App-lifetime SkiaSharp 3 typeface and SKFont singleton cache.
/// Loads all 5 bundled open-source static TTFs via AssetLoader -&gt; SKData.Create -&gt; SKTypeface.FromData
/// (never SKTypeface.FromStream) and resolves system fallbacks for CJK, RTL, Emoji, Mono, and UI scripts.
/// </summary>
public sealed class FontAssetRegistry : IDisposable
{
    public const int MaxCachedFonts = 64;

    private static readonly FrozenDictionary<LyricFontId, FontOpticalMetrics> OpticalCatalog =
        new Dictionary<LyricFontId, FontOpticalMetrics>
        {
            [LyricFontId.BodoniModa] = new(
                LyricFontId.BodoniModa,
                "Bodoni Moda",
                TrackingEm: -0.015f,
                LineHeightMultiplier: 1.14f,
                StrokeHaloRatio: 0.036f,
                MinOverlaySizeDip: 36f,
                BundledAssetUri: "avares://LyricPulse.Rendering/Assets/Fonts/BodoniModa-Bold.ttf"),
            [LyricFontId.Jost] = new(
                LyricFontId.Jost,
                "Jost",
                TrackingEm: -0.020f,
                LineHeightMultiplier: 1.12f,
                StrokeHaloRatio: 0.048f,
                MinOverlaySizeDip: 24f,
                BundledAssetUri: "avares://LyricPulse.Rendering/Assets/Fonts/Jost-Bold.ttf"),
            [LyricFontId.Mulish] = new(
                LyricFontId.Mulish,
                "Mulish",
                TrackingEm: -0.010f,
                LineHeightMultiplier: 1.20f,
                StrokeHaloRatio: 0.048f,
                MinOverlaySizeDip: 22f,
                BundledAssetUri: "avares://LyricPulse.Rendering/Assets/Fonts/Mulish-Bold.ttf"),
            [LyricFontId.Nunito] = new(
                LyricFontId.Nunito,
                "Nunito",
                TrackingEm: 0.000f,
                LineHeightMultiplier: 1.22f,
                StrokeHaloRatio: 0.050f,
                MinOverlaySizeDip: 22f,
                BundledAssetUri: "avares://LyricPulse.Rendering/Assets/Fonts/Nunito-SemiBold.ttf"),
            [LyricFontId.FluentIcons] = new(
                LyricFontId.FluentIcons,
                "FluentSystemIcons-Filled",
                TrackingEm: 0.000f,
                LineHeightMultiplier: 1.00f,
                StrokeHaloRatio: 0.048f,
                MinOverlaySizeDip: 20f,
                BundledAssetUri: "avares://LyricPulse.Rendering/Assets/Fonts/FluentSystemIcons-Filled.ttf"),
            [LyricFontId.SystemMono] = new(
                LyricFontId.SystemMono,
                "Cascadia Mono",
                TrackingEm: 0.000f,
                LineHeightMultiplier: 1.38f,
                StrokeHaloRatio: 0.044f,
                MinOverlaySizeDip: 18f,
                BundledAssetUri: null),
            [LyricFontId.SystemUi] = new(
                LyricFontId.SystemUi,
                "Segoe UI Variable",
                TrackingEm: -0.005f,
                LineHeightMultiplier: 1.34f,
                StrokeHaloRatio: 0.000f,
                MinOverlaySizeDip: 15f,
                BundledAssetUri: null),
            [LyricFontId.Emoji] = new(
                LyricFontId.Emoji,
                "Segoe UI Emoji",
                TrackingEm: 0.000f,
                LineHeightMultiplier: 1.00f,
                StrokeHaloRatio: 0.000f,
                MinOverlaySizeDip: 20f,
                BundledAssetUri: null)
        }.ToFrozenDictionary();

    private readonly Dictionary<LyricFontId, SKTypeface> _bundledTypefaces = new(5);
    private readonly Dictionary<FontCacheKey, SKFont> _fontCache = new(MaxCachedFonts);
    private readonly Lock _cacheLock = new();

    private readonly SKTypeface _fallbackMono;
    private readonly SKTypeface _fallbackUi;
    private readonly SKTypeface _fallbackEmoji;
    private bool _disposed;

    public FontAssetRegistry(Func<Uri, Stream>? assetOpener = null)
    {
        var opener = assetOpener ?? OpenAvaloniaAsset;

        foreach (var (fontId, metrics) in OpticalCatalog)
        {
            if (metrics.BundledAssetUri is not null)
            {
                _bundledTypefaces[fontId] = LoadBundledTypeface(metrics.BundledAssetUri, opener);
            }
        }

        _fallbackMono = ResolveSystemTypeface("Cascadia Mono", "Consolas", "Courier New");
        _fallbackUi = ResolveSystemTypeface("Segoe UI Variable Text", "Segoe UI Variable", "Segoe UI");
        _fallbackEmoji = ResolveSystemTypeface("Segoe UI Emoji");
    }

    public static IReadOnlyDictionary<LyricFontId, FontOpticalMetrics> AllOpticalMetrics => OpticalCatalog;

    public static FontOpticalMetrics GetOpticalMetrics(LyricFontId id) =>
        OpticalCatalog.TryGetValue(id, out var metrics) ? metrics : OpticalCatalog[LyricFontId.Jost];

    public static LyricFontId ResolveFontId(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return LyricFontId.Jost;
        }

        var trimmed = fontFamily.Trim();
        if (trimmed.Contains("Bodoni", StringComparison.OrdinalIgnoreCase))
        {
            return LyricFontId.BodoniModa;
        }

        if (trimmed.Contains("Mulish", StringComparison.OrdinalIgnoreCase))
        {
            return LyricFontId.Mulish;
        }

        if (trimmed.Contains("Nunito", StringComparison.OrdinalIgnoreCase))
        {
            return LyricFontId.Nunito;
        }

        if (trimmed.Contains("Fluent", StringComparison.OrdinalIgnoreCase))
        {
            return LyricFontId.FluentIcons;
        }

        if (trimmed.Contains("Cascadia", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Consolas", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Mono", StringComparison.OrdinalIgnoreCase))
        {
            return LyricFontId.SystemMono;
        }

        if (trimmed.Contains("Segoe UI Emoji", StringComparison.OrdinalIgnoreCase))
        {
            return LyricFontId.Emoji;
        }

        if (trimmed.Contains("Segoe", StringComparison.OrdinalIgnoreCase))
        {
            return LyricFontId.SystemUi;
        }

        return LyricFontId.Jost;
    }

    public SKTypeface GetTypeface(LyricFontId id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return id switch
        {
            LyricFontId.SystemMono => _fallbackMono,
            LyricFontId.SystemUi => _fallbackUi,
            LyricFontId.Emoji => _fallbackEmoji,
            _ => _bundledTypefaces.TryGetValue(id, out var tf) ? tf : SKTypeface.Default
        };
    }

    /// <summary>
    /// Returns a cached SkiaSharp 3 SKFont configured for transparent layered compositing
    /// (Antialias edging, Subpixel=true, LinearMetrics=true, NEVER ClearType SubpixelAntialias).
    /// </summary>
    public SKFont GetOrCreateFont(LyricFontId id, float sizeDip)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var metrics = GetOpticalMetrics(id);
        var clampedSize = Math.Clamp(
            float.IsFinite(sizeDip) ? Math.Max(sizeDip, metrics.MinOverlaySizeDip) : 48f,
            8f,
            256f);

        // Quantize to 0.5 DIP steps to bound cache cardinality while preserving smooth scaling.
        var quantizedHalfDip = (int)MathF.Round(clampedSize * 2f);
        var key = new FontCacheKey(id, quantizedHalfDip);

        lock (_cacheLock)
        {
            if (_fontCache.TryGetValue(key, out var existing))
            {
                return existing;
            }

            if (_fontCache.Count >= MaxCachedFonts)
            {
                ClearFontCacheLocked();
            }

            var font = CreateConfiguredFont(GetTypeface(id), quantizedHalfDip * 0.5f);
            _fontCache[key] = font;
            return font;
        }
    }

    /// <summary>
    /// Creates an uncached SkiaSharp 3 SKFont (caller owns disposal) for explicit sizing such as previews.
    /// </summary>
    public SKFont CreateFont(LyricFontId id, float sizeDip, bool enforceMinOverlayFloor = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var metrics = GetOpticalMetrics(id);
        var safeSize = float.IsFinite(sizeDip) ? sizeDip : 48f;
        if (enforceMinOverlayFloor)
        {
            safeSize = Math.Max(safeSize, metrics.MinOverlaySizeDip);
        }

        return CreateConfiguredFont(GetTypeface(id), Math.Clamp(safeSize, 6f, 256f));
    }

    /// <summary>
    /// Resolves a system fallback typeface for CJK, RTL, Cyrillic, Devanagari, or Emoji codepoints
    /// missing from the primary SIL OFL typeface.
    /// </summary>
    public SKTypeface MatchFallbackForCodepoint(int codepoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return SKFontManager.Default.MatchCharacter(codepoint) ?? _fallbackEmoji;
    }

    /// <summary>
    /// Selects either the requested primary typeface or an OS fallback typeface if any character
    /// in the text cannot be represented by the primary typeface (prevents tofu boxes).
    /// </summary>
    public SKTypeface ResolveTypefaceForText(LyricFontId primaryId, ReadOnlySpan<char> text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var primary = GetTypeface(primaryId);
        if (text.IsEmpty)
        {
            return primary;
        }

        using var probeFont = CreateConfiguredFont(primary, 24f);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            int codepoint;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codepoint = char.ConvertToUtf32(c, text[i + 1]);
                i++;
            }
            else
            {
                codepoint = c;
            }

            if (!probeFont.ContainsGlyph(codepoint))
            {
                return MatchFallbackForCodepoint(codepoint);
            }
        }

        return primary;
    }

    public static SKFont CreateConfiguredFont(SKTypeface typeface, float sizeDip)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        return new SKFont(typeface, sizeDip)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
            LinearMetrics = true,
            Hinting = SKFontHinting.Normal
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_cacheLock)
        {
            ClearFontCacheLocked();
        }

        foreach (var tf in _bundledTypefaces.Values)
        {
            tf.Dispose();
        }

        _bundledTypefaces.Clear();

        if (!ReferenceEquals(_fallbackMono, SKTypeface.Default))
        {
            _fallbackMono.Dispose();
        }

        if (!ReferenceEquals(_fallbackUi, SKTypeface.Default))
        {
            _fallbackUi.Dispose();
        }

        if (!ReferenceEquals(_fallbackEmoji, SKTypeface.Default))
        {
            _fallbackEmoji.Dispose();
        }
    }

    private void ClearFontCacheLocked()
    {
        foreach (var font in _fontCache.Values)
        {
            font.Dispose();
        }

        _fontCache.Clear();
    }

    private static SKTypeface LoadBundledTypeface(string avaresUri, Func<Uri, Stream> assetOpener)
    {
        var uri = new Uri(avaresUri, UriKind.Absolute);
        using var stream = assetOpener(uri);
        using var data = SKData.Create(stream)
            ?? throw new InvalidOperationException($"Failed to copy font asset into SKData: {avaresUri}");

        return SKTypeface.FromData(data)
            ?? throw new InvalidOperationException($"Skia failed to decode bundled font typeface: {avaresUri}");
    }

    private static Stream OpenAvaloniaAsset(Uri uri)
    {
        try
        {
            return AssetLoader.Open(uri);
        }
        catch (InvalidOperationException)
        {
            var standaloneLoader = new StandardAssetLoader(typeof(FontAssetRegistry).Assembly);
            return standaloneLoader.Open(uri);
        }
    }

    private static SKTypeface ResolveSystemTypeface(params string[] preferredFamilies)
    {
        foreach (var family in preferredFamilies)
        {
            var matched = SKFontManager.Default.MatchFamily(family);
            if (matched is not null && !string.IsNullOrWhiteSpace(matched.FamilyName) &&
                matched.FamilyName.Contains(family.Split(' ')[0], StringComparison.OrdinalIgnoreCase))
            {
                return matched;
            }

            matched?.Dispose();
        }

        return SKTypeface.Default;
    }

    private readonly record struct FontCacheKey(LyricFontId FontId, int QuantizedHalfDip);
}
