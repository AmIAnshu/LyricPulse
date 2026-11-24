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
    public const string DefaultUserAgent = "LyricPulse/1.0.0 (https://github.com/lyricpulse/lyricpulse)";

    private readonly HttpClient _httpClient;
    private readonly ILyricsParser _parser;
    private readonly ITitleNormalizer _normalizer;
    private readonly ILogger<LrcLibLyricsProvider> _logger;
    private readonly ProviderHttpResilience _resilience;

    public LrcLibLyricsProvider(
        HttpClient httpClient,
        ILyricsParser parser,
        ITitleNormalizer normalizer,
        ILogger<LrcLibLyricsProvider> logger,
        Func<DateTimeOffset>? utcNowProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resilience = new ProviderHttpResilience(
            Name,
            new TokenBucketRateLimiterOptions
            {
                TokenLimit = 4,
                TokensPerPeriod = 2,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = 8,
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

        // Step 1A: Exact /api/get with raw title, artist, album, and integer seconds duration.
        var exactUri = BuildExactGetUri(track.RawTitle, track.RawArtist, track.AlbumTitle, track.Duration);
        var exactDto = await FetchTrackDtoAsync(exactUri, cancellationToken).ConfigureAwait(false);
        var exactResult = EvaluateTrackDto(track, exactDto, ref plainFallback);
        if (exactResult is not null)
        {
            return exactResult;
        }

        // Step 1B: Retry /api/get with normalized title + primary artist (no album/duration constraint).
        var normalizedChanged =
            !track.NormalizedTitle.Equals(track.RawTitle, StringComparison.OrdinalIgnoreCase) ||
            !track.PrimaryArtist.Equals(track.RawArtist, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(track.AlbumTitle) ||
            track.Duration > TimeSpan.Zero;

        if (normalizedChanged && !string.IsNullOrWhiteSpace(track.PrimaryArtist))
        {
            var normUri = BuildExactGetUri(track.NormalizedTitle, track.PrimaryArtist, albumName: null, duration: TimeSpan.Zero);
            var normDto = await FetchTrackDtoAsync(normUri, cancellationToken).ConfigureAwait(false);
            var normResult = EvaluateTrackDto(track, normDto, ref plainFallback);
            if (normResult is not null)
            {
                return normResult;
            }
        }

        // Step 1C: Fuzzy /api/search with deterministic 0..115 candidate scorer.
        var searchUri = BuildSearchUri(track.NormalizedTitle, track.PrimaryArtist);
        var candidates = await FetchSearchDtosAsync(searchUri, cancellationToken).ConfigureAwait(false);

        if (candidates.IsDefaultOrEmpty)
        {
            var combinedQuery = $"{track.NormalizedTitle} {track.PrimaryArtist}".Trim();
            var fallbackSearchUri = $"/api/search?q={Uri.EscapeDataString(combinedQuery)}";
            candidates = await FetchSearchDtosAsync(fallbackSearchUri, cancellationToken).ConfigureAwait(false);
        }

        var scoredResult = SelectBestSearchCandidate(track, candidates, ref plainFallback);
        if (scoredResult is not null)
        {
            return scoredResult;
        }

        return plainFallback ?? new LyricsResult(LyricsStatus.NotFound, null);
    }

    public void Dispose() => _resilience.Dispose();

    private async Task<LrclibTrackDto?> FetchTrackDtoAsync(string relativeUri, CancellationToken ct)
    {
        using var response = await _resilience.SendWithResilienceAsync(
            _httpClient,
            () => CreateGetRequest(relativeUri),
            ct).ConfigureAwait(false);

        if (response is null || response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(stream, LyricPulseJsonContext.Default.LrclibTrackDto, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogProviderMalformedJson(ex, Name, "/api/get");
            return null;
        }
    }

    private async Task<ImmutableArray<LrclibTrackDto>> FetchSearchDtosAsync(string relativeUri, CancellationToken ct)
    {
        using var response = await _resilience.SendWithResilienceAsync(
            _httpClient,
            () => CreateGetRequest(relativeUri),
            ct).ConfigureAwait(false);

        if (response is null || response.StatusCode != HttpStatusCode.OK)
        {
            return ImmutableArray<LrclibTrackDto>.Empty;
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(
                stream,
                LyricPulseJsonContext.Default.ImmutableArrayLrclibTrackDto,
                ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogProviderMalformedJson(ex, Name, "/api/search");
            return ImmutableArray<LrclibTrackDto>.Empty;
        }
    }

    private LyricsResult? EvaluateTrackDto(TrackIdentity track, LrclibTrackDto? dto, ref LyricsResult? plainFallback)
    {
        if (dto is null)
        {
            return null;
        }

        if (dto.Instrumental)
        {
            return new LyricsResult(LyricsStatus.Instrumental, null);
        }

        if (!string.IsNullOrWhiteSpace(dto.SyncedLyrics))
        {
            var doc = _parser.ParseLrc(track, dto.SyncedLyrics, SourceKind, Name, isFromCache: false);
            if (!doc.Lines.IsEmpty)
            {
                return new LyricsResult(LyricsStatus.Synced, doc);
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

            if (score >= CandidateScorer.MinimumAcceptScore && score > bestScore)
            {
                bestScore = score;
                bestDto = cand;
            }
        }

        return EvaluateTrackDto(track, bestDto, ref plainFallback);
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
            $"track_name={Uri.EscapeDataString(trackName)}",
            $"artist_name={Uri.EscapeDataString(artistName)}"
        };

        if (!string.IsNullOrWhiteSpace(albumName))
        {
            parts.Add($"album_name={Uri.EscapeDataString(albumName)}");
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
            $"track_name={Uri.EscapeDataString(normalizedTitle)}"
        };

        if (!string.IsNullOrWhiteSpace(primaryArtist))
        {
            parts.Add($"artist_name={Uri.EscapeDataString(primaryArtist)}");
        }

        return "/api/search?" + string.Join('&', parts);
    }
}
