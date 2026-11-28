using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Lyrics.Dtos;
using LyricPulse.Core.Models;
using LyricPulse.Core.Serialization;
using Microsoft.Extensions.Logging;

namespace LyricPulse.Core.Lyrics;

/// <summary>
/// Fallback NetEase Cloud Music lyrics provider (POST /api/search/get + GET /api/song/lyric/v1)
/// preferring word-synced YRC over LRC while preserving bilingual translation and romanization lines.
/// </summary>
public sealed partial class NetEaseLyricsProvider : ILyricsProvider, IDisposable
{
    public const string HttpClientName = "LyricPulse.NetEase";
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private readonly Func<HttpClient> _httpClientAccessor;
    private readonly ILyricsParser _parser;
    private readonly ITitleNormalizer _normalizer;
    private readonly ILogger<NetEaseLyricsProvider> _logger;
    private readonly ProviderHttpResilience _resilience;

    public NetEaseLyricsProvider(
        IHttpClientFactory httpClientFactory,
        ILyricsParser parser,
        ITitleNormalizer normalizer,
        ILogger<NetEaseLyricsProvider> logger,
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

    public NetEaseLyricsProvider(
        HttpClient httpClient,
        ILyricsParser parser,
        ITitleNormalizer normalizer,
        ILogger<NetEaseLyricsProvider> logger,
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

    private NetEaseLyricsProvider(
        Func<HttpClient> httpClientAccessor,
        ILyricsParser parser,
        ITitleNormalizer normalizer,
        ILogger<NetEaseLyricsProvider> logger,
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
                TokenLimit = 2,
                TokensPerPeriod = 1,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = 4,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            },
            logger,
            utcNowProvider,
            delayAsync);
    }

    public string Name => "NetEase";
    public LyricsSourceKind SourceKind => LyricsSourceKind.NetEase;
    public int PriorityOrder => 2;

    public async Task<LyricsResult> FetchAsync(TrackIdentity track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!track.IsValid)
        {
            return new LyricsResult(LyricsStatus.NotFound, null);
        }

        var searchFetch = await SearchBestSongIdAsync(track, cancellationToken).ConfigureAwait(false);
        if (searchFetch.IsTransientError)
        {
            return new LyricsResult(LyricsStatus.Error, null, "NetEase search request failed transiently.");
        }

        if (!searchFetch.Value.HasValue || searchFetch.Value.Value <= 0)
        {
            return new LyricsResult(LyricsStatus.NotFound, null);
        }

        var lyricFetch = await FetchSongLyricsAsync(searchFetch.Value.Value, cancellationToken).ConfigureAwait(false);
        if (lyricFetch.IsTransientError)
        {
            return new LyricsResult(LyricsStatus.Error, null, "NetEase lyric request failed transiently.");
        }

        var lyricDto = lyricFetch.Value;
        if (lyricDto is null || lyricDto.Code != 200)
        {
            return new LyricsResult(LyricsStatus.NotFound, null);
        }

        if (lyricDto.NoLyric == true)
        {
            return new LyricsResult(LyricsStatus.Instrumental, null);
        }

        if (lyricDto.Uncollected == true)
        {
            return new LyricsResult(LyricsStatus.NotFound, null);
        }

        var tlyric = lyricDto.TranslationLrc?.Lyric;
        var romalrc = lyricDto.RomanizedLrc?.Lyric;

        if (!string.IsNullOrWhiteSpace(lyricDto.Yrc?.Lyric))
        {
            var yrcDoc = _parser.ParseYrc(track, lyricDto.Yrc.Lyric, SourceKind, Name, isFromCache: false, tlyric, romalrc);
            if (!yrcDoc.Lines.IsEmpty && yrcDoc.TimingKind != TimingKind.PlainUnsynced)
            {
                return new LyricsResult(LyricsStatus.Synced, yrcDoc);
            }
        }

        if (!string.IsNullOrWhiteSpace(lyricDto.Lrc?.Lyric))
        {
            var lrcDoc = _parser.ParseLrc(track, lyricDto.Lrc.Lyric, SourceKind, Name, isFromCache: false, tlyric, romalrc);
            if (!lrcDoc.Lines.IsEmpty)
            {
                var status = lrcDoc.TimingKind == TimingKind.PlainUnsynced ? LyricsStatus.PlainOnly : LyricsStatus.Synced;
                return new LyricsResult(status, lrcDoc);
            }
        }

