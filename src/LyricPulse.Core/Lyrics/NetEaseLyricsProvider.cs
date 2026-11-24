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
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private readonly HttpClient _httpClient;
    private readonly ILyricsParser _parser;
    private readonly ITitleNormalizer _normalizer;
    private readonly ILogger<NetEaseLyricsProvider> _logger;
    private readonly ProviderHttpResilience _resilience;

    public NetEaseLyricsProvider(
        HttpClient httpClient,
        ILyricsParser parser,
        ITitleNormalizer normalizer,
        ILogger<NetEaseLyricsProvider> logger,
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

        var songId = await SearchBestSongIdAsync(track, cancellationToken).ConfigureAwait(false);
        if (!songId.HasValue)
        {
            return new LyricsResult(LyricsStatus.NotFound, null);
        }

        var lyricDto = await FetchSongLyricsAsync(songId.Value, cancellationToken).ConfigureAwait(false);
        if (lyricDto is null || lyricDto.Code != 200 || lyricDto.NoLyric == true || lyricDto.Uncollected == true)
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

    private async Task<long?> SearchBestSongIdAsync(TrackIdentity track, CancellationToken ct)
    {
        var query = $"{track.NormalizedTitle} {track.PrimaryArtist}".Trim();
        var body = $"s={Uri.EscapeDataString(query)}&type=1&limit=15&offset=0&total=true";

        using var response = await _resilience.SendWithResilienceAsync(
            _httpClient,
            () => CreateSearchPostRequest(body),
            ct).ConfigureAwait(false);

        if (response is null || response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        NetEaseSearchResponseDto? searchDto;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            searchDto = await JsonSerializer.DeserializeAsync(
                stream,
                LyricPulseJsonContext.Default.NetEaseSearchResponseDto,
                ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogProviderMalformedJson(ex, Name, "/api/search/get");
            return null;
        }

        if (searchDto is null || searchDto.Code != 200 || searchDto.Result?.Songs is null)
        {
            return null;
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

        return bestId;
    }

    private async Task<NetEaseLyricResponseDto?> FetchSongLyricsAsync(long songId, CancellationToken ct)
    {
        var uri = $"/api/song/lyric/v1?id={songId}&lv=1&tv=-1&kv=1&yv=1";

        using var response = await _resilience.SendWithResilienceAsync(
            _httpClient,
            () => CreateGetRequest(uri),
            ct).ConfigureAwait(false);

        if (response is null || response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(
                stream,
                LyricPulseJsonContext.Default.NetEaseLyricResponseDto,
                ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogProviderMalformedJson(ex, Name, "/api/song/lyric/v1");
            return null;
        }
    }

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
