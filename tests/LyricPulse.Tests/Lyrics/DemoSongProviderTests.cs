using AwesomeAssertions;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Visuals;
using Xunit;

namespace LyricPulse.Tests.Lyrics;

public sealed class DemoSongProviderTests
{
    [Fact]
    public async Task LoadNeonHorizon_Has12Lines36SecondsCuratedPaletteAndAll12Keywords()
    {
        var parser = new LyricsParser();
        var normalizer = new TitleNormalizer();
        var demoProvider = new DemoSongProvider(parser, normalizer);

        var doc = demoProvider.GetDemoDocument();

        doc.Track.RawTitle.Should().Be("Neon Horizon");
        doc.Track.RawArtist.Should().Be("LyricPulse Studio");
        doc.Track.Duration.Should().Be(TimeSpan.FromSeconds(36));
        doc.SourceKind.Should().Be(LyricsSourceKind.Demo);
        doc.TimingKind.Should().Be(TimingKind.WordNative);
        doc.Lines.Should().HaveCount(12);

        DemoSongProvider.DemoPalette.AccentColor1.ToHex().Should().Be("#5EEAD4");
        DemoSongProvider.DemoPalette.AccentColor2.ToHex().Should().Be("#818CF8");
        DemoSongProvider.DemoPalette.AccentColor3.ToHex().Should().Be("#94A3B8");
        DemoSongProvider.DemoPalette.ScrimTint.ToHex().Should().Be("#0F0F23");

        foreach (var style in Enum.GetValues<OverlayStyleId>())
        {
            DemoSongProvider.GetGalleryPreviewPhaseOffset(style).Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
            DemoSongProvider.GetGalleryPreviewPhaseOffset(style).Should().BeLessThan(TimeSpan.FromSeconds(36));
        }

        using var matcher = new StarterLexiconVisualMatcher();
        await matcher.InitializeAsync();
        var annotated = matcher.AnnotateDocument(doc);

        var expectedKeywords = new[]
        {
            "star", "fire", "ocean", "night", "heart", "code",
            "rain", "city", "flight", "time", "light", "dream"
        };

        for (var i = 0; i < expectedKeywords.Length; i++)
        {
            var matchedWord = annotated.Lines[i].Words
                .FirstOrDefault(w => w.Text.Equals(expectedKeywords[i], StringComparison.OrdinalIgnoreCase));
            matchedWord.Should().NotBeNull($"line {i + 1} must contain keyword '{expectedKeywords[i]}'");
            matchedWord!.Visual.Should().NotBeNull($"keyword '{expectedKeywords[i]}' must match an offline visual");
        }
    }
}
