using System.Diagnostics;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Audio;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricPulse.Core.Runtime;

/// <summary>
/// Thread-safe live runtime orchestrator connecting GSMTC media tracking, Demo Mode looping,
/// multi-provider lyrics fetching, background semantic visual pre-annotation, OKLab palette morphing,
/// timing offsets, OS Reduced Motion, fullscreen auto-hide, and settings persistence behind a lock-free SampleFrame() hot path.
/// </summary>
public sealed partial class OverlayRuntimeController : IAsyncDisposable
{
    public static readonly TimeSpan FullscreenPollInterval = TimeSpan.FromMilliseconds(1500);

    private readonly IMediaSessionProvider _mediaProvider;
    private readonly ILyricsOrchestrator _lyricsOrchestrator;
    private readonly IWordVisualMatcher _visualMatcher;
    private readonly ISettingsStore _settingsStore;
    private readonly TimingOffsetService _timingOffsetService;
    private readonly DemoSongProvider _demoSongProvider;
    private readonly IAlbumColorExtractor? _albumColorExtractor;
    private readonly IFullscreenDetector? _fullscreenDetector;
    private readonly IAudioSpectrumProvider? _audioSpectrumProvider;
    private readonly ILogger<OverlayRuntimeController> _logger;
    private readonly Func<long> _timestampProvider;
    private readonly long _timestampFrequency;
    private readonly Func<DateTimeOffset> _utcNowProvider;

    private readonly Lock _stateSync = new();
    private readonly Lock _trackLoadSync = new();
    private readonly SemaphoreSlim _settingsWriteLock = new(1, 1);

