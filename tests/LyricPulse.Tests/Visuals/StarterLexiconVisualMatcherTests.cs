using AwesomeAssertions;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Visuals;
using Xunit;

namespace LyricPulse.Tests.Visuals;

public sealed class StarterLexiconVisualMatcherTests
{
    [Theory]
    [InlineData("the")]
    [InlineData("and")]
    [InlineData("you")]
    [InlineData("yeah")]
    [InlineData("ooh")]
    [InlineData("gonna")]
    [InlineData("a")]
    [InlineData("123")]
    public async Task MatchWord_RejectsStopwordsAndShortOrNumericTokens(string token)
    {
        using var matcher = new StarterLexiconVisualMatcher();
        await matcher.InitializeAsync();

        matcher.MatchWord(token).Should().BeNull();
    }

    [Theory]
    [InlineData("star", "ic_fluent_star_24_filled", "⭐")]
    [InlineData("fire", "ic_fluent_fire_24_filled", "🔥")]
    [InlineData("ocean", "ic_fluent_water_24_filled", "🌊")]
    [InlineData("night", "ic_fluent_weather_moon_24_filled", "🌙")]
    [InlineData("heart", "ic_fluent_heart_24_filled", "❤️")]
    [InlineData("code", "ic_fluent_code_24_filled", "💻")]
    [InlineData("rain", "ic_fluent_weather_rain_24_filled", "🌧️")]
    [InlineData("city", "ic_fluent_city_24_filled", "🏙️")]
    [InlineData("flight", "ic_fluent_airplane_24_filled", "✈️")]
    [InlineData("time", "ic_fluent_clock_24_filled", "⏱️")]
    [InlineData("light", "ic_fluent_lightbulb_24_filled", "💡")]
    [InlineData("dream", "ic_fluent_sparkle_24_filled", "✨")]
    public async Task MatchWord_ResolvesAll12NeonHorizonKeywordsToExactFluentIconsAndEmojis(
        string keyword,
        string expectedFluentIcon,
        string expectedEmoji)
    {
        using var matcher = new StarterLexiconVisualMatcher();
        await matcher.InitializeAsync();

        matcher.Mode.Should().Be(VisualMatcherMode.StarterLexicon);
        matcher.IsReady.Should().BeTrue();

        var fluentMatch = matcher.MatchWord(keyword, VisualPreference.Balanced);
        fluentMatch.Should().NotBeNull();
        fluentMatch!.Kind.Should().Be(VisualKind.FluentIcon);
        fluentMatch.IconName.Should().Be(expectedFluentIcon);
        fluentMatch.EmojiFallback.Should().Be(expectedEmoji);
        fluentMatch.IsExactMatch.Should().BeTrue();

        var emojiMatch = matcher.MatchWord(keyword, VisualPreference.EmojiOnly);
        emojiMatch.Should().NotBeNull();
        emojiMatch!.Kind.Should().Be(VisualKind.Emoji);
        emojiMatch.Value.Should().Be(expectedEmoji);
    }

    [Theory]
    [InlineData("Stars!")]
    [InlineData("flames,")]
    [InlineData("heart's")]
    [InlineData("flew")]
    [InlineData("cried")]
    [InlineData("shining")]
    [InlineData("memories")]
    [InlineData("sang")]
    public async Task MatchWord_HandlesPunctuationContractionsAndLemmatization(string word)
    {
        using var matcher = new StarterLexiconVisualMatcher();
        await matcher.InitializeAsync();

        var match = matcher.MatchWord(word);
        match.Should().NotBeNull($"'{word}' should lemmatize to a known starter concept");
    }

    [Fact]
    public async Task AnnotateDocument_DeduplicatesAdjacentIdenticalVisualConcepts()
    {
        using var matcher = new StarterLexiconVisualMatcher();
        await matcher.InitializeAsync();

        var normalizer = new TitleNormalizer();
        var parser = new LyricsParser();
        var track = normalizer.CreateIdentity("Fire Song", "Artist", "Album", TimeSpan.FromSeconds(30));
        var doc = parser.ParseLrc(track, "[00:01.00]fire flame star", LyricsSourceKind.Demo, "Demo", false);

        var annotated = matcher.AnnotateDocument(doc);

        // "fire" and "flame" map to the exact same concept -> second adjacent duplicate suppressed
        annotated.Lines[0].Words[0].Visual.Should().NotBeNull();
        annotated.Lines[0].Words[1].Visual.Should().BeNull();
        annotated.Lines[0].Words[2].Visual.Should().NotBeNull();
    }
}
