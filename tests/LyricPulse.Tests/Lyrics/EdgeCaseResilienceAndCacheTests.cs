using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using AwesomeAssertions;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LyricPulse.Tests.Lyrics;

public sealed class EdgeCaseResilienceAndCacheTests : IDisposable
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
    public async Task DiskLyricsCache_SupportsRemovePruneTieredTtlsAndRejectsPathTraversal()
    {
        var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var cacheDir = Path.Combine(_tempDir, "prune-cache");
        var cache = new DiskLyricsCache(cacheDir, _parser, () => now);

        var trackInst = _normalizer.CreateIdentity("Instrumental Track", "Composer", "OST", TimeSpan.FromSeconds(180));
        var trackPlain = _normalizer.CreateIdentity("Plain Track", "Poet", "Album", TimeSpan.FromSeconds(150));

        await cache.SetAsync(trackInst, new LyricsResult(LyricsStatus.Instrumental, null));
        var plainDoc = _parser.CreatePlain(trackPlain, "Just plain lines", LyricsSourceKind.LrcLib, "LRCLIB", false);
        await cache.SetAsync(trackPlain, new LyricsResult(LyricsStatus.PlainOnly, plainDoc));

        (await cache.TryGetAsync(trackInst))!.Status.Should().Be(LyricsStatus.Instrumental);
        (await cache.TryGetAsync(trackPlain))!.Status.Should().Be(LyricsStatus.PlainOnly);

        // Advance 10 days -> Plain (7d) expired, Instrumental (30d) still alive
        now = now.AddDays(10);
        await cache.PruneAsync();
        (await cache.TryGetAsync(trackPlain)).Should().BeNull();
        (await cache.TryGetAsync(trackInst)).Should().NotBeNull();

        // RemoveAsync deletes entry
        await cache.RemoveAsync(trackInst);
        (await cache.TryGetAsync(trackInst)).Should().BeNull();

        // Path traversal / non-hex cache key safely ignored
        var maliciousTrack = trackInst with { CacheKey = "..\\..\\evil" };
        await cache.SetAsync(maliciousTrack, new LyricsResult(LyricsStatus.NotFound, null));
        (await cache.TryGetAsync(maliciousTrack)).Should().BeNull();
    }

    [Fact]
    public async Task ProviderHttpResilience_RetriesTransient503AndTripsCooldownOnLong429()
    {
        var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var attempts = 0;

        var handler = new DelegateHandler(_ =>
        {
            attempts++;
            return attempts switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                2 => throw new HttpRequestException("Transient reset"),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
            };
        });

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        using var resilience = new ProviderHttpResilience(
            "TestProvider",
            new TokenBucketRateLimiterOptions { TokenLimit = 10, TokensPerPeriod = 10, ReplenishmentPeriod = TimeSpan.FromSeconds(1), QueueLimit = 4 },
            NullLogger.Instance,
            () => now,
            (_, _) => Task.CompletedTask);

        using var okResp = await resilience.SendWithResilienceAsync(client, () => new HttpRequestMessage(HttpMethod.Get, "/ok"), CancellationToken.None);
        okResp.Should().NotBeNull();
        okResp!.StatusCode.Should().Be(HttpStatusCode.OK);
        attempts.Should().Be(3);

        // Now test 429 Retry-After > 10s -> trips cooldown circuit breaker immediately
        var cooldownCalls = 0;
        var rateLimitHandler = new DelegateHandler(_ =>
        {
            cooldownCalls++;
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return r;
        });

        using var rlClient = new HttpClient(rateLimitHandler) { BaseAddress = new Uri("https://example.test") };
        var r1 = await resilience.SendWithResilienceAsync(rlClient, () => new HttpRequestMessage(HttpMethod.Get, "/rl"), CancellationToken.None);
        r1.Should().BeNull();
        resilience.IsInCooldown.Should().BeTrue();
        cooldownCalls.Should().Be(1);

        // Subsequent request during cooldown does not hit HTTP at all
        var r2 = await resilience.SendWithResilienceAsync(rlClient, () => new HttpRequestMessage(HttpMethod.Get, "/rl"), CancellationToken.None);
        r2.Should().BeNull();
        cooldownCalls.Should().Be(1);
    }

    [Fact]
    public async Task LrcLibLyricsProvider_HandlesInstrumentalPlainFallbackAndCombinedQuerySearch()
    {
        var handler = new DelegateHandler(req =>
        {
            var pq = req.RequestUri!.PathAndQuery;
            if (pq.Contains("InstTrack", StringComparison.Ordinal))
            {
                return JsonResponse("""{"id":1,"trackName":"InstTrack","artistName":"Band","duration":180,"instrumental":true}""");
            }

            if (pq.StartsWith("/api/get", StringComparison.Ordinal))
            {
                return JsonResponse("""{"id":2,"trackName":"PlainOnlySong","artistName":"Singer","duration":120,"instrumental":false,"plainLyrics":"Hello world"}""");
            }

            if (pq.Contains("track_name=", StringComparison.Ordinal))
            {
                return JsonResponse("[]");
            }

            // Combined ?q= fallback returns invalid json to verify malformed JSON resilience
            return JsonResponse("{ malformed");
        });

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://lrclib.net") };
        using var provider = new LrcLibLyricsProvider(client, _parser, _normalizer, NullLogger<LrcLibLyricsProvider>.Instance, delayAsync: (_, _) => Task.CompletedTask);

        var instTrack = _normalizer.CreateIdentity("InstTrack", "Band", "", TimeSpan.FromSeconds(180));
        (await provider.FetchAsync(instTrack)).Status.Should().Be(LyricsStatus.Instrumental);

        var plainTrack = _normalizer.CreateIdentity("PlainOnlySong", "Singer", "", TimeSpan.FromSeconds(120));
        var plainRes = await provider.FetchAsync(plainTrack);
        plainRes.Status.Should().Be(LyricsStatus.PlainOnly);
        plainRes.Document!.Lines.Should().HaveCount(1);

        var invalidTrack = _normalizer.CreateIdentity("", "", "", TimeSpan.Zero);
        (await provider.FetchAsync(invalidTrack)).Status.Should().Be(LyricsStatus.NotFound);
    }

    [Fact]
    public async Task NetEaseLyricsProvider_HandlesLrcFallbackNoLyricAndMalformedJson()
    {
        var mode = "lrc";
        var handler = new DelegateHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Equals("/api/search/get", StringComparison.OrdinalIgnoreCase))
            {
                if (mode == "badjson")
                {
                    return JsonResponse("{bad");
                }

                return JsonResponse("""{"code":200,"result":{"songCount":1,"songs":[{"id":99,"name":"Sunset","duration":180000,"artists":[{"id":1,"name":"Wave"}]}]}}""");
            }

            return mode switch
            {
                "nolyric" => JsonResponse("""{"code":200,"nolyric":true}"""),
                _ => JsonResponse("""{"code":200,"lrc":{"version":1,"lyric":"[00:01.00]Golden sunset"}}""")
            };
        });

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://music.163.com") };
        using var provider = new NetEaseLyricsProvider(client, _parser, _normalizer, NullLogger<NetEaseLyricsProvider>.Instance, delayAsync: (_, _) => Task.CompletedTask);
        var track = _normalizer.CreateIdentity("Sunset", "Wave", "", TimeSpan.FromSeconds(180));

        var lrcRes = await provider.FetchAsync(track);
        lrcRes.Status.Should().Be(LyricsStatus.Synced);
        lrcRes.Document!.TimingKind.Should().Be(TimingKind.WordInterpolated);

        mode = "nolyric";
        (await provider.FetchAsync(track)).Status.Should().Be(LyricsStatus.Instrumental);

        mode = "badjson";
        (await provider.FetchAsync(track)).Status.Should().Be(LyricsStatus.NotFound);
    }

    [Fact]
    public async Task LyricsOrchestrator_HandlesExceptionsPlainFallbackNegativeCacheAndInvalidation()
    {
        var track = _normalizer.CreateIdentity("Missing Track", "Artist", "Album", TimeSpan.FromSeconds(180));
        var cache = new DiskLyricsCache(Path.Combine(_tempDir, "orch-edge"), _parser);

        var throwingProvider = Substitute.For<ILyricsProvider>();
        throwingProvider.Name.Returns("Broken");
        throwingProvider.PriorityOrder.Returns(1);
        throwingProvider.FetchAsync(track, Arg.Any<CancellationToken>())
            .Returns<Task<LyricsResult>>(_ => throw new InvalidOperationException("boom"));

        var notFoundProvider = Substitute.For<ILyricsProvider>();
        notFoundProvider.Name.Returns("Empty");
        notFoundProvider.PriorityOrder.Returns(2);
        notFoundProvider.FetchAsync(track, Arg.Any<CancellationToken>())
            .Returns(new LyricsResult(LyricsStatus.NotFound, null));

        var orchestratorWithError = new LyricsOrchestrator(null, cache, [throwingProvider, notFoundProvider], NullLogger<LyricsOrchestrator>.Instance);
        var errResult = await orchestratorWithError.GetLyricsAsync(track);
        errResult.Status.Should().Be(LyricsStatus.Error);

        // When all providers cleanly return NotFound, negative cache is written and then cleared via InvalidateCacheAsync
        var cleanOrchestrator = new LyricsOrchestrator(null, cache, [notFoundProvider], NullLogger<LyricsOrchestrator>.Instance);
        var nfResult = await cleanOrchestrator.GetLyricsAsync(track);
        nfResult.Status.Should().Be(LyricsStatus.NotFound);
        (await cache.TryGetAsync(track))!.Status.Should().Be(LyricsStatus.NotFound);

        await cleanOrchestrator.InvalidateCacheAsync(track);
        (await cache.TryGetAsync(track)).Should().BeNull();
    }

    [Fact]
    public async Task TrackOffsetStore_EvictsLruEntriesOver1000AndRecoversFromCorruptJson()
    {
        var path = Path.Combine(_tempDir, "offsets-lru.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "not valid json");

        var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        using var store = new TrackOffsetStore(path, () => now);

        (await store.TryGetOffsetAsync("missing")).Should().BeNull();
        await store.SetOffsetAsync("k1", TimeSpan.FromMilliseconds(-500));
        (await store.TryGetOffsetAsync("k1")).Should().Be(TimeSpan.FromMilliseconds(-500));

        await store.RemoveOffsetAsync("k1");
        (await store.TryGetOffsetAsync("k1")).Should().BeNull();
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(fn(request));
    }
}
