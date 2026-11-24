using System.Diagnostics;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Color;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricPulse.Core.Runtime;

/// <summary>
/// Thread-safe live runtime orchestrator connecting GSMTC media tracking, Demo Mode looping,
/// multi-provider lyrics fetching, background semantic visual pre-annotation, OKLab palette morphing,
/// timing offsets, fullscreen auto-hide, and settings persistence behind a lock-free SampleFrame() hot path.
/// </summary>
public sealed partial class OverlayRuntimeController : IAsyncDisposable
{
    public static readonly TimeSpan FullscreenPollInterval = TimeSpan.FromMilliseconds(1500);
    private const double NormalMorphDurationSeconds = 0.650;
    private const double ReducedMotionMorphDurationSeconds = 0.150;

    private readonly IMediaSessionProvider _mediaProvider;
    private readonly ILyricsOrchestrator _lyricsOrchestrator;
    private readonly IWordVisualMatcher _visualMatcher;
    private readonly ISettingsStore _settingsStore;
    private readonly TimingOffsetService _timingOffsetService;
    private readonly DemoSongProvider _demoSongProvider;
    private readonly IAlbumColorExtractor? _albumColorExtractor;
    private readonly IFullscreenDetector? _fullscreenDetector;
    private readonly ILogger<OverlayRuntimeController> _logger;
    private readonly Func<long> _timestampProvider;
    private readonly long _timestampFrequency;
    private readonly Func<DateTimeOffset> _utcNowProvider;

    private readonly Lock _stateSync = new();
    private readonly Lock _trackLoadSync = new();
    private readonly SemaphoreSlim _settingsWriteLock = new(1, 1);

    private RuntimeStateBox _state = RuntimeStateBox.Initial;
    private CancellationTokenSource? _trackLoadCts;
    private Timer? _fullscreenTimer;
    private LyricsDocument? _cachedAnnotatedDemoDoc;
    private VisualPreference _cachedDemoVisualPreference = VisualPreference.Balanced;
    private bool _started;
    private bool _disposed;

    public OverlayRuntimeController(
        IMediaSessionProvider mediaProvider,
        ILyricsOrchestrator lyricsOrchestrator,
        IWordVisualMatcher visualMatcher,
        ISettingsStore settingsStore,
        TimingOffsetService timingOffsetService,
        DemoSongProvider demoSongProvider,
        IAlbumColorExtractor? albumColorExtractor = null,
        IFullscreenDetector? fullscreenDetector = null,
        ILogger<OverlayRuntimeController>? logger = null,
        Func<long>? timestampProvider = null,
        long? timestampFrequency = null,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        _mediaProvider = mediaProvider ?? throw new ArgumentNullException(nameof(mediaProvider));
        _lyricsOrchestrator = lyricsOrchestrator ?? throw new ArgumentNullException(nameof(lyricsOrchestrator));
        _visualMatcher = visualMatcher ?? throw new ArgumentNullException(nameof(visualMatcher));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _timingOffsetService = timingOffsetService ?? throw new ArgumentNullException(nameof(timingOffsetService));
        _demoSongProvider = demoSongProvider ?? throw new ArgumentNullException(nameof(demoSongProvider));
        _albumColorExtractor = albumColorExtractor;
        _fullscreenDetector = fullscreenDetector;
        _logger = logger ?? NullLogger<OverlayRuntimeController>.Instance;
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        _timestampFrequency = timestampFrequency is > 0 ? timestampFrequency.Value : Stopwatch.Frequency;
        _utcNowProvider = utcNowProvider ?? (static () => DateTimeOffset.UtcNow);
    }

    public AppSettings Settings => Volatile.Read(ref _state).Settings;
    public bool IsDemoMode => Volatile.Read(ref _state).IsDemoMode;

    public event EventHandler<AppSettings>? SettingsChanged;
    public event EventHandler<OverlayPlaybackSnapshot>? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        var loadedSettings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _visualMatcher.InitializeAsync(cancellationToken).ConfigureAwait(false);

