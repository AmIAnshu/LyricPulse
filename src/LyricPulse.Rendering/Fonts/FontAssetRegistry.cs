using System.Collections.Frozen;
using System.Text;
using Avalonia.Platform;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace LyricPulse.Rendering.Fonts;

/// <summary>
/// App-lifetime SkiaSharp 3 typeface, SKFont, fallback typeface, and HarfBuzz SKShaper singleton cache.
/// Loads all 5 bundled open-source static TTFs via AssetLoader -&gt; SKData.Create -&gt; SKTypeface.FromData
/// (avoiding unsafe stream-backed typefaces), caches all OS fallback typefaces so zero native handles leak,
/// and never disposes cached SKFont instances while render threads may hold references.
/// </summary>
public sealed class FontAssetRegistry : IDisposable
{
    public const int MaxCachedFonts = 256;

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
                BundledAssetUri: null),
            [LyricFontId.Bahnschrift] = new(
                LyricFontId.Bahnschrift,
                "Bahnschrift",
                TrackingEm: -0.012f,
                LineHeightMultiplier: 1.12f,
                StrokeHaloRatio: 0.048f,
                MinOverlaySizeDip: 24f,
                BundledAssetUri: null),
            [LyricFontId.Impact] = new(
                LyricFontId.Impact,
                "Impact",
                TrackingEm: 0.015f,
                LineHeightMultiplier: 1.08f,
                StrokeHaloRatio: 0.052f,
                MinOverlaySizeDip: 26f,
                BundledAssetUri: null),
            [LyricFontId.Georgia] = new(
                LyricFontId.Georgia,
                "Georgia",
                TrackingEm: -0.005f,
                LineHeightMultiplier: 1.18f,
                StrokeHaloRatio: 0.040f,
                MinOverlaySizeDip: 24f,
                BundledAssetUri: null),
            [LyricFontId.SitkaDisplay] = new(
                LyricFontId.SitkaDisplay,
                "Sitka Display",
                TrackingEm: -0.010f,
                LineHeightMultiplier: 1.16f,
                StrokeHaloRatio: 0.038f,
                MinOverlaySizeDip: 26f,
                BundledAssetUri: null),
            [LyricFontId.InkFree] = new(
                LyricFontId.InkFree,
                "Ink Free",
                TrackingEm: 0.005f,
                LineHeightMultiplier: 1.24f,
                StrokeHaloRatio: 0.046f,
                MinOverlaySizeDip: 26f,
                BundledAssetUri: null)
        }.ToFrozenDictionary();

    private static readonly (LyricFontId Id, SKFontStyleWeight Weight, string[] Families)[] SystemDisplaySpecs =
    [
        (LyricFontId.Bahnschrift, SKFontStyleWeight.SemiBold, ["Bahnschrift", "Segoe UI Variable Display", "Segoe UI"]),
        (LyricFontId.Impact, SKFontStyleWeight.Normal, ["Impact", "Arial Black", "Segoe UI"]),
        (LyricFontId.Georgia, SKFontStyleWeight.Bold, ["Georgia", "Times New Roman"]),
        (LyricFontId.SitkaDisplay, SKFontStyleWeight.Bold, ["Sitka Display", "Sitka", "Georgia"]),
        (LyricFontId.InkFree, SKFontStyleWeight.Normal, ["Ink Free", "Segoe Print", "Segoe Script"])
    ];

    private readonly Dictionary<LyricFontId, SKTypeface> _bundledTypefaces = new(5);
    private readonly Dictionary<LyricFontId, SKTypeface> _systemDisplayTypefaces = new(5);
    private readonly Dictionary<FontCacheKey, SKFont> _fontCache = new(MaxCachedFonts);
    private readonly Dictionary<FallbackFontCacheKey, SKFont> _fallbackFontCache = new(64);
    private readonly Dictionary<int, SKTypeface> _fallbackCodepointCache = new(64);
    private readonly Dictionary<string, SKTypeface> _ownedFallbackTypefaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<nint, SKShaper> _shaperCache = new(16);
    private readonly List<SKFont> _retiredFonts = [];
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

        _fallbackMono = ResolveSystemTypeface(SKFontStyleWeight.SemiBold, "Cascadia Mono", "Consolas", "Courier New");
        _fallbackUi = ResolveSystemTypeface(SKFontStyleWeight.Normal, "Segoe UI Variable Text", "Segoe UI Variable", "Segoe UI");
        _fallbackEmoji = ResolveSystemTypeface(SKFontStyleWeight.Normal, "Segoe UI Emoji");

        foreach (var (fontId, weight, families) in SystemDisplaySpecs)
        {
            _systemDisplayTypefaces[fontId] = ResolveSystemTypeface(weight, families);
        }
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

        if (trimmed.Contains("Bahnschrift", StringComparison.OrdinalIgnoreCase))
        {
            return LyricFontId.Bahnschrift;
        }

        if (trimmed.Contains("Impact", StringComparison.OrdinalIgnoreCase))
        {
            return LyricFontId.Impact;
        }

        if (trimmed.Contains("Georgia", StringComparison.OrdinalIgnoreCase))
        {
            return LyricFontId.Georgia;
        }

        if (trimmed.Contains("Sitka", StringComparison.OrdinalIgnoreCase))
        {
            return LyricFontId.SitkaDisplay;
        }

        if (trimmed.Contains("Ink", StringComparison.OrdinalIgnoreCase))
        {
            return LyricFontId.InkFree;
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
            _ when _systemDisplayTypefaces.TryGetValue(id, out var sysTf) => sysTf,
            _ => _bundledTypefaces.TryGetValue(id, out var tf) ? tf : SKTypeface.Default
        };
    }

    /// <summary>
    /// Returns a cached SkiaSharp 3 SKFont configured for transparent layered compositing
    /// (Antialias edging, Subpixel=true, LinearMetrics=true, NEVER ClearType SubpixelAntialias).
    /// Cached instances remain alive until <see cref="Dispose"/> so concurrent render threads never hit freed handles.
    /// </summary>
    public SKFont GetOrCreateFont(LyricFontId id, float sizeDip, bool enforceMinOverlayFloor = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var metrics = GetOpticalMetrics(id);
        var rawSize = float.IsFinite(sizeDip) ? sizeDip : 48f;
        var flooredSize = enforceMinOverlayFloor ? Math.Max(rawSize, metrics.MinOverlaySizeDip) : rawSize;
        var clampedSize = Math.Clamp(flooredSize, 8f, 256f);

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
                // Retire into deferred disposal list (never dispose while render threads could read them!)
                foreach (var oldFont in _fontCache.Values)
                {
                    _retiredFonts.Add(oldFont);
                }

                _fontCache.Clear();
            }

            var font = CreateConfiguredFont(GetTypeface(id), quantizedHalfDip * 0.5f);
            _fontCache[key] = font;
            return font;
        }
    }

    /// <summary>
    /// Returns a cached <see cref="SKFont"/> for a registry-owned fallback typeface at the requested size.
    /// </summary>
    public SKFont GetOrCreateFallbackFont(SKTypeface fallbackTypeface, float sizeDip)
    {
        ArgumentNullException.ThrowIfNull(fallbackTypeface);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var clampedSize = Math.Clamp(float.IsFinite(sizeDip) ? sizeDip : 48f, 8f, 256f);
        var quantizedHalfDip = (int)MathF.Round(clampedSize * 2f);
        var key = new FallbackFontCacheKey(fallbackTypeface.Handle, quantizedHalfDip);

        lock (_cacheLock)
        {
            if (_fallbackFontCache.TryGetValue(key, out var existing))
            {
                return existing;
            }

            if (_fallbackFontCache.Count >= MaxCachedFonts)
            {
                foreach (var oldFont in _fallbackFontCache.Values)
                {
                    _retiredFonts.Add(oldFont);
                }

                _fallbackFontCache.Clear();
            }

            var font = CreateConfiguredFont(fallbackTypeface, quantizedHalfDip * 0.5f);
            _fallbackFontCache[key] = font;
            return font;
        }
    }

    /// <summary>
    /// Returns a cached HarfBuzz <see cref="SKShaper"/> for the specified typeface (owned and disposed by registry).
    /// Prefer <see cref="ShapeSynchronized"/> or <see cref="ShapeToBlobSynchronized"/> for concurrent Gallery/Overlay threads.
    /// </summary>
    public SKShaper GetOrCreateShaper(SKTypeface typeface)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_cacheLock)
        {
            return GetOrCreateShaperLocked(typeface);
        }
    }

    /// <summary>
    /// Thread-safely shapes text with the cached HarfBuzz <see cref="SKShaper"/> under synchronization so
    /// concurrent Gallery cards and live overlays never race on a shared native shaper buffer.
    /// </summary>
    public SKShaper.Result ShapeSynchronized(SKTypeface typeface, string text, SKFont font)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        ArgumentNullException.ThrowIfNull(font);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_cacheLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var shaper = GetOrCreateShaperLocked(typeface);
            return shaper.Shape(text, font);
        }
    }

    /// <summary>
    /// Thread-safely shapes text into a positioned <see cref="SKTextBlob"/> (caller owns disposal).
    /// </summary>
    public SKTextBlob? ShapeToBlobSynchronized(SKTypeface typeface, string text, SKFont font, out float advanceWidth)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        ArgumentNullException.ThrowIfNull(font);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_cacheLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var shaper = GetOrCreateShaperLocked(typeface);
            return TextShaperService.ShapeToBlob(text, font, out advanceWidth, shaper);
        }
    }

    private SKShaper GetOrCreateShaperLocked(SKTypeface typeface)
    {
        if (_shaperCache.TryGetValue(typeface.Handle, out var existing))
        {
            return existing;
        }

        var shaper = new SKShaper(typeface);
        _shaperCache[typeface.Handle] = shaper;
        return shaper;
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
    /// Resolves and caches a registry-owned system fallback typeface for CJK, RTL, Cyrillic,
    /// Devanagari, or Emoji codepoints missing from the primary SIL OFL typeface.
    /// Callers must NOT dispose the returned <see cref="SKTypeface"/>.
    /// </summary>
    public SKTypeface MatchFallbackForCodepoint(int codepoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_cacheLock)
        {
            if (_fallbackCodepointCache.TryGetValue(codepoint, out var cached))
            {
                return cached;
            }

            SKTypeface? matched = null;
            if (codepoint is >= 0x0900 and <= 0x097F)
            {
                matched = SKFontManager.Default.MatchFamily(
                    "Nirmala UI",
                    new SKFontStyle(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright));
                if (matched is not null && !matched.ContainsGlyph(codepoint))
                {
                    matched.Dispose();
                    matched = null;
                }
            }

            matched ??= SKFontManager.Default.MatchCharacter(
                null,
                SKFontStyleWeight.Bold,
                SKFontStyleWidth.Normal,
                SKFontStyleSlant.Upright,
                null,
                codepoint) ?? SKFontManager.Default.MatchCharacter(codepoint);

            if (matched is null)
            {
                _fallbackCodepointCache[codepoint] = _fallbackEmoji;
                return _fallbackEmoji;
            }

            var familyKey = !string.IsNullOrWhiteSpace(matched.FamilyName)
                ? $"{matched.FamilyName}_{matched.FontWeight}"
                : $"cp_{codepoint:X}";

            if (_ownedFallbackTypefaces.TryGetValue(familyKey, out var existingFamily))
            {
                matched.Dispose();
                _fallbackCodepointCache[codepoint] = existingFamily;
                return existingFamily;
            }

            _ownedFallbackTypefaces[familyKey] = matched;
            _fallbackCodepointCache[codepoint] = matched;
            return matched;
        }
    }

    /// <summary>
    /// Checks whether <paramref name="text"/> requires either OS font fallback (missing glyph in primary font)
    /// or HarfBuzz complex-script shaping (RTL, Indic, combining marks, non-ASCII).
    /// </summary>
    public static bool InspectScriptRequirements(
        SKFont primaryFont,
        ReadOnlySpan<char> text,
        out int firstMissingCodepoint,
        out bool needsHarfBuzzShaping)
    {
        ArgumentNullException.ThrowIfNull(primaryFont);
        firstMissingCodepoint = 0;
        needsHarfBuzzShaping = false;

        if (text.IsEmpty)
        {
            return false;
        }

        var remaining = text;
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var charsConsumed);
            remaining = remaining[Math.Max(1, charsConsumed)..];

            if (status != System.Buffers.OperationStatus.Done)
            {
                continue;
            }

            var cp = rune.Value;
            if (cp > 0x7F)
            {
                needsHarfBuzzShaping = true;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                continue;
            }

            if (firstMissingCodepoint == 0 && !primaryFont.ContainsGlyph(cp))
            {
                firstMissingCodepoint = cp;
            }
        }

        return firstMissingCodepoint != 0 || needsHarfBuzzShaping;
    }

    /// <summary>
    /// Selects either the requested primary typeface or a cached registry-owned OS fallback typeface
    /// if any character in the text cannot be represented by the primary typeface (prevents tofu boxes).
    /// </summary>
    public SKTypeface ResolveTypefaceForText(LyricFontId primaryId, ReadOnlySpan<char> text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var primary = GetTypeface(primaryId);
        if (text.IsEmpty)
        {
            return primary;
        }

        var probeFont = GetOrCreateFont(primaryId, 24f);
        InspectScriptRequirements(probeFont, text, out var missingCp, out _);
        return missingCp != 0 ? MatchFallbackForCodepoint(missingCp) : primary;
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
            foreach (var shaper in _shaperCache.Values)
            {
                shaper.Dispose();
            }

            _shaperCache.Clear();

            foreach (var font in _fontCache.Values)
            {
                font.Dispose();
            }

            _fontCache.Clear();

            foreach (var font in _fallbackFontCache.Values)
            {
                font.Dispose();
            }

            _fallbackFontCache.Clear();

            foreach (var retired in _retiredFonts)
            {
                retired.Dispose();
            }

            _retiredFonts.Clear();

            foreach (var owned in _ownedFallbackTypefaces.Values)
            {
                owned.Dispose();
            }

            _ownedFallbackTypefaces.Clear();
            _fallbackCodepointCache.Clear();
        }

        foreach (var tf in _bundledTypefaces.Values)
        {
            tf.Dispose();
        }

        _bundledTypefaces.Clear();

        foreach (var tf in _systemDisplayTypefaces.Values)
        {
            if (!ReferenceEquals(tf, SKTypeface.Default))
            {
                tf.Dispose();
            }
        }

        _systemDisplayTypefaces.Clear();

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

    private static SKTypeface ResolveSystemTypeface(SKFontStyleWeight preferredWeight, params string[] preferredFamilies)
    {
        using var style = new SKFontStyle(preferredWeight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        foreach (var family in preferredFamilies)
        {
            var matched = SKFontManager.Default.MatchFamily(family, style)
                          ?? SKFontManager.Default.MatchFamily(family);
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

    private readonly record struct FallbackFontCacheKey(nint TypefaceHandle, int QuantizedHalfDip);
}
