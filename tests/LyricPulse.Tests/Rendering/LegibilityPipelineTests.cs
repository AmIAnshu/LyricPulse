using AwesomeAssertions;
using LyricPulse.Core.Models;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Legibility;
using SkiaSharp;
using Xunit;

namespace LyricPulse.Tests.Rendering;

public sealed class LegibilityPipelineTests
{
    [Theory]
    [InlineData(ScrimIntensity.Off, false, 0.00f)]
    [InlineData(ScrimIntensity.Subtle, false, 0.24f)]
    [InlineData(ScrimIntensity.Balanced, false, 0.42f)]
    [InlineData(ScrimIntensity.Strong, false, 0.62f)]
    [InlineData(ScrimIntensity.Off, true, 0xCC / 255f)]
    [InlineData(ScrimIntensity.Strong, true, 0xCC / 255f)]
    public void ScrimAlpha_MatchesLockedDesignTokensAcrossAllModes(
        ScrimIntensity intensity,
        bool highContrast,
        float expectedAlpha)
    {
        LegibilityStyleSpec.GetScrimAlpha(intensity, highContrast)
            .Should().BeApproximately(expectedAlpha, 0.005f);

        if (highContrast)
        {
            var resolved = LegibilityStyleSpec.ResolveScrimBaseColor(AlbumAccentPalette.Default.ScrimTint, intensity, true);
            resolved.Should().Be(new SKColor(0x08, 0x09, 0x0D, 0xCC));
        }
    }

    [Fact]
    public void OpticalStrokeHalo_InvertsToLightWhenManualFillLightnessBelow038()
    {
        // Deep dark fill (OKLCH L < 0.38) -> must invert halo to #F8FAFC @ 68%
        var darkFill = new RgbColor(0x14, 0x18, 0x26);
        LegibilityStyleSpec.ShouldInvertHaloForDarkFill(darkFill).Should().BeTrue();
        var invertedHalo = LegibilityStyleSpec.ResolveOpticalHaloColor(darkFill);
        invertedHalo.Red.Should().Be(0xF8);
        invertedHalo.Green.Should().Be(0xFA);
        invertedHalo.Blue.Should().Be(0xFC);
        invertedHalo.Alpha.Should().Be(0xAD);

        // Luminous standard fill (OKLCH L >= 0.38) -> dark keyline #070712 @ 76%
        var brightFill = new RgbColor(0xF8, 0xFA, 0xFC);
        LegibilityStyleSpec.ShouldInvertHaloForDarkFill(brightFill).Should().BeFalse();
        var darkHalo = LegibilityStyleSpec.ResolveOpticalHaloColor(brightFill);
        darkHalo.Red.Should().Be(0x07);
        darkHalo.Green.Should().Be(0x07);
        darkHalo.Blue.Should().Be(0x12);
        darkHalo.Alpha.Should().Be(0xC2);
    }

    [Fact]
    public void OpticalStrokeWidth_ThinsBodoniModaTo0036xAndObeysClampFloorAndCeiling()
    {
        var bodoni60 = LegibilityStyleSpec.ComputeOpticalStrokeWidth(60f, LyricFontId.BodoniModa);
        var jost60 = LegibilityStyleSpec.ComputeOpticalStrokeWidth(60f, LyricFontId.Jost);

        bodoni60.Should().BeApproximately(60f * 0.036f, 0.01f);
        jost60.Should().BeApproximately(60f * 0.048f, 0.01f);
        bodoni60.Should().BeLessThan(jost60);

        // Min floor 1.8px, max ceiling 5.0px, and SystemUi = 0 (Messages uses bubble cards)
        LegibilityStyleSpec.ComputeOpticalStrokeWidth(24f, LyricFontId.Jost).Should().Be(1.8f);
        LegibilityStyleSpec.ComputeOpticalStrokeWidth(160f, LyricFontId.Jost).Should().Be(5.0f);
        LegibilityStyleSpec.ComputeOpticalStrokeWidth(48f, LyricFontId.SystemUi).Should().Be(0f);
    }

    [Fact]
    public void LegibilityPipeline_RendersAllFourLayersCleanlyAndCachesShadowBuckets()
    {
        using var fonts = new FontAssetRegistry();
        using var pipeline = new LegibilityPipeline();

        var info = new SKImageInfo(480, 180, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var font = fonts.GetOrCreateFont(LyricFontId.Jost, 48f);
        var bounds = new SKRect(80, 50, 400, 130);

        var save = pipeline.BeginGlobalOpacityLayer(canvas, 0.90f);
        pipeline.DrawAmbientScrim(canvas, bounds, 48f, AlbumAccentPalette.Default.ScrimTint, ScrimIntensity.Balanced, highContrastScrim: false);
        pipeline.DrawLegibleText(
            canvas,
            "Neon Horizon",
            240f,
            106f,
            SKTextAlign.Center,
            font,
            LyricFontId.Jost,
            AlbumAccentPalette.Default.AccentColor1,
            opacity: 1.0f,
            activeGlowColor: AlbumAccentPalette.Default.AccentColor2);
        LegibilityPipeline.EndGlobalOpacityLayer(canvas, save);

        pipeline.CachedShadowBucketCount.Should().Be(1);

        // Repeat with same bucket -> must reuse cached filters without growing count
        pipeline.DrawLegibleText(
            canvas,
            "Second Pass",
            240f,
            106f,
            SKTextAlign.Center,
            font,
            LyricFontId.Jost,
            AlbumAccentPalette.Default.AccentColor1);
        pipeline.CachedShadowBucketCount.Should().Be(1);

        // Also exercise HighContrastScrim branch cleanly
        pipeline.DrawAmbientScrim(canvas, bounds, 48f, AlbumAccentPalette.Default.ScrimTint, ScrimIntensity.Off, highContrastScrim: true);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        bitmap.GetPixel(240, 90).Alpha.Should().BeGreaterThan(100);
    }
}
