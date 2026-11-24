using System.Diagnostics;

namespace LyricPulse.Core.Media;

public enum ClockUpdateKind
{
    HardSnap = 0,
    IgnoredJitter = 1,
    SmoothSlew = 2,
    SeekSnap = 3,
    PausedFreeze = 4
}

public readonly record struct ClockUpdateResult(
    ClockUpdateKind Kind,
    TimeSpan PreviousPosition,
    TimeSpan NewPosition);

/// <summary>
/// Lock-free, zero-allocation monotonic playback clock driven by Stopwatch timestamps
/// with drift clamping (&lt;=40ms deadzone, 40-250ms exponential slew, &gt;250ms seek snap).
/// </summary>
public sealed class MonotonicPlaybackClock
{
    public static readonly TimeSpan JitterDeadzone = TimeSpan.FromMilliseconds(40);
    public static readonly TimeSpan MaxSmoothSlewDrift = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan MaxTrustedStaleness = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan LiveStreamStallGuard = TimeSpan.FromMinutes(15);

    private const double SlewTimeConstantSeconds = 0.08;
    private const double MinPlaybackRate = 0.25;
    private const double MaxPlaybackRate = 4.0;

    private readonly Func<long> _timestampProvider;
    private readonly long _timestampFrequency;
    private readonly Func<DateTimeOffset> _utcNowProvider;

    private ClockAnchorBox _anchor = ClockAnchorBox.Empty;

    public MonotonicPlaybackClock()
        : this(Stopwatch.GetTimestamp, Stopwatch.Frequency, static () => DateTimeOffset.UtcNow)
    {
    }

