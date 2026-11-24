using System.Collections.Immutable;
using AwesomeAssertions;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Core.Settings;
using LyricPulse.Core.Visuals;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LyricPulse.Tests.Runtime;

public sealed class OverlayRuntimeControllerTests : IDisposable
{
    private const long Frequency = 10_000_000;
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "LyricPulseTests", Guid.NewGuid().ToString("N"));
    private readonly TitleNormalizer _normalizer = new();
    private readonly LyricsParser _parser = new();

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void ActiveLyricSlice_Resolve_FindsLineWordProgressAndInstrumentalGaps()
    {
        var demo = new DemoSongProvider(_parser, _normalizer).GetDemoDocument();

        // Before first line (0.20s)
        var beforeFirst = ActiveLyricSlice.Resolve(demo, TimeSpan.FromMilliseconds(100));
        beforeFirst.ActiveLineIndex.Should().Be(-1);
        beforeFirst.CurrentLine.Should().BeNull();
        beforeFirst.NextLine.Should().Be(demo.Lines[0]);
        beforeFirst.TimeUntilNextLine.Should().Be(TimeSpan.FromMilliseconds(100));

        // Mid-word on line 0: word 0 is "Chasing" [0.20s..0.65s] -> at 0.425s progress = 0.5
        var midWord = ActiveLyricSlice.Resolve(demo, TimeSpan.FromMilliseconds(425));
        midWord.ActiveLineIndex.Should().Be(0);
        midWord.ActiveWordIndex.Should().Be(0);
        midWord.PreviousLine.Should().BeNull();
        midWord.CurrentLine.Should().Be(demo.Lines[0]);
        midWord.NextLine.Should().Be(demo.Lines[1]);
        midWord.WordProgress.Should().BeApproximately(0.5, 0.01);
        midWord.IsInInstrumentalGap.Should().BeFalse();

        // Plain unsynced doc returns line 0 with ActiveWordIndex = -1
        var track = _normalizer.CreateIdentity("Plain Track", "Band", "", TimeSpan.FromSeconds(40));
        var plainDoc = _parser.CreatePlain(track, "First plain\nSecond plain", LyricsSourceKind.LrcLib, "LRCLIB", false);
        var plainSlice = ActiveLyricSlice.Resolve(plainDoc, TimeSpan.FromSeconds(5));
        plainSlice.ActiveLineIndex.Should().Be(0);
        plainSlice.ActiveWordIndex.Should().Be(-1);
        plainSlice.NextLine!.RawText.Should().Be("Second plain");

        // Instrumental gap detection (> 4.0s silence between lines + 0.9s linger)
        var soloDoc = _parser.ParseLrc(track, "[00:01.00]First line\n[00:15.00]After solo", LyricsSourceKind.LrcLib, "LRCLIB", false);
        var duringSolo = ActiveLyricSlice.Resolve(soloDoc, TimeSpan.FromSeconds(10));
        duringSolo.IsInInstrumentalGap.Should().BeTrue();
        duringSolo.TimeUntilNextLine.Should().Be(TimeSpan.FromSeconds(5));

        // Explicit instrumental marker line
        var markerDoc = _parser.ParseLrc(track, "[00:01.00]♪\n[00:10.00]Vocals", LyricsSourceKind.LrcLib, "LRCLIB", false);
        ActiveLyricSlice.Resolve(markerDoc, TimeSpan.FromSeconds(2)).IsInInstrumentalGap.Should().BeTrue();

        // Empty / null document
        ActiveLyricSlice.Resolve(null, TimeSpan.FromSeconds(5)).Should().Be(ActiveLyricSlice.Empty);
    }

    [Fact]
    public async Task DemoMode_LoopsNeonHorizon36SecondsWithPreResolvedVisualsAndRestoresLiveOnExit()
    {
        long ticks = 1_000 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        await using var media = new FakeMediaSessionProvider();
        var orchestrator = Substitute.For<ILyricsOrchestrator>();
        using var matcher = new CountingVisualMatcher(new StarterLexiconVisualMatcher());
        using var settingsStore = new JsonSettingsStore(Path.Combine(_tempDir, "demo-settings.json"));
        using var offsetStore = new TrackOffsetStore(Path.Combine(_tempDir, "demo-offsets.json"));
        using var offsetService = new TimingOffsetService(offsetStore);
        var demoProvider = new DemoSongProvider(_parser, _normalizer);

        await using var controller = new OverlayRuntimeController(
            media,
            orchestrator,
            matcher,
            settingsStore,
            offsetService,
            demoProvider,
            timestampProvider: () => ticks,
            timestampFrequency: Frequency,
            utcNowProvider: () => utc);

        await controller.StartAsync();
        controller.IsDemoMode.Should().BeFalse();

        await controller.ToggleDemoModeAsync();
        controller.IsDemoMode.Should().BeTrue();

        // Advance 1.50s -> Line 0 ("Chasing a silver star tonight"), word 3 is "star" [1.40s..2.05s]
        ticks += (long)(1.50 * Frequency);
        var snap1 = controller.SampleFrame(ticks);
        snap1.IsDemoMode.Should().BeTrue();
        snap1.IsPlaying.Should().BeTrue();
        snap1.LyricsStatus.Should().Be(LyricsStatus.Synced);
        snap1.ActiveLineIndex.Should().Be(0);
        snap1.ActiveWordIndex.Should().Be(3);
        snap1.CurrentLine!.Words[3].Text.Should().Be("star");
        snap1.CurrentLine.Words[3].Visual.Should().NotBeNull();
        snap1.CurrentLine.Words[3].Visual!.IconName.Should().Be("ic_fluent_star_24_filled");

        // Advance +36.0s -> wraps cleanly back to 1.50s!
        ticks += 36 * Frequency;
        var snapWrapped = controller.SampleFrame(ticks);
        snapWrapped.EffectivePosition.Should().BeCloseTo(TimeSpan.FromSeconds(1.50), TimeSpan.FromMilliseconds(5));
        snapWrapped.ActiveLineIndex.Should().Be(0);
        snapWrapped.ActiveWordIndex.Should().Be(3);

        // Verify zero MatchWord calls on SampleFrame hot path (visuals pre-resolved once during demo load!)
        matcher.AnnotateCalls.Should().Be(1);
        matcher.DirectMatchWordCalls.Should().Be(0);

        // Changing VisualPreference re-annotates Demo doc to EmojiOnly cleanly
        await controller.UpdateSettingsAsync(s => s with { VisualPreference = VisualPreference.EmojiOnly });
        var emojiSnap = controller.SampleFrame(ticks);
        emojiSnap.CurrentLine!.Words[3].Visual!.Kind.Should().Be(VisualKind.Emoji);
        emojiSnap.CurrentLine.Words[3].Visual!.Value.Should().Be("⭐");

        // Toggle Demo Mode back OFF restores live mode cleanly
        await controller.ToggleDemoModeAsync();
        controller.IsDemoMode.Should().BeFalse();
        controller.SampleFrame(ticks).IsDemoMode.Should().BeFalse();
    }

    [Fact]
    public async Task LiveTrackLoad_FetchesLyricsExtractsPaletteMorphsOver650msAndCancelsStaleTrackOnRapidSkip()
    {
        long ticks = 500 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        await using var media = new FakeMediaSessionProvider { Position = TimeSpan.FromSeconds(1.5) };
        var track1 = _normalizer.CreateIdentity("Slow Song", "Artist A", "Album A", TimeSpan.FromMinutes(3));
        var track2 = _normalizer.CreateIdentity("Fast Song", "Artist B", "Album B", TimeSpan.FromMinutes(3));

        var track1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var track1Canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var track2Done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var orchestrator = Substitute.For<ILyricsOrchestrator>();
        orchestrator.GetLyricsAsync(track1, Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                var ct = ci.Arg<CancellationToken>();
                track1Started.TrySetResult();
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                }
                catch (OperationCanceledException)
                {
                    track1Canceled.TrySetResult();
                    throw;
                }

                return new LyricsResult(LyricsStatus.NotFound, null);
            });

        var track2Doc = _parser.ParseLrc(track2, "[00:01.00]<00:01.00>Glowing <00:01.40>star <00:02.00>tonight", LyricsSourceKind.LrcLib, "LRCLIB", false);
        orchestrator.GetLyricsAsync(track2, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new LyricsResult(LyricsStatus.Synced, track2Doc)));

        media.AlbumArt = new AlbumArtPayload("hash-2", new byte[] { 10, 20, 30, 40 });

        var extractedPalette = new AlbumAccentPalette(
            new RgbColor(0xFF, 0x80, 0xA0),
            new RgbColor(0x40, 0xD0, 0xFF),
            new RgbColor(0xA0, 0xB0, 0xC0),
            new RgbColor(0x18, 0x10, 0x20),
            IsMonochromeFallback: false,
            SourceAlbumHash: "hash-2");

        var colorExtractor = Substitute.For<IAlbumColorExtractor>();
        colorExtractor.Extract(Arg.Any<AlbumArtPayload?>()).Returns(extractedPalette);

        using var matcher = new StarterLexiconVisualMatcher();
        using var settingsStore = new JsonSettingsStore(Path.Combine(_tempDir, "live-settings.json"));
        using var offsetStore = new TrackOffsetStore(Path.Combine(_tempDir, "live-offsets.json"));
        using var offsetService = new TimingOffsetService(offsetStore);

        await using var controller = new OverlayRuntimeController(
            media,
            orchestrator,
            matcher,
            settingsStore,
            offsetService,
            new DemoSongProvider(_parser, _normalizer),
            colorExtractor,
            fullscreenDetector: null,
            NullLogger<OverlayRuntimeController>.Instance,
            () => ticks,
            Frequency,
            () => utc);

        controller.StateChanged += (_, snap) =>
        {
            if (snap.Track?.CacheKey == track2.CacheKey && snap.LyricsStatus == LyricsStatus.Synced && snap.TargetPalette == extractedPalette)
            {
                track2Done.TrySetResult();
            }
        };

        await controller.StartAsync();

        // Trigger Track 1, wait until fetch begins, then rapidly skip to Track 2!
        media.EmitTrackChanged(track1);
        await track1Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var playingSession2 = new MediaSessionSnapshot(
            "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify",
            MediaPlayerKind.Spotify,
            MediaItemKind.Music,
            MediaPlaybackState.Playing,
            track2,
            TimeSpan.FromSeconds(1.5),
            TimeSpan.Zero,
            track2.Duration,
            1.0,
            utc,
            null);
        media.EmitSessionChanged(playingSession2);
        media.EmitTrackChanged(track2);

        await track1Canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await track2Done.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Morph progresses smoothly across 650ms in OKLab space
        var atStart = controller.SampleFrame(ticks);
        atStart.Track.Should().Be(track2);
        atStart.LyricsStatus.Should().Be(LyricsStatus.Synced);
        atStart.CurrentLine!.Words[1].Visual.Should().NotBeNull();
        atStart.TargetPalette.Should().Be(extractedPalette);

        ticks += (long)(0.325 * Frequency);
        var atMid = controller.SampleFrame(ticks);
        atMid.ActivePalette.AccentColor1.Should().NotBe(extractedPalette.AccentColor1);

        ticks += (long)(0.400 * Frequency);
        var atEnd = controller.SampleFrame(ticks);
        atEnd.ActivePalette.Should().Be(extractedPalette);

        // Nudge timing offset earlier (-500ms) -> EffectivePosition advances by +500ms (1.5s -> 2.0s)
        var prevSeekGen = atEnd.SeekGeneration;
        await controller.NudgeTimingOffsetAsync(TimeSpan.FromMilliseconds(-500));
        var afterNudge = controller.SampleFrame(ticks);
        afterNudge.TimingOffset.Offset.Should().Be(TimeSpan.FromMilliseconds(-500));
        afterNudge.EffectivePosition.Should().Be(TimeSpan.FromSeconds(2.0));
        afterNudge.SeekGeneration.Should().BeGreaterThan(prevSeekGen);
        afterNudge.LastOffsetNudgeUtc.Should().Be(utc);

        // Seek event increments SeekGeneration; Pause records PausedSinceUtc
        media.EmitSeekDetected(TimeSpan.FromSeconds(2.2));
        controller.SampleFrame(ticks).SeekGeneration.Should().BeGreaterThan(afterNudge.SeekGeneration);

        media.EmitSessionChanged(playingSession2 with { PlaybackState = MediaPlaybackState.Paused });
        controller.SampleFrame(ticks).PausedSinceUtc.Should().Be(utc);

        await controller.ResetTimingOffsetAsync();
        controller.SampleFrame(ticks).EffectivePosition.Should().Be(TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public async Task Commands_CyclePinnedStylesToggleVisibilityReducedMotionManualColorsAndFullscreenHide()
    {
        long ticks = 100 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        await using var media = new FakeMediaSessionProvider();
        var orchestrator = Substitute.For<ILyricsOrchestrator>();
        var fullscreen = Substitute.For<IFullscreenDetector>();
        fullscreen.IsFullscreenExclusiveOrPresentation().Returns(false);

        using var matcher = new StarterLexiconVisualMatcher();
        using var settingsStore = new JsonSettingsStore(Path.Combine(_tempDir, "cmd-settings.json"));
        using var offsetStore = new TrackOffsetStore(Path.Combine(_tempDir, "cmd-offsets.json"));
        using var offsetService = new TimingOffsetService(offsetStore);

        await using var controller = new OverlayRuntimeController(
            media,
            orchestrator,
            matcher,
            settingsStore,
            offsetService,
            new DemoSongProvider(_parser, _normalizer),
            albumColorExtractor: null,
            fullscreenDetector: fullscreen,
            NullLogger<OverlayRuntimeController>.Instance,
            () => ticks,
            Frequency,
            () => utc);

        await controller.StartAsync();

        // Cycle pinned styles: default [Fisheye, Codestyle, VisualStack]
        controller.Settings.ActiveStyle.Should().Be(OverlayStyleId.Fisheye);
        (await controller.CyclePinnedStyleAsync()).Should().Be(OverlayStyleId.Codestyle);
        (await controller.CyclePinnedStyleAsync()).Should().Be(OverlayStyleId.VisualStack);
        (await controller.CyclePinnedStyleAsync()).Should().Be(OverlayStyleId.Fisheye);

        // SetActiveStyle to an unpinned style, then CyclePinnedStyle jumps cleanly to PinnedStyles[0]
        await controller.SetActiveStyleAsync(OverlayStyleId.KineticWall);
        controller.Settings.ActiveStyle.Should().Be(OverlayStyleId.KineticWall);
        (await controller.CyclePinnedStyleAsync()).Should().Be(OverlayStyleId.Fisheye);

        // Toggle visibility & reduced motion
        await controller.ToggleLyricsVisibilityAsync();
        controller.SampleFrame(ticks).IsLyricsVisible.Should().BeFalse();
        await controller.ToggleLyricsVisibilityAsync();
        controller.SampleFrame(ticks).IsLyricsVisible.Should().BeTrue();

        await controller.ToggleReducedMotionAsync();
        controller.Settings.ReducedMotion.Should().BeTrue();

        // Fullscreen game detection hides overlay when HideDuringFullscreen = true
        fullscreen.IsFullscreenExclusiveOrPresentation().Returns(true);
        controller.RefreshFullscreenState();
        controller.SampleFrame(ticks).IsFullscreenHidden.Should().BeTrue();
        controller.SampleFrame(ticks).IsLyricsVisible.Should().BeFalse();

        // UpdateSettings disables HideDuringFullscreen and switches to manual colors + UniversalPlayerMode
        var manual1 = new RgbColor(0x11, 0xAA, 0xFF);
        await controller.UpdateSettingsAsync(s => s with
        {
            HideDuringFullscreen = false,
            AutoSyncColors = false,
            ManualAccent1 = manual1,
            UniversalPlayerMode = true
        });

        var finalSnap = controller.SampleFrame(ticks);
        finalSnap.IsFullscreenHidden.Should().BeFalse();
        finalSnap.IsLyricsVisible.Should().BeTrue();
        finalSnap.ActivePalette.AccentColor1.Should().Be(manual1);
        media.LastPolicy.Should().Be(MediaSourcePolicy.UniversalFallback);
    }

    [Fact]
    public async Task LiveTrackLoad_SurvivesAlbumArtExtractorExceptionAndLoadsPreExistingSessionOnStart()
    {
        long ticks = 100 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var track = _normalizer.CreateIdentity("Preloaded Song", "Artist", "Album", TimeSpan.FromMinutes(3));
        var session = new MediaSessionSnapshot(
            "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify",
            MediaPlayerKind.Spotify,
            MediaItemKind.Music,
            MediaPlaybackState.Playing,
            track,
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero,
            track.Duration,
            1.0,
            utc,
            null);

        await using var media = new FakeMediaSessionProvider
        {
            CurrentSession = session,
            Position = TimeSpan.FromSeconds(1),
            AlbumArt = new AlbumArtPayload("bad-art", new byte[] { 0xFF })
        };

        var doc = _parser.ParseLrc(track, "[00:00.50]Still loads lyrics cleanly", LyricsSourceKind.LrcLib, "LRCLIB", false);
        var orchestrator = Substitute.For<ILyricsOrchestrator>();
        orchestrator.GetLyricsAsync(track, Arg.Any<CancellationToken>())
            .Returns(new LyricsResult(LyricsStatus.Synced, doc));

        var throwingExtractor = Substitute.For<IAlbumColorExtractor>();
        throwingExtractor.Extract(Arg.Any<AlbumArtPayload?>())
            .Returns(_ => throw new InvalidOperationException("corrupt image"));

        var loadedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var matcher = new StarterLexiconVisualMatcher();
        using var settingsStore = new JsonSettingsStore(Path.Combine(_tempDir, "pre-settings.json"));
        using var offsetStore = new TrackOffsetStore(Path.Combine(_tempDir, "pre-offsets.json"));
        using var offsetService = new TimingOffsetService(offsetStore);

        await using var controller = new OverlayRuntimeController(
            media,
            orchestrator,
            matcher,
            settingsStore,
            offsetService,
            new DemoSongProvider(_parser, _normalizer),
            throwingExtractor,
            fullscreenDetector: null,
            NullLogger<OverlayRuntimeController>.Instance,
            () => ticks,
            Frequency,
            () => utc);

        controller.StateChanged += (_, snap) =>
        {
            if (snap.LyricsStatus == LyricsStatus.Synced)
            {
                loadedTcs.TrySetResult();
            }
        };

        await controller.StartAsync();
        await loadedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var snap = controller.SampleFrame();
        snap.LyricsStatus.Should().Be(LyricsStatus.Synced);
        snap.ActivePalette.Should().Be(AlbumAccentPalette.Default);
        snap.CurrentLine!.RawText.Should().Be("Still loads lyrics cleanly");

        // Clearing track resets cleanly
        media.EmitTrackChanged(null);
        controller.SampleFrame(ticks).Track.Should().BeNull();
    }

    private sealed class FakeMediaSessionProvider : IMediaSessionProvider
    {
        public MediaSessionSnapshot? CurrentSession { get; set; }
        public TimeSpan Position { get; set; }
        public AlbumArtPayload? AlbumArt { get; set; }
        public MediaSourcePolicy LastPolicy { get; private set; }

        public event EventHandler<MediaSessionSnapshot?>? SessionChanged;
        public event EventHandler<TrackIdentity?>? TrackChanged;
        public event EventHandler<TimeSpan>? SeekDetected;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public TimeSpan GetInterpolatedPosition() => Position;
        public Task<AlbumArtPayload?> GetCurrentAlbumArtAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AlbumArt);

        public void UpdateSourcePolicy(
            MediaSourcePolicy policy,
            ImmutableArray<string> blockedAppIds,
            bool ignoreBrowserVideoSessions = true) =>
            LastPolicy = policy;

        public void EmitSessionChanged(MediaSessionSnapshot? snapshot)
        {
            CurrentSession = snapshot;
            SessionChanged?.Invoke(this, snapshot);
        }

        public void EmitTrackChanged(TrackIdentity? track) => TrackChanged?.Invoke(this, track);
        public void EmitSeekDetected(TimeSpan position) => SeekDetected?.Invoke(this, position);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingVisualMatcher(IWordVisualMatcher inner) : IWordVisualMatcher
    {
        public int AnnotateCalls { get; private set; }
        public int DirectMatchWordCalls { get; private set; }
        public VisualMatcherMode Mode => inner.Mode;
        public bool IsReady => inner.IsReady;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            inner.InitializeAsync(cancellationToken);

        public WordVisualMatch? MatchWord(ReadOnlySpan<char> word, VisualPreference preference = VisualPreference.Balanced)
        {
            DirectMatchWordCalls++;
            return inner.MatchWord(word, preference);
        }

        public LyricsDocument AnnotateDocument(LyricsDocument document, VisualPreference preference = VisualPreference.Balanced)
        {
            AnnotateCalls++;
            return inner.AnnotateDocument(document, preference);
        }

        public void Dispose() => inner.Dispose();
    }
}
