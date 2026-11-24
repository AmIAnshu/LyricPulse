using AwesomeAssertions;
using LyricPulse.Rendering.Fonts;
using SkiaSharp;
using Xunit;

namespace LyricPulse.Tests.Rendering;

public sealed class FontAssetRegistryTests
{
    [Fact]
    public void BundledFonts_LoadAllFiveStaticTtfsWithValidFamiliesAndAntialiasEdging()
    {
        using var registry = new FontAssetRegistry();

        var bundled = new[]
        {
            (Id: LyricFontId.BodoniModa, ExpectedFragment: "Bodoni"),
            (Id: LyricFontId.Jost, ExpectedFragment: "Jost"),
            (Id: LyricFontId.Mulish, ExpectedFragment: "Mulish"),
            (Id: LyricFontId.Nunito, ExpectedFragment: "Nunito"),
            (Id: LyricFontId.FluentIcons, ExpectedFragment: "Fluent")
        };

        foreach (var (id, expectedFragment) in bundled)
        {
            var typeface = registry.GetTypeface(id);
            typeface.Should().NotBeNull();
            typeface.FamilyName.Should().ContainEquivalentOf(expectedFragment, $"bundled {id} must load its real TTF family");
            typeface.GlyphCount.Should().BeGreaterThan(100, $"bundled {id} should contain full glyph tables");

            var font = registry.GetOrCreateFont(id, 48f);
            font.Edging.Should().Be(SKFontEdging.Antialias, "ClearType SubpixelAntialias must NEVER be used on transparent layered windows");
            font.Subpixel.Should().BeTrue();
            font.LinearMetrics.Should().BeTrue();
        }
    }

    [Fact]
    public void BodoniModa_Enforces36DipMinimumOverlayFloorAndCachesSkFontInstances()
    {
        using var registry = new FontAssetRegistry();

        var smallBodoni = registry.GetOrCreateFont(LyricFontId.BodoniModa, 24f);
        smallBodoni.Size.Should().BeGreaterThanOrEqualTo(36f);

        var cachedBodoni = registry.GetOrCreateFont(LyricFontId.BodoniModa, 36f);
        ReferenceEquals(smallBodoni, cachedBodoni).Should().BeTrue("identical quantized font buckets should reuse cached SKFont");

        using var previewBodoni = registry.CreateFont(LyricFontId.BodoniModa, 18f, enforceMinOverlayFloor: false);
        previewBodoni.Size.Should().BeApproximately(18f, 0.01f);
    }

    [Theory]
    [InlineData("Bodoni Moda", LyricFontId.BodoniModa)]
    [InlineData("Jost", LyricFontId.Jost)]
    [InlineData("Mulish", LyricFontId.Mulish)]
    [InlineData("Nunito", LyricFontId.Nunito)]
    [InlineData("Cascadia Mono", LyricFontId.SystemMono)]
    [InlineData("Segoe UI Variable", LyricFontId.SystemUi)]
    [InlineData("Segoe UI Emoji", LyricFontId.Emoji)]
    [InlineData("FluentSystemIcons-Filled", LyricFontId.FluentIcons)]
    [InlineData("", LyricFontId.Jost)]
    [InlineData(null, LyricFontId.Jost)]
    public void ResolveFontId_MapsFamilyNamesDeterministically(string? family, LyricFontId expected)
    {
        FontAssetRegistry.ResolveFontId(family).Should().Be(expected);
    }

    [Fact]
    public void SystemFallbacksAndComplexScriptShaping_WorkWithoutTofuBoxes()
    {
        using var registry = new FontAssetRegistry();

        registry.GetTypeface(LyricFontId.SystemMono).Should().NotBeNull();
        registry.GetTypeface(LyricFontId.SystemUi).Should().NotBeNull();
        registry.GetTypeface(LyricFontId.Emoji).Should().NotBeNull();

        var cjkTypeface = registry.ResolveTypefaceForText(LyricFontId.Jost, "東京の夜");
        cjkTypeface.Should().NotBeNull();

        var latinTypeface = registry.ResolveTypefaceForText(LyricFontId.Jost, "Neon Horizon");
        latinTypeface.FamilyName.Should().ContainEquivalentOf("Jost");

        var jostFont = registry.GetOrCreateFont(LyricFontId.Jost, 48f);
        var asciiWidth = TextShaperService.MeasureWidth("Neon Horizon", jostFont);
        asciiWidth.Should().BeGreaterThan(100f);

        using var cjkFont = FontAssetRegistry.CreateConfiguredFont(cjkTypeface, 36f);
        var cjkWidth = TextShaperService.MeasureWidth("東京の夜", cjkFont);
        cjkWidth.Should().BeGreaterThan(20f);

        using var blob = TextShaperService.ShapeToBlob("Café 東京", cjkFont, out var shapedWidth);
        blob.Should().NotBeNull();
        shapedWidth.Should().BeGreaterThan(20f);

        TextShaperService.ComputeAutoFitScale(1000f, 500f, 0.80f).Should().BeApproximately(0.56f, 0.001f);
        TextShaperService.ComputeAutoFitScale(200f, 1000f, 0.80f).Should().Be(1.0f);
    }

    [Fact]
    public void LyricPulseFontCollection_ExposesCanonicalUriKey()
    {
        var collection = new LyricPulseFontCollection();
        collection.Key.ToString().Should().Be(LyricPulseFontCollection.SchemeKey);
    }
}