    private RuntimeStateBox _state = RuntimeStateBox.Initial;
    private CachedDemoDocument? _cachedDemo;
    private CancellationTokenSource? _trackLoadCts;
    private Timer? _fullscreenTimer;
    private volatile bool _started;
    private volatile bool _disposed;

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
        Func<DateTimeOffset>? utcNowProvider = null,
        IAudioSpectrumProvider? audioSpectrumProvider = null)
    {
        _mediaProvider = mediaProvider ?? throw new ArgumentNullException(nameof(mediaProvider));
        _lyricsOrchestrator = lyricsOrchestrator ?? throw new ArgumentNullException(nameof(lyricsOrchestrator));
        _visualMatcher = visualMatcher ?? throw new ArgumentNullException(nameof(visualMatcher));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _timingOffsetService = timingOffsetService ?? throw new ArgumentNullException(nameof(timingOffsetService));
        _demoSongProvider = demoSongProvider ?? throw new ArgumentNullException(nameof(demoSongProvider));
        _albumColorExtractor = albumColorExtractor;
        _fullscreenDetector = fullscreenDetector;
        _audioSpectrumProvider = audioSpectrumProvider;
        _logger = logger ?? NullLogger<OverlayRuntimeController>.Instance;
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        _timestampFrequency = timestampFrequency is > 0 ? timestampFrequency.Value : Stopwatch.Frequency;
        _utcNowProvider = utcNowProvider ?? (static () => DateTimeOffset.UtcNow);
    }

    public AppSettings Settings => Volatile.Read(ref _state).Settings;
    public bool IsDemoMode => Volatile.Read(ref _state).IsDemoMode;
    public bool IsEffectiveReducedMotion => Volatile.Read(ref _state).IsEffectiveReducedMotion;
    public bool IsStageWindowOpen => Volatile.Read(ref _state).IsStageWindowOpen;

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

        _audioSpectrumProvider?.StartCapture();
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

        _audioSpectrumProvider?.StopCapture();
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

    public Task ToggleUniversalPlayerModeAsync(CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { UniversalPlayerMode = !s.UniversalPlayerMode }, cancellationToken);

    public Task SetStageBackdropModeAsync(StageBackdropMode mode, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { StageBackdropMode = mode }, cancellationToken);

    public Task ToggleFollowingDotAsync(CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { ShowFollowingDot = !s.ShowFollowingDot }, cancellationToken);

    public Task ToggleColorSweepLayerAsync(CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { ShowColorSweepLayer = !s.ShowColorSweepLayer }, cancellationToken);

    public Task ToggleDesktopOverlayAsync(CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { DesktopOverlayEnabled = !s.DesktopOverlayEnabled }, cancellationToken);

    public Task SetPartyEnergyLevelAsync(float level, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { PartyEnergyLevel = level }, cancellationToken);

    public Task SetFullscreenStageScaleAsync(float scale, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { FullscreenStageScale = scale }, cancellationToken);

    public Task SetEdgeGlowModeAsync(EdgeGlowMode mode, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { EdgeGlowMode = mode }, cancellationToken);

    public Task SetEdgeGlowIntensityAsync(float intensity, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { EdgeGlowIntensity = intensity }, cancellationToken);

    public Task SetEdgeGlowSpeedAsync(float speed, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { EdgeGlowSpeed = speed }, cancellationToken);

    public Task SetEdgeGlowThicknessAsync(float thickness, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { EdgeGlowThickness = thickness }, cancellationToken);

    public Task SetAudioVisualizerModeAsync(AudioVisualizerMode mode, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { AudioVisualizerMode = mode }, cancellationToken);

    public Task SetAudioVisualizerIntensityAsync(float intensity, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(s => s with { AudioVisualizerIntensity = intensity }, cancellationToken);

    public void SetStageWindowOpen(bool isOpen)
    {
        if (_disposed || Volatile.Read(ref _state).IsStageWindowOpen == isOpen)
        {
            return;
        }

        var updated = UpdateState(s => s with { IsStageWindowOpen = isOpen });
        NotifyStateChanged(updated);
    }

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
        if (!OverlaySnapshotBuilder.CanAdjustTimingOffset(state))
        {
            return state.TimingOffset;
        }

        var generation = state.TrackGeneration;
        var persist = !state.IsDemoMode && state.Settings.EnablePerTrackSavedOffsets;
        var updated = await _timingOffsetService.NudgeAsync(delta, persist, cancellationToken).ConfigureAwait(false);
        return CommitTimingOffsetChange(updated, generation);
    }

    public async Task<TimingOffsetState> ResetTimingOffsetAsync(CancellationToken cancellationToken = default)
    {
        var state = Volatile.Read(ref _state);
        if (!OverlaySnapshotBuilder.CanAdjustTimingOffset(state))
        {
            return state.TimingOffset;
        }

        var generation = state.TrackGeneration;
        var persist = !state.IsDemoMode && state.Settings.EnablePerTrackSavedOffsets;
        var updated = await _timingOffsetService.ResetAsync(persist, cancellationToken).ConfigureAwait(false);
        return CommitTimingOffsetChange(updated, generation);
    }

    private TimingOffsetState CommitTimingOffsetChange(TimingOffsetState updated, long expectedGeneration)
    {
        var nowUtc = _utcNowProvider();
        RuntimeStateBox? published = null;

        lock (_stateSync)
        {
            if (_state.TrackGeneration != expectedGeneration)
            {
                return _state.TimingOffset;
            }

            _state = _state with
            {
                TimingOffset = updated,
                HasUserNudgedInCurrentTrack = true,
                LastOffsetNudgeUtc = nowUtc,
                SeekGeneration = _state.SeekGeneration + 1,
                OffsetGeneration = _state.OffsetGeneration + 1
            };
            published = _state;
        }

        if (!_disposed)
        {
            NotifyStateChanged(published);
        }

        return updated;
    }

    public Task RetryLyricsFetchAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var current = Volatile.Read(ref _state);
        if (current.IsDemoMode || current.LiveTrack is null)
        {
            return Task.CompletedTask;
        }

        var track = current.LiveTrack;
        var updated = UpdateState(s => s with
        {
            LiveDocument = null,
            LiveLyricsStatus = LyricsStatus.Loading,
            TrackGeneration = s.TrackGeneration + 1
        });

        NotifyStateChanged(updated);
        BeginTrackLoad(track, updated.TrackGeneration, forceRefresh: true);
        return Task.CompletedTask;
    }

    public Task ToggleDemoModeAsync(CancellationToken cancellationToken = default) =>
        SetDemoModeAsync(!Volatile.Read(ref _state).IsDemoMode, cancellationToken);

    public async Task SetDemoModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _state).IsDemoMode == enabled)
        {
            if (enabled && !Volatile.Read(ref _state).Settings.LyricsVisible)
            {
                await UpdateSettingsAsync(s => s with { LyricsVisible = true }, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await _visualMatcher.InitializeAsync(cancellationToken).ConfigureAwait(false);

        if (enabled && !Volatile.Read(ref _state).Settings.LyricsVisible)
        {
            await UpdateSettingsAsync(s => s with { LyricsVisible = true }, cancellationToken).ConfigureAwait(false);
        }

        var nowTicks = _timestampProvider();
        var nextState = enabled
            ? await EnableDemoModeStateAsync(nowTicks, cancellationToken).ConfigureAwait(false)
            : await DisableDemoModeStateAsync(nowTicks, cancellationToken).ConfigureAwait(false);

        NotifyStateChanged(nextState);
    }

    private async Task<RuntimeStateBox> EnableDemoModeStateAsync(long nowTicks, CancellationToken cancellationToken)
    {
        var pref = Volatile.Read(ref _state).Settings.VisualPreference;
        var demoDoc = GetOrCreateAnnotatedDemoDocument(pref);
        var demoMood = _visualMatcher.InferTrackMoodVisual(demoDoc.Track, demoDoc, pref);
        var resetOffset = await _timingOffsetService
            .OnTrackChangedAsync(null, enablePerTrackSavedOffsets: false, cancellationToken)
            .ConfigureAwait(false);

        return UpdateState(s => s with
        {
            IsDemoMode = true,
            DemoStartTimestamp = nowTicks,
            DemoDocument = demoDoc,
            DemoMoodVisual = demoMood,
            TimingOffset = resetOffset,
            HasUserNudgedInCurrentTrack = false,
            LastOffsetNudgeUtc = null,
            PaletteMorph = OverlaySnapshotBuilder.CreatePaletteMorph(s, DemoSongProvider.DemoPalette, nowTicks, _timestampFrequency),
            TrackGeneration = s.TrackGeneration + 1
        });
    }

    private async Task<RuntimeStateBox> DisableDemoModeStateAsync(long nowTicks, CancellationToken cancellationToken)
    {
        var liveState = Volatile.Read(ref _state);
        var liveOffset = await _timingOffsetService
            .OnTrackChangedAsync(liveState.LiveTrack?.CacheKey, liveState.Settings.EnablePerTrackSavedOffsets, cancellationToken)
            .ConfigureAwait(false);

        return UpdateState(s => s with
        {
            IsDemoMode = false,
            TimingOffset = liveOffset,
            HasUserNudgedInCurrentTrack = false,
            LastOffsetNudgeUtc = null,
            PaletteMorph = OverlaySnapshotBuilder.CreatePaletteMorph(s, s.LiveTargetPalette, nowTicks, _timestampFrequency),
            TrackGeneration = s.TrackGeneration + 1
        });
    }

    public void SetOsReducedMotion(bool enabled)
    {
        if (_disposed)
        {
            return;
        }

        var current = Volatile.Read(ref _state);
        if (current.IsOsReducedMotion == enabled)
        {
            return;
        }

        var nowTicks = _timestampProvider();
        var updated = UpdateState(s =>
        {
            var effective = s.Settings.ReducedMotion || enabled;
            var morph = effective
                ? s.PaletteMorph.ClampRemainingDuration(nowTicks, _timestampFrequency, OverlaySnapshotBuilder.ReducedMotionMorphDurationSeconds)
                : s.PaletteMorph;

            return s with
            {
                IsOsReducedMotion = enabled,
                PaletteMorph = morph
            };
        });

        NotifyStateChanged(updated);
    }

    public async Task<AppSettings> UpdateSettingsAsync(
        Func<AppSettings, AppSettings> updater,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updater);

        AppSettings normalized;
        RuntimeStateBox nextState;
        bool policyChanged;

        await _settingsWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = Volatile.Read(ref _state).Settings;
            normalized = updater(previous).ValidateAndNormalize();
            policyChanged = HasMediaPolicyChanged(previous, normalized);
            var visualPrefChanged = previous.VisualPreference != normalized.VisualPreference;

            await _settingsStore.SaveAsync(normalized, cancellationToken).ConfigureAwait(false);
            nextState = ApplySettingsToState(normalized, visualPrefChanged);
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

    private static bool HasMediaPolicyChanged(AppSettings previous, AppSettings normalized) =>
        previous.UniversalPlayerMode != normalized.UniversalPlayerMode ||
        previous.IgnoreBrowserVideoSessions != normalized.IgnoreBrowserVideoSessions ||
        !previous.BlockedAppIds.SequenceEqual(normalized.BlockedAppIds);

    private RuntimeStateBox ApplySettingsToState(AppSettings normalized, bool visualPrefChanged)
    {
        var isFullscreen = normalized.HideDuringFullscreen &&
                           (_fullscreenDetector?.IsFullscreenExclusiveOrPresentation() ?? false);
        var nowTicks = _timestampProvider();

        return UpdateState(s =>
        {
            var reannotatedDemo = visualPrefChanged && s.IsDemoMode
                ? GetOrCreateAnnotatedDemoDocument(normalized.VisualPreference)
                : s.DemoDocument;
            var demoMood = visualPrefChanged && reannotatedDemo is not null
                ? _visualMatcher.InferTrackMoodVisual(reannotatedDemo.Track, reannotatedDemo, normalized.VisualPreference)
                : s.DemoMoodVisual;
            var reannotatedLive = visualPrefChanged && s.LiveDocument is not null
                ? _visualMatcher.AnnotateDocument(s.LiveDocument, normalized.VisualPreference)
                : s.LiveDocument;
            var liveMood = visualPrefChanged && s.LiveTrack is not null
                ? _visualMatcher.InferTrackMoodVisual(s.LiveTrack, reannotatedLive, normalized.VisualPreference)
                : s.LiveMoodVisual;

            var effectiveReduced = normalized.ReducedMotion || s.IsOsReducedMotion;
            var morph = effectiveReduced
                ? s.PaletteMorph.ClampRemainingDuration(nowTicks, _timestampFrequency, OverlaySnapshotBuilder.ReducedMotionMorphDurationSeconds)
                : s.PaletteMorph;

            return s with
            {
                Settings = normalized,
                IsFullscreenHidden = isFullscreen,
                DemoDocument = reannotatedDemo,
                DemoMoodVisual = demoMood,
                LiveDocument = reannotatedLive,
                LiveMoodVisual = liveMood,
                PaletteMorph = morph
            };
        });
    }

    public void RefreshFullscreenState()
    {
        if (_disposed || !_started || _fullscreenDetector is null)
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
        if (!_disposed)
        {
            NotifyStateChanged(updated);
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
        _settingsWriteLock.Dispose();
    }

    private void OnMediaSessionChanged(object? sender, MediaSessionSnapshot? snapshot)
    {
        if (_disposed)
        {
            return;
        }

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
        if (_disposed)
        {
            return;
        }

        var nowTicks = _timestampProvider();
        var updated = UpdateState(s =>
        {
            var nextGen = s.TrackGeneration + 1;
            var nextTargetPalette = track is null ? AlbumAccentPalette.Default : s.LiveTargetPalette;
            var morph = !s.IsDemoMode && track is null
                ? OverlaySnapshotBuilder.CreatePaletteMorph(s, AlbumAccentPalette.Default, nowTicks, _timestampFrequency)
                : s.PaletteMorph;

            return s with
            {
                LiveTrack = track,
                LiveDocument = null,
                LiveMoodVisual = null,
                LiveLyricsStatus = track is null ? LyricsStatus.NotFound : LyricsStatus.Loading,
                LiveTargetPalette = nextTargetPalette,
                LiveAlbumArt = null,
                PaletteMorph = morph,
                TimingOffset = s.IsDemoMode ? s.TimingOffset : new TimingOffsetState(track?.CacheKey, TimeSpan.Zero, false),
                HasUserNudgedInCurrentTrack = s.IsDemoMode && s.HasUserNudgedInCurrentTrack,
                LastOffsetNudgeUtc = s.IsDemoMode ? s.LastOffsetNudgeUtc : null,
                TrackGeneration = nextGen
            };
        });

        NotifyStateChanged(updated);
        BeginTrackLoad(track, updated.TrackGeneration, forceRefresh: false);
    }

    private void OnMediaSeekDetected(object? sender, TimeSpan position)
    {
        if (_disposed)
        {
            return;
        }

        var updated = UpdateState(s => s with { SeekGeneration = s.SeekGeneration + 1 });
        NotifyStateChanged(updated);
    }

    private void BeginTrackLoad(TrackIdentity? track, long generation, bool forceRefresh)
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

        _ = Task.Run(() => LoadTrackResourcesAsync(track, generation, forceRefresh, ct), CancellationToken.None);
    }

    private async Task LoadTrackResourcesAsync(TrackIdentity track, long generation, bool forceRefresh, CancellationToken ct)
    {
        try
        {
            var settings = Volatile.Read(ref _state).Settings;

            // Hydrate per-track saved offset immediately without waiting on remote HTTP fetches.
            var offsetTask = HydrateTrackOffsetAsync(track.CacheKey, generation, settings.EnablePerTrackSavedOffsets, ct);
            var lyricsTask = FetchAndAnnotateLyricsAsync(track, settings.VisualPreference, forceRefresh, ct);
            var artTask = FetchAlbumArtAndPaletteAsync(ct);

            await Task.WhenAll(offsetTask, lyricsTask, artTask).ConfigureAwait(false);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var (status, document, moodVisual) = await lyricsTask.ConfigureAwait(false);
            var (albumArt, extractedPalette) = await artTask.ConfigureAwait(false);
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
                    : OverlaySnapshotBuilder.CreatePaletteMorph(_state, targetPalette, nowTicks, _timestampFrequency);

                _state = _state with
                {
                    LiveLyricsStatus = status,
                    LiveDocument = document,
                    LiveMoodVisual = moodVisual,
                    LiveTargetPalette = targetPalette,
                    LiveAlbumArt = albumArt,
                    PaletteMorph = morph
                };
                published = _state;
            }

            if (published is not null && !_disposed)
            {
                NotifyStateChanged(published);
            }

            // Spotify / browsers sometimes delay thumbnail stream availability by 300-650ms after MediaPropertiesChanged
            if (albumArt is null && !ct.IsCancellationRequested && !_disposed)
            {
                await Task.Delay(650, ct).ConfigureAwait(false);
                var (retryArt, retryPalette) = await FetchAlbumArtAndPaletteAsync(ct).ConfigureAwait(false);
                if (retryArt is not null && !ct.IsCancellationRequested && !_disposed)
                {
                    RuntimeStateBox? artPublished = null;
                    var retryTicks = _timestampProvider();
                    lock (_stateSync)
                    {
                        if (_state.TrackGeneration != generation)
                        {
                            return;
                        }

                        var nextTargetPalette = retryPalette ?? _state.LiveTargetPalette;
                        var nextMorph = _state.IsDemoMode
                            ? _state.PaletteMorph
                            : OverlaySnapshotBuilder.CreatePaletteMorph(_state, nextTargetPalette, retryTicks, _timestampFrequency);

                        _state = _state with
                        {
                            LiveTargetPalette = nextTargetPalette,
                            LiveAlbumArt = retryArt,
                            PaletteMorph = nextMorph
                        };
                        artPublished = _state;
                    }

                    if (artPublished is not null && !_disposed)
                    {
                        NotifyStateChanged(artPublished);
                    }
                }
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

    private async Task HydrateTrackOffsetAsync(
        string trackCacheKey,
        long generation,
        bool enablePerTrackSavedOffsets,
        CancellationToken ct)
    {
        var loadedOffset = await _timingOffsetService
            .OnTrackChangedAsync(trackCacheKey, enablePerTrackSavedOffsets, ct)
            .ConfigureAwait(false);

        if (ct.IsCancellationRequested)
        {
            return;
        }

        RuntimeStateBox? published = null;
        lock (_stateSync)
        {
            if (_state.TrackGeneration != generation || _state.IsDemoMode || _state.HasUserNudgedInCurrentTrack)
            {
                return;
            }

            if (_state.TimingOffset != loadedOffset)
            {
                _state = _state with { TimingOffset = loadedOffset };
                published = _state;
            }
        }

        if (published is not null && !_disposed)
        {
            NotifyStateChanged(published);
        }
    }

    private async Task<(LyricsStatus Status, LyricsDocument? Document, WordVisualMatch? MoodVisual)> FetchAndAnnotateLyricsAsync(
        TrackIdentity track,
        VisualPreference preference,
        bool forceRefresh,
        CancellationToken ct)
    {
        var result = forceRefresh
            ? await _lyricsOrchestrator.GetLyricsAsync(track, forceRefresh: true, ct).ConfigureAwait(false)
            : await _lyricsOrchestrator.GetLyricsAsync(track, ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested)
        {
            return (result.Status, null, null);
        }

        if (result.Document is null)
        {
            return (result.Status, null, _visualMatcher.InferTrackMoodVisual(track, null, preference));
        }

        var annotated = _visualMatcher.AnnotateDocument(result.Document, preference);
        var mood = _visualMatcher.InferTrackMoodVisual(track, annotated, preference);
        return (result.Status, annotated, mood);
    }

    private async Task<(AlbumArtPayload? Art, AlbumAccentPalette? Palette)> FetchAlbumArtAndPaletteAsync(CancellationToken ct)
    {
        try
        {
            var art = await _mediaProvider.GetCurrentAlbumArtAsync(ct).ConfigureAwait(false);
            if (art is null || ct.IsCancellationRequested)
            {
                return (null, null);
            }

            var palette = _albumColorExtractor?.Extract(art);
            return (art, palette);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogRuntimeAlbumArtFailed(ex);
            return (null, null);
        }
    }

    private OverlayPlaybackSnapshot BuildSnapshot(RuntimeStateBox state, long nowTimestamp) =>
        OverlaySnapshotBuilder.Build(
            state,
            nowTimestamp,
            _timestampFrequency,
            GetOrCreateAnnotatedDemoDocument,
            _mediaProvider.GetInterpolatedPosition,
            _audioSpectrumProvider is null
                ? null
                : _audioSpectrumProvider.GetCurrentMetrics);

    private LyricsDocument GetOrCreateAnnotatedDemoDocument(VisualPreference preference)
    {
        var cached = Volatile.Read(ref _cachedDemo);
        if (cached is not null && cached.Preference == preference)
        {
            return cached.Document;
        }

        var annotated = _visualMatcher.AnnotateDocument(_demoSongProvider.GetDemoDocument(), preference);
        Volatile.Write(ref _cachedDemo, new CachedDemoDocument(annotated, preference));
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

    private sealed record CachedDemoDocument(LyricsDocument Document, VisualPreference Preference);
}