        UpdateState(s => s with { Settings = loadedSettings });
        ApplyMediaPolicy(loadedSettings);

        _mediaProvider.SessionChanged += OnMediaSessionChanged;
        _mediaProvider.TrackChanged += OnMediaTrackChanged;
        _mediaProvider.SeekDetected += OnMediaSeekDetected;
        _started = true;

        await _mediaProvider.StartAsync(cancellationToken).ConfigureAwait(false);

        if (_fullscreenDetector is not null)
        {
            RefreshFullscreenState();
            _fullscreenTimer = new Timer(_ => RefreshFullscreenState(), null, FullscreenPollInterval, FullscreenPollInterval);
        }

        var initialSession = _mediaProvider.CurrentSession;
        if (initialSession is not null)
        {
            OnMediaSessionChanged(_mediaProvider, initialSession);
            if (initialSession.Track is not null)
            {
                OnMediaTrackChanged(_mediaProvider, initialSession.Track);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _fullscreenTimer?.Dispose();
        _fullscreenTimer = null;

        _mediaProvider.SessionChanged -= OnMediaSessionChanged;
        _mediaProvider.TrackChanged -= OnMediaTrackChanged;
        _mediaProvider.SeekDetected -= OnMediaSeekDetected;

        CancelInFlightTrackLoad();
        await _mediaProvider.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public OverlayPlaybackSnapshot SampleFrame() => SampleFrame(_timestampProvider());

    public OverlayPlaybackSnapshot SampleFrame(long nowTimestamp)
    {
        var state = Volatile.Read(ref _state);
        return BuildSnapshot(state, nowTimestamp);
    }

    public Task SetActiveStyleAsync(OverlayStyleId styleId, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { ActiveStyle = styleId }, cancellationToken);

    public Task ToggleLyricsVisibilityAsync(CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { LyricsVisible = !s.LyricsVisible }, cancellationToken);

    public Task ToggleReducedMotionAsync(CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { ReducedMotion = !s.ReducedMotion }, cancellationToken);

    public async Task<OverlayStyleId> CyclePinnedStyleAsync(CancellationToken cancellationToken = default)
    {
        OverlayStyleId nextStyle = OverlayStyleId.Fisheye;
        await UpdateSettingsAsync(s =>
        {
            var pinned = s.PinnedStyles.IsDefaultOrEmpty ? AppSettings.Default.PinnedStyles : s.PinnedStyles;
            var currentIdx = pinned.IndexOf(s.ActiveStyle);
            nextStyle = currentIdx >= 0 ? pinned[(currentIdx + 1) % pinned.Length] : pinned[0];
            return s with { ActiveStyle = nextStyle };
        }, cancellationToken).ConfigureAwait(false);

        return nextStyle;
    }

    public async Task<TimingOffsetState> NudgeTimingOffsetAsync(TimeSpan delta, CancellationToken cancellationToken = default)
    {
        var state = Volatile.Read(ref _state);
        var persist = !state.IsDemoMode && state.Settings.EnablePerTrackSavedOffsets;
        var updated = await _timingOffsetService.NudgeAsync(delta, persist, cancellationToken).ConfigureAwait(false);
        var nowUtc = _utcNowProvider();

        var nextState = UpdateState(s => s with
        {
            TimingOffset = updated,
            LastOffsetNudgeUtc = nowUtc,
            SeekGeneration = s.SeekGeneration + 1
        });

        NotifyStateChanged(nextState);
        return updated;
    }

    public async Task<TimingOffsetState> ResetTimingOffsetAsync(CancellationToken cancellationToken = default)
    {
        var state = Volatile.Read(ref _state);
        var persist = !state.IsDemoMode && state.Settings.EnablePerTrackSavedOffsets;
        var updated = await _timingOffsetService.ResetAsync(persist, cancellationToken).ConfigureAwait(false);
        var nowUtc = _utcNowProvider();

        var nextState = UpdateState(s => s with
        {
            TimingOffset = updated,
            LastOffsetNudgeUtc = nowUtc,
            SeekGeneration = s.SeekGeneration + 1
        });

        NotifyStateChanged(nextState);
        return updated;
    }

    public Task ToggleDemoModeAsync(CancellationToken cancellationToken = default) =>
        SetDemoModeAsync(!Volatile.Read(ref _state).IsDemoMode, cancellationToken);

    public async Task SetDemoModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _state).IsDemoMode == enabled)
        {
            return;
        }

