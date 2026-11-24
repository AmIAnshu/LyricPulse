namespace LyricPulse.Core.Models;

public enum MediaPlayerKind
{
    Unknown = 0,
    Spotify = 1,
    AppleMusic = 2,
    DedicatedAudioPlayer = 3,
    BrowserOrGeneric = 4,
    Ignored = 5
}

public enum MediaSourcePolicy
{
    SupportedPlayersOnly = 0,
    UniversalFallback = 1
}

public enum MediaPlaybackState
{
    Closed = 0,
    Opened = 1,
    Changing = 2,
    Stopped = 3,
    Playing = 4,
    Paused = 5
}

public enum MediaItemKind
{
    Music = 0,
    Video = 1,
    Advertisement = 2,
    PodcastOrLongForm = 3,
    Unknown = 4
}

public sealed record TrackIdentity(
    string RawTitle,
    string RawArtist,
    string AlbumTitle,
    string NormalizedTitle,
    string PrimaryArtist,
    TimeSpan Duration,
    string CacheKey)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(RawTitle);
}

public sealed record MediaSessionSnapshot(
    string SourceAppUserModelId,
    MediaPlayerKind PlayerKind,
    MediaItemKind ItemKind,
    MediaPlaybackState PlaybackState,
    TrackIdentity? Track,
    TimeSpan ReportedPosition,
    TimeSpan StartTime,
    TimeSpan EndTime,
    double PlaybackRate,
    DateTimeOffset LastUpdatedUtc,
    string? AlbumArtHash);

public sealed record SessionCandidate(
    string SourceAppUserModelId,
    MediaPlayerKind PlayerKind,
    MediaItemKind ItemKind,
    MediaPlaybackState PlaybackState,
    string Title,
    string Artist,
    string AlbumTitle,
    TimeSpan ReportedPosition,
    TimeSpan StartTime,
    TimeSpan EndTime,
    double PlaybackRate,
    DateTimeOffset LastUpdatedUtc,
    bool IsSystemCurrentSession,
    string? AlbumArtHash = null);

public sealed record AlbumArtPayload(
    string Sha256Hash,
    ReadOnlyMemory<byte> ImageBytes);
