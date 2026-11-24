using System.Collections.Immutable;
using System.Security.Cryptography;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Media;

/// <summary>
/// Pure platform-agnostic coordinator combining MediaSessionSelector,
/// MonotonicPlaybackClock, and bounded album-art deduplication.
/// </summary>
public sealed class MediaSessionCoordinator
{
    public const int MaxThumbnailBytes = 10 * 1024 * 1024;

    private readonly ITitleNormalizer _normalizer;
    private readonly MediaSessionSelector _selector;
    private readonly MonotonicPlaybackClock _clock;
    private readonly Func<DateTimeOffset> _utcNowProvider;

    private volatile MediaSessionSnapshot? _currentSession;
    private volatile AlbumArtPayload? _cachedAlbumArt;
    private MediaSourcePolicy _policy = MediaSourcePolicy.SupportedPlayersOnly;
    private ImmutableArray<string> _blockedAppIds = AppSettings.Default.BlockedAppIds;
    private bool _ignoreBrowserVideoSessions = true;

    public MediaSessionCoordinator(
        ITitleNormalizer normalizer,
        MediaSessionSelector? selector = null,
        MonotonicPlaybackClock? clock = null,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _selector = selector ?? new MediaSessionSelector();
        _clock = clock ?? new MonotonicPlaybackClock();
        _utcNowProvider = utcNowProvider ?? (static () => DateTimeOffset.UtcNow);
    }

    public MediaSessionSnapshot? CurrentSession => _currentSession;
    public AlbumArtPayload? CachedAlbumArt => _cachedAlbumArt;
    public MediaSourcePolicy SourcePolicy => _policy;
    public ImmutableArray<string> BlockedAppIds => _blockedAppIds;
    public bool IgnoreBrowserVideoSessions => _ignoreBrowserVideoSessions;

    public event EventHandler<MediaSessionSnapshot?>? SessionChanged;
    public event EventHandler<TrackIdentity?>? TrackChanged;
    public event EventHandler<TimeSpan>? SeekDetected;

    public TimeSpan GetInterpolatedPosition() => _clock.GetInterpolatedPosition();

    public void UpdateSourcePolicy(
        MediaSourcePolicy policy,
        ImmutableArray<string> blockedAppIds,
        bool ignoreBrowserVideoSessions = true)
    {
        _policy = policy;
        _blockedAppIds = blockedAppIds.IsDefault ? ImmutableArray<string>.Empty : blockedAppIds;
        _ignoreBrowserVideoSessions = ignoreBrowserVideoSessions;
    }

    public void Reset()
    {
        _clock.Reset();
        _currentSession = null;
        _cachedAlbumArt = null;
    }

    public MediaSessionSnapshot? ProcessCandidates(ImmutableArray<SessionCandidate> candidates)
    {
        var selected = _selector.Select(
            candidates,
            _policy,
            _ignoreBrowserVideoSessions,
            _utcNowProvider());

        return PublishSelection(selected);
    }

    public async Task<AlbumArtPayload?> ReadAndDeduplicateThumbnailAsync(
        Stream stream,
        long? declaredLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (declaredLength.HasValue && declaredLength.Value > MaxThumbnailBytes)
        {
            return null;
        }

        using var memory = new MemoryStream(capacity: declaredLength is > 0 and <= 65_536 ? (int)declaredLength.Value : 16_384);
        var buffer = new byte[16_384];
        var totalRead = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
            if (totalRead > MaxThumbnailBytes)
            {
                return null;
            }

            memory.Write(buffer, 0, read);
        }

        if (totalRead == 0)
        {
            return null;
        }

        var bytes = memory.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var existing = _cachedAlbumArt;
        if (existing is not null && existing.Sha256Hash.Equals(hash, StringComparison.Ordinal))
        {
            return existing;
        }

        var payload = new AlbumArtPayload(hash, bytes);
        _cachedAlbumArt = payload;
        return payload;
    }

    private MediaSessionSnapshot? PublishSelection(SessionCandidate? selected)
    {
        var previous = _currentSession;

        if (selected is null)
        {
            if (previous is null)
            {
                return null;
            }

            Reset();
            SessionChanged?.Invoke(this, null);
            TrackChanged?.Invoke(this, null);
            return null;
        }

        var track = selected.ItemKind == MediaItemKind.Music && !string.IsNullOrWhiteSpace(selected.Title)
            ? _normalizer.CreateIdentity(selected.Title, selected.Artist, selected.AlbumTitle, selected.EndTime)
            : null;

        var clockUpdate = _clock.UpdateFromTimeline(
            trackKey: track?.CacheKey ?? selected.SourceAppUserModelId,
            reportedPosition: selected.ReportedPosition,
            startTime: selected.StartTime,
            endTime: selected.EndTime,
            playbackRate: selected.PlaybackRate,
            isPlaying: selected.PlaybackState == MediaPlaybackState.Playing,
            timelineLastUpdatedUtc: selected.LastUpdatedUtc);

        var snapshot = new MediaSessionSnapshot(
            SourceAppUserModelId: selected.SourceAppUserModelId,
            PlayerKind: selected.PlayerKind,
            ItemKind: selected.ItemKind,
            PlaybackState: selected.PlaybackState,
            Track: track,
            ReportedPosition: selected.ReportedPosition,
            StartTime: selected.StartTime,
            EndTime: selected.EndTime,
            PlaybackRate: selected.PlaybackRate,
            LastUpdatedUtc: selected.LastUpdatedUtc,
            AlbumArtHash: _cachedAlbumArt?.Sha256Hash);

        var trackChanged = !string.Equals(previous?.Track?.CacheKey, track?.CacheKey, StringComparison.Ordinal);
        _currentSession = snapshot;

        if (previous != snapshot)
        {
            SessionChanged?.Invoke(this, snapshot);
        }

        if (trackChanged)
        {
            TrackChanged?.Invoke(this, track);
        }

        if (clockUpdate.Kind == ClockUpdateKind.SeekSnap)
        {
            SeekDetected?.Invoke(this, clockUpdate.NewPosition);
        }

        return snapshot;
    }
}
