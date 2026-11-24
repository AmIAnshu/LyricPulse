using System.Collections.Immutable;
using AwesomeAssertions;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Media;
using LyricPulse.Core.Models;
using Xunit;

namespace LyricPulse.Tests.Media;

public sealed class MediaSessionCoordinatorTests
{
    private const long Frequency = 10_000_000;

    [Fact]
    public void ProcessCandidates_RaisesSessionTrackAndSeekEventsAndClearsOnEmpty()
    {
        long ticks = 100 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var clock = new MonotonicPlaybackClock(() => ticks, Frequency, () => utc);
        var coordinator = new MediaSessionCoordinator(new TitleNormalizer(), new MediaSessionSelector(), clock, () => utc);

        var sessionEvents = new List<MediaSessionSnapshot?>();
        var trackEvents = new List<TrackIdentity?>();
        var seekEvents = new List<TimeSpan>();

        coordinator.SessionChanged += (_, s) => sessionEvents.Add(s);
        coordinator.TrackChanged += (_, t) => trackEvents.Add(t);
        coordinator.SeekDetected += (_, pos) => seekEvents.Add(pos);

        var candidate1 = new SessionCandidate(
            "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify",
            MediaPlayerKind.Spotify,
            MediaItemKind.Music,
            MediaPlaybackState.Playing,
            "Neon Horizon",
            "LyricPulse Studio",
            "Demo",
            TimeSpan.FromSeconds(5),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(36),
            1.0,
            utc,
            true);

        var snap1 = coordinator.ProcessCandidates([candidate1]);
        snap1.Should().NotBeNull();
        coordinator.CurrentSession.Should().Be(snap1);
        sessionEvents.Should().HaveCount(1);
        trackEvents.Should().HaveCount(1);
        trackEvents[0]!.NormalizedTitle.Should().Be("Neon Horizon");

        // Advance 2s and seek to 20s -> raises SeekDetected
        ticks += 2 * Frequency;
        utc = utc.AddSeconds(2);
        var candidateSeek = candidate1 with { ReportedPosition = TimeSpan.FromSeconds(20), LastUpdatedUtc = utc };
        coordinator.ProcessCandidates([candidateSeek]);
        seekEvents.Should().ContainSingle().Which.Should().Be(TimeSpan.FromSeconds(20));
        coordinator.GetInterpolatedPosition().Should().Be(TimeSpan.FromSeconds(20));

        // Update source policy
        coordinator.UpdateSourcePolicy(MediaSourcePolicy.UniversalFallback, ["Discord.exe"], ignoreBrowserVideoSessions: false);
        coordinator.SourcePolicy.Should().Be(MediaSourcePolicy.UniversalFallback);
        coordinator.IgnoreBrowserVideoSessions.Should().BeFalse();

        // Empty candidates clears active session and notifies listeners
        var cleared = coordinator.ProcessCandidates(ImmutableArray<SessionCandidate>.Empty);
        cleared.Should().BeNull();
        coordinator.CurrentSession.Should().BeNull();
        sessionEvents[^1].Should().BeNull();
        trackEvents[^1].Should().BeNull();
    }

    [Fact]
    public async Task ReadAndDeduplicateThumbnailAsync_Enforces10MbCapAndSha256Deduplication()
    {
        var coordinator = new MediaSessionCoordinator(new TitleNormalizer());
        var sampleBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4, 5, 6 };

        await using var s1 = new MemoryStream(sampleBytes);
        var payload1 = await coordinator.ReadAndDeduplicateThumbnailAsync(s1, sampleBytes.Length);
        payload1.Should().NotBeNull();
        payload1!.Sha256Hash.Should().MatchRegex("^[0-9a-f]{64}$");
        payload1.ImageBytes.Length.Should().Be(sampleBytes.Length);

        // Identical bytes reuse exact cached payload instance via SHA-256 hash match
        await using var s2 = new MemoryStream(sampleBytes);
        var payload2 = await coordinator.ReadAndDeduplicateThumbnailAsync(s2, sampleBytes.Length);
        ReferenceEquals(payload1, payload2).Should().BeTrue();
        coordinator.CachedAlbumArt.Should().BeSameAs(payload1);

        // Empty stream returns null
        await using var empty = new MemoryStream([]);
        (await coordinator.ReadAndDeduplicateThumbnailAsync(empty, 0)).Should().BeNull();

        // Declared length > 10 MB rejected immediately without reading
        await using var fakeLarge = new MemoryStream(sampleBytes);
        (await coordinator.ReadAndDeduplicateThumbnailAsync(fakeLarge, MediaSessionCoordinator.MaxThumbnailBytes + 1)).Should().BeNull();
    }
}
