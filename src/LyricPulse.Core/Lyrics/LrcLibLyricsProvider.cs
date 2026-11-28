using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.RateLimiting;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Lyrics.Dtos;
using LyricPulse.Core.Models;
using LyricPulse.Core.Serialization;
using Microsoft.Extensions.Logging;

namespace LyricPulse.Core.Lyrics;

/// <summary>
/// Primary LRCLIB lyrics provider implementing /api/get (exact + integer seconds duration),
/// normalized /api/get retry, and /api/search deterministic 0..115 candidate scoring.
/// </summary>
public sealed partial class LrcLibLyricsProvider : ILyricsProvider, IDisposable
{
    public const string HttpClientName = "LyricPulse.LrcLib";
    public const string DefaultUserAgent = "LyricPulse/1.0.0 (https://github.com/lyricpulse/lyricpulse)";

    private readonly Func<HttpClient> _httpClientAccessor;
    private readonly ILyricsParser _parser;
    private readonly ITitleNormalizer _normalizer;
    private readonly ILogger<LrcLibLyricsProvider> _logger;
    private readonly ProviderHttpResilience _resilience;

    public LrcLibLyricsProvider(
        IHttpClientFactory httpClientFactory,
        ILyricsParser parser,
        ITitleNormalizer normalizer,
        ILogger<LrcLibLyricsProvider> logger,
        Func<DateTimeOffset>? utcNowProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
        : this(
            () => (httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory))).CreateClient(HttpClientName),
            parser,
            normalizer,
            logger,
            utcNowProvider,
            delayAsync)
    {
    }

    public LrcLibLyricsProvider(
        HttpClient httpClient,
        ILyricsParser parser,
        ITitleNormalizer normalizer,
        ILogger<LrcLibLyricsProvider> logger,
        Func<DateTimeOffset>? utcNowProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
        : this(
            () => httpClient ?? throw new ArgumentNullException(nameof(httpClient)),
            parser,
            normalizer,
            logger,
            utcNowProvider,
            delayAsync)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
    }

    private LrcLibLyricsProvider(
        Func<HttpClient> httpClientAccessor,
        ILyricsParser parser,
        ITitleNormalizer normalizer,
        ILogger<LrcLibLyricsProvider> logger,
        Func<DateTimeOffset>? utcNowProvider,
        Func<TimeSpan, CancellationToken, Task>? delayAsync)
    {
        _httpClientAccessor = httpClientAccessor;
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resilience = new ProviderHttpResilience(
            Name,
            new TokenBucketRateLimiterOptions
            {
                TokenLimit = 8,
                TokensPerPeriod = 4,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = 12,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            },
            logger,
            utcNowProvider,
            delayAsync);
    }

    public string Name => "LRCLIB";
    public LyricsSourceKind SourceKind => LyricsSourceKind.LrcLib;
    public int PriorityOrder => 1;

    public async Task<LyricsResult> FetchAsync(TrackIdentity track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!track.IsValid)
        {
            return new LyricsResult(LyricsStatus.NotFound, null);
        }

        LyricsResult? plainFallback = null;
        LyricsResult? looseDurationFallback = null;

        // Step 1A: Exact /api/get with raw title, artist, album, and integer seconds duration (tight 2.5s sync tolerance).
        var exactUri = BuildExactGetUri(track.RawTitle, track.RawArtist, track.AlbumTitle, track.Duration);
        var exactFetch = await FetchTrackDtoAsync(exactUri, cancellationToken).ConfigureAwait(false);
        if (exactFetch.IsTransientError)
        {
            return new LyricsResult(LyricsStatus.Error, null, "LRCLIB request failed transiently.");
        }

        var exactResult = EvaluateTrackDto(track, exactFetch.Value, ref plainFallback, maxAllowedDurationDeltaSec: 2.5, ref looseDurationFallback);
        if (exactResult is not null)
        {
            return exactResult;
        }

        // Step 1B: Retry /api/get with normalized title + primary artist (strict 2.5s sync tolerance before trying /api/search).
        var normalizedChanged =
            !track.NormalizedTitle.Equals(track.RawTitle, StringComparison.OrdinalIgnoreCase) ||
            !track.PrimaryArtist.Equals(track.RawArtist, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(track.AlbumTitle) ||
            track.Duration > TimeSpan.Zero;

        if (normalizedChanged && !string.IsNullOrWhiteSpace(track.PrimaryArtist))
        {
            var normUri = BuildExactGetUri(track.NormalizedTitle, track.PrimaryArtist, albumName: null, duration: TimeSpan.Zero);
            var normFetch = await FetchTrackDtoAsync(normUri, cancellationToken).ConfigureAwait(false);
            if (normFetch.IsTransientError)
            {
                return plainFallback ?? new LyricsResult(LyricsStatus.Error, null, "LRCLIB request failed transiently.");
            }

            var normResult = EvaluateTrackDto(track, normFetch.Value, ref plainFallback, maxAllowedDurationDeltaSec: 2.5, ref looseDurationFallback);
            if (normResult is not null)
            {
                return normResult;
            }
        }

        // Step 1C: Fuzzy /api/search with deterministic 0..115 candidate scorer + tightest duration tie-breaking.
        var searchUri = BuildSearchUri(track.NormalizedTitle, track.PrimaryArtist);
        var searchFetch = await FetchSearchDtosAsync(searchUri, cancellationToken).ConfigureAwait(false);
        var hadTransientError = searchFetch.IsTransientError;
        var candidates = searchFetch.Value;

        if (!hadTransientError && candidates.IsDefaultOrEmpty)
        {
            var combinedQuery = BoundQueryValue($"{track.NormalizedTitle} {track.PrimaryArtist}");
            var fallbackSearchUri = $"/api/search?q={Uri.EscapeDataString(combinedQuery)}";
            var fallbackFetch = await FetchSearchDtosAsync(fallbackSearchUri, cancellationToken).ConfigureAwait(false);
            hadTransientError = fallbackFetch.IsTransientError;
            candidates = fallbackFetch.Value;
        }

        var scoredResult = SelectBestSearchCandidate(track, candidates, ref plainFallback);
        if (scoredResult is not null)
        {
            return scoredResult;
        }

        if (looseDurationFallback is not null)
        {
            return looseDurationFallback;
        }

        if (plainFallback is not null)
        {
            return plainFallback;
        }

        return hadTransientError
            ? new LyricsResult(LyricsStatus.Error, null, "LRCLIB request failed transiently.")
            : new LyricsResult(LyricsStatus.NotFound, null);
    }

    public void Dispose() => _resilience.Dispose();

    private async Task<HttpFetchResult<LrclibTrackDto?>> FetchTrackDtoAsync(string relativeUri, CancellationToken ct)
    {
        using var response = await _resilience.SendWithResilienceAsync(
            _httpClientAccessor(),
            () => CreateGetRequest(relativeUri),
            ct).ConfigureAwait(false);

        if (response is null || IsTransientFailureStatus(response.StatusCode))
        {
            return new HttpFetchResult<LrclibTrackDto?>(null, IsTransientError: true);
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return new HttpFetchResult<LrclibTrackDto?>(null, IsTransientError: false);
        }

        try
        {
            var (exceededLimit, dto) = await BoundedHttpResponseReader.ReadJsonAsync(
                response,
                LyricPulseJsonContext.Secure.LrclibTrackDto,
                ct).ConfigureAwait(false);
            if (exceededLimit)
            {
                _logger.LogProviderOversizedResponse(Name, "/api/get");
                return new HttpFetchResult<LrclibTrackDto?>(null, IsTransientError: false);
            }

            return new HttpFetchResult<LrclibTrackDto?>(dto, IsTransientError: false);
        }
        catch (JsonException ex)
        {
            _logger.LogProviderMalformedJson(ex, Name, "/api/get");
            return new HttpFetchResult<LrclibTrackDto?>(null, IsTransientError: false);
        }
    }

    private async Task<HttpFetchResult<ImmutableArray<LrclibTrackDto>>> FetchSearchDtosAsync(string relativeUri, CancellationToken ct)
    {
        using var response = await _resilience.SendWithResilienceAsync(
            _httpClientAccessor(),
            () => CreateGetRequest(relativeUri),
            ct).ConfigureAwait(false);

        if (response is null || IsTransientFailureStatus(response.StatusCode))
        {
            return new HttpFetchResult<ImmutableArray<LrclibTrackDto>>(ImmutableArray<LrclibTrackDto>.Empty, IsTransientError: true);
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return new HttpFetchResult<ImmutableArray<LrclibTrackDto>>(ImmutableArray<LrclibTrackDto>.Empty, IsTransientError: false);
        }

        try
        {
            var (exceededLimit, results) = await BoundedHttpResponseReader.ReadJsonAsync(
                response,
                LyricPulseJsonContext.Secure.ImmutableArrayLrclibTrackDto,
                ct).ConfigureAwait(false);
            if (exceededLimit)
            {
                _logger.LogProviderOversizedResponse(Name, "/api/search");
                return new HttpFetchResult<ImmutableArray<LrclibTrackDto>>(ImmutableArray<LrclibTrackDto>.Empty, IsTransientError: false);
            }

            return new HttpFetchResult<ImmutableArray<LrclibTrackDto>>(results, IsTransientError: false);
        }
        catch (JsonException ex)
        {
            _logger.LogProviderMalformedJson(ex, Name, "/api/search");
            return new HttpFetchResult<ImmutableArray<LrclibTrackDto>>(ImmutableArray<LrclibTrackDto>.Empty, IsTransientError: false);
        }
    }

    private static bool IsTransientFailureStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout ||
        (int)statusCode >= 500;

    private readonly record struct HttpFetchResult<T>(T Value, bool IsTransientError);

    private LyricsResult? EvaluateTrackDto(
        TrackIdentity track,
        LrclibTrackDto? dto,
        ref LyricsResult? plainFallback,
        double? maxAllowedDurationDeltaSec,
        ref LyricsResult? looseDurationFallback)
    {
        if (dto is null)
        {
            return null;
        }

        var durationDelta = track.Duration > TimeSpan.Zero && dto.Duration is > 0
            ? Math.Abs(dto.Duration.Value - track.Duration.TotalSeconds)
            : 0.0;

        if (maxAllowedDurationDeltaSec.HasValue && durationDelta > 8.0)
        {
            return null;
        }

        if (dto.Instrumental)
        {
            if (!maxAllowedDurationDeltaSec.HasValue || durationDelta <= maxAllowedDurationDeltaSec.Value)
            {
                return new LyricsResult(LyricsStatus.Instrumental, null);
            }

            looseDurationFallback ??= new LyricsResult(LyricsStatus.Instrumental, null);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(dto.SyncedLyrics))
        {
            var doc = _parser.ParseLrc(track, dto.SyncedLyrics, SourceKind, Name, isFromCache: false);
            if (!doc.Lines.IsEmpty)
            {
                var synced = new LyricsResult(LyricsStatus.Synced, doc);
                if (!maxAllowedDurationDeltaSec.HasValue || durationDelta <= maxAllowedDurationDeltaSec.Value)
                {
                    return synced;
                }

                // Store loose match (2.5s..8.0s off) so exact-duration /api/search candidate can win first!
                looseDurationFallback ??= synced;
                return null;
            }
        }

        if (plainFallback is null && !string.IsNullOrWhiteSpace(dto.PlainLyrics))
        {
            var plain = _parser.CreatePlain(track, dto.PlainLyrics, SourceKind, Name, isFromCache: false);
            if (!plain.Lines.IsEmpty)
            {
                plainFallback = new LyricsResult(LyricsStatus.PlainOnly, plain);
            }
        }

        return null;
    }

    private LyricsResult? SelectBestSearchCandidate(
        TrackIdentity track,
        ImmutableArray<LrclibTrackDto> candidates,
        ref LyricsResult? plainFallback)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return null;
        }

        LrclibTrackDto? bestDto = null;
        var bestScore = -1;
        var bestDurationDelta = double.MaxValue;
        var bestHasWordSync = false;

        foreach (var cand in candidates)
        {
            var hasSynced = !string.IsNullOrWhiteSpace(cand.SyncedLyrics);
            var hasWordSync = cand.HasWordSync == true ||
                              (hasSynced && cand.SyncedLyrics!.Contains('<', StringComparison.Ordinal));

            var score = CandidateScorer.Score(
                track,
                cand.TrackName ?? cand.Name,
                cand.ArtistName,
                cand.Duration,
                hasWordSync,
                hasSynced,
                _normalizer);

            if (cand.Instrumental && score >= CandidateScorer.MinimumAcceptScore - 15)
            {
                var baseMatchScore = CandidateScorer.Score(
                    track,
                    cand.TrackName ?? cand.Name,
                    cand.ArtistName,
                    cand.Duration,
                    hasWordSync: false,
                    hasLineSync: true,
                    _normalizer);
                if (baseMatchScore >= CandidateScorer.MinimumAcceptScore)
                {
                    return new LyricsResult(LyricsStatus.Instrumental, null);
                }
            }

            if (score < CandidateScorer.MinimumAcceptScore)
            {
                continue;
            }

            var deltaSec = track.Duration > TimeSpan.Zero && cand.Duration is > 0
                ? Math.Abs(cand.Duration.Value - track.Duration.TotalSeconds)
                : 9999.0;

            var isBetter = score > bestScore ||
                (score == bestScore && hasWordSync != bestHasWordSync && hasWordSync) ||
                (score == bestScore && hasWordSync == bestHasWordSync && deltaSec < bestDurationDelta - 0.1);

            if (isBetter)
            {
                bestScore = score;
                bestDurationDelta = deltaSec;
                bestHasWordSync = hasWordSync;
                bestDto = cand;
            }
        }

        LyricsResult? unusedLoose = null;
        return EvaluateTrackDto(track, bestDto, ref plainFallback, maxAllowedDurationDeltaSec: null, ref unusedLoose);
    }

    private static HttpRequestMessage CreateGetRequest(string relativeUri)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        req.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    private static string BuildExactGetUri(string trackName, string artistName, string? albumName, TimeSpan duration)
    {
        var parts = new List<string>(4)
        {
            $"track_name={Uri.EscapeDataString(BoundQueryValue(trackName))}",
            $"artist_name={Uri.EscapeDataString(BoundQueryValue(artistName))}"
        };

        if (!string.IsNullOrWhiteSpace(albumName))
        {
            parts.Add($"album_name={Uri.EscapeDataString(BoundQueryValue(albumName))}");
        }

        if (duration > TimeSpan.Zero)
        {
            var intSeconds = (int)Math.Round(duration.TotalSeconds);
            parts.Add($"duration={intSeconds.ToString(CultureInfo.InvariantCulture)}");
        }

        return "/api/get?" + string.Join('&', parts);
    }

    private static string BuildSearchUri(string normalizedTitle, string primaryArtist)
    {
        var parts = new List<string>(2)
        {
            $"track_name={Uri.EscapeDataString(BoundQueryValue(normalizedTitle))}"
        };

        if (!string.IsNullOrWhiteSpace(primaryArtist))
        {
            parts.Add($"artist_name={Uri.EscapeDataString(BoundQueryValue(primaryArtist))}");
        }

        return "/api/search?" + string.Join('&', parts);
    }

    private static string BoundQueryValue(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length > 256 ? trimmed[..256] : trimmed;
    }
}