        return new LyricsResult(LyricsStatus.NotFound, null);
    }

    public void Dispose() => _resilience.Dispose();

    private async Task<HttpFetchResult<long?>> SearchBestSongIdAsync(TrackIdentity track, CancellationToken ct)
    {
        var rawQuery = $"{track.NormalizedTitle} {track.PrimaryArtist}".Trim();
        var query = rawQuery.Length > 256 ? rawQuery[..256] : rawQuery;
        var body = $"s={Uri.EscapeDataString(query)}&type=1&limit=15&offset=0&total=true";

        using var response = await _resilience.SendWithResilienceAsync(
            _httpClientAccessor(),
            () => CreateSearchPostRequest(body),
            ct).ConfigureAwait(false);

        if (response is null || IsTransientFailureStatus(response.StatusCode))
        {
            return new HttpFetchResult<long?>(null, IsTransientError: true);
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return new HttpFetchResult<long?>(null, IsTransientError: false);
        }

        NetEaseSearchResponseDto? searchDto;
        try
        {
            var (exceededLimit, parsed) = await BoundedHttpResponseReader.ReadJsonAsync(
                response,
                LyricPulseJsonContext.Secure.NetEaseSearchResponseDto,
                ct).ConfigureAwait(false);
            if (exceededLimit)
            {
                _logger.LogProviderOversizedResponse(Name, "/api/search/get");
                return new HttpFetchResult<long?>(null, IsTransientError: false);
            }

            searchDto = parsed;
        }
        catch (JsonException ex)
        {
            _logger.LogProviderMalformedJson(ex, Name, "/api/search/get");
            return new HttpFetchResult<long?>(null, IsTransientError: false);
        }

        if (searchDto is null || searchDto.Code != 200 || searchDto.Result?.Songs is null)
        {
            return new HttpFetchResult<long?>(null, IsTransientError: false);
        }

        long? bestId = null;
        var bestScore = -1;

        foreach (var song in searchDto.Result.Songs.Value)
        {
            var artistNames = song.Artists.HasValue && !song.Artists.Value.IsDefaultOrEmpty
                ? string.Join(", ", song.Artists.Value.Select(a => a.Name))
                : string.Empty;

            // NetEase returns duration in milliseconds -> convert to seconds.
            var durationSec = song.DurationMs > 0 ? song.DurationMs / 1000.0 : (double?)null;

            // Score assuming line-sync baseline (+15) so strong title/artist/duration candidates qualify >= 65.
            var score = CandidateScorer.Score(
                track,
                song.Name,
                artistNames,
                durationSec,
                hasWordSync: false,
                hasLineSync: true,
                _normalizer);

            if (score >= CandidateScorer.MinimumAcceptScore && score > bestScore)
            {
                bestScore = score;
                bestId = song.Id;
            }
        }

        return new HttpFetchResult<long?>(bestId, IsTransientError: false);
    }

    private async Task<HttpFetchResult<NetEaseLyricResponseDto?>> FetchSongLyricsAsync(long songId, CancellationToken ct)
    {
        var uri = $"/api/song/lyric/v1?id={songId}&lv=1&tv=-1&kv=1&yv=1";

        using var response = await _resilience.SendWithResilienceAsync(
            _httpClientAccessor(),
            () => CreateGetRequest(uri),
            ct).ConfigureAwait(false);

        if (response is null || IsTransientFailureStatus(response.StatusCode))
        {
            return new HttpFetchResult<NetEaseLyricResponseDto?>(null, IsTransientError: true);
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return new HttpFetchResult<NetEaseLyricResponseDto?>(null, IsTransientError: false);
        }

        try
        {
            var (exceededLimit, dto) = await BoundedHttpResponseReader.ReadJsonAsync(
                response,
                LyricPulseJsonContext.Secure.NetEaseLyricResponseDto,
                ct).ConfigureAwait(false);
            if (exceededLimit)
            {
                _logger.LogProviderOversizedResponse(Name, "/api/song/lyric/v1");
                return new HttpFetchResult<NetEaseLyricResponseDto?>(null, IsTransientError: false);
            }

            return new HttpFetchResult<NetEaseLyricResponseDto?>(dto, IsTransientError: false);
        }
        catch (JsonException ex)
        {
            _logger.LogProviderMalformedJson(ex, Name, "/api/song/lyric/v1");
            return new HttpFetchResult<NetEaseLyricResponseDto?>(null, IsTransientError: false);
        }
    }

    private static bool IsTransientFailureStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout ||
        (int)statusCode >= 500;

    private readonly record struct HttpFetchResult<T>(T Value, bool IsTransientError);

    private static HttpRequestMessage CreateSearchPostRequest(string formBody)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/search/get")
        {
            Content = new StringContent(formBody, Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        ApplyHeaders(req);
        return req;
    }

    private static HttpRequestMessage CreateGetRequest(string relativeUri)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        ApplyHeaders(req);
        return req;
    }

    private static void ApplyHeaders(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
        req.Headers.TryAddWithoutValidation("Referer", "https://music.163.com/");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