    public MonotonicPlaybackClock(
        Func<long> timestampProvider,
        long timestampFrequency,
        Func<DateTimeOffset> utcNowProvider)
    {
        _timestampProvider = timestampProvider ?? throw new ArgumentNullException(nameof(timestampProvider));
        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency), "Timestamp frequency must be positive.");
        }

        _timestampFrequency = timestampFrequency;
        _utcNowProvider = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));
    }

    public TimeSpan GetInterpolatedPosition() => EvaluatePosition(Volatile.Read(ref _anchor), _timestampProvider());

    public void Reset() => Volatile.Write(ref _anchor, ClockAnchorBox.Empty);

    public ClockUpdateResult UpdateFromTimeline(
        string? trackKey,
        TimeSpan reportedPosition,
        TimeSpan startTime,
        TimeSpan endTime,
        double playbackRate,
        bool isPlaying,
        DateTimeOffset timelineLastUpdatedUtc)
    {
        var nowTicks = _timestampProvider();
        var nowUtc = _utcNowProvider();
        var rate = double.IsFinite(playbackRate) && playbackRate > 0
            ? Math.Clamp(playbackRate, MinPlaybackRate, MaxPlaybackRate)
            : 1.0;

        var safeStart = startTime < TimeSpan.Zero ? TimeSpan.Zero : startTime;
        var safeEnd = endTime > safeStart ? endTime : TimeSpan.Zero;
        var current = Volatile.Read(ref _anchor);
        var currentEst = EvaluatePosition(current, nowTicks);

        var reportedNow = ComputeReportedNow(reportedPosition, rate, isPlaying, timelineLastUpdatedUtc, nowUtc);
        reportedNow = ClampPosition(reportedNow, safeStart, safeEnd);

        var sameTrack = current.HasAnchor &&
                        string.Equals(current.TrackKey, trackKey, StringComparison.Ordinal);

        if (!sameTrack)
        {
            WriteAnchor(trackKey, reportedNow, nowTicks, rate, safeStart, safeEnd, isPlaying, TimeSpan.Zero);
            return new ClockUpdateResult(ClockUpdateKind.HardSnap, currentEst, reportedNow);
        }

        // Transition Playing -> Paused: freeze at exact interpolated position (never jump backward to stale timeline).
        if (current.IsPlaying && !isPlaying)
        {
            var frozen = ClampPosition(currentEst, safeStart, safeEnd);
            WriteAnchor(trackKey, frozen, nowTicks, rate, safeStart, safeEnd, isPlaying: false, TimeSpan.Zero);
            return new ClockUpdateResult(ClockUpdateKind.PausedFreeze, currentEst, frozen);
        }

        // Transition Paused -> Playing or still Paused with new position.
        if (!current.IsPlaying)
        {
            WriteAnchor(trackKey, reportedNow, nowTicks, rate, safeStart, safeEnd, isPlaying, TimeSpan.Zero);
            return new ClockUpdateResult(ClockUpdateKind.HardSnap, currentEst, reportedNow);
        }

        var drift = reportedNow - currentEst;
        var absDrift = drift.Duration();

        if (absDrift <= JitterDeadzone)
        {
            return new ClockUpdateResult(ClockUpdateKind.IgnoredJitter, currentEst, currentEst);
        }

        if (absDrift <= MaxSmoothSlewDrift)
        {
            WriteAnchor(trackKey, currentEst, nowTicks, rate, safeStart, safeEnd, isPlaying: true, pendingDrift: drift);
            return new ClockUpdateResult(ClockUpdateKind.SmoothSlew, currentEst, reportedNow);
        }

        WriteAnchor(trackKey, reportedNow, nowTicks, rate, safeStart, safeEnd, isPlaying: true, pendingDrift: TimeSpan.Zero);
        return new ClockUpdateResult(ClockUpdateKind.SeekSnap, currentEst, reportedNow);
    }

    private static TimeSpan ComputeReportedNow(
        TimeSpan reportedPosition,
        double rate,
        bool isPlaying,
        DateTimeOffset timelineLastUpdatedUtc,
        DateTimeOffset nowUtc)
    {
        if (!isPlaying || timelineLastUpdatedUtc.Year < 2000)
        {
            return reportedPosition;
        }

        var staleness = nowUtc - timelineLastUpdatedUtc;
        if (staleness >= TimeSpan.Zero && staleness <= MaxTrustedStaleness)
        {
            return reportedPosition + TimeSpan.FromTicks((long)(staleness.Ticks * rate));
        }

        return reportedPosition;
    }

    private TimeSpan EvaluatePosition(ClockAnchorBox anchor, long nowTicks)
    {
        if (!anchor.HasAnchor)
        {
            return TimeSpan.Zero;
        }

        if (!anchor.IsPlaying)
        {
            return anchor.BasePosition;
        }

        var deltaTicks = Math.Max(0L, nowTicks - anchor.AnchorStopwatchTimestamp);
        var elapsedSeconds = deltaTicks / (double)_timestampFrequency;

        if (anchor.EndTime == TimeSpan.Zero && elapsedSeconds > LiveStreamStallGuard.TotalSeconds)
        {
            elapsedSeconds = LiveStreamStallGuard.TotalSeconds;
        }

        var rawSeconds = anchor.BasePosition.TotalSeconds + (elapsedSeconds * anchor.PlaybackRate);

        if (anchor.PendingDrift != TimeSpan.Zero)
        {
            var slewProgress = elapsedSeconds >= MaxSmoothSlewDrift.TotalSeconds
                ? 1.0
                : 1.0 - Math.Exp(-elapsedSeconds / SlewTimeConstantSeconds);
            rawSeconds += anchor.PendingDrift.TotalSeconds * slewProgress;
        }

        var raw = TimeSpan.FromSeconds(rawSeconds);
        return ClampPosition(raw, anchor.StartTime, anchor.EndTime);
    }

    private void WriteAnchor(
        string? trackKey,
        TimeSpan basePosition,
        long anchorTimestamp,
        double playbackRate,
        TimeSpan startTime,
        TimeSpan endTime,
        bool isPlaying,
        TimeSpan pendingDrift)
    {
        Volatile.Write(
            ref _anchor,
            new ClockAnchorBox(
                HasAnchor: true,
                TrackKey: trackKey,
                BasePosition: basePosition,
                AnchorStopwatchTimestamp: anchorTimestamp,
                PlaybackRate: playbackRate,
                StartTime: startTime,
                EndTime: endTime,
                IsPlaying: isPlaying,
                PendingDrift: pendingDrift));
    }

    private static TimeSpan ClampPosition(TimeSpan position, TimeSpan startTime, TimeSpan endTime)
    {
        if (position < startTime)
        {
            return startTime;
        }

        if (endTime > startTime && position > endTime)
        {
            return endTime;
        }

        return position;
    }

    private sealed record ClockAnchorBox(
        bool HasAnchor,
        string? TrackKey,
        TimeSpan BasePosition,
        long AnchorStopwatchTimestamp,
        double PlaybackRate,
        TimeSpan StartTime,
        TimeSpan EndTime,
        bool IsPlaying,
        TimeSpan PendingDrift)
    {
        public static ClockAnchorBox Empty { get; } = new(
            HasAnchor: false,
            TrackKey: null,
            BasePosition: TimeSpan.Zero,
            AnchorStopwatchTimestamp: 0,
            PlaybackRate: 1.0,
            StartTime: TimeSpan.Zero,
            EndTime: TimeSpan.Zero,
            IsPlaying: false,
            PendingDrift: TimeSpan.Zero);
    }
}
