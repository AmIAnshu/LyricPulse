using System.Collections.Immutable;
using System.Net;
using System.Text;
using AwesomeAssertions;
using LyricPulse.Core;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Color;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Media;
using LyricPulse.Core.Models;
using LyricPulse.Core.Visuals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LyricPulse.Tests;

public sealed class Phase3QaPolishRegressionTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "LyricPulseTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task DiskLyricsCache_CjkYrcRoundTrip_PreservesZeroSpacesBetweenCjkCharacters()
    {
        var normalizer = new TitleNormalizer();
        var parser = new LyricsParser();
        var cache = new DiskLyricsCache(Path.Combine(_tempDir, "cjk-cache"), parser);
        var track = normalizer.CreateIdentity("夜空", "歌手", "专辑", TimeSpan.FromSeconds(30));

        const string yrc = "[4000,1500](4000,500,0)星(4500,500,0)光(5000,500,0)夜";
        var parsed = parser.ParseYrc(track, yrc, LyricsSourceKind.NetEase, "NetEase", isFromCache: false);
        parsed.Lines[0].RawText.Should().Be("星光夜");

        await cache.SetAsync(track, new LyricsResult(LyricsStatus.Synced, parsed));
        var rehydrated = await cache.TryGetAsync(track);

        rehydrated.Should().NotBeNull();
        rehydrated!.Status.Should().Be(LyricsStatus.Synced);
        rehydrated.Document!.TimingKind.Should().Be(TimingKind.WordNative);
        rehydrated.Document.Lines[0].RawText.Should().Be("星光夜");
        rehydrated.Document.Lines[0].Words.Select(w => w.Text).Should().Equal("星", "光", "夜");
        rehydrated.Document.Lines[0].Words.Should().AllSatisfy(w => w.IsSpaceSeparated.Should().BeFalse());
    }

    [Fact]
    public async Task MediaSessionCoordinator_ClearsStaleAlbumArtOnTrackChangeAndSuppressesChangingFlicker()
    {
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var coordinator = new MediaSessionCoordinator(new TitleNormalizer(), utcNowProvider: () => utc);

        var trackEvents = new List<TrackIdentity?>();
        coordinator.TrackChanged += (_, t) => trackEvents.Add(t);

        var spotifyAumid = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify";
        var song1 = new SessionCandidate(
            spotifyAumid,
            MediaPlayerKind.Spotify,
            MediaItemKind.Music,
            MediaPlaybackState.Playing,
            "First Song",
            "Artist One",
            "Album One",
            TimeSpan.FromSeconds(10),
            TimeSpan.Zero,
            TimeSpan.FromMinutes(3),
            1.0,
            utc,
            true);

        coordinator.ProcessCandidates([song1]);
        await using (var artStream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]))
        {
            var art = await coordinator.ReadAndDeduplicateThumbnailAsync(artStream, 8);
            art.Should().NotBeNull();
        }

        // Transient Changing state with empty title within 1500ms hysteresis must NOT emit TrackChanged(null).
        utc = utc.AddMilliseconds(400);
        var changingFlicker = song1 with
        {
            PlaybackState = MediaPlaybackState.Changing,
            Title = "",
            Artist = "",
            LastUpdatedUtc = utc
        };
        var flickerSnapshot = coordinator.ProcessCandidates([changingFlicker]);
        flickerSnapshot.Should().NotBeNull();
        flickerSnapshot!.Track.Should().NotBeNull();
        flickerSnapshot.Track!.RawTitle.Should().Be("First Song");
        trackEvents.Should().HaveCount(1);

        // Actual switch to Song 2 must clear stale AlbumArtHash immediately on the new snapshot.
        utc = utc.AddMilliseconds(300);
        var song2 = song1 with
        {
            PlaybackState = MediaPlaybackState.Playing,
            Title = "Second Song",
            Artist = "Artist Two",
            ReportedPosition = TimeSpan.Zero,
            LastUpdatedUtc = utc
        };
        var song2Snapshot = coordinator.ProcessCandidates([song2]);
        song2Snapshot.Should().NotBeNull();
        song2Snapshot!.AlbumArtHash.Should().BeNull("new track must never advertise previous track's AlbumArtHash");
        coordinator.CachedAlbumArt.Should().BeNull();
        trackEvents.Should().HaveCount(2);
        trackEvents[1]!.RawTitle.Should().Be("Second Song");
    }

    [Fact]
    public async Task TimingOffsetService_ConcurrentRapidNudges_NeverDropUpdates()
    {
        using var store = new TrackOffsetStore(Path.Combine(_tempDir, "concurrent-offsets.json"));
        using var service = new TimingOffsetService(store);

        await service.OnTrackChangedAsync("rapid-track", enablePerTrackSavedOffsets: true);

        // Fire 10 rapid concurrent -500ms nudges (simulating rapid global hotkey presses).
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => service.NudgeAsync(TimeSpan.FromMilliseconds(-500), enablePerTrackSavedOffsets: true))
            .ToArray();
        await Task.WhenAll(tasks);

        service.Current.Offset.Should().Be(TimeSpan.FromMilliseconds(-5000));
        (await store.TryGetOffsetAsync("rapid-track")).Should().Be(TimeSpan.FromMilliseconds(-5000));
    }

    [Fact]
    public async Task ServiceCollectionExtensions_RegistersTrueSingletonProvidersBackedByHttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLyricPulseCore();

        await using var sp = services.BuildServiceProvider();

        var lrclibDirect = sp.GetRequiredService<LrcLibLyricsProvider>();
        var neteaseDirect = sp.GetRequiredService<NetEaseLyricsProvider>();
        var viaInterface = sp.GetServices<ILyricsProvider>().ToList();

        viaInterface.Should().Contain(p => ReferenceEquals(p, lrclibDirect));
        viaInterface.Should().Contain(p => ReferenceEquals(p, neteaseDirect));
        ReferenceEquals(sp.GetRequiredService<LrcLibLyricsProvider>(), lrclibDirect).Should().BeTrue();
        ReferenceEquals(sp.GetRequiredService<NetEaseLyricsProvider>(), neteaseDirect).Should().BeTrue();
    }

    [Theory]
    [InlineData("living")]
    [InlineData("life")]
    [InlineData("alive")]
    public async Task StarterLexiconVisualMatcher_MapsLivingAndLifeToPositiveConceptNotSkull(string word)
    {
        using var matcher = new StarterLexiconVisualMatcher();
        await matcher.InitializeAsync();

        var match = matcher.MatchWord(word, VisualPreference.Balanced);
        match.Should().NotBeNull();
        match!.ConceptName.Should().NotBe("skull");
        match.IconName.Should().NotContain("warning");
        match.EmojiFallback.Should().NotBe("💀");
    }

    [Fact]
    public async Task NetEaseLyricsProvider_MapsNoLyricToInstrumentalAndUncollectedToNotFound()
    {
        var mode = "nolyric";
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Equals("/api/search/get", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"code":200,"result":{"songCount":1,"songs":[{"id":77,"name":"Ambient Drift","duration":200000,"artists":[{"id":1,"name":"Eno"}]}]}}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            var json = mode == "nolyric"
                ? """{"code":200,"nolyric":true}"""
                : """{"code":200,"uncollected":true}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var normalizer = new TitleNormalizer();
        var parser = new LyricsParser();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://music.163.com") };
        using var provider = new NetEaseLyricsProvider(client, parser, normalizer, NullLogger<NetEaseLyricsProvider>.Instance, delayAsync: (_, _) => Task.CompletedTask);
        var track = normalizer.CreateIdentity("Ambient Drift", "Eno", "", TimeSpan.FromSeconds(200));

        (await provider.FetchAsync(track)).Status.Should().Be(LyricsStatus.Instrumental);

        mode = "uncollected";
        (await provider.FetchAsync(track)).Status.Should().Be(LyricsStatus.NotFound);
    }

    [Fact]
    public void OklabPaletteExtractor_GuardsAgainst32BitDimensionOverflow()
    {
        var tiny = new byte[16];
        var palette = OklabPaletteExtractor.ExtractFromRgba(tiny, width: 100_000, height: 100_000, seedBytes: tiny);
        palette.Should().Be(AlbumAccentPalette.Default);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(fn(request));
    }
}
