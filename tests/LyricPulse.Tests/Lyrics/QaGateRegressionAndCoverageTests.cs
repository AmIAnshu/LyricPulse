using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.RateLimiting;
using AwesomeAssertions;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Color;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Media;
using LyricPulse.Core.Models;
using LyricPulse.Core.Settings;
using LyricPulse.Core.Visuals;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LyricPulse.Tests.Lyrics;

/// <summary>
/// Phase 1 QA Gate regression and edge-case tests verifying transient error propagation
/// (anti-cache-poisoning), &gt;1,000-entry LRU offset eviction, rapid track-skip cancellation,
/// local sidecar resolution order, and full Core branch coverage (&gt;= 80%).
/// </summary>
public sealed class QaGateRegressionAndCoverageTests : IDisposable
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
    public async Task ProvidersAndOrchestrator_TransientHttpAndCooldownErrors_ReturnErrorWithoutPoisoningNegativeCache()
    {
        var track = _normalizer.CreateIdentity("Midnight Drive", "Synthwave Band", "Album", TimeSpan.FromSeconds(210));
        var cache = new DiskLyricsCache(Path.Combine(_tempDir, "no-poison-cache"), _parser);

        var lrcLibCalls = 0;
        var failingLrcLibHandler = new FuncHandler(_ =>
        {
            lrcLibCalls++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        var failingNetEaseHandler = new FuncHandler(_ => throw new HttpRequestException("Offline network"));

        using var lrcClient = new HttpClient(failingLrcLibHandler) { BaseAddress = new Uri("https://lrclib.net") };
        using var netEaseClient = new HttpClient(failingNetEaseHandler) { BaseAddress = new Uri("https://music.163.com") };
        using var lrcProvider = new LrcLibLyricsProvider(
            lrcClient, _parser, _normalizer, NullLogger<LrcLibLyricsProvider>.Instance,
            delayAsync: (_, _) => Task.CompletedTask);
        using var netEaseProvider = new NetEaseLyricsProvider(
            netEaseClient, _parser, _normalizer, NullLogger<NetEaseLyricsProvider>.Instance,
            delayAsync: (_, _) => Task.CompletedTask);

        var orchestrator = new LyricsOrchestrator(
            sidecarProvider: null,
            cache,
            [lrcProvider, netEaseProvider],
            NullLogger<LyricsOrchestrator>.Instance);

        var result = await orchestrator.GetLyricsAsync(track);

        result.Status.Should().Be(LyricsStatus.Error);
        result.Document.Should().BeNull();
        // Transient outage must NEVER write a 24h NotFound negative cache entry
        (await cache.TryGetAsync(track)).Should().BeNull();
        // Step 1A retries 3 times (initial + 2 backoffs), then short-circuits without wasting Steps 1B/1C
        lrcLibCalls.Should().Be(3);
    }

    [Fact]
    public async Task LrcLibAndNetEase_429CooldownTripsErrorStatusNotNotFound()
    {
        var track = _normalizer.CreateIdentity("Rate Limited Song", "Artist", "Album", TimeSpan.FromSeconds(180));
        var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        static HttpResponseMessage Create429()
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
            return r;
        }

        using var lrcClient = new HttpClient(new FuncHandler(_ => Create429())) { BaseAddress = new Uri("https://lrclib.net") };
        using var lrcProvider = new LrcLibLyricsProvider(
            lrcClient, _parser, _normalizer, NullLogger<LrcLibLyricsProvider>.Instance,
            utcNowProvider: () => now,
            delayAsync: (_, _) => Task.CompletedTask);

        using var neClient = new HttpClient(new FuncHandler(_ => Create429())) { BaseAddress = new Uri("https://music.163.com") };
        using var neProvider = new NetEaseLyricsProvider(
            neClient, _parser, _normalizer, NullLogger<NetEaseLyricsProvider>.Instance,
            utcNowProvider: () => now,
            delayAsync: (_, _) => Task.CompletedTask);

        (await lrcProvider.FetchAsync(track)).Status.Should().Be(LyricsStatus.Error);
        (await lrcProvider.FetchAsync(track)).Status.Should().Be(LyricsStatus.Error);

        (await neProvider.FetchAsync(track)).Status.Should().Be(LyricsStatus.Error);
        (await neProvider.FetchAsync(track)).Status.Should().Be(LyricsStatus.Error);
    }

    [Fact]
    public async Task LrcLibLyricsProvider_CoversNormalizedSuccessStep1BTransientFallbackAndSearchInstrumental()
    {
        var scenario = "norm-synced";
        var handler = new FuncHandler(req =>
        {
            var pq = req.RequestUri!.PathAndQuery;
            if (scenario == "norm-synced")
            {
                if (pq.Contains("duration=", StringComparison.Ordinal))
                {
                    return JsonResponse("{ bad json");
                }

                return JsonResponse("""{"id":5,"trackName":"Starlight","artistName":"Muse","duration":240,"instrumental":false,"syncedLyrics":"[00:01.00]Hold you in my arms"}""");
            }

            if (scenario == "step1b-503-keeps-plain")
            {
                if (pq.Contains("duration=", StringComparison.Ordinal))
                {
                    return JsonResponse("""{"id":6,"trackName":"PlainSong","artistName":"Muse","duration":240,"instrumental":false,"plainLyrics":"Fallback plain line"}""");
                }

                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            if (scenario == "search-instrumental")
            {
                if (pq.StartsWith("/api/get", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                return JsonResponse("""[{"id":7,"name":"Piano Interlude","artistName":"Composer","duration":180,"instrumental":true}]""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://lrclib.net") };
        using var provider = new LrcLibLyricsProvider(
            client, _parser, _normalizer, NullLogger<LrcLibLyricsProvider>.Instance,
            delayAsync: (_, _) => Task.CompletedTask);

        var track1 = _normalizer.CreateIdentity("Starlight (2011 Remaster)", "Muse", "Black Holes", TimeSpan.FromSeconds(240));
        var res1 = await provider.FetchAsync(track1);
        res1.Status.Should().Be(LyricsStatus.Synced);

        scenario = "step1b-503-keeps-plain";
        var res2 = await provider.FetchAsync(track1);
        res2.Status.Should().Be(LyricsStatus.PlainOnly);
        res2.Document!.Lines[0].RawText.Should().Be("Fallback plain line");

        scenario = "search-instrumental";
        var instTrack = _normalizer.CreateIdentity("Piano Interlude", "Composer", "", TimeSpan.FromSeconds(180));
        var res3 = await provider.FetchAsync(instTrack);
        res3.Status.Should().Be(LyricsStatus.Instrumental);

        scenario = "all-404";
        var missingTrack = _normalizer.CreateIdentity("No Such Track", "", "", TimeSpan.Zero);
        (await provider.FetchAsync(missingTrack)).Status.Should().Be(LyricsStatus.NotFound);
    }

    [Fact]
    public async Task NetEaseLyricsProvider_CoversInvalidTrackLyricEndpointErrorsPlainAndUncollected()
    {
        var mode = "lyric-503";
        var handler = new FuncHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Equals("/api/search/get", StringComparison.OrdinalIgnoreCase))
            {
                if (mode == "search-404")
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                if (mode == "search-empty")
                {
                    return JsonResponse("""{"code":200,"result":{"songCount":0,"songs":[]}}""");
                }

                return JsonResponse("""{"code":200,"result":{"songCount":1,"songs":[{"id":42,"name":"Echoes","duration":180000,"artists":[{"id":1,"name":"Nova"}]}]}}""");
            }

            return mode switch
            {
                "lyric-503" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                "lyric-badjson" => JsonResponse("{broken"),
                "lyric-plain" => JsonResponse("""{"code":200,"lrc":{"version":1,"lyric":"Plain unsynced line"}}"""),
                "lyric-uncollected" => JsonResponse("""{"code":200,"uncollected":true}"""),
                "lyric-empty" => JsonResponse("""{"code":200}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://music.163.com") };
        using var provider = new NetEaseLyricsProvider(
            client, _parser, _normalizer, NullLogger<NetEaseLyricsProvider>.Instance,
            delayAsync: (_, _) => Task.CompletedTask);

        var emptyTrack = _normalizer.CreateIdentity("", "", "", TimeSpan.Zero);
        (await provider.FetchAsync(emptyTrack)).Status.Should().Be(LyricsStatus.NotFound);

        var track = _normalizer.CreateIdentity("Echoes", "Nova", "", TimeSpan.FromSeconds(180));
        (await provider.FetchAsync(track)).Status.Should().Be(LyricsStatus.Error);

        mode = "lyric-badjson";
        (await provider.FetchAsync(track)).Status.Should().Be(LyricsStatus.NotFound);

        mode = "lyric-plain";
        var plainRes = await provider.FetchAsync(track);
        plainRes.Status.Should().Be(LyricsStatus.PlainOnly);

        mode = "lyric-uncollected";
        (await provider.FetchAsync(track)).Status.Should().Be(LyricsStatus.NotFound);

        mode = "lyric-empty";
        (await provider.FetchAsync(track)).Status.Should().Be(LyricsStatus.NotFound);

        mode = "search-404";
        (await provider.FetchAsync(track)).Status.Should().Be(LyricsStatus.NotFound);

        mode = "search-empty";
        (await provider.FetchAsync(track)).Status.Should().Be(LyricsStatus.NotFound);
    }

    [Fact]
    public async Task LyricsOrchestrator_HandlesRapidSkipCallerCancellationInvalidTrackSidecarPriorityAndPlainOnly()
    {
        var cacheDir = Path.Combine(_tempDir, "orch-qa-cache");
        var sidecarDir = Path.Combine(_tempDir, "orch-qa-sidecar");
        var cache = new DiskLyricsCache(cacheDir, _parser);
        var sidecar = new LocalLrcSidecarProvider(sidecarDir, _parser);

        var track = _normalizer.CreateIdentity("Skip Track", "Artist", "Album", TimeSpan.FromSeconds(180));
        var tcs = new TaskCompletionSource<LyricsResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var provider = Substitute.For<ILyricsProvider>();
        provider.Name.Returns("SlowProvider");
        provider.PriorityOrder.Returns(1);
        provider.FetchAsync(track, Arg.Any<CancellationToken>()).Returns(_ => tcs.Task);

        var orchestrator = new LyricsOrchestrator(sidecar, cache, [provider], NullLogger<LyricsOrchestrator>.Instance);

        // Invalid empty track short-circuits immediately
        var invalid = _normalizer.CreateIdentity("", "", "", TimeSpan.Zero);
        (await orchestrator.GetLyricsAsync(invalid)).Status.Should().Be(LyricsStatus.NotFound);

        // Rapid track skip: caller A cancels while caller B awaits the same in-flight task
        using var ctsA = new CancellationTokenSource();
        var taskA = orchestrator.GetLyricsAsync(track, ctsA.Token);
        var taskB = orchestrator.GetLyricsAsync(track);

        await ctsA.CancelAsync();
        await FluentActions.Awaiting(() => taskA).Should().ThrowAsync<OperationCanceledException>();

        var plainDoc = _parser.CreatePlain(track, "Plain only text", LyricsSourceKind.LrcLib, "SlowProvider", false);
        tcs.SetResult(new LyricsResult(LyricsStatus.PlainOnly, plainDoc));

        var resultB = await taskB;
        resultB.Status.Should().Be(LyricsStatus.PlainOnly);
        (await cache.TryGetAsync(track))!.Status.Should().Be(LyricsStatus.PlainOnly);

        // Priority-0 local sidecar wins immediately over cached PlainOnly
        Directory.CreateDirectory(sidecarDir);
        await File.WriteAllTextAsync(Path.Combine(sidecarDir, $"{track.CacheKey}.lrc"), "[00:01.00]Sidecar override");
        var sidecarHit = await orchestrator.GetLyricsAsync(track);
        sidecarHit.Status.Should().Be(LyricsStatus.Synced);
        sidecarHit.Document!.SourceKind.Should().Be(LyricsSourceKind.LocalSidecar);
    }

    [Fact]
    public async Task TrackOffsetStore_EvictsOldestEntriesBeyond1000LimitAndTimingServiceHandlesEdgeModes()
    {
        var path = Path.Combine(_tempDir, "offsets-1005.json");
        var baseTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var currentNow = baseTime;

        using (var store = new TrackOffsetStore(path, () => currentNow))
        {
            // Blank keys safely no-op
            (await store.TryGetOffsetAsync("   ")).Should().BeNull();
            await store.SetOffsetAsync("   ", TimeSpan.FromSeconds(1));
            await store.RemoveOffsetAsync("   ");
            await store.RemoveOffsetAsync("never-existed");

            for (var i = 0; i < TrackOffsetStore.MaxEntries + 5; i++)
            {
                currentNow = baseTime.AddSeconds(i);
                await store.SetOffsetAsync($"track-{i:D4}", TimeSpan.FromMilliseconds(500));
            }
        }

        // Reload fresh store from disk and confirm strict 1,000-entry LRU cap evicted track-0000..track-0004
        using var reloaded = new TrackOffsetStore(path);
        for (var i = 0; i < 5; i++)
        {
            (await reloaded.TryGetOffsetAsync($"track-{i:D4}")).Should().BeNull();
        }

        (await reloaded.TryGetOffsetAsync("track-0005")).Should().Be(TimeSpan.FromMilliseconds(500));
        (await reloaded.TryGetOffsetAsync("track-1004")).Should().Be(TimeSpan.FromMilliseconds(500));

        // Verify TimingOffsetService non-persisted nudge, nudge-back-to-zero removal, and GetEffectiveLookupTime
        var svc = new TimingOffsetService(reloaded);
        var events = new List<TimingOffsetState>();
        svc.OffsetChanged += (_, s) => events.Add(s);

        await svc.OnTrackChangedAsync("ephemeral-track", enablePerTrackSavedOffsets: true);
        await svc.NudgeAsync(TimeSpan.FromMilliseconds(-500), enablePerTrackSavedOffsets: false);
        svc.Current.IsPersistedOverride.Should().BeFalse();
        svc.GetEffectiveLookupTime(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60))
            .Should().Be(TimeSpan.FromSeconds(10.5));

        // Nudge to +500ms with persistence enabled, then nudge -500ms back to 0 -> deletes persisted override
        await svc.ResetAsync(enablePerTrackSavedOffsets: false);
        await svc.NudgeAsync(TimeSpan.FromMilliseconds(500), enablePerTrackSavedOffsets: true);
        (await reloaded.TryGetOffsetAsync("ephemeral-track")).Should().Be(TimeSpan.FromMilliseconds(500));

        await svc.NudgeAsync(TimeSpan.FromMilliseconds(-500), enablePerTrackSavedOffsets: true);
        svc.Current.Offset.Should().Be(TimeSpan.Zero);
        svc.Current.IsPersistedOverride.Should().BeFalse();
        (await reloaded.TryGetOffsetAsync("ephemeral-track")).Should().BeNull();
        events.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LocalLrcSidecarProvider_CoversResolutionFallbacksSanitizationEmptyAndPlainFiles()
    {
        var missingDir = Path.Combine(_tempDir, "does-not-exist");
        var missingSidecar = new LocalLrcSidecarProvider(missingDir, _parser);
        var track = _normalizer.CreateIdentity("Song: Part 1 (Remastered)", "Artist?One", "Album", TimeSpan.FromSeconds(180));

        (await missingSidecar.FetchAsync(track)).Status.Should().Be(LyricsStatus.NotFound);
        (await missingSidecar.FetchAsync(_normalizer.CreateIdentity("", "", "", TimeSpan.Zero))).Status.Should().Be(LyricsStatus.NotFound);

        var sidecarDir = Path.Combine(_tempDir, "sidecars");
        Directory.CreateDirectory(sidecarDir);
        var provider = new LocalLrcSidecarProvider(sidecarDir, _parser);

        // Empty whitespace file is skipped; falls through to sanitized title-only plain-text file
        await File.WriteAllTextAsync(Path.Combine(sidecarDir, $"{track.CacheKey}.lrc"), "   ");
        await File.WriteAllTextAsync(Path.Combine(sidecarDir, "Song_ Part 1.lrc"), "Unsynced custom line");

        var plainRes = await provider.FetchAsync(track);
        plainRes.Status.Should().Be(LyricsStatus.PlainOnly);
        plainRes.Document!.Lines[0].RawText.Should().Be("Unsynced custom line");

        // Raw Artist - Raw Title takes precedence over title-only when present
        await File.WriteAllTextAsync(Path.Combine(sidecarDir, "Artist_One - Song_ Part 1 (Remastered).lrc"), "[00:02.00]Raw match");
        var rawRes = await provider.FetchAsync(track);
        rawRes.Status.Should().Be(LyricsStatus.Synced);
        rawRes.Document!.Lines[0].RawText.Should().Be("Raw match");
    }

    [Fact]
    public async Task DiskLyricsCache_RoundTripsBilingualPayloadsAndRecoversFromCorruptOrWrongSchemaFiles()
    {
        var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var cacheDir = Path.Combine(_tempDir, "bilingual-cache");
        var cache = new DiskLyricsCache(cacheDir, _parser, () => now);

        // PruneAsync on non-existent directory safely no-ops
        await cache.PruneAsync();

        var track = _normalizer.CreateIdentity("Nightfall", "Hoshino", "Sky", TimeSpan.FromSeconds(210));

        // Errors are never persisted
        await cache.SetAsync(track, new LyricsResult(LyricsStatus.Error, null, "transient"));
        (await cache.TryGetAsync(track)).Should().BeNull();

        // Bilingual round-trip preserves translation & romanization
        var doc = _parser.ParseLrc(
            track,
            "[00:01.00]Starlight",
            LyricsSourceKind.NetEase,
            "NetEase",
            isFromCache: false,
            translationLrc: "[00:01.00]星光",
            romanizedLrc: "[00:01.00]hoshi");
        await cache.SetAsync(track, new LyricsResult(LyricsStatus.Synced, doc));

        var hit = await cache.TryGetAsync(track);
        hit.Should().NotBeNull();
        hit!.Document!.Lines[0].TranslationText.Should().Be("星光");
        hit.Document.Lines[0].RomanizedText.Should().Be("hoshi");

        // Corrupt JSON cache entry is evicted on TryGetAsync
        var shardDir = Path.Combine(cacheDir, track.CacheKey[..2]);
        var cacheFile = Path.Combine(shardDir, $"{track.CacheKey}.json");
        await File.WriteAllTextAsync(cacheFile, "{ corrupt");
        (await cache.TryGetAsync(track)).Should().BeNull();
        File.Exists(cacheFile).Should().BeFalse();

        // Wrong schema version is treated as cache miss and removed
        await File.WriteAllTextAsync(cacheFile, """{"schemaVersion":99,"cacheKey":"abc","cachedAtUtc":"2026-11-25T00:00:00Z","expiresAtUtc":"2027-01-01T00:00:00Z","status":"NotFound","sourceKind":"DiskCache","provider":"Cache","format":"None"}""");
        (await cache.TryGetAsync(track)).Should().BeNull();

        // Corrupt file in cache dir is deleted by PruneAsync
        await File.WriteAllTextAsync(cacheFile, "{ broken");
        await cache.PruneAsync();
        File.Exists(cacheFile).Should().BeFalse();

        // Short cache key safely rejected
        var shortKeyTrack = track with { CacheKey = "ab" };
        await cache.RemoveAsync(shortKeyTrack);
        (await cache.TryGetAsync(shortKeyTrack)).Should().BeNull();
    }

    [Fact]
    public void CandidateScorer_CoversAllTitleArtistDurationAndSyncBands()
    {
        var track = _normalizer.CreateIdentity("Electric Dreams Tonight", "Neon Pulse", "Album", TimeSpan.FromSeconds(200));

        // High similarity (0.85+) title, Jaccard artist overlap (>= 0.50), <=5s duration, line sync
        var highSim = CandidateScorer.Score(track, "Electric Dreams Tonight!", "Neon Pulse Live", 204.0, false, true, _normalizer);
        highSim.Should().Be(32 + 30 + 14 + 15);

        // Artist token Jaccard >= 0.50 without substring containment ("Alpha Beta Gamma" vs "Alpha Beta Delta" => 2/4 = 0.50 -> +18)
        var jaccardTrack = _normalizer.CreateIdentity("Electric Dreams Tonight", "Alpha Beta Gamma", "Album", TimeSpan.FromSeconds(200));
        var jaccardArtistScore = CandidateScorer.Score(jaccardTrack, "Electric Dreams Tonight", "Alpha Beta Delta", 204.0, false, true, _normalizer);
        jaccardArtistScore.Should().Be(40 + 18 + 14 + 15);

        // Medium similarity (0.65..0.84 -> +20), <=10s duration (+6), plain (0 sync)
        var medSim = CandidateScorer.Score(track, "Electric Dreams Tonite", "Neon Pulse", 208.0, false, false, _normalizer);
        medSim.Should().Be(20 + 30 + 6 + 0);

        // Low-pass similarity (0.50..0.64 -> +0), <=10s duration (+6), plain (0 sync)
        var lowPassSim = CandidateScorer.Score(track, "Electric Dreams Forever", "Neon Pulse", 208.0, false, false, _normalizer);
        lowPassSim.Should().Be(0 + 30 + 6 + 0);

        // 10..20s duration band & missing duration band
        var midDur = CandidateScorer.Score(track, "Electric Dreams Tonight", "Neon Pulse", 215.0, false, false, _normalizer);
        var noDur = CandidateScorer.Score(track, "Electric Dreams Tonight", "Neon Pulse", null, false, false, _normalizer);
        midDur.Should().Be(noDur);

        // Missing candidate artist incurs -30 penalty
        var noCandArtist = CandidateScorer.Score(track, "Electric Dreams Tonight", "", 200.0, false, true, _normalizer);
        noCandArtist.Should().BeLessThan(CandidateScorer.Score(track, "Electric Dreams Tonight", "Neon Pulse", 200.0, false, true, _normalizer));

        // Missing target artist gets neutral +15
        var noArtistTrack = _normalizer.CreateIdentity("Electric Dreams Tonight", "", "", TimeSpan.FromSeconds(200));
        CandidateScorer.Score(noArtistTrack, "Electric Dreams Tonight", "Any Artist", 200.0, false, true, _normalizer)
            .Should().Be(40 + 15 + 20 + 15);

        // Empty candidate title returns 0
        CandidateScorer.Score(track, "", "Neon Pulse", 200.0, true, true, _normalizer).Should().Be(0);
        CandidateScorer.ComputeCombinedSimilarity("", "").Should().Be(1.0);
        CandidateScorer.ComputeCombinedSimilarity("abc", "!!!").Should().BeLessThan(0.5);
    }

    [Fact]
    public async Task ProviderHttpResilience_CoversHttpDateRetryAfterExhaustedRetriesAndDisposal()
    {
        var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var delays = new List<TimeSpan>();

        // 1. HTTP-date Retry-After <= 10s retries inline and succeeds
        var dateAttempt = 0;
        var dateHandler = new FuncHandler(_ =>
        {
            dateAttempt++;
            if (dateAttempt == 1)
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                r.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddSeconds(2));
                return r;
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var dateClient = new HttpClient(dateHandler) { BaseAddress = new Uri("https://example.test") };
        using var resilience = new ProviderHttpResilience(
            "DateTest",
            new TokenBucketRateLimiterOptions { TokenLimit = 10, TokensPerPeriod = 10, ReplenishmentPeriod = TimeSpan.FromSeconds(1), QueueLimit = 4 },
            NullLogger.Instance,
            () => now,
            (d, _) => { delays.Add(d); return Task.CompletedTask; });

        using (var ok = await resilience.SendWithResilienceAsync(dateClient, () => new HttpRequestMessage(HttpMethod.Get, "/a"), CancellationToken.None))
        {
            ok.Should().NotBeNull();
            ok!.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        delays.Should().ContainSingle(d => d >= TimeSpan.FromSeconds(2));

        // 2. Exhausted HttpRequestException returns null cleanly
        using var errClient = new HttpClient(new FuncHandler(_ => throw new HttpRequestException("down"))) { BaseAddress = new Uri("https://example.test") };
        (await resilience.SendWithResilienceAsync(errClient, () => new HttpRequestMessage(HttpMethod.Get, "/err"), CancellationToken.None))
            .Should().BeNull();

        // 3. Exhausted inline 429 retries trips cooldown (> 60s capped)
        using var rlClient = new HttpClient(new FuncHandler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return r;
        })) { BaseAddress = new Uri("https://example.test") };

        (await resilience.SendWithResilienceAsync(rlClient, () => new HttpRequestMessage(HttpMethod.Get, "/429"), CancellationToken.None))
            .Should().BeNull();
        resilience.IsInCooldown.Should().BeTrue();

        // 4. Double dispose is safe and subsequent send throws ObjectDisposedException
        resilience.Dispose();
        resilience.Dispose();
        await FluentActions.Awaiting(() =>
            resilience.SendWithResilienceAsync(dateClient, () => new HttpRequestMessage(HttpMethod.Get, "/x"), CancellationToken.None))
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void LyricsParserAndWordTimingInterpolator_CoverEdgeFormatsMixedCjkAndShortRapidLines()
    {
        var track = _normalizer.CreateIdentity("Edge Song", "Artist", "Album", TimeSpan.FromSeconds(20));

        // LRC with no timestamps falls back to PlainUnsynced
        var plainFallback = _parser.ParseLrc(track, "Just plain lyrics without timestamps", LyricsSourceKind.LrcLib, "LRCLIB", false);
        plainFallback.TimingKind.Should().Be(TimingKind.PlainUnsynced);

        // 1-digit fractional seconds, overlapping enhanced tags, empty enhanced line, and instrumental symbols
        const string edgeLrc = """
            [offset:invalid]
            [00:01.5]<00:01.5>Sparkle, <00:01.4>time! <00:02.0>shine?
            [00:03.00]<00:03.00>   
            [00:04.00]♫
            [00:05.00]...
            [00:06.00]…
            [00:07.00]~
            [00:08.00]-
            [00:09.00]Hello星光World; fast: rapid, burst!
            [00:09.10]Next rapid line
            """;
        var doc = _parser.ParseLrc(track, edgeLrc, LyricsSourceKind.LrcLib, "LRCLIB", false);
        doc.Lines.Should().HaveCount(9);
        doc.Lines[0].Words.Should().HaveCount(3);
        doc.Lines[1].IsInstrumentalBreak.Should().BeTrue();
        doc.Lines[2].IsInstrumentalBreak.Should().BeTrue();
        doc.Lines[7].Words.Select(w => w.Text).Should().Contain(["Hello", "星", "光", "World;"]);

        // YRC edge cases: empty string, whitespace syllable chunks, non-matching lines, empty words
        _parser.ParseYrc(track, "", LyricsSourceKind.NetEase, "NetEase", false).TimingKind.Should().Be(TimingKind.None);
        const string edgeYrc = """
            invalid line ignored
            [1000,1000](1000,200,0)hi(1200,100,0) (1100,0,0)there
            [2500,500]no word tokens here
            """;
        var yrcDoc = _parser.ParseYrc(
            track, edgeYrc, LyricsSourceKind.NetEase, "NetEase", false,
            translationLrc: "non-timestamp\n[00:01.00]   \n[00:09.00]Too far away");
        yrcDoc.Lines.Should().HaveCount(2);
        yrcDoc.Lines[0].Words.Select(w => w.Text).Should().Equal("hi", "there");
        yrcDoc.Lines[0].TranslationText.Should().BeNull();
        yrcDoc.Lines[1].IsInstrumentalBreak.Should().BeTrue();

        // WordTimingInterpolator empty line & silent-e vs -le
        WordTimingInterpolator.SynthesizeWords("   ", TimeSpan.Zero, null, TimeSpan.FromSeconds(10), out _).Should().BeEmpty();
        WordTimingInterpolator.EstimateSyllables("sparkle").Should().Be(2);
        WordTimingInterpolator.EstimateSyllables("shine").Should().Be(1);
    }

    [Fact]
    public async Task MediaSessionClockAndClassifier_CoverRapidSkipsPausedFallbackAndOversizedStream()
    {
        long ticks = 100 * 10_000_000L;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var clock = new MonotonicPlaybackClock(() => ticks, 10_000_000L, () => utc);

        FluentActions.Invoking(() => new MonotonicPlaybackClock(() => ticks, 0, () => utc))
            .Should().Throw<ArgumentOutOfRangeException>();

        // Clock: paused -> playing resume, negative startTime, NaN playback rate, future timestamp, Reset()
        clock.UpdateFromTimeline("t1", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(-2), TimeSpan.FromSeconds(60), double.NaN, false, utc);
        var resumed = clock.UpdateFromTimeline("t1", TimeSpan.FromSeconds(5), TimeSpan.Zero, TimeSpan.FromSeconds(60), 10.0, true, utc.AddSeconds(5));
        resumed.Kind.Should().Be(ClockUpdateKind.HardSnap);
        clock.Reset();
        clock.GetInterpolatedPosition().Should().Be(TimeSpan.Zero);

        // PlayerClassifier edge cases
        PlayerClassifier.ClassifyPlayer("   ", ImmutableArray<string>.Empty).Should().Be(MediaPlayerKind.Unknown);
        PlayerClassifier.ClassifyPlayer("CustomPlayer.exe", ImmutableArray<string>.Empty).Should().Be(MediaPlayerKind.BrowserOrGeneric);
        PlayerClassifier.GetPriorityWeight(MediaPlayerKind.Unknown).Should().Be(0);

        // Selector: SelectBestPaused when zero playing & no active session, skips Closed/Opened/Ignored
        var selector = new MediaSessionSelector();
        var closedCand = new SessionCandidate("Spotify.exe", MediaPlayerKind.Spotify, MediaItemKind.Music, MediaPlaybackState.Closed, "A", "B", "C", TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMinutes(3), 1, utc, false);
        var ignoredCand = closedCand with { SourceAppUserModelId = "Discord.exe", PlayerKind = MediaPlayerKind.Ignored, PlaybackState = MediaPlaybackState.Playing };
        var pausedCand = closedCand with { PlaybackState = MediaPlaybackState.Paused, Title = "Paused Track", Artist = "Artist" };
        var pickedPaused = selector.Select([closedCand, ignoredCand, pausedCand], MediaSourcePolicy.SupportedPlayersOnly, true, utc);
        pickedPaused.Should().NotBeNull();
        pickedPaused!.Title.Should().Be("Paused Track");

        // Rapid track skip in Coordinator + oversized stream with unknown declaredLength (> 10 MB)
        var coordinator = new MediaSessionCoordinator(_normalizer, new MediaSessionSelector(), clock, () => utc);
        var tracksSeen = new List<string?>();
        coordinator.TrackChanged += (_, t) => tracksSeen.Add(t?.NormalizedTitle);

        // Empty candidates when already empty returns null without raising events
        coordinator.ProcessCandidates(ImmutableArray<SessionCandidate>.Empty).Should().BeNull();
        tracksSeen.Should().BeEmpty();

        var playingA = pausedCand with { PlaybackState = MediaPlaybackState.Playing, Title = "Track A" };
        var playingB = playingA with { Title = "Track B", ReportedPosition = TimeSpan.Zero };
        var playingC = playingA with { Title = "Track C", ReportedPosition = TimeSpan.Zero };
        var ad = playingA with { Title = "Advertisement", Artist = "", ItemKind = MediaItemKind.Advertisement };

        coordinator.ProcessCandidates([playingA]);
        coordinator.ProcessCandidates([playingB]);
        coordinator.ProcessCandidates([playingC]);
        coordinator.ProcessCandidates([ad]);
        tracksSeen.Should().Equal("Track A", "Track B", "Track C", null);

        // Stream exceeding 10 MB with null declaredLength aborts safely and returns null
        await using var oversizedStream = new OversizedReadStream(MediaSessionCoordinator.MaxThumbnailBytes + 32_768);
        (await coordinator.ReadAndDeduplicateThumbnailAsync(oversizedStream, declaredLength: null)).Should().BeNull();
    }

    [Fact]
    public async Task SettingsOklabNormalizerAndVisualMatcher_CoverRemainingDefensiveBranches()
    {
        // AppSettings SourcePolicy & ValidateAndNormalize defensive branches
        AppSettings.Default.SourcePolicy.Should().Be(MediaSourcePolicy.UniversalFallback);
        (AppSettings.Default with { UniversalPlayerMode = false }).SourcePolicy.Should().Be(MediaSourcePolicy.SupportedPlayersOnly);

        var brokenSettings = new AppSettings
        {
            SchemaVersion = 0,
            FontFamily = "   ",
            FontScale = double.NaN,
            OverlayOpacity = double.PositiveInfinity,
            PinnedStyles = ImmutableArray<OverlayStyleId>.Empty,
            DisabledDisplays = default,
            BlockedAppIds = default,
            PerDisplayBounds = null!,
            PerTrackOffsetsMs = null!,
            GlobalHotkeys = ImmutableDictionary<HotkeyAction, string>.Empty
        }.ValidateAndNormalize();

        brokenSettings.SchemaVersion.Should().Be(AppSettings.CurrentSchemaVersion);
        brokenSettings.FontFamily.Should().Be("Jost");
        brokenSettings.FontScale.Should().Be(1.00);
        brokenSettings.OverlayOpacity.Should().Be(0.95);
        brokenSettings.PinnedStyles.Should().NotBeEmpty();
        brokenSettings.GlobalHotkeys.Should().ContainKey(HotkeyAction.ToggleLyrics);

        // TitleNormalizer defensive branches
        _normalizer.NormalizeTitle("").Should().BeEmpty();
        _normalizer.NormalizeTitle("\u200B\uFEFF").Should().BeEmpty();
        _normalizer.ExtractPrimaryArtist("").Should().BeEmpty();
        _normalizer.ExtractPrimaryArtist("\u200B").Should().BeEmpty();
        _normalizer.ExtractPrimaryArtist("Simon & Garfunkel feat. Guest").Should().Be("Simon & Garfunkel");
        _normalizer.NormalizeTitle("“Quote” – 2020 Remaster").Should().Be("\"Quote\"");
        _normalizer.CreateIdentity("Song", "Artist", "Album", TimeSpan.FromSeconds(-5)).Duration.Should().Be(TimeSpan.Zero);

        // OklabColorMath & OklabPaletteExtractor defensive branches
        OklabColorMath.OklchToSrgbGamutMapped(new OklchColor(double.NaN, double.NaN, double.NaN)).Should().BeOfType<RgbColor>();
        OklabColorMath.ExtractPaletteFromRgba([], 0, 0, []).Should().Be(AlbumAccentPalette.Default);

        // All-transparent pixels return default palette
        var transparent = new byte[4 * 4 * 4];
        OklabColorMath.ExtractPaletteFromRgba(transparent, 4, 4, []).Should().Be(AlbumAccentPalette.Default);

        // Saturated yellow-green (h1 in [40..110]) with empty seedBytes exercises fallback hue offset and seed derivation
        var lime = new byte[16 * 16 * 4];
        for (var i = 0; i < lime.Length; i += 4) { lime[i] = 180; lime[i + 1] = 220; lime[i + 2] = 20; lime[i + 3] = 255; }
        var limePalette = OklabColorMath.ExtractPaletteFromRgba(lime, 16, 16, ReadOnlySpan<byte>.Empty, "lime");
        limePalette.IsMonochromeFallback.Should().BeFalse();

        // StarterLexiconVisualMatcher: actual -s, -ves, doubled-consonant -ing stemming, idempotency, disposed & empty-line doc
        using var matcher = new StarterLexiconVisualMatcher();
        await matcher.InitializeAsync();
        await matcher.InitializeAsync(); // idempotent

        matcher.MatchWord("comets").Should().NotBeNull();   // -s -> comet
        matcher.MatchWord("knives").Should().NotBeNull();   // -ves -> knife
        matcher.MatchWord("trapping").Should().NotBeNull(); // doubled consonant -ing -> trap
        matcher.MatchWord("wireless").Should().BeNull();    // ends in -ss, not in lexicon
        matcher.MatchWord("").Should().BeNull();
        matcher.MatchWord("x's").Should().BeNull();         // <2 chars after contraction strip

        var track = _normalizer.CreateIdentity("Inst", "A", "", TimeSpan.FromSeconds(10));
        var instDoc = _parser.ParseLrc(track, "[00:01.00]♪", LyricsSourceKind.Demo, "Demo", false);
        matcher.AnnotateDocument(instDoc).Lines[0].Words.Should().BeEmpty();

        matcher.Dispose();
        matcher.MatchWord("star").Should().BeNull();
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FuncHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(fn(request));
    }

    private sealed class OversizedReadStream(int totalBytes) : Stream
    {
        private int _remaining = totalBytes;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) return 0;
            var toRead = Math.Min(count, _remaining);
            _remaining -= toRead;
            return toRead;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
