using System.Collections.Immutable;
using AwesomeAssertions;
using LyricPulse.Core.Audio;
using LyricPulse.Core.Media;
using LyricPulse.Core.Models;
using Xunit;

namespace LyricPulse.Tests;

public sealed class Phase6V23PolishAndDetectionTests
{
    [Fact]
    public void AudioSpectrumAnalyzer_CachesFrameMetricsWithinWindow_AndInvalidatesOnNewPcmSamples()
    {
        var analyzer = new AudioSpectrumAnalyzer(48_000);
        var pcm = new float[2048];
        for (var i = 0; i < pcm.Length; i += 2)
        {
            var sample = MathF.Sin(2f * MathF.PI * 110f * (i / 2f) / 48_000f) * 0.65f;
            pcm[i] = sample;
            pcm[i + 1] = sample;
        }

        analyzer.PushInterleavedFloatSamples(pcm, channels: 2);
        _ = analyzer.ComputeMetrics(timeSeconds: 0.99, isPlaying: true, partyEnergy: 1.0f); // JIT warm-up
        analyzer.PushInterleavedFloatSamples(pcm, channels: 2);
        var first = analyzer.ComputeMetrics(timeSeconds: 1.0, isPlaying: true, partyEnergy: 1.0f);
        var second = analyzer.ComputeMetrics(timeSeconds: 1.004, isPlaying: true, partyEnergy: 1.0f);

        ReferenceEquals(first, second).Should().BeTrue("multi-canvas renders within the 11ms window reuse the cached FFT frame");

        analyzer.PushInterleavedFloatSamples(pcm, channels: 2);
        var third = analyzer.ComputeMetrics(timeSeconds: 1.008, isPlaying: true, partyEnergy: 1.0f);

        ReferenceEquals(second, third).Should().BeFalse("pushing new PCM samples invalidates the cached FFT frame");
    }

    [Fact]
    public void PlayerClassifier_CleanDetectedMetadata_StripsPlatformSuffixesTopicAndSplitsArtistDashTitle()
    {
        PlayerClassifier.CleanDetectedMetadata("Midnight City - YouTube Music", "M83 - Topic", out var title1, out var artist1);
        title1.Should().Be("Midnight City");
        artist1.Should().Be("M83");

        PlayerClassifier.CleanDetectedMetadata("Daft Punk - Instant Crush", string.Empty, out var title2, out var artist2);
        title2.Should().Be("Instant Crush");
        artist2.Should().Be("Daft Punk");

        PlayerClassifier.CleanDetectedMetadata("Part 2 - Finale", string.Empty, out var title3, out var artist3);
        title3.Should().Be("Part 2 - Finale");
        artist3.Should().BeEmpty();
    }

    [Fact]
    public void PlayerClassifier_ClassifyItem_PromotesMusicStreamMetadata_AndRejectsPlainVideos()
    {
        PlayerClassifier.ClassifyItem("Starlight", "Muse - Topic", "", TimeSpan.FromMinutes(4), isVideo: true)
            .Should().Be(MediaItemKind.Music);

        PlayerClassifier.ClassifyItem("Starlight - YouTube Music", "Muse", "", TimeSpan.FromMinutes(4), isVideo: true)
            .Should().Be(MediaItemKind.Music);

        PlayerClassifier.ClassifyItem("Starlight", "Muse", "Black Holes and Revelations", TimeSpan.FromMinutes(4), isVideo: true)
            .Should().Be(MediaItemKind.Music);

        PlayerClassifier.ClassifyItem("Music Video", "Artist", "", TimeSpan.FromMinutes(3), isVideo: true)
            .Should().Be(MediaItemKind.Video);
    }

    [Fact]
    public void MediaSessionSelector_SelectBestPaused_AllowsTitleOnlyTracks_AndPrefersArtistPresent()
    {
        var selector = new MediaSessionSelector();
        var now = DateTimeOffset.UtcNow;

        var titleOnlyPaused = new SessionCandidate(
            "msedge.exe",
            MediaPlayerKind.BrowserOrGeneric,
            MediaItemKind.Music,
            MediaPlaybackState.Paused,
            "Title Only Track",
            string.Empty,
            string.Empty,
            TimeSpan.FromSeconds(30),
            TimeSpan.Zero,
            TimeSpan.FromMinutes(3),
            1.0,
            now,
            IsSystemCurrentSession: true);

        var pickedTitleOnly = selector.Select(
            [titleOnlyPaused],
            MediaSourcePolicy.UniversalFallback,
            ignoreBrowserVideoSessions: true,
            now);

        pickedTitleOnly.Should().NotBeNull();
        pickedTitleOnly!.Title.Should().Be("Title Only Track");

        var withArtistPaused = titleOnlyPaused with
        {
            SourceAppUserModelId = "music.youtube.exe",
            PlayerKind = MediaPlayerKind.DedicatedAudioPlayer,
            Title = "Track With Artist",
            Artist = "Known Artist"
        };

        var freshSelector = new MediaSessionSelector();
        var pickedArtist = freshSelector.Select(
            [titleOnlyPaused, withArtistPaused],
            MediaSourcePolicy.UniversalFallback,
            ignoreBrowserVideoSessions: true,
            now);

        pickedArtist.Should().NotBeNull();
        pickedArtist!.Title.Should().Be("Track With Artist");
    }
}