        await _visualMatcher.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var nowTicks = _timestampProvider();

        if (enabled)
        {
            var pref = Volatile.Read(ref _state).Settings.VisualPreference;
            var demoDoc = GetOrCreateAnnotatedDemoDocument(pref);
            var resetOffset = await _timingOffsetService.OnTrackChangedAsync(null, enablePerTrackSavedOffsets: false, cancellationToken).ConfigureAwait(false);

            var nextState = UpdateState(s => s with
            {
                IsDemoMode = true,
                DemoStartTimestamp = nowTicks,
                DemoDocument = demoDoc,
                TimingOffset = resetOffset,
                PaletteMorph = CreatePaletteMorph(s, DemoSongProvider.DemoPalette, nowTicks),
                TrackGeneration = s.TrackGeneration + 1
            });

            NotifyStateChanged(nextState);
            return;
        }

        var liveTrackKey = Volatile.Read(ref _state).LiveTrack?.CacheKey;
        var liveOffset = await _timingOffsetService.OnTrackChangedAsync(
            liveTrackKey,
            Volatile.Read(ref _state).Settings.EnablePerTrackSavedOffsets,
            cancellationToken).ConfigureAwait(false);

        var restored = UpdateState(s => s with
        {
            IsDemoMode = false,
            TimingOffset = liveOffset,
            PaletteMorph = CreatePaletteMorph(s, s.LiveTargetPalette, nowTicks),
            TrackGeneration = s.TrackGeneration + 1
        });

