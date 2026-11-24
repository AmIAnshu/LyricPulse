using System.Collections.Immutable;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Abstractions;

public interface IMediaSessionProvider : IAsyncDisposable
{
    MediaSessionSnapshot? CurrentSession { get; }

    event EventHandler<MediaSessionSnapshot?>? SessionChanged;
    event EventHandler<TrackIdentity?>? TrackChanged;
    event EventHandler<TimeSpan>? SeekDetected;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lock-free, zero-allocation sub-millisecond interpolated playback position
    /// suitable for 60-240Hz SkiaSharp render loops.
    /// </summary>
    TimeSpan GetInterpolatedPosition();

    /// <summary>
    /// Fetches raw album thumbnail bytes for the active session (bounded &lt;= 10 MB).
    /// </summary>
    Task<AlbumArtPayload?> GetCurrentAlbumArtAsync(CancellationToken cancellationToken = default);

    void UpdateSourcePolicy(
        MediaSourcePolicy policy,
        ImmutableArray<string> blockedAppIds,
        bool ignoreBrowserVideoSessions = true);
}

public interface ITitleNormalizer
{
    TrackIdentity CreateIdentity(string rawTitle, string rawArtist, string albumTitle, TimeSpan duration);
    string NormalizeTitle(string rawTitle);
    string ExtractPrimaryArtist(string rawArtist);
}

public interface ILyricsProvider
{
    string Name { get; }
    LyricsSourceKind SourceKind { get; }
    int PriorityOrder { get; }
    Task<LyricsResult> FetchAsync(TrackIdentity track, CancellationToken cancellationToken = default);
}

public interface ILyricsOrchestrator
{
    Task<LyricsResult> GetLyricsAsync(TrackIdentity track, CancellationToken cancellationToken = default);
    Task InvalidateCacheAsync(TrackIdentity track, CancellationToken cancellationToken = default);
}

public interface ILyricsCache
{
    Task<LyricsResult?> TryGetAsync(TrackIdentity track, CancellationToken cancellationToken = default);
    Task SetAsync(TrackIdentity track, LyricsResult result, CancellationToken cancellationToken = default);
    Task RemoveAsync(TrackIdentity track, CancellationToken cancellationToken = default);
    Task PruneAsync(CancellationToken cancellationToken = default);
}

public interface ILyricsParser
{
    LyricsDocument ParseLrc(
        TrackIdentity track,
        string rawLrc,
        LyricsSourceKind sourceKind,
        string providerName,
        bool isFromCache,
        string? translationLrc = null,
        string? romanizedLrc = null);

    LyricsDocument ParseYrc(
        TrackIdentity track,
        string rawYrc,
        LyricsSourceKind sourceKind,
        string providerName,
        bool isFromCache,
        string? translationLrc = null,
        string? romanizedLrc = null);

    LyricsDocument CreatePlain(
        TrackIdentity track,
        string plainText,
        LyricsSourceKind sourceKind,
        string providerName,
        bool isFromCache);
}

public interface IWordVisualMatcher : IDisposable
{
    VisualMatcherMode Mode { get; }
    bool IsReady { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    WordVisualMatch? MatchWord(ReadOnlySpan<char> word, VisualPreference preference = VisualPreference.Balanced);
    LyricsDocument AnnotateDocument(LyricsDocument document, VisualPreference preference = VisualPreference.Balanced);
}

public interface IAlbumColorExtractor
{
    AlbumAccentPalette Extract(AlbumArtPayload? artPayload);
}

public interface ISettingsStore
{
    AppSettings Current { get; }
    event EventHandler<AppSettings>? SettingsChanged;

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings updatedSettings, CancellationToken cancellationToken = default);
    Task<AppSettings> ResetToDefaultsAsync(CancellationToken cancellationToken = default);
}

public interface ITrackOffsetStore
{
    Task<TimeSpan?> TryGetOffsetAsync(string trackCacheKey, CancellationToken cancellationToken = default);
    Task SetOffsetAsync(string trackCacheKey, TimeSpan offset, CancellationToken cancellationToken = default);
    Task RemoveOffsetAsync(string trackCacheKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Platform abstraction for Win32 click-through overlay window configuration (implemented in Phase 2).
/// </summary>
public interface IOverlayWindowPlatform
{
    void ApplyClickThroughOverlayStyles(nint windowHandle);
}

/// <summary>
/// Platform abstraction for RegisterHotKey global shortcuts (implemented in Phase 2).
/// </summary>
public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler<HotkeyAction>? HotkeyPressed;
    bool TryRegister(HotkeyAction action, string gesture, out string? errorMessage);
    void UnregisterAll();
}

/// <summary>
/// Platform abstraction for fullscreen game/presentation detection (implemented in Phase 2).
/// </summary>
public interface IFullscreenDetector
{
    bool IsFullscreenExclusiveOrPresentation();
}
