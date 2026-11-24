using AwesomeAssertions;
using LyricPulse.Core.Media;
using Xunit;

namespace LyricPulse.Tests.Media;

public sealed class MonotonicPlaybackClockTests
{
    private const long Frequency = 10_000_000; // 1 tick = 100ns (exact TimeSpan ticks)

    [Fact]
    public void GetInterpolatedPosition_AdvancesMonotonicallyWhilePlaying()
    {
        long nowTicks = 1_000 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var clock = new MonotonicPlaybackClock(() => nowTicks, Frequency, () => utc);

        clock.UpdateFromTimeline(
            trackKey: "track-1",
            reportedPosition: TimeSpan.FromSeconds(10),
            startTime: TimeSpan.Zero,
            endTime: TimeSpan.FromMinutes(3),
            playbackRate: 1.0,
            isPlaying: true,
            timelineLastUpdatedUtc: utc);

        nowTicks += (long)(1.25 * Frequency);
        clock.GetInterpolatedPosition().Should().BeCloseTo(TimeSpan.FromSeconds(11.25), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void UpdateFromTimeline_AppliesStalenessOnlyWithinZeroToFiveSeconds()
    {
        long nowTicks = 100 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 10, TimeSpan.Zero);
        var clock = new MonotonicPlaybackClock(() => nowTicks, Frequency, () => utc);

        // Valid 2s staleness -> compensates +2s
        clock.UpdateFromTimeline(
            "track-1",
            TimeSpan.FromSeconds(10),
            TimeSpan.Zero,
            TimeSpan.FromMinutes(3),
            1.0,
            true,
            utc.AddSeconds(-2));
        clock.GetInterpolatedPosition().Should().BeCloseTo(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(1));

        // Corrupt/stale 12s timestamp -> uses raw reported position without extrapolation
        clock.UpdateFromTimeline(
            "track-2",
            TimeSpan.FromSeconds(20),
            TimeSpan.Zero,
            TimeSpan.FromMinutes(3),
            1.0,
            true,
            utc.AddSeconds(-12));
        clock.GetInterpolatedPosition().Should().BeCloseTo(TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(1));

        // MinValue timestamp -> uses raw reported position
        clock.UpdateFromTimeline(
            "track-3",
            TimeSpan.FromSeconds(5),
            TimeSpan.Zero,
            TimeSpan.FromMinutes(3),
            1.0,
            true,
            DateTimeOffset.MinValue);
        clock.GetInterpolatedPosition().Should().BeCloseTo(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void UpdateFromTimeline_IgnoresJitterWithin40msDeadzone()
    {
        long nowTicks = 100 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var clock = new MonotonicPlaybackClock(() => nowTicks, Frequency, () => utc);

        clock.UpdateFromTimeline("track-1", TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromMinutes(3), 1.0, true, utc);

        nowTicks += 2 * Frequency; // est = 12.000s
        utc = utc.AddSeconds(2);

        // +30ms jitter should be ignored (no seek, no jump)
        var update = clock.UpdateFromTimeline("track-1", TimeSpan.FromMilliseconds(12_030), TimeSpan.Zero, TimeSpan.FromMinutes(3), 1.0, true, utc);

        update.Kind.Should().Be(ClockUpdateKind.IgnoredJitter);
        clock.GetInterpolatedPosition().Should().Be(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public void UpdateFromTimeline_SmoothlySlewsDriftBetween40msAnd250ms()
    {
        long nowTicks = 100 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var clock = new MonotonicPlaybackClock(() => nowTicks, Frequency, () => utc);

        clock.UpdateFromTimeline("track-1", TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromMinutes(3), 1.0, true, utc);

        nowTicks += 1 * Frequency; // est = 11.000s
        utc = utc.AddSeconds(1);

        // +160ms drift -> smooth slew
        var update = clock.UpdateFromTimeline("track-1", TimeSpan.FromMilliseconds(11_160), TimeSpan.Zero, TimeSpan.FromMinutes(3), 1.0, true, utc);
        update.Kind.Should().Be(ClockUpdateKind.SmoothSlew);

        // Immediately at t=0 of slew, position shouldn't jump abruptly
        var immediate = clock.GetInterpolatedPosition();
        immediate.Should().BeCloseTo(TimeSpan.FromSeconds(11.0), TimeSpan.FromMilliseconds(5));

        // Halfway through slew (~80ms), partial correction applied
        nowTicks += (long)(0.08 * Frequency);
        var mid = clock.GetInterpolatedPosition();
        mid.Should().BeGreaterThan(TimeSpan.FromMilliseconds(11_120));
        mid.Should().BeLessThan(TimeSpan.FromMilliseconds(11_240));

        // After >= 250ms, slew converges smoothly onto corrected timeline
        nowTicks += (long)(0.25 * Frequency); // total +330ms from slew start
        var settled = clock.GetInterpolatedPosition();
        settled.Should().BeCloseTo(TimeSpan.FromMilliseconds(11_490), TimeSpan.FromMilliseconds(12));
    }

    [Fact]
    public void UpdateFromTimeline_SnapsAndEmitsSeekWhenDriftExceeds250ms()
    {
        long nowTicks = 100 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var clock = new MonotonicPlaybackClock(() => nowTicks, Frequency, () => utc);

        clock.UpdateFromTimeline("track-1", TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromMinutes(3), 1.0, true, utc);
        nowTicks += 2 * Frequency; // est = 12s
        utc = utc.AddSeconds(2);

        var update = clock.UpdateFromTimeline("track-1", TimeSpan.FromSeconds(45), TimeSpan.Zero, TimeSpan.FromMinutes(3), 1.0, true, utc);

        update.Kind.Should().Be(ClockUpdateKind.SeekSnap);
        update.PreviousPosition.Should().BeCloseTo(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(1));
        update.NewPosition.Should().Be(TimeSpan.FromSeconds(45));
        clock.GetInterpolatedPosition().Should().Be(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void Pause_FreezesAtExactInterpolatedPositionWithoutJumpingBack()
    {
        long nowTicks = 100 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var clock = new MonotonicPlaybackClock(() => nowTicks, Frequency, () => utc);

        clock.UpdateFromTimeline("track-1", TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromMinutes(3), 1.0, true, utc);
        nowTicks += (long)(3.4 * Frequency); // est = 13.4s
        utc = utc.AddSeconds(3.4);

        // Stale GSMTC pause reports old 10.0s timeline position; clock must freeze at interpolated 13.4s
        clock.UpdateFromTimeline("track-1", TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromMinutes(3), 1.0, false, utc);

        nowTicks += 10 * Frequency;
        clock.GetInterpolatedPosition().Should().BeCloseTo(TimeSpan.FromSeconds(13.4), TimeSpan.FromMilliseconds(2));
    }

    [Fact]
    public void GetInterpolatedPosition_ClampsAtEndTimeAndLiveStreamStallGuard()
    {
        long nowTicks = 100 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var clock = new MonotonicPlaybackClock(() => nowTicks, Frequency, () => utc);

        // Bounded track clamps to EndTime
        clock.UpdateFromTimeline("track-1", TimeSpan.FromSeconds(30), TimeSpan.Zero, TimeSpan.FromSeconds(36), 1.0, true, utc);
        nowTicks += 20 * Frequency;
        clock.GetInterpolatedPosition().Should().Be(TimeSpan.FromSeconds(36));

        // Live stream (EndTime == 0) stops advancing after 15 minute stall guard
        clock.UpdateFromTimeline("stream-1", TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.Zero, 1.0, true, utc);
        nowTicks += 20 * 60 * Frequency;
        clock.GetInterpolatedPosition().Should().Be(TimeSpan.FromSeconds(10) + TimeSpan.FromMinutes(15));
    }
}