        NotifyStateChanged(restored);
    }

    public async Task<AppSettings> UpdateSettingsAsync(
        Func<AppSettings, AppSettings> updater,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updater);

        AppSettings normalized;
        RuntimeStateBox nextState;
        bool policyChanged;
        bool visualPrefChanged;

        await _settingsWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = Volatile.Read(ref _state).Settings;
            normalized = updater(previous).ValidateAndNormalize();

            policyChanged =
                previous.UniversalPlayerMode != normalized.UniversalPlayerMode ||
                previous.IgnoreBrowserVideoSessions != normalized.IgnoreBrowserVideoSessions ||
                !previous.BlockedAppIds.SequenceEqual(normalized.BlockedAppIds);
            visualPrefChanged = previous.VisualPreference != normalized.VisualPreference;

            await _settingsStore.SaveAsync(normalized, cancellationToken).ConfigureAwait(false);

            var isFullscreen = normalized.HideDuringFullscreen &&
                               (_fullscreenDetector?.IsFullscreenExclusiveOrPresentation() ?? false);

            nextState = UpdateState(s =>
            {
                var reannotatedDemo = visualPrefChanged && s.IsDemoMode
                    ? GetOrCreateAnnotatedDemoDocument(normalized.VisualPreference)
                    : s.DemoDocument;
                var reannotatedLive = visualPrefChanged && s.LiveDocument is not null
                    ? _visualMatcher.AnnotateDocument(s.LiveDocument, normalized.VisualPreference)
                    : s.LiveDocument;

                return s with
                {
                    Settings = normalized,
                    IsFullscreenHidden = isFullscreen,
                    DemoDocument = reannotatedDemo,
                    LiveDocument = reannotatedLive
                };
            });
        }
        finally
        {
            _settingsWriteLock.Release();
        }

        if (policyChanged)
        {
            ApplyMediaPolicy(normalized);
        }

        SettingsChanged?.Invoke(this, normalized);
        NotifyStateChanged(nextState);
        return normalized;
    }

    public void RefreshFullscreenState()
    {
        if (_fullscreenDetector is null)
        {
            return;
        }

        var current = Volatile.Read(ref _state);
        var hidden = current.Settings.HideDuringFullscreen && _fullscreenDetector.IsFullscreenExclusiveOrPresentation();
        if (hidden == current.IsFullscreenHidden)
        {
            return;
        }

        var updated = UpdateState(s => s with { IsFullscreenHidden = hidden });
        NotifyStateChanged(updated);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _settingsWriteLock.Dispose();
    }

    private void OnMediaSessionChanged(object? sender, MediaSessionSnapshot? snapshot)
    {
        var nowUtc = _utcNowProvider();
        var updated = UpdateState(s =>
        {
            var wasPlaying = s.LiveSession?.PlaybackState == MediaPlaybackState.Playing;
            var isNowPaused = snapshot?.PlaybackState == MediaPlaybackState.Paused;
            DateTimeOffset? pausedSince = isNowPaused
                ? (wasPlaying || s.PausedSinceUtc is null ? nowUtc : s.PausedSinceUtc)
                : null;

            return s with
            {
                LiveSession = snapshot,
                PausedSinceUtc = pausedSince
            };
        });

        NotifyStateChanged(updated);
    }

    private void OnMediaTrackChanged(object? sender, TrackIdentity? track)
    {
        var nowTicks = _timestampProvider();
        var updated = UpdateState(s =>
        {
            var nextGen = s.TrackGeneration + 1;
            var nextTargetPalette = track is null ? AlbumAccentPalette.Default : s.LiveTargetPalette;
            var morph = !s.IsDemoMode && track is null
                ? CreatePaletteMorph(s, AlbumAccentPalette.Default, nowTicks)
                : s.PaletteMorph;

            return s with
            {
                LiveTrack = track,
                LiveDocument = null,
                LiveLyricsStatus = track is null ? LyricsStatus.NotFound : LyricsStatus.NotFound,
                LiveTargetPalette = nextTargetPalette,
                PaletteMorph = morph,
                TrackGeneration = nextGen
            };
        });

        NotifyStateChanged(updated);
        BeginTrackLoad(track, updated.TrackGeneration);
    }

    private void OnMediaSeekDetected(object? sender, TimeSpan position)
    {
        var updated = UpdateState(s => s with { SeekGeneration = s.SeekGeneration + 1 });
        NotifyStateChanged(updated);
    }

    private void BeginTrackLoad(TrackIdentity? track, long generation)
    {
        CancellationToken ct;
        lock (_trackLoadSync)
        {
            _trackLoadCts?.Cancel();
            _trackLoadCts?.Dispose();
            _trackLoadCts = null;

            if (track is null || _disposed)
            {
                return;
            }

            _trackLoadCts = new CancellationTokenSource();
            ct = _trackLoadCts.Token;
        }

        _ = Task.Run(() => LoadTrackResourcesAsync(track, generation, ct), CancellationToken.None);
    }

    private async Task LoadTrackResourcesAsync(TrackIdentity track, long generation, CancellationToken ct)
    {
        try
        {
            var settings = Volatile.Read(ref _state).Settings;

            var offsetTask = _timingOffsetService.OnTrackChangedAsync(track.CacheKey, settings.EnablePerTrackSavedOffsets, ct);
            var lyricsTask = FetchAndAnnotateLyricsAsync(track, settings.VisualPreference, ct);
            var paletteTask = ExtractAlbumPaletteAsync(ct);

            await Task.WhenAll(offsetTask, lyricsTask, paletteTask).ConfigureAwait(false);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var offset = await offsetTask.ConfigureAwait(false);
            var (status, document) = await lyricsTask.ConfigureAwait(false);
            var extractedPalette = await paletteTask.ConfigureAwait(false);
            var nowTicks = _timestampProvider();

            RuntimeStateBox? published = null;
            lock (_stateSync)
            {
                if (_state.TrackGeneration != generation)
                {
                    return;
                }

                var targetPalette = extractedPalette ?? AlbumAccentPalette.Default;
                var morph = _state.IsDemoMode
                    ? _state.PaletteMorph
                    : CreatePaletteMorph(_state, targetPalette, nowTicks);

                _state = _state with
                {
                    LiveLyricsStatus = status,
                    LiveDocument = document,
                    LiveTargetPalette = targetPalette,
                    PaletteMorph = morph,
                    TimingOffset = _state.IsDemoMode ? _state.TimingOffset : offset
                };
                published = _state;
            }

            if (published is not null)
            {
                NotifyStateChanged(published);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on rapid track skips or shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogRuntimeTrackLoadFailed(ex, track.RawTitle);
        }
    }

    private async Task<(LyricsStatus Status, LyricsDocument? Document)> FetchAndAnnotateLyricsAsync(
        TrackIdentity track,
        VisualPreference preference,
        CancellationToken ct)
    {
        var result = await _lyricsOrchestrator.GetLyricsAsync(track, ct).ConfigureAwait(false);
        if (result.Document is null || ct.IsCancellationRequested)
        {
            return (result.Status, null);
        }

        var annotated = _visualMatcher.AnnotateDocument(result.Document, preference);
        return (result.Status, annotated);
    }

    private async Task<AlbumAccentPalette?> ExtractAlbumPaletteAsync(CancellationToken ct)
    {
        if (_albumColorExtractor is null)
        {
            return null;
        }

        try
        {
            var art = await _mediaProvider.GetCurrentAlbumArtAsync(ct).ConfigureAwait(false);
            return art is null || ct.IsCancellationRequested ? null : _albumColorExtractor.Extract(art);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogRuntimeAlbumArtFailed(ex);
            return null;
        }
    }

    private OverlayPlaybackSnapshot BuildSnapshot(RuntimeStateBox state, long nowTimestamp)
    {
        var targetPalette = ResolveTargetPalette(state);
        var activePalette = state.Settings.AutoSyncColors
            ? state.PaletteMorph.Evaluate(nowTimestamp, _timestampFrequency)
            : targetPalette;

        if (state.IsDemoMode)
        {
            var demoDoc = state.DemoDocument ?? GetOrCreateAnnotatedDemoDocument(state.Settings.VisualPreference);
            var elapsedSec = Math.Max(0L, nowTimestamp - state.DemoStartTimestamp) / (double)_timestampFrequency;
            var loopSec = elapsedSec % DemoSongProvider.LoopDuration.TotalSeconds;
            var rawPos = TimeSpan.FromSeconds(loopSec);
            var effectivePos = TimingOffsetService.ComputeEffectiveLookupTime(rawPos, state.TimingOffset.Offset, DemoSongProvider.LoopDuration);
            var slice = ActiveLyricSlice.Resolve(demoDoc, effectivePos);

            return new OverlayPlaybackSnapshot(
                Track: demoDoc.Track,
                Document: demoDoc,
                LyricsStatus: LyricsStatus.Synced,
                PlaybackState: MediaPlaybackState.Playing,
                PlayerKind: MediaPlayerKind.Unknown,
                SourceAppUserModelId: "LyricPulse.Demo",
                RawPosition: rawPos,
                EffectivePosition: effectivePos,
                ActiveSlice: slice,
                ActivePalette: activePalette,
                TargetPalette: targetPalette,
                TimingOffset: state.TimingOffset,
                Settings: state.Settings,
                IsPlaying: true,
                IsDemoMode: true,
                IsFullscreenHidden: state.IsFullscreenHidden,
                PausedSinceUtc: null,
                LastOffsetNudgeUtc: state.LastOffsetNudgeUtc,
                TrackGeneration: state.TrackGeneration,
                SeekGeneration: state.SeekGeneration);
        }

        var session = state.LiveSession;
        var rawLivePos = _mediaProvider.GetInterpolatedPosition();
        var duration = state.LiveTrack?.Duration ?? session?.EndTime ?? TimeSpan.Zero;
        var effectiveLivePos = TimingOffsetService.ComputeEffectiveLookupTime(rawLivePos, state.TimingOffset.Offset, duration);
        var liveSlice = ActiveLyricSlice.Resolve(state.LiveDocument, effectiveLivePos);

        return new OverlayPlaybackSnapshot(
            Track: state.LiveTrack,
            Document: state.LiveDocument,
            LyricsStatus: state.LiveLyricsStatus,
            PlaybackState: session?.PlaybackState ?? MediaPlaybackState.Stopped,
            PlayerKind: session?.PlayerKind ?? MediaPlayerKind.Unknown,
            SourceAppUserModelId: session?.SourceAppUserModelId,
            RawPosition: rawLivePos,
            EffectivePosition: effectiveLivePos,
            ActiveSlice: liveSlice,
            ActivePalette: activePalette,
            TargetPalette: targetPalette,
            TimingOffset: state.TimingOffset,
            Settings: state.Settings,
            IsPlaying: session?.PlaybackState == MediaPlaybackState.Playing,
            IsDemoMode: false,
            IsFullscreenHidden: state.IsFullscreenHidden,
            PausedSinceUtc: state.PausedSinceUtc,
            LastOffsetNudgeUtc: state.LastOffsetNudgeUtc,
            TrackGeneration: state.TrackGeneration,
            SeekGeneration: state.SeekGeneration);
    }

    private static AlbumAccentPalette ResolveTargetPalette(RuntimeStateBox state)
    {
        if (!state.Settings.AutoSyncColors)
        {
            return new AlbumAccentPalette(
                state.Settings.ManualAccent1,
                state.Settings.ManualAccent2,
                state.Settings.ManualAccent3,
                AlbumAccentPalette.Default.ScrimTint,
                IsMonochromeFallback: false,
                SourceAlbumHash: null);
        }

        return state.IsDemoMode ? DemoSongProvider.DemoPalette : state.LiveTargetPalette;
    }

    private PaletteMorphState CreatePaletteMorph(RuntimeStateBox currentState, AlbumAccentPalette targetPalette, long nowTimestamp)
    {
        var fromPalette = currentState.PaletteMorph.Evaluate(nowTimestamp, _timestampFrequency);
        var durationSec = currentState.Settings.ReducedMotion ? ReducedMotionMorphDurationSeconds : NormalMorphDurationSeconds;
        return new PaletteMorphState(fromPalette, targetPalette, nowTimestamp, durationSec);
    }

    private LyricsDocument GetOrCreateAnnotatedDemoDocument(VisualPreference preference)
    {
        if (_cachedAnnotatedDemoDoc is not null && _cachedDemoVisualPreference == preference)
        {
            return _cachedAnnotatedDemoDoc;
        }

        var annotated = _visualMatcher.AnnotateDocument(_demoSongProvider.GetDemoDocument(), preference);
        _cachedAnnotatedDemoDoc = annotated;
        _cachedDemoVisualPreference = preference;
        return annotated;
    }

    private void ApplyMediaPolicy(AppSettings settings) =>
        _mediaProvider.UpdateSourcePolicy(settings.SourcePolicy, settings.BlockedAppIds, settings.IgnoreBrowserVideoSessions);

    private RuntimeStateBox UpdateState(Func<RuntimeStateBox, RuntimeStateBox> mutator)
    {
        lock (_stateSync)
        {
            var next = mutator(_state);
            Volatile.Write(ref _state, next);
            return next;
        }
    }

    private void CancelInFlightTrackLoad()
    {
        lock (_trackLoadSync)
        {
            _trackLoadCts?.Cancel();
            _trackLoadCts?.Dispose();
            _trackLoadCts = null;
        }
    }

    private void NotifyStateChanged(RuntimeStateBox state) =>
        StateChanged?.Invoke(this, BuildSnapshot(state, _timestampProvider()));
}
