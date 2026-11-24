using AwesomeAssertions;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using Xunit;

namespace LyricPulse.Tests.Lyrics;

public sealed class LyricsParserTests
{
    private readonly LyricsParser _sut = new();
    private readonly TrackIdentity _track = new TitleNormalizer()
        .CreateIdentity("Test Song", "Test Artist", "Album", TimeSpan.FromSeconds(60));

    [Fact]
    public void ParseLrc_ExpandsMultiTagChorusAndSortsAscending()
    {
        const string lrc = """
            [ti:Test Song]
            [00:10.00][00:30.50]Chorus line repeats
            [00:20.000]Middle verse line
            [00:05:50]Opening line
            """;

        var doc = _sut.ParseLrc(_track, lrc, LyricsSourceKind.LrcLib, "LRCLIB", isFromCache: false);

        doc.TimingKind.Should().Be(TimingKind.WordInterpolated);
        doc.Lines.Should().HaveCount(4);
        doc.Lines.Select(l => l.StartTime).Should().BeInAscendingOrder();
        doc.Lines[0].RawText.Should().Be("Opening line");
        doc.Lines[0].StartTime.Should().Be(TimeSpan.FromMilliseconds(5_500));
        doc.Lines[1].RawText.Should().Be("Chorus line repeats");
        doc.Lines[2].RawText.Should().Be("Middle verse line");
        doc.Lines[3].RawText.Should().Be("Chorus line repeats");
        doc.Lines[3].StartTime.Should().Be(TimeSpan.FromMilliseconds(30_500));

        AssertStrictMonotonicInvariants(doc);
    }

    [Fact]
    public void ParseLrc_AppliesHeaderOffsetTagClampedAtZero()
    {
        const string lrc = """
            [offset:+500]
            [00:00.20]Very early line
            [00:05.00]Second line
            """;

        var doc = _sut.ParseLrc(_track, lrc, LyricsSourceKind.LrcLib, "LRCLIB", isFromCache: false);

        doc.EmbeddedLrcOffset.Should().Be(TimeSpan.FromMilliseconds(500));
        doc.Lines[0].StartTime.Should().Be(TimeSpan.Zero);
        doc.Lines[1].StartTime.Should().Be(TimeSpan.FromMilliseconds(4_500));
    }

    [Fact]
    public void ParseLrc_ParsesEnhancedWordTagsAsWordNative()
    {
        const string enhanced = """
            [00:00.20]<00:00.20>Chasing <00:00.65>a <00:00.85>silver <00:01.40>star <00:02.05>tonight
            [00:03.00]<00:03.00>Sparks <00:03.50>of <00:03.75>fire
            """;

        var doc = _sut.ParseLrc(_track, enhanced, LyricsSourceKind.Demo, "Demo", isFromCache: false);

        doc.TimingKind.Should().Be(TimingKind.WordNative);
        doc.Lines.Should().HaveCount(2);
        doc.Lines[0].Words.Should().HaveCount(5);
        doc.Lines[0].Words[0].Text.Should().Be("Chasing");
        doc.Lines[0].Words[0].StartTime.Should().Be(TimeSpan.FromMilliseconds(200));
        doc.Lines[0].Words[0].EndTime.Should().Be(TimeSpan.FromMilliseconds(650));
        doc.Lines[0].Words[3].Text.Should().Be("star");
        doc.Lines[0].Words[3].StartTime.Should().Be(TimeSpan.FromMilliseconds(1400));

        AssertStrictMonotonicInvariants(doc);
    }

    [Fact]
    public void ParseLrc_CapsInstrumentalGapAndTagsInstrumentalMarkers()
    {
        const string lrc = """
            [00:01.00]Short line before solo
            [00:25.00]♪
            [00:35.00]Back from guitar solo
            """;

        var doc = _sut.ParseLrc(_track, lrc, LyricsSourceKind.LrcLib, "LRCLIB", isFromCache: false);

        doc.Lines.Should().HaveCount(3);
        // Line 0 should NOT stretch across the 24s solo!
        (doc.Lines[0].EndTime - doc.Lines[0].StartTime).Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(9));
        doc.Lines[1].IsInstrumentalBreak.Should().BeTrue();
        doc.Lines[1].Words.Should().BeEmpty();
        AssertStrictMonotonicInvariants(doc);
    }

    [Fact]
    public void ParseYrc_SkipsJsonPreambleMergesLatinSyllablesPreservesCjkAndAlignsBilingual()
    {
        const string yrc = """
            {"t":0,"c":[{"tx":"作词: Demo"}]}
            [1000,2000](1000,150,0)to(1150,250,0)night (1400,600,0)stars
            [4000,1500](4000,500,0)星(4500,500,0)光(5000,500,0)夜
            """;

        const string tlyric = """
            [00:01.10]今晚星辰
            [00:04.05]Starlight night
            """;

        const string romalrc = """
            [00:04.00]xing guang ye
            """;

        var doc = _sut.ParseYrc(_track, yrc, LyricsSourceKind.NetEase, "NetEase", isFromCache: false, tlyric, romalrc);

        doc.TimingKind.Should().Be(TimingKind.WordNative);
        doc.Lines.Should().HaveCount(2);

        // Latin syllables "to" + "night" merged into "tonight"
        doc.Lines[0].Words.Select(w => w.Text).Should().Equal("tonight", "stars");
        doc.Lines[0].Words[0].StartTime.Should().Be(TimeSpan.FromMilliseconds(1000));
        doc.Lines[0].Words[0].EndTime.Should().Be(TimeSpan.FromMilliseconds(1400));
        doc.Lines[0].TranslationText.Should().Be("今晚星辰");

        // CJK characters remain standalone units
        doc.Lines[1].Words.Select(w => w.Text).Should().Equal("星", "光", "夜");
        doc.Lines[1].TranslationText.Should().Be("Starlight night");
        doc.Lines[1].RomanizedText.Should().Be("xing guang ye");

        AssertStrictMonotonicInvariants(doc);
    }

    [Fact]
    public void CreatePlain_CreatesUnsyncedDocument()
    {
        var doc = _sut.CreatePlain(_track, "Line one\nLine two\n\nLine three", LyricsSourceKind.LrcLib, "LRCLIB", false);

        doc.TimingKind.Should().Be(TimingKind.PlainUnsynced);
        doc.Lines.Should().HaveCount(3);
        doc.Lines[0].RawText.Should().Be("Line one");
    }

    private static void AssertStrictMonotonicInvariants(LyricsDocument doc)
    {
        for (var i = 0; i < doc.Lines.Length; i++)
        {
            var line = doc.Lines[i];
            line.EndTime.Should().BeGreaterThanOrEqualTo(line.StartTime);

            if (i > 0)
            {
                line.StartTime.Should().BeGreaterThanOrEqualTo(doc.Lines[i - 1].StartTime);
            }

            for (var w = 0; w < line.Words.Length; w++)
            {
                var word = line.Words[w];
                word.EndTime.Should().BeGreaterThan(word.StartTime);

                if (w > 0)
                {
                    word.StartTime.Should().BeGreaterThanOrEqualTo(line.Words[w - 1].EndTime);
                }
            }
        }
    }
}
