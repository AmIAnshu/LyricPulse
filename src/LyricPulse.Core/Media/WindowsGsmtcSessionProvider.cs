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
    private static readonly TimeSpan CoalescingWindow = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan[] InitBackoffDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(10)
    ];

    private readonly MediaSessionCoordinator _coordinator;
    private readonly ILogger<WindowsGsmtcSessionProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
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
        _coordinator.TrackChanged += (s, e) => TrackChanged?.Invoke(this, e);
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

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_trackedSessions.TryGetValue(snapshot.SourceAppUserModelId, out var handle))
            {
                return _coordinator.CachedAlbumArt;
            }

            var props = await TryInvokeWinRtAsync(
                () => handle.Session.TryGetMediaPropertiesAsync().AsTask(cancellationToken),
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
        finally
        {
            _refreshLock.Release();
        }
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
                var pollDelayMs = _coordinator.CurrentSession?.PlaybackState == MediaPlaybackState.Playing ? 3200 : 1600;
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
            SyncTrackedSessions();

            var systemCurrentAumid = TryReadSystemCurrentAumid();
            var candidates = ImmutableArray.CreateBuilder<SessionCandidate>(_trackedSessions.Count);

            foreach (var handle in _trackedSessions.Values)
            {
                var candidate = await BuildCandidateAsync(handle.Session, systemCurrentAumid, ct).ConfigureAwait(false);
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }

            _coordinator.ProcessCandidates(candidates.ToImmutable());
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void SyncTrackedSessions()
    {
        if (_manager is null)
        {
            return;
        }

        IReadOnlyList<GlobalSystemMediaTransportControlsSession> liveSessions;
        try
        {
            liveSessions = _manager.GetSessions();
        }
        catch (Exception ex) when (IsTransientWinRtException(ex))
        {
            _logger.LogGsmtcEnumerateError(ex);
            return;
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

        var removedKeys = _trackedSessions.Keys.Where(k => !liveMap.ContainsKey(k)).ToList();
        foreach (var key in removedKeys)
        {
            _trackedSessions[key].Detach();
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

        var playbackInfo = TryInvokeWinRt(() => session.GetPlaybackInfo(), "GetPlaybackInfo");
        var timeline = TryInvokeWinRt(() => session.GetTimelineProperties(), "GetTimelineProperties");
        var mediaProps = await TryInvokeWinRtAsync(
            () => session.TryGetMediaPropertiesAsync().AsTask(ct),
            "TryGetMediaPropertiesAsync").ConfigureAwait(false);

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
        foreach (var handle in _trackedSessions.Values)
        {
            handle.Detach();
        }

        _trackedSessions.Clear();
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
