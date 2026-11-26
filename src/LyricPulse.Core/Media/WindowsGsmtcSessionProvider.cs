using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;
using Microsoft.Extensions.Logging;
using Windows.Media;
using Windows.Media.Control;

namespace LyricPulse.Core.Media;

/// <summary>
/// Thin Windows GSMTC WinRT adapter delegating selection, clock interpolation,
/// and bounded thumbnail deduplication to MediaSessionCoordinator.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Thin Windows WinRT COM OS interop shell; all business logic is unit tested in MediaSessionCoordinator, MediaSessionSelector, PlayerClassifier, and MonotonicPlaybackClock.")]
public sealed partial class WindowsGsmtcSessionProvider : IMediaSessionProvider
{
    private static readonly TimeSpan CoalescingWindow = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan[] InitBackoffDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(10)
    ];

    private readonly MediaSessionCoordinator _coordinator;
    private readonly ILogger<WindowsGsmtcSessionProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Lock _sessionsLock = new();
    private readonly Dictionary<string, GsmtcSessionHandle> _trackedSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<byte> _refreshChannel = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private CancellationTokenSource? _cts;
    private Task? _pumpTask;
    private bool _disposed;

    public WindowsGsmtcSessionProvider(
        ITitleNormalizer normalizer,
        ILogger<WindowsGsmtcSessionProvider> logger)
    {
        _coordinator = new MediaSessionCoordinator(normalizer);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _coordinator.SessionChanged += (s, e) => SessionChanged?.Invoke(this, e);
        _coordinator.TrackChanged += (s, e) =>
        {
            TrackChanged?.Invoke(this, e);
            if (e is not null && !_disposed)
            {
                // Follow up quickly so delayed TimelineProperties reset (00:00) & delayed artwork arrive immediately
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(160).ConfigureAwait(false);
                        if (!_disposed)
                        {
                            SignalRefresh();
                        }
                    }
                    catch
                    {
                        // Ignore during teardown.
                    }
                });
            }
        };
        _coordinator.SeekDetected += (s, e) => SeekDetected?.Invoke(this, e);
    }

    public MediaSessionSnapshot? CurrentSession => _coordinator.CurrentSession;

    public event EventHandler<MediaSessionSnapshot?>? SessionChanged;
    public event EventHandler<TrackIdentity?>? TrackChanged;
    public event EventHandler<TimeSpan>? SeekDetected;

    public TimeSpan GetInterpolatedPosition() => _coordinator.GetInterpolatedPosition();

    public void UpdateSourcePolicy(
        MediaSourcePolicy policy,
        ImmutableArray<string> blockedAppIds,
        bool ignoreBrowserVideoSessions = true)
    {
        _coordinator.UpdateSourcePolicy(policy, blockedAppIds, ignoreBrowserVideoSessions);
        SignalRefresh();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cts is not null)
        {
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await InitializeManagerWithBackoffAsync(_cts.Token).ConfigureAwait(false);
        _pumpTask = Task.Run(() => DrainRefreshLoopAsync(_cts.Token), _cts.Token);
        SignalRefresh();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);

        if (_pumpTask is not null)
        {
            try
            {
                await _pumpTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DetachAllSessions();
            DetachManager();
            _coordinator.Reset();
        }
        finally
        {
            _refreshLock.Release();
        }

        _cts.Dispose();
        _cts = null;
        _pumpTask = null;
    }

    public async Task<AlbumArtPayload?> GetCurrentAlbumArtAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _coordinator.CurrentSession;
        if (snapshot is null)
        {
            return null;
        }

        GlobalSystemMediaTransportControlsSession? targetSession;
        lock (_sessionsLock)
        {
            targetSession = _trackedSessions.TryGetValue(snapshot.SourceAppUserModelId, out var handle)
                ? handle.Session
                : null;
        }

        if (targetSession is null)
        {
            return _coordinator.CachedAlbumArt;
        }

        // Never hold _refreshLock across slow WinRT thumbnail streams so song switches are never stalled
        var props = await TryInvokeWinRtAsync(
            () => targetSession.TryGetMediaPropertiesAsync().AsTask(cancellationToken),
            "TryGetMediaPropertiesAsync").ConfigureAwait(false);

        if (props?.Thumbnail is null)
        {
            return null;
        }

        using var winRtStream = await TryInvokeWinRtAsync(
            () => props.Thumbnail.OpenReadAsync().AsTask(cancellationToken),
            "Thumbnail.OpenReadAsync").ConfigureAwait(false);

        if (winRtStream is null)
        {
            return null;
        }

        if (winRtStream.Size > (ulong)MediaSessionCoordinator.MaxThumbnailBytes)
        {
            _logger.LogGsmtcOversizedThumbnail(winRtStream.Size);
            return null;
        }

        await using var managedStream = winRtStream.AsStreamForRead();
        var payload = await _coordinator.ReadAndDeduplicateThumbnailAsync(
            managedStream,
            (long)winRtStream.Size,
            cancellationToken).ConfigureAwait(false);

        if (payload is null)
        {
            _logger.LogGsmtcAbortedThumbnail();
        }

        return payload;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _refreshLock.Dispose();
    }

    private async Task InitializeManagerWithBackoffAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt <= InitBackoffDelays.Length; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager
                    .RequestAsync()
                    .AsTask(ct)
                    .ConfigureAwait(false);

                _manager.SessionsChanged += OnManagerSessionsChanged;
                _manager.CurrentSessionChanged += OnManagerCurrentSessionChanged;
                return;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or COMException)
            {
                _logger.LogGsmtcInitFailed(ex, attempt + 1);
                if (attempt >= InitBackoffDelays.Length)
                {
                    return;
                }

                await Task.Delay(InitBackoffDelays[attempt], ct).ConfigureAwait(false);
            }
        }
    }

    private async Task DrainRefreshLoopAsync(CancellationToken ct)
    {
        try
        {
            // Immediate initial snapshot scan on startup before event-driven + watchdog loop
            await RefreshSnapshotAsync(ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                var current = _coordinator.CurrentSession;
                var pollDelayMs = current is { PlaybackState: MediaPlaybackState.Changing } ||
                                  (current?.PlaybackState == MediaPlaybackState.Playing && string.IsNullOrWhiteSpace(current.Track?.RawTitle))
                    ? 200
                    : current?.PlaybackState == MediaPlaybackState.Playing
                        ? 340
                        : 780;
                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                waitCts.CancelAfter(pollDelayMs);

                var triggeredByEvent = false;
                try
                {
                    triggeredByEvent = await _refreshChannel.Reader.WaitToReadAsync(waitCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Periodic GSMTC watchdog wakeup: recovers missed COM events, delayed metadata, or newly opened players
                }

                if (triggeredByEvent)
                {
                    while (_refreshChannel.Reader.TryRead(out _))
                    {
                    }

                    await Task.Delay(CoalescingWindow, ct).ConfigureAwait(false);
                    while (_refreshChannel.Reader.TryRead(out _))
                    {
                    }
                }

                await RefreshSnapshotAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Clean shutdown.
        }
    }

    private async Task RefreshSnapshotAsync(CancellationToken ct)
    {
        if (_manager is null)
        {
            return;
        }

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sessions = SyncTrackedSessions();
            var systemCurrentAumid = TryReadSystemCurrentAumid();

            if (sessions.Length == 0)
            {
                _coordinator.ProcessCandidates(ImmutableArray<SessionCandidate>.Empty);
                return;
            }

            var tasks = new Task<SessionCandidate?>[sessions.Length];
            for (var i = 0; i < sessions.Length; i++)
            {
                tasks[i] = BuildCandidateAsync(sessions[i], systemCurrentAumid, ct);
            }

            var resolved = await Task.WhenAll(tasks).ConfigureAwait(false);
            var candidates = ImmutableArray.CreateBuilder<SessionCandidate>(resolved.Length);
            for (var i = 0; i < resolved.Length; i++)
            {
                if (resolved[i] is { } c)
                {
                    candidates.Add(c);
                }
            }

            _coordinator.ProcessCandidates(candidates.ToImmutable());
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private GlobalSystemMediaTransportControlsSession[] SyncTrackedSessions()
    {
        if (_manager is null)
        {
            return [];
        }

        IReadOnlyList<GlobalSystemMediaTransportControlsSession> liveSessions;
        try
        {
            liveSessions = _manager.GetSessions();
        }
        catch (Exception ex) when (IsTransientWinRtException(ex))
        {
            _logger.LogGsmtcEnumerateError(ex);
            lock (_sessionsLock)
            {
                return _trackedSessions.Values.Select(static h => h.Session).ToArray();
            }
        }

        var liveMap = new Dictionary<string, GlobalSystemMediaTransportControlsSession>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in liveSessions)
        {
            var aumid = SafeGetAumid(session);
            if (!string.IsNullOrWhiteSpace(aumid))
            {
                liveMap[aumid] = session;
            }
        }

        List<GsmtcSessionHandle>? detached = null;
        GlobalSystemMediaTransportControlsSession[] snapshot;
        lock (_sessionsLock)
        {
            var removedKeys = _trackedSessions.Keys.Where(k => !liveMap.ContainsKey(k)).ToList();
            foreach (var key in removedKeys)
            {
                detached ??= new List<GsmtcSessionHandle>();
                detached.Add(_trackedSessions[key]);
                _trackedSessions.Remove(key);
            }

            foreach (var (aumid, session) in liveMap)
            {
                if (!_trackedSessions.ContainsKey(aumid))
                {
                    var handle = new GsmtcSessionHandle(session, SignalRefresh);
                    handle.Attach();
                    _trackedSessions[aumid] = handle;
                }
            }

            snapshot = _trackedSessions.Values.Select(static h => h.Session).ToArray();
        }

        if (detached is not null)
        {
            foreach (var h in detached)
            {
                h.Detach();
            }
        }

        return snapshot;
    }

    private async Task<SessionCandidate?> BuildCandidateAsync(
        GlobalSystemMediaTransportControlsSession session,
        string? systemCurrentAumid,
        CancellationToken ct)
    {
        var aumid = SafeGetAumid(session);
        if (string.IsNullOrWhiteSpace(aumid))
        {
            return null;
        }

        var mediaProps = await TryInvokeWinRtAsync(
            () => session.TryGetMediaPropertiesAsync().AsTask(ct),
            "TryGetMediaPropertiesAsync").ConfigureAwait(false);

        // Read playback status & timeline position AFTER awaiting media properties so WinRT COM latency doesn't stale-shift position
        var playbackInfo = TryInvokeWinRt(() => session.GetPlaybackInfo(), "GetPlaybackInfo");
        var timeline = TryInvokeWinRt(() => session.GetTimelineProperties(), "GetTimelineProperties");

        if (playbackInfo is null)
        {
            return null;
        }

        var rawTitle = mediaProps?.Title ?? string.Empty;
        var rawArtist = mediaProps?.Artist ?? string.Empty;
        var album = mediaProps?.AlbumTitle ?? string.Empty;
        var endTime = timeline?.EndTime ?? TimeSpan.Zero;
        var itemKind = PlayerClassifier.ClassifyItem(rawTitle, rawArtist, album, endTime, playbackInfo.PlaybackType == MediaPlaybackType.Video);
        PlayerClassifier.CleanDetectedMetadata(rawTitle, rawArtist, out var title, out var artist);

        return new SessionCandidate(
            SourceAppUserModelId: aumid,
            PlayerKind: PlayerClassifier.ClassifyPlayer(aumid, _coordinator.BlockedAppIds),
            ItemKind: itemKind,
            PlaybackState: MapPlaybackStatus(playbackInfo.PlaybackStatus),
            Title: title,
            Artist: artist,
            AlbumTitle: album,
            ReportedPosition: timeline?.Position ?? TimeSpan.Zero,
            StartTime: timeline?.StartTime ?? TimeSpan.Zero,
            EndTime: endTime,
            PlaybackRate: playbackInfo.PlaybackRate ?? 1.0,
            LastUpdatedUtc: timeline?.LastUpdatedTime ?? DateTimeOffset.MinValue,
            IsSystemCurrentSession: string.Equals(aumid, systemCurrentAumid, StringComparison.OrdinalIgnoreCase));
    }

    private string? TryReadSystemCurrentAumid()
    {
        if (_manager is null)
        {
            return null;
        }

        var current = TryInvokeWinRt(() => _manager.GetCurrentSession(), "GetCurrentSession");
        return current is null ? null : SafeGetAumid(current);
    }

    private static string SafeGetAumid(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.SourceAppUserModelId ?? string.Empty;
        }
        catch (Exception ex) when (IsTransientWinRtException(ex))
        {
            return string.Empty;
        }
    }

    private T? TryInvokeWinRt<T>(Func<T> call, string operation)
        where T : class
    {
        try
        {
            return call();
        }
        catch (Exception ex) when (IsTransientWinRtException(ex))
        {
            _logger.LogGsmtcComException(ex, operation);
            return null;
        }
    }

    private async Task<T?> TryInvokeWinRtAsync<T>(Func<Task<T>> call, string operation)
        where T : class
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientWinRtException(ex))
        {
            _logger.LogGsmtcComException(ex, operation);
            return null;
        }
    }

    private static bool IsTransientWinRtException(Exception ex) =>
        ex is COMException or ObjectDisposedException or InvalidOperationException;

    private static MediaPlaybackState MapPlaybackStatus(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) =>
        status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => MediaPlaybackState.Closed,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => MediaPlaybackState.Opened,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackState.Changing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackState.Stopped,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackState.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackState.Paused,
            _ => MediaPlaybackState.Closed
        };

    private void OnManagerSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        SignalRefresh();

    private void OnManagerCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        SignalRefresh();

    private void SignalRefresh() => _refreshChannel.Writer.TryWrite(1);

    private void DetachAllSessions()
    {
        GsmtcSessionHandle[] toDetach;
        lock (_sessionsLock)
        {
            toDetach = _trackedSessions.Values.ToArray();
            _trackedSessions.Clear();
        }

        foreach (var handle in toDetach)
        {
            handle.Detach();
        }
    }

    private void DetachManager()
    {
        if (_manager is null)
        {
            return;
        }

        try
        {
            _manager.SessionsChanged -= OnManagerSessionsChanged;
            _manager.CurrentSessionChanged -= OnManagerCurrentSessionChanged;
        }
        catch (Exception ex) when (IsTransientWinRtException(ex))
        {
            // Ignore disconnect during teardown.
        }

        _manager = null;
    }
}
