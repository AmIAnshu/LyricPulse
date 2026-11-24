using System.Net;
using System.Text;
using AwesomeAssertions;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LyricPulse.Tests.Lyrics;

public sealed class LyricsProvidersAndOrchestratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "LyricPulseTests", Guid.NewGuid().ToString("N"));
    private readonly TitleNormalizer _normalizer = new();
    private readonly LyricsParser _parser = new();

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void CandidateScorer_ScoresAndRejectsMismatchesDeterministically()
    {
        var track = _normalizer.CreateIdentity("Blinding Lights", "The Weeknd", "After Hours", TimeSpan.FromSeconds(200));

        var perfectWordSynced = CandidateScorer.Score(
            track,
            candidateTitle: "Blinding Lights",
            candidateArtist: "The Weeknd",
            candidateDurationSec: 200.5,
            hasWordSync: true,
            hasLineSync: true,
            _normalizer);
        perfectWordSynced.Should().Be(115);

        var wrongSong = CandidateScorer.Score(
            track,
            candidateTitle: "Completely Different Song",
            candidateArtist: "Other Band",
            candidateDurationSec: 320,
            hasWordSync: true,
            hasLineSync: true,
            _normalizer);
        wrongSong.Should().BeLessThan(CandidateScorer.MinimumAcceptScore);

        var wrongDurationSameName = CandidateScorer.Score(
            track,
            candidateTitle: "Blinding Lights",
            candidateArtist: "Random Cover",
            candidateDurationSec: 310,
            hasWordSync: false,
            hasLineSync: true,
            _normalizer);
        wrongDurationSameName.Should().BeLessThan(CandidateScorer.MinimumAcceptScore);
    }

    [Fact]
    public async Task LrcLibLyricsProvider_FallsThroughExactToNormalizedToSearchWithUserAgent()
    {
        var seenUris = new List<string>();
        string? seenUserAgent = null;

        var handler = new StubHttpMessageHandler(req =>
        {
            seenUris.Add(req.RequestUri!.PathAndQuery);
            seenUserAgent = req.Headers.UserAgent.ToString();

            if (req.RequestUri.AbsolutePath.Equals("/api/get", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (req.RequestUri.AbsolutePath.Equals("/api/search", StringComparison.OrdinalIgnoreCase))
            {
                const string json = """
                    [
                      {
                        "id": 101,
                        "trackName": "Levitating",
                        "artistName": "Dua Lipa",
                        "albumName": "Future Nostalgia",
                        "duration": 203.0,
                        "instrumental": false,
                        "syncedLyrics": "[00:01.00]If you wanna run away with me\n[00:04.00]I know a galaxy"
                      }
                    ]
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://lrclib.net") };
        using var provider = new LrcLibLyricsProvider(
            httpClient,
            _parser,
            _normalizer,
            NullLogger<LrcLibLyricsProvider>.Instance,
            delayAsync: (_, _) => Task.CompletedTask);

        var track = _normalizer.CreateIdentity(
            "Levitating (feat. DaBaby) - Radio Edit",
            "Dua Lipa, DaBaby",
            "Future Nostalgia",
            TimeSpan.FromSeconds(203));

        var result = await provider.FetchAsync(track);

        result.Status.Should().Be(LyricsStatus.Synced);
        result.Document.Should().NotBeNull();
        result.Document!.SourceKind.Should().Be(LyricsSourceKind.LrcLib);
        result.Document.Lines.Should().HaveCount(2);
        seenUserAgent.Should().Contain("LyricPulse/");
        seenUris.Should().Contain(u => u.Contains("/api/get?", StringComparison.Ordinal) && u.Contains("duration=203", StringComparison.Ordinal));
        seenUris.Should().Contain(u => u.Contains("/api/search?", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LrcLibLyricsProvider_Honors429RetryAfterAndCooldown()
    {
        var callCount = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                var tooMany = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                tooMany.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                return tooMany;
            }

            const string json = """
                {
                  "id": 7,
                  "trackName": "Starlight",
                  "artistName": "Muse",
                  "duration": 240.0,
                  "instrumental": false,
                  "syncedLyrics": "[00:02.00]Far away"
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        TimeSpan? observedDelay = null;
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://lrclib.net") };
        using var provider = new LrcLibLyricsProvider(
            httpClient,
            _parser,
            _normalizer,
            NullLogger<LrcLibLyricsProvider>.Instance,
            delayAsync: (delay, _) =>
            {
                observedDelay = delay;
                return Task.CompletedTask;
            });

        var track = _normalizer.CreateIdentity("Starlight", "Muse", "Black Holes", TimeSpan.FromSeconds(240));
        var result = await provider.FetchAsync(track);

        result.Status.Should().Be(LyricsStatus.Synced);
        observedDelay.Should().NotBeNull();
        observedDelay!.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task NetEaseLyricsProvider_PrefersYrcAndPreservesBilingualTracks()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Equals("/api/search/get", StringComparison.OrdinalIgnoreCase))
            {
                const string searchJson = """
                    {
                      "code": 200,
                      "result": {
                        "songCount": 1,
                        "songs": [
                          {
                            "id": 55501,
                            "name": "Nightfall",
                            "duration": 210000,
                            "artists": [{ "id": 1, "name": "Hoshino" }],
                            "album": { "id": 2, "name": "Sky" }
                          }
                        ]
                      }
                    }
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(searchJson, Encoding.UTF8, "application/json")
                };
            }

            const string lyricJson = """
                {
                  "code": 200,
                  "yrc": { "version": 1, "lyric": "[1000,1200](1000,600,0)Star(1600,600,0)light" },
                  "lrc": { "version": 1, "lyric": "[00:01.00]Starlight" },
                  "tlyric": { "version": 1, "lyric": "[00:01.00]星光" },
                  "romalrc": { "version": 1, "lyric": "[00:01.00]hoshi" }
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(lyricJson, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://music.163.com") };
        using var provider = new NetEaseLyricsProvider(
            httpClient,
            _parser,
            _normalizer,
            NullLogger<NetEaseLyricsProvider>.Instance,
            delayAsync: (_, _) => Task.CompletedTask);

        var track = _normalizer.CreateIdentity("Nightfall", "Hoshino", "Sky", TimeSpan.FromSeconds(210));
        var result = await provider.FetchAsync(track);

        result.Status.Should().Be(LyricsStatus.Synced);
        result.Document.Should().NotBeNull();
        result.Document!.TimingKind.Should().Be(TimingKind.WordNative);
        result.Document.SourceKind.Should().Be(LyricsSourceKind.NetEase);
        result.Document.Lines[0].TranslationText.Should().Be("星光");
        result.Document.Lines[0].RomanizedText.Should().Be("hoshi");
    }

    [Fact]
    public async Task DiskLyricsCache_RoundTripsAtomicallyAndEnforcesTieredTtl()
    {
        var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var cacheDir = Path.Combine(_tempDir, "cache");
        var cache = new DiskLyricsCache(cacheDir, _parser, () => now);
        var track = _normalizer.CreateIdentity("Neon Horizon", "LyricPulse Studio", "Demo", TimeSpan.FromSeconds(36));

        // 1. Synced lyrics cached for 90 days
        var doc = _parser.ParseLrc(track, "[00:00.20]<00:00.20>Chasing <00:00.85>star", LyricsSourceKind.LrcLib, "LRCLIB", false);
        await cache.SetAsync(track, new LyricsResult(LyricsStatus.Synced, doc));

        var hit = await cache.TryGetAsync(track);
        hit.Should().NotBeNull();
        hit!.Status.Should().Be(LyricsStatus.Synced);
        hit.Document!.IsFromCache.Should().BeTrue();
        hit.Document.TimingKind.Should().Be(TimingKind.WordNative);

        // Advance 91 days -> expired
        now = now.AddDays(91);
        (await cache.TryGetAsync(track)).Should().BeNull();

        // 2. NotFound negative cache expires after 24 hours
        var missingTrack = _normalizer.CreateIdentity("Obscure Track", "Unknown", "", TimeSpan.FromSeconds(120));
        await cache.SetAsync(missingTrack, new LyricsResult(LyricsStatus.NotFound, null));

        var negHit = await cache.TryGetAsync(missingTrack);
        negHit.Should().NotBeNull();
        negHit!.Status.Should().Be(LyricsStatus.NotFound);

        now = now.AddHours(25);
        (await cache.TryGetAsync(missingTrack)).Should().BeNull();
    }

    [Fact]
    public async Task LocalLrcSidecarProvider_LoadsMatchingLocalFile()
    {
        var sidecarDir = Path.Combine(_tempDir, "custom-lyrics");
        Directory.CreateDirectory(sidecarDir);
        await File.WriteAllTextAsync(
            Path.Combine(sidecarDir, "Dua Lipa - Levitating.lrc"),
            "[00:01.00]Custom local synced lyric");

        var sidecar = new LocalLrcSidecarProvider(sidecarDir, _parser);
        var track = _normalizer.CreateIdentity("Levitating", "Dua Lipa", "Future Nostalgia", TimeSpan.FromSeconds(203));

        var result = await sidecar.FetchAsync(track);
        result.Status.Should().Be(LyricsStatus.Synced);
        result.Document!.SourceKind.Should().Be(LyricsSourceKind.LocalSidecar);
        result.Document.Lines[0].RawText.Should().Be("Custom local synced lyric");
    }

    [Fact]
    public async Task LyricsOrchestrator_CoalescesConcurrentRequestsAndExecutesWaterfall()
    {
        var track = _normalizer.CreateIdentity("Coalesce Track", "Artist", "Album", TimeSpan.FromSeconds(180));
        var cache = new DiskLyricsCache(Path.Combine(_tempDir, "orch-cache"), _parser);
        var sidecar = new LocalLrcSidecarProvider(Path.Combine(_tempDir, "empty-sidecar"), _parser);

        var tcs = new TaskCompletionSource<LyricsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider1 = Substitute.For<ILyricsProvider>();
        provider1.Name.Returns("LRCLIB");
        provider1.SourceKind.Returns(LyricsSourceKind.LrcLib);
        provider1.PriorityOrder.Returns(1);
        provider1.FetchAsync(track, Arg.Any<CancellationToken>()).Returns(_ => tcs.Task);

        var provider2 = Substitute.For<ILyricsProvider>();
        provider2.Name.Returns("NetEase");
        provider2.SourceKind.Returns(LyricsSourceKind.NetEase);
        provider2.PriorityOrder.Returns(2);
        var netEaseDoc = _parser.ParseLrc(track, "[00:01.00]Fallback synced line", LyricsSourceKind.NetEase, "NetEase", false);
        provider2.FetchAsync(track, Arg.Any<CancellationToken>())
            .Returns(new LyricsResult(LyricsStatus.Synced, netEaseDoc));

        var orchestrator = new LyricsOrchestrator(
            sidecar,
            cache,
            [provider2, provider1],
            NullLogger<LyricsOrchestrator>.Instance);

        var taskA = orchestrator.GetLyricsAsync(track);
        var taskB = orchestrator.GetLyricsAsync(track);

        // Provider1 returns PlainOnly -> waterfall falls through to Provider2 (NetEase Synced!)
        var plainDoc = _parser.CreatePlain(track, "Plain fallback", LyricsSourceKind.LrcLib, "LRCLIB", false);
        tcs.SetResult(new LyricsResult(LyricsStatus.PlainOnly, plainDoc));

        var results = await Task.WhenAll(taskA, taskB);

        results[0].Status.Should().Be(LyricsStatus.Synced);
        results[1].Status.Should().Be(LyricsStatus.Synced);
        results[0].Document!.ProviderName.Should().Be("NetEase");

        // In-flight coalescing guarantees each provider was invoked exactly once
        await provider1.Received(1).FetchAsync(track, Arg.Any<CancellationToken>());
        await provider2.Received(1).FetchAsync(track, Arg.Any<CancellationToken>());

        // Subsequent call hits disk cache with zero provider calls
        var cachedResult = await orchestrator.GetLyricsAsync(track);
        cachedResult.Document!.IsFromCache.Should().BeTrue();
        await provider1.Received(1).FetchAsync(track, Arg.Any<CancellationToken>());
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
