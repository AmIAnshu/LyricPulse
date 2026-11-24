using AwesomeAssertions;
using LyricPulse.Core;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Core.Visuals;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LyricPulse.Tests;

public sealed class CoreInfrastructureTests
{
    [Fact]
    public async Task AddLyricPulseCore_RegistersAndResolvesAllCoreServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLyricPulseCore();

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITitleNormalizer>().Should().BeOfType<TitleNormalizer>();
        provider.GetRequiredService<ILyricsParser>().Should().BeOfType<LyricsParser>();
        provider.GetRequiredService<ISettingsStore>().Should().NotBeNull();
        provider.GetRequiredService<ITrackOffsetStore>().Should().NotBeNull();
        provider.GetRequiredService<TimingOffsetService>().Should().NotBeNull();
        provider.GetRequiredService<DemoSongProvider>().Should().NotBeNull();
        provider.GetRequiredService<IWordVisualMatcher>().Should().BeOfType<StarterLexiconVisualMatcher>();
        provider.GetRequiredService<ILyricsCache>().Should().BeOfType<DiskLyricsCache>();
        provider.GetRequiredService<ILyricsOrchestrator>().Should().BeOfType<LyricsOrchestrator>();
        provider.GetRequiredService<IMediaSessionProvider>().Should().NotBeNull();
        provider.GetRequiredService<OverlayRuntimeController>().Should().NotBeNull();
        provider.GetServices<ILyricsProvider>().Should().HaveCount(2);
    }

    [Theory]
    [InlineData(AnchorZone.TopCenter, 0.50, 0.18)]
    [InlineData(AnchorZone.MiddleCenter, 0.50, 0.50)]
    [InlineData(AnchorZone.BottomThird, 0.50, 0.76)]
    [InlineData(AnchorZone.BottomLeft, 0.22, 0.80)]
    [InlineData(AnchorZone.BottomRight, 0.78, 0.80)]
    public void OverlayBoundsConfig_FromAnchorZone_ReturnsExpectedNormalizedCoordinates(
        AnchorZone zone,
        double expectedX,
        double expectedY)
    {
        var cfg = OverlayBoundsConfig.FromAnchorZone(zone, safeMarginPx: 48);
        cfg.AnchorZone.Should().Be(zone);
        cfg.AnchorX.Should().Be(expectedX);
        cfg.AnchorY.Should().Be(expectedY);
        cfg.SafeMarginPx.Should().Be(48);
    }

    [Fact]
    public void RgbColor_TryParseHexAndToArgbUInt32_WorkFor6And8DigitHex()
    {
        RgbColor.TryParseHex("#5EEAD4", out var c6).Should().BeTrue();
        c6.Should().Be(new RgbColor(0x5E, 0xEA, 0xD4, 0xFF));
        c6.ToArgbUInt32().Should().Be(0xFF5EEAD4u);

        RgbColor.TryParseHex("80112233", out var c8).Should().BeTrue();
        c8.Should().Be(new RgbColor(0x11, 0x22, 0x33, 0x80));

        RgbColor.TryParseHex("not-a-color", out _).Should().BeFalse();
        RgbColor.TryParseHex("", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("galaxies")]
    [InlineData("wolves")]
    [InlineData("sparkling")]
    [InlineData("whispered")]
    [InlineData("flashes")]
    [InlineData("oceans")]
    public async Task StarterLexiconVisualMatcher_CoversSuffixStemRulesAndFluentOnlyMode(string word)
    {
        using var matcher = new StarterLexiconVisualMatcher();
        await matcher.InitializeAsync();

        var match = matcher.MatchWord(word, VisualPreference.FluentIconsOnly);
        match.Should().NotBeNull($"'{word}' should match via suffix stemming");
        match!.Kind.Should().Be(VisualKind.FluentIcon);
    }

    [Fact]
    public void LyricsParser_HandlesCjkProportionalTimingAndEmptyPayloads()
    {
        var normalizer = new TitleNormalizer();
        var parser = new LyricsParser();
        var track = normalizer.CreateIdentity("夜空", "歌手", "专辑", TimeSpan.FromSeconds(30));

        var cjkDoc = parser.ParseLrc(track, "[00:01.00]星光夜空\n[00:05.00]梦境", LyricsSourceKind.NetEase, "NetEase", false);
        cjkDoc.Lines.Should().HaveCount(2);
        cjkDoc.Lines[0].Words.Select(w => w.Text).Should().Equal("星", "光", "夜", "空");

        var emptyDoc = parser.ParseLrc(track, "", LyricsSourceKind.LrcLib, "LRCLIB", false);
        emptyDoc.TimingKind.Should().Be(TimingKind.None);
        emptyDoc.Lines.Should().BeEmpty();

        var yrcFallback = parser.ParseYrc(track, "[00:02.00]Fallback LRC line", LyricsSourceKind.NetEase, "NetEase", false);
        yrcFallback.Lines.Should().HaveCount(1);
    }
}
