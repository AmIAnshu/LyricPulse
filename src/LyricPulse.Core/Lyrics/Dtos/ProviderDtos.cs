using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace LyricPulse.Core.Lyrics.Dtos;

public sealed record LrclibTrackDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("trackName")] string? TrackName,
    [property: JsonPropertyName("artistName")] string? ArtistName,
    [property: JsonPropertyName("albumName")] string? AlbumName,
    [property: JsonPropertyName("duration")] double? Duration,
    [property: JsonPropertyName("instrumental")] bool Instrumental,
    [property: JsonPropertyName("hasWordSync")] bool? HasWordSync,
    [property: JsonPropertyName("plainLyrics")] string? PlainLyrics,
    [property: JsonPropertyName("syncedLyrics")] string? SyncedLyrics);

public sealed record LrclibErrorDto(
    [property: JsonPropertyName("statusCode")] int StatusCode,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("message")] string? Message);

public sealed record NetEaseSearchResponseDto(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("result")] NetEaseSearchResultDto? Result);

public sealed record NetEaseSearchResultDto(
    [property: JsonPropertyName("songs")] ImmutableArray<NetEaseSongDto>? Songs,
    [property: JsonPropertyName("songCount")] int SongCount);

public sealed record NetEaseSongDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("duration")] long DurationMs,
    [property: JsonPropertyName("artists")] ImmutableArray<NetEaseNamedDto>? Artists,
    [property: JsonPropertyName("album")] NetEaseNamedDto? Album);

public sealed record NetEaseNamedDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record NetEaseLyricResponseDto(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("nolyric")] bool? NoLyric,
    [property: JsonPropertyName("uncollected")] bool? Uncollected,
    [property: JsonPropertyName("lrc")] NetEaseLyricBlockDto? Lrc,
    [property: JsonPropertyName("yrc")] NetEaseLyricBlockDto? Yrc,
    [property: JsonPropertyName("tlyric")] NetEaseLyricBlockDto? TranslationLrc,
    [property: JsonPropertyName("romalrc")] NetEaseLyricBlockDto? RomanizedLrc);

public sealed record NetEaseLyricBlockDto(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("lyric")] string? Lyric);

public sealed record CachedLyricsEnvelopeDto(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("cacheKey")] string CacheKey,
    [property: JsonPropertyName("cachedAtUtc")] DateTimeOffset CachedAtUtc,
    [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("sourceKind")] string SourceKind,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("rawPayload")] string? RawPayload,
    [property: JsonPropertyName("translationPayload")] string? TranslationPayload,
    [property: JsonPropertyName("romanizedPayload")] string? RomanizedPayload,
    [property: JsonPropertyName("matchedTrack")] string? MatchedTrack,
    [property: JsonPropertyName("matchedArtist")] string? MatchedArtist,
    [property: JsonPropertyName("matchedDurationSec")] double? MatchedDurationSec);

public sealed record TrackOffsetEntryDto(
    [property: JsonPropertyName("offsetMs")] int OffsetMs,
    [property: JsonPropertyName("lastAccessedUtc")] DateTimeOffset LastAccessedUtc);

public sealed record TrackOffsetsEnvelopeDto(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("entries")] ImmutableDictionary<string, TrackOffsetEntryDto> Entries);
