using AwesomeAssertions;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using Xunit;

namespace LyricPulse.Tests.Lyrics;

public sealed class TimingOffsetServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "LyricPulseTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void ComputeEffectiveLookupTime_FollowsLockedNegativeIsEarlierSignConvention()
    {
        // LOCKED CONTRACT (ROADMAP #6 / Graphify Risk 1):
        // -0.5s = Lyrics appear EARLIER -> EffectiveLookupTime = Position - (-0.5s) = Position + 0.5s
        // So a lyric cue at 10.0s activates at PlaybackPosition = 9.5s!
        var position = TimeSpan.FromSeconds(9.5);
        var duration = TimeSpan.FromMinutes(3);

        var earlierLookup = TimingOffsetService.ComputeEffectiveLookupTime(
            position,
            timingOffset: TimeSpan.FromMilliseconds(-500),
            duration);
        earlierLookup.Should().Be(TimeSpan.FromSeconds(10.0));

        // +0.5s = Lyrics appear LATER -> EffectiveLookupTime = Position - (+0.5s) = Position - 0.5s
        // So a lyric cue at 10.0s activates at PlaybackPosition = 10.5s!
        var laterLookup = TimingOffsetService.ComputeEffectiveLookupTime(
            TimeSpan.FromSeconds(10.5),
            timingOffset: TimeSpan.FromMilliseconds(500),
            duration);
        laterLookup.Should().Be(TimeSpan.FromSeconds(10.0));

        // Clamps cleanly at [0, duration]
        TimingOffsetService.ComputeEffectiveLookupTime(TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(1.0), duration)
            .Should().Be(TimeSpan.Zero);
        TimingOffsetService.ComputeEffectiveLookupTime(TimeSpan.FromSeconds(179.8), TimeSpan.FromSeconds(-1.0), duration)
            .Should().Be(duration);
    }

    [Fact]
    public async Task NudgeAndPersist_ClampsRangeAndPersistsPerTrackWhenEnabled()
    {
        var storePath = Path.Combine(_tempDir, "track-offsets.json");
        var store = new TrackOffsetStore(storePath);
        var sut = new TimingOffsetService(store);

        await sut.OnTrackChangedAsync("track-alpha", enablePerTrackSavedOffsets: true);
        sut.Current.Offset.Should().Be(TimeSpan.Zero);

        await sut.NudgeAsync(TimeSpan.FromMilliseconds(-500), enablePerTrackSavedOffsets: true);
        sut.Current.Offset.Should().Be(TimeSpan.FromMilliseconds(-500));
        sut.Current.IsPersistedOverride.Should().BeTrue();

        // Clamp upper & lower boundaries [-15s, +15s]
        await sut.NudgeAsync(TimeSpan.FromSeconds(-30), enablePerTrackSavedOffsets: true);
        sut.Current.Offset.Should().Be(TimingOffsetState.MinOffset);

        await sut.NudgeAsync(TimeSpan.FromSeconds(60), enablePerTrackSavedOffsets: true);
        sut.Current.Offset.Should().Be(TimingOffsetState.MaxOffset);

        // Set back to -1000ms and reload via a brand new service + store instance
        await sut.ResetAsync(enablePerTrackSavedOffsets: true);
        await sut.NudgeAsync(TimeSpan.FromMilliseconds(-1000), enablePerTrackSavedOffsets: true);

        var freshService = new TimingOffsetService(new TrackOffsetStore(storePath));
        await freshService.OnTrackChangedAsync("track-alpha", enablePerTrackSavedOffsets: true);
        freshService.Current.Offset.Should().Be(TimeSpan.FromMilliseconds(-1000));
        freshService.Current.IsPersistedOverride.Should().BeTrue();

        // When per-track persistence is disabled, switching tracks resets offset to zero
        await freshService.OnTrackChangedAsync("track-alpha", enablePerTrackSavedOffsets: false);
        freshService.Current.Offset.Should().Be(TimeSpan.Zero);
        freshService.Current.IsPersistedOverride.Should().BeFalse();
    }
}
