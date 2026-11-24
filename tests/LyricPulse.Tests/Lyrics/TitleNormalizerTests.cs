using AwesomeAssertions;
using LyricPulse.Core.Lyrics;
using Xunit;

namespace LyricPulse.Tests.Lyrics;

public sealed class TitleNormalizerTests
{
    private readonly TitleNormalizer _sut = new();

    [Theory]
    [InlineData("Bohemian Rhapsody - Remastered 2011", "Bohemian Rhapsody")]
    [InlineData("Blinding Lights (Deluxe Edition) [Explicit]", "Blinding Lights")]
    [InlineData("All The Stars (with SZA) (From \"Black Panther\")", "All The Stars")]
    [InlineData("Cruel Summer (Live from TS | The Eras Tour)", "Cruel Summer")]
    [InlineData("Levitating (feat. DaBaby) - Radio Edit", "Levitating")]
    [InlineData("Dreams - 2004 Remaster", "Dreams")]
    [InlineData("Starboy feat. Daft Punk", "Starboy")]
    [InlineData("Song\u200B Name — 2024 Remaster", "Song Name")]
    [InlineData("Don’t Stop Me Now (2011 Remaster)", "Don't Stop Me Now")]
    [InlineData("(Explicit)", "(Explicit)")]
    [InlineData("Remix", "Remix")]
    public void NormalizeTitle_StripsMetadataAndPreservesSafetyGuard(string rawTitle, string expected)
    {
        _sut.NormalizeTitle(rawTitle).Should().Be(expected);
    }

    [Theory]
    [InlineData("Daft Punk, Pharrell Williams", "Daft Punk")]
    [InlineData("The Weeknd feat. Daft Punk", "The Weeknd")]
    [InlineData("Calvin Harris & Dua Lipa", "Calvin Harris")]
    [InlineData("Simon & Garfunkel", "Simon & Garfunkel")]
    [InlineData("Earth, Wind & Fire", "Earth, Wind & Fire")]
    [InlineData("Hall & Oates", "Hall & Oates")]
    [InlineData("Florence + The Machine", "Florence + The Machine")]
    [InlineData("AC/DC", "AC/DC")]
    public void ExtractPrimaryArtist_RespectsCanonicalBandWhitelist(string rawArtist, string expected)
    {
        _sut.ExtractPrimaryArtist(rawArtist).Should().Be(expected);
    }

    [Fact]
    public void CreateIdentity_BucketsDurationToTwoSecondIncrementsForStableCacheKeys()
    {
        var id1 = _sut.CreateIdentity("Levitating (feat. DaBaby) - Radio Edit", "Dua Lipa, DaBaby", "Future Nostalgia", TimeSpan.FromSeconds(203.1));
        var id2 = _sut.CreateIdentity("Levitating", "Dua Lipa", "Future Nostalgia", TimeSpan.FromSeconds(203.8));

        id1.NormalizedTitle.Should().Be("Levitating");
        id1.PrimaryArtist.Should().Be("Dua Lipa");
        id1.CacheKey.Should().Be(id2.CacheKey);
        id1.CacheKey.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
