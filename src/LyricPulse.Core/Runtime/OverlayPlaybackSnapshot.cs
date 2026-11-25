using LyricPulse.Core.Audio;
using LyricPulse.Core.Color;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Runtime;

/// <summary>
/// Immutable OKLab palette crossfade descriptor evaluated lock-free on each frame.
/// </summary>
public sealed record PaletteMorphState(
    AlbumAccentPalette FromPalette,
    AlbumAccentPalette ToPalette,
    long StartTimestamp,
    double DurationSeconds)
{
    public static PaletteMorphState Static(AlbumAccentPalette palette) =>
        new(palette, palette, 0, 0.0);

    public AlbumAccentPalette Evaluate(long nowTimestamp, long timestampFrequency)
    {
        if (DurationSeconds <= 0.0 || ReferenceEquals(FromPalette, ToPalette) || FromPalette == ToPalette)
        {
            return ToPalette;
        }

        var elapsedSec = Math.Max(0L, nowTimestamp - StartTimestamp) / (double)timestampFrequency;
        if (elapsedSec >= DurationSeconds)
        {
            return ToPalette;
        }

        return OklabColorMath.MorphPalette(FromPalette, ToPalette, elapsedSec / DurationSeconds);
    }

    public PaletteMorphState ClampRemainingDuration(long nowTimestamp, long timestampFrequency, double maxDurationSeconds)
    {
        if (DurationSeconds <= 0.0 || ReferenceEquals(FromPalette, ToPalette) || FromPalette == ToPalette)
        {
            return this;
        }

        var elapsedSec = Math.Max(0L, nowTimestamp - StartTimestamp) / (double)timestampFrequency;
        var remainingSec = DurationSeconds - elapsedSec;
        if (remainingSec <= maxDurationSeconds)
        {
            return this;
        }

        var currentColor = Evaluate(nowTimestamp, timestampFrequency);
        return new PaletteMorphState(currentColor, ToPalette, nowTimestamp, maxDurationSeconds);
    }
}

/// <summary>
/// Immutable per-frame runtime state snapshot sampled lock-free by the 60-240 FPS
/// SkiaSharp render host and observed by System Tray / Gallery UI controllers.
/// </summary>
public sealed record OverlayPlaybackSnapshot(
    TrackIdentity? Track,
    LyricsDocument? Document,
    LyricsStatus LyricsStatus,
    MediaPlaybackState PlaybackState,
    MediaPlayerKind PlayerKind,
    string? SourceAppUserModelId,
    TimeSpan RawPosition,
    TimeSpan EffectivePosition,
    ActiveLyricSlice ActiveSlice,
    AlbumAccentPalette ActivePalette,
    AlbumAccentPalette TargetPalette,
    TimingOffsetState TimingOffset,
    AppSettings Settings,
    bool IsPlaying,
    bool IsDemoMode,
    bool IsFullscreenHidden,
    DateTimeOffset? PausedSinceUtc,
    DateTimeOffset? LastOffsetNudgeUtc,
    long TrackGeneration,
    long SeekGeneration,
    bool IsOsReducedMotion = false,
    long OffsetGeneration = 0,
    bool IsStageWindowOpen = false,
    WordVisualMatch? TrackMoodVisual = null,
    AlbumArtPayload? AlbumArt = null,
    AudioFrameMetrics? AudioMetrics = null)
{
    public int ActiveLineIndex => ActiveSlice.ActiveLineIndex;
    public int ActiveWordIndex => ActiveSlice.ActiveWordIndex;
    public LyricLine? PreviousLine => ActiveSlice.PreviousLine;
    public LyricLine? CurrentLine => ActiveSlice.CurrentLine;
    public LyricLine? NextLine => ActiveSlice.NextLine;
    public double WordProgress => ActiveSlice.WordProgress;

    public bool IsLyricsVisible => Settings.LyricsVisible && !IsFullscreenHidden;
    public bool IsEffectiveReducedMotion => Settings.ReducedMotion || IsOsReducedMotion;
    public AudioFrameMetrics EffectiveAudioMetrics => AudioMetrics ?? AudioFrameMetrics.Silent;
}

internal sealed record RuntimeStateBox(
    AppSettings Settings,
    MediaSessionSnapshot? LiveSession,
    TrackIdentity? LiveTrack,
    LyricsDocument? LiveDocument,
    LyricsStatus LiveLyricsStatus,
    WordVisualMatch? LiveMoodVisual,
    AlbumAccentPalette LiveTargetPalette,
    AlbumArtPayload? LiveAlbumArt,
    PaletteMorphState PaletteMorph,
    TimingOffsetState TimingOffset,
    bool IsDemoMode,
    long DemoStartTimestamp,
    LyricsDocument? DemoDocument,
    WordVisualMatch? DemoMoodVisual,
    bool IsFullscreenHidden,
    bool IsOsReducedMotion,
    bool IsStageWindowOpen,
    bool HasUserNudgedInCurrentTrack,
    DateTimeOffset? PausedSinceUtc,
    DateTimeOffset? LastOffsetNudgeUtc,
    long TrackGeneration,
    long SeekGeneration,
    long OffsetGeneration)
{
    public bool IsEffectiveReducedMotion => Settings.ReducedMotion || IsOsReducedMotion;

    public static RuntimeStateBox Initial { get; } = new(
        Settings: AppSettings.Default,
        LiveSession: null,
        LiveTrack: null,
        LiveDocument: null,
        LiveLyricsStatus: LyricsStatus.NotFound,
        LiveMoodVisual: null,
        LiveTargetPalette: AlbumAccentPalette.Default,
        LiveAlbumArt: null,
        PaletteMorph: PaletteMorphState.Static(AlbumAccentPalette.Default),
        TimingOffset: new TimingOffsetState(null, TimeSpan.Zero, false),
        IsDemoMode: false,
        DemoStartTimestamp: 0,
        DemoDocument: null,
        DemoMoodVisual: null,
        IsFullscreenHidden: false,
        IsOsReducedMotion: false,
        IsStageWindowOpen: false,
        HasUserNudgedInCurrentTrack: false,
        PausedSinceUtc: null,
        LastOffsetNudgeUtc: null,
        TrackGeneration: 0,
        SeekGeneration: 0,
        OffsetGeneration: 0);
}
