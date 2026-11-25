using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Runtime;

/// <summary>
/// Pure, lock-free snapshot projection and palette morph helpers for <see cref="OverlayRuntimeController"/>.
/// </summary>
internal static class OverlaySnapshotBuilder
{
    public const double NormalMorphDurationSeconds = 0.650;
    public const double ReducedMotionMorphDurationSeconds = 0.150;

    public static OverlayPlaybackSnapshot Build(
        RuntimeStateBox state,
        long nowTimestamp,
        long timestampFrequency,
        Func<VisualPreference, LyricsDocument> demoDocumentResolver,
        Func<TimeSpan> livePositionResolver)
    {
        var targetPalette = ResolveTargetPalette(state);
        var activePalette = state.Settings.AutoSyncColors
            ? state.PaletteMorph.Evaluate(nowTimestamp, timestampFrequency)
            : targetPalette;

        return state.IsDemoMode
            ? BuildDemoSnapshot(state, nowTimestamp, timestampFrequency, demoDocumentResolver, activePalette, targetPalette)
            : BuildLiveSnapshot(state, livePositionResolver, activePalette, targetPalette);
    }

    public static bool CanAdjustTimingOffset(RuntimeStateBox state)
    {
        var hasSyncedTrack = state.IsDemoMode ||
                             (state.LiveTrack is not null &&
                              state.LiveLyricsStatus == LyricsStatus.Synced &&
                              state.LiveDocument is not null);
        if (!hasSyncedTrack)
        {
            return false;
        }

        return state.IsStageWindowOpen || (state.Settings.LyricsVisible && !state.IsFullscreenHidden);
    }

    public static AlbumAccentPalette ResolveTargetPalette(RuntimeStateBox state)
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

    public static PaletteMorphState CreatePaletteMorph(
        RuntimeStateBox currentState,
        AlbumAccentPalette targetPalette,
        long nowTimestamp,
        long timestampFrequency)
    {
        var fromPalette = currentState.PaletteMorph.Evaluate(nowTimestamp, timestampFrequency);
        var durationSec = currentState.IsEffectiveReducedMotion
            ? ReducedMotionMorphDurationSeconds
            : NormalMorphDurationSeconds;
        return new PaletteMorphState(fromPalette, targetPalette, nowTimestamp, durationSec);
    }

    private static OverlayPlaybackSnapshot BuildDemoSnapshot(
        RuntimeStateBox state,
        long nowTimestamp,
        long timestampFrequency,
        Func<VisualPreference, LyricsDocument> demoDocumentResolver,
        AlbumAccentPalette activePalette,
        AlbumAccentPalette targetPalette)
    {
        var demoDoc = state.DemoDocument ?? demoDocumentResolver(state.Settings.VisualPreference);
        var elapsedSec = Math.Max(0L, nowTimestamp - state.DemoStartTimestamp) / (double)timestampFrequency;
        var loopSec = elapsedSec % DemoSongProvider.LoopDuration.TotalSeconds;
        var rawPos = TimeSpan.FromSeconds(loopSec);
        var effectivePos = TimingOffsetService.ComputeEffectiveLookupTime(rawPos, state.TimingOffset.Offset, DemoSongProvider.LoopDuration);
        var slice = ActiveLyricSlice.Resolve(demoDoc, effectivePos, DemoSongProvider.LoopDuration);

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
            SeekGeneration: state.SeekGeneration,
            IsOsReducedMotion: state.IsOsReducedMotion,
            OffsetGeneration: state.OffsetGeneration,
            IsStageWindowOpen: state.IsStageWindowOpen,
            TrackMoodVisual: state.DemoMoodVisual);
    }

    private static OverlayPlaybackSnapshot BuildLiveSnapshot(
        RuntimeStateBox state,
        Func<TimeSpan> livePositionResolver,
        AlbumAccentPalette activePalette,
        AlbumAccentPalette targetPalette)
    {
        var session = state.LiveSession;
        var rawLivePos = livePositionResolver();
        var duration = state.LiveTrack?.Duration ?? session?.EndTime ?? TimeSpan.Zero;
        var effectiveLivePos = TimingOffsetService.ComputeEffectiveLookupTime(rawLivePos, state.TimingOffset.Offset, duration);
        var liveSlice = ActiveLyricSlice.Resolve(state.LiveDocument, effectiveLivePos, duration);

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
            SeekGeneration: state.SeekGeneration,
            IsOsReducedMotion: state.IsOsReducedMotion,
            OffsetGeneration: state.OffsetGeneration,
            IsStageWindowOpen: state.IsStageWindowOpen,
            TrackMoodVisual: state.LiveMoodVisual);
    }
}
