using System.Collections.Immutable;
using AwesomeAssertions;
using LyricPulse.Core.Media;
using LyricPulse.Core.Models;
using Xunit;

namespace LyricPulse.Tests.Media;

public sealed class MediaSessionSelectorTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", MediaPlayerKind.Spotify, 100)]
    [InlineData("C:\\Users\\Test\\AppData\\Roaming\\Spotify\\Spotify.exe", MediaPlayerKind.Spotify, 100)]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", MediaPlayerKind.AppleMusic, 90)]
    [InlineData("TIDAL.exe", MediaPlayerKind.DedicatedAudioPlayer, 60)]
    [InlineData("foobar2000.exe", MediaPlayerKind.DedicatedAudioPlayer, 60)]
    [InlineData("MusicBee.exe", MediaPlayerKind.DedicatedAudioPlayer, 60)]
    [InlineData("chrome.exe", MediaPlayerKind.BrowserOrGeneric, 30)]
    [InlineData("msedge.exe", MediaPlayerKind.BrowserOrGeneric, 30)]
    [InlineData("Discord.exe", MediaPlayerKind.Ignored, -1)]
    [InlineData("Teams.exe", MediaPlayerKind.Ignored, -1)]
    [InlineData("Zoom.exe", MediaPlayerKind.Ignored, -1)]
    public void ClassifyPlayer_AssignsExpectedKindAndPriority(string aumid, MediaPlayerKind expectedKind, int expectedPriority)
    {
        var blocked = AppSettings.Default.BlockedAppIds;
        var kind = PlayerClassifier.ClassifyPlayer(aumid, blocked);

        kind.Should().Be(expectedKind);
        PlayerClassifier.GetPriorityWeight(kind).Should().Be(expectedPriority);
    }

    [Fact]
    public void ClassifyItem_DetectsSpotifyAdsAndPodcastsAndEmptyTitles()
    {
        PlayerClassifier.ClassifyItem("Advertisement", "", "", TimeSpan.FromSeconds(30), isVideo: false)
            .Should().Be(MediaItemKind.Advertisement);

        PlayerClassifier.ClassifyItem("Spotify", "Spotify", "", TimeSpan.FromSeconds(25), isVideo: false)
            .Should().Be(MediaItemKind.Advertisement);

        PlayerClassifier.ClassifyItem("Episode 42", "Host", "Show", TimeSpan.FromMinutes(45), isVideo: false)
            .Should().Be(MediaItemKind.PodcastOrLongForm);

        PlayerClassifier.ClassifyItem("   ", "Artist", "Album", TimeSpan.FromMinutes(3), isVideo: false)
            .Should().Be(MediaItemKind.Unknown);

        PlayerClassifier.ClassifyItem("Music Video", "Artist", "", TimeSpan.FromMinutes(3), isVideo: true)
            .Should().Be(MediaItemKind.Video);

        PlayerClassifier.ClassifyItem("Neon Horizon", "LyricPulse Studio", "Demo", TimeSpan.FromSeconds(36), isVideo: false)
            .Should().Be(MediaItemKind.Music);
    }

    [Fact]
    public void Select_SpotifyPlayingBeatsAppleMusicPlaying()
    {
        var selector = new MediaSessionSelector();
        var candidates = ImmutableArray.Create(
            CreateCandidate("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", MediaPlayerKind.AppleMusic, MediaPlaybackState.Playing, "Apple Song", "Artist A", isCurrent: true),
            CreateCandidate("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", MediaPlayerKind.Spotify, MediaPlaybackState.Playing, "Spotify Song", "Artist S", isCurrent: false));

        var selected = selector.Select(candidates, MediaSourcePolicy.SupportedPlayersOnly, ignoreBrowserVideoSessions: true, BaseTime);

        selected.Should().NotBeNull();
        selected!.PlayerKind.Should().Be(MediaPlayerKind.Spotify);
        selected.Title.Should().Be("Spotify Song");
    }

    [Fact]
    public void Select_AppleMusicPlayingBeatsPausedSpotify()
    {
        var selector = new MediaSessionSelector();
        var candidates = ImmutableArray.Create(
            CreateCandidate("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", MediaPlayerKind.Spotify, MediaPlaybackState.Paused, "Spotify Song", "Artist S"),
            CreateCandidate("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", MediaPlayerKind.AppleMusic, MediaPlaybackState.Playing, "Apple Song", "Artist A"));

        var selected = selector.Select(candidates, MediaSourcePolicy.SupportedPlayersOnly, ignoreBrowserVideoSessions: true, BaseTime);

        selected.Should().NotBeNull();
        selected!.PlayerKind.Should().Be(MediaPlayerKind.AppleMusic);
    }

    [Fact]
    public void Select_SupportedPlayersOnly_ExcludesDedicatedAndBrowserSessions()
    {
        var selector = new MediaSessionSelector();
        var candidates = ImmutableArray.Create(
            CreateCandidate("foobar2000.exe", MediaPlayerKind.DedicatedAudioPlayer, MediaPlaybackState.Playing, "Track 1", "Artist 1"),
            CreateCandidate("chrome.exe", MediaPlayerKind.BrowserOrGeneric, MediaPlaybackState.Playing, "Track 2", "Artist 2"));

        var selected = selector.Select(candidates, MediaSourcePolicy.SupportedPlayersOnly, ignoreBrowserVideoSessions: true, BaseTime);

        selected.Should().BeNull();
    }

    [Fact]
    public void Select_UniversalFallback_FiltersBrowserVideoSessionsWhenEnabled()
    {
        var selector = new MediaSessionSelector();
        var videoCandidate = CreateCandidate("chrome.exe", MediaPlayerKind.BrowserOrGeneric, MediaPlaybackState.Playing, "C# Tutorial", "Channel", itemKind: MediaItemKind.Video);
        var audioCandidate = CreateCandidate("msedge.exe", MediaPlayerKind.BrowserOrGeneric, MediaPlaybackState.Playing, "Blinding Lights", "The Weeknd", itemKind: MediaItemKind.Music);

        var filtered = selector.Select([videoCandidate], MediaSourcePolicy.UniversalFallback, ignoreBrowserVideoSessions: true, BaseTime);
        filtered.Should().BeNull();

        var allowed = selector.Select([videoCandidate], MediaSourcePolicy.UniversalFallback, ignoreBrowserVideoSessions: false, BaseTime);
        allowed.Should().NotBeNull();

        var prefersAudio = selector.Select([videoCandidate, audioCandidate], MediaSourcePolicy.UniversalFallback, ignoreBrowserVideoSessions: true, BaseTime);
        prefersAudio!.Title.Should().Be("Blinding Lights");
    }

    [Fact]
    public void Select_HoldsStickySessionDuring1500msTrackTransitionHysteresis()
    {
        var selector = new MediaSessionSelector();
        var spotifyAumid = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify";
        var browserAumid = "chrome.exe";

        var t0 = BaseTime;
        var initial = selector.Select(
            [CreateCandidate(spotifyAumid, MediaPlayerKind.Spotify, MediaPlaybackState.Playing, "Song 1", "Artist")],
            MediaSourcePolicy.UniversalFallback,
            ignoreBrowserVideoSessions: true,
            t0);
        initial!.SourceAppUserModelId.Should().Be(spotifyAumid);

        // At +500ms, Spotify enters Changing state with empty title while a browser starts playing.
        var t1 = t0.AddMilliseconds(500);
        var duringTransition = selector.Select(
            [
                CreateCandidate(spotifyAumid, MediaPlayerKind.Spotify, MediaPlaybackState.Changing, "", ""),
                CreateCandidate(browserAumid, MediaPlayerKind.BrowserOrGeneric, MediaPlaybackState.Playing, "YouTube Music", "Artist B")
            ],
            MediaSourcePolicy.UniversalFallback,
            ignoreBrowserVideoSessions: true,
            t1);
        duringTransition!.SourceAppUserModelId.Should().Be(spotifyAumid);

        // After > 1500ms grace period expires, lower-priority playing session takes over.
        var t2 = t0.AddMilliseconds(2100);
        var afterHysteresis = selector.Select(
            [
                CreateCandidate(spotifyAumid, MediaPlayerKind.Spotify, MediaPlaybackState.Changing, "", ""),
                CreateCandidate(browserAumid, MediaPlayerKind.BrowserOrGeneric, MediaPlaybackState.Playing, "YouTube Music", "Artist B")
            ],
            MediaSourcePolicy.UniversalFallback,
            ignoreBrowserVideoSessions: true,
            t2);
        afterHysteresis!.SourceAppUserModelId.Should().Be(browserAumid);
    }

    [Fact]
    public void Select_RetainsPausedActiveSessionInsteadOfJumpingToStalePausedBrowser()
    {
        var selector = new MediaSessionSelector();
        var spotifyAumid = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify";
        var browserAumid = "chrome.exe";

        selector.Select(
            [CreateCandidate(spotifyAumid, MediaPlayerKind.Spotify, MediaPlaybackState.Playing, "Song 1", "Artist 1")],
            MediaSourcePolicy.UniversalFallback,
            ignoreBrowserVideoSessions: true,
            BaseTime);

        var paused = selector.Select(
            [
                CreateCandidate(spotifyAumid, MediaPlayerKind.Spotify, MediaPlaybackState.Paused, "Song 1", "Artist 1"),
                CreateCandidate(browserAumid, MediaPlayerKind.BrowserOrGeneric, MediaPlaybackState.Paused, "Old Tab", "Artist 2", isCurrent: true)
            ],
            MediaSourcePolicy.UniversalFallback,
            ignoreBrowserVideoSessions: true,
            BaseTime.AddSeconds(3));

        paused!.SourceAppUserModelId.Should().Be(spotifyAumid);
    }

    private static SessionCandidate CreateCandidate(
        string aumid,
        MediaPlayerKind kind,
        MediaPlaybackState state,
        string title,
        string artist,
        MediaItemKind itemKind = MediaItemKind.Music,
        bool isCurrent = false) =>
        new(
            SourceAppUserModelId: aumid,
            PlayerKind: kind,
            ItemKind: itemKind,
            PlaybackState: state,
            Title: title,
            Artist: artist,
            AlbumTitle: "Album",
            ReportedPosition: TimeSpan.FromSeconds(10),
            StartTime: TimeSpan.Zero,
            EndTime: TimeSpan.FromMinutes(3),
            PlaybackRate: 1.0,
            LastUpdatedUtc: BaseTime,
            IsSystemCurrentSession: isCurrent);
}
