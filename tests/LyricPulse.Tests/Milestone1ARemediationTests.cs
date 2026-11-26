using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AwesomeAssertions;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Media;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Core.Settings;
using LyricPulse.Core.Visuals;
using LyricPulse.Rendering.Fonts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LyricPulse.Tests;

public sealed class Milestone1ARemediationTests : IDisposable
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
    public void StarterLexiconTsv_All180RowsBijectivelyMatchBundledFluentIconsTtfCmapAndPostTables()
    {
        using var registry = new FontAssetRegistry();
        var typeface = registry.GetTypeface(LyricFontId.FluentIcons);
        var font = registry.GetOrCreateFont(LyricFontId.FluentIcons, 24f);

        const uint cmapTag = 0x636D6170; // 'cmap'
        const uint postTag = 0x706F7374; // 'post'

        var cmapBytes = typeface.GetTableData(cmapTag);
        var postBytes = typeface.GetTableData(postTag);
        cmapBytes.Should().NotBeNull("FluentSystemIcons-Filled.ttf must contain SFNT cmap table");
        postBytes.Should().NotBeNull("FluentSystemIcons-Filled.ttf must contain SFNT post table");

        var cpToGid = ParseCmapFormat12(cmapBytes!);
        var gidToName = ParsePostV2GlyphNames(postBytes!);

        using var tsvStream = typeof(StarterLexiconVisualMatcher).Assembly
            .GetManifestResourceStream(StarterLexiconVisualMatcher.ResourceName);
        tsvStream.Should().NotBeNull();
        using var reader = new StreamReader(tsvStream!, Encoding.UTF8);

        var rowCount = 0;
        var mismatches = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            rowCount++;
            var parts = line.Split('\t');
            parts.Length.Should().BeGreaterThanOrEqualTo(5);

            var concept = parts[0].Trim();
            var expectedIconName = parts[1].Trim();
            var hex = parts[2].Trim();

            var parsed = int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp);
            if (!parsed)
            {
                mismatches.Add($"{concept}: invalid hex '{hex}'");
                continue;
            }

            if (!font.ContainsGlyph(cp))
            {
                mismatches.Add($"{concept}: SKFont.ContainsGlyph(U+{hex}) returned false for {expectedIconName}");
                continue;
            }

            if (!cpToGid.TryGetValue(cp, out var gid) || !gidToName.TryGetValue(gid, out var actualGlyphName))
            {
                mismatches.Add($"{concept}: U+{hex} missing from SFNT cmap/post tables");
                continue;
            }

            if (!string.Equals(actualGlyphName, expectedIconName, StringComparison.Ordinal))
            {
                mismatches.Add($"{concept}: U+{hex} maps to '{actualGlyphName}' instead of '{expectedIconName}'");
            }
        }

        rowCount.Should().BeGreaterThanOrEqualTo(180);
        mismatches.Should().BeEmpty("100% of StarterLexicon.tsv rows must match FluentSystemIcons-Filled.ttf cmap+post tables");
    }

    [Theory]
    [InlineData("star", "ic_fluent_star_24_filled", 0xF719)]
    [InlineData("fire", "ic_fluent_fire_24_filled", 0xEF6A)]
    [InlineData("ocean", "ic_fluent_water_24_filled", 0xF064)]
    [InlineData("night", "ic_fluent_weather_moon_24_filled", 0xF896)]
    [InlineData("heart", "ic_fluent_heart_24_filled", 0xF47E)]
    [InlineData("code", "ic_fluent_code_24_filled", 0xF2F0)]
    [InlineData("rain", "ic_fluent_weather_rain_24_filled", 0xF89F)]
    [InlineData("city", "ic_fluent_city_24_filled", 0xF2C3)]
    [InlineData("flight", "ic_fluent_airplane_24_filled", 0xF110)]
    [InlineData("time", "ic_fluent_clock_24_filled", 0xF2DE)]
    [InlineData("light", "ic_fluent_lightbulb_24_filled", 0xF4E1)]
    [InlineData("dream", "ic_fluent_sparkle_24_filled", 0xEB3D)]
    [InlineData("life", "ic_fluent_heart_pulse_24_filled", 0xE70F)]
    [InlineData("door", "ic_fluent_door_24_filled", 0xF0A37)]
    public async Task StarterLexiconVisualMatcher_ResolvesExactVerifiedCodepointsIncludingSupplementaryPua(
        string keyword,
        string expectedIconName,
        int expectedCodepoint)
    {
        using var matcher = new StarterLexiconVisualMatcher();
        await matcher.InitializeAsync();

        var match = matcher.MatchWord(keyword, VisualPreference.Balanced);
        match.Should().NotBeNull();
        match!.IconName.Should().Be(expectedIconName);
        Rune.GetRuneAt(match.Value, 0).Value.Should().Be(expectedCodepoint);
    }

    [Fact]
    public async Task Runtime_TrackChangeSetsLoadingAndRetryLyricsFetchBypassesNegativeDiskCache()
    {
        long ticks = 100 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        var track = _normalizer.CreateIdentity("Recoverable Song", "Artist", "Album", TimeSpan.FromMinutes(3));
        var cache = new DiskLyricsCache(Path.Combine(_tempDir, "retry-cache"), _parser);

        var fetchAttempt = 0;
        var firstFetchGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = Substitute.For<ILyricsProvider>();
        provider.Name.Returns("MockProvider");
        provider.SourceKind.Returns(LyricsSourceKind.LrcLib);
        provider.PriorityOrder.Returns(1);
        provider.FetchAsync(track, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var attempt = Interlocked.Increment(ref fetchAttempt);
                if (attempt == 1)
                {
                    await firstFetchGate.Task;
                    return new LyricsResult(LyricsStatus.NotFound, null);
                }

                var doc = _parser.ParseLrc(track, "[00:01.00]Recovered synced lyric", LyricsSourceKind.LrcLib, "MockProvider", false);
                return new LyricsResult(LyricsStatus.Synced, doc);
            });

        var orchestrator = new LyricsOrchestrator(null, cache, [provider], NullLogger<LyricsOrchestrator>.Instance);
        await using var media = new FakeMediaSessionProvider();
        using var matcher = new StarterLexiconVisualMatcher();
        using var settingsStore = new JsonSettingsStore(Path.Combine(_tempDir, "retry-settings.json"));
        using var offsetStore = new TrackOffsetStore(Path.Combine(_tempDir, "retry-offsets.json"));
        using var offsetService = new TimingOffsetService(offsetStore);

        var notFoundSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncedSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var controller = new OverlayRuntimeController(
            media,
            orchestrator,
            matcher,
            settingsStore,
            offsetService,
            new DemoSongProvider(_parser, _normalizer),
            timestampProvider: () => ticks,
            timestampFrequency: Frequency,
            utcNowProvider: () => utc);

        controller.StateChanged += (_, snap) =>
        {
            if (snap.LyricsStatus == LyricsStatus.NotFound && fetchAttempt >= 1)
            {
                notFoundSeen.TrySetResult();
            }

            if (snap.LyricsStatus == LyricsStatus.Synced)
            {
                syncedSeen.TrySetResult();
            }
        };

        await controller.StartAsync();

        // Trigger track change -> must immediately report LyricsStatus.Loading (never NotFound flash!)
        media.EmitTrackChanged(track);
        controller.SampleFrame(ticks).LyricsStatus.Should().Be(LyricsStatus.Loading);

        // Let initial fetch finish with NotFound (which writes a 24h negative cache entry)
        firstFetchGate.SetResult();
        await notFoundSeen.Task.WaitAsync(TimeSpan.FromSeconds(2));
        (await cache.TryGetAsync(track))!.Status.Should().Be(LyricsStatus.NotFound);

        // RetryLyricsFetchAsync must immediately set Loading, bypass negative cache, and load Synced!
        await controller.RetryLyricsFetchAsync();
        await syncedSeen.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var recovered = controller.SampleFrame(ticks);
        recovered.LyricsStatus.Should().Be(LyricsStatus.Synced);
        recovered.Document!.Lines[0].RawText.Should().Be("Recovered synced lyric");
        fetchAttempt.Should().Be(2);
    }

    [Fact]
    public void ActiveLyricSlice_ClearsFinalLineIntoPostSongOutroWithoutBreakingNeonHorizonLoop()
    {
        var track = _normalizer.CreateIdentity("Long Outro", "Band", "", TimeSpan.FromSeconds(60));
        var doc = _parser.ParseLrc(track, "[00:05.00]Opening\n[00:10.00]Final vocal line", LyricsSourceKind.LrcLib, "LRCLIB", false);
        var lastEnd = doc.Lines[^1].EndTime;

        // Within 0.9s linger -> final line still visible
        var duringLinger = ActiveLyricSlice.Resolve(doc, lastEnd + TimeSpan.FromMilliseconds(400), track.Duration);
        duringLinger.IsPostSongOutro.Should().BeFalse();
        duringLinger.ActiveLineIndex.Should().Be(1);
        duringLinger.CurrentLine.Should().NotBeNull();

        // After 0.9s linger with >1.0s remaining track -> enters clean PostSongOutro
        var inOutro = ActiveLyricSlice.Resolve(doc, lastEnd + TimeSpan.FromSeconds(1.2), track.Duration);
        inOutro.IsInInstrumentalGap.Should().BeTrue();
        inOutro.IsPostSongOutro.Should().BeTrue();
        inOutro.ActiveLineIndex.Should().Be(-1);
        inOutro.CurrentLine.Should().BeNull();
        inOutro.PreviousLine.Should().Be(doc.Lines[^1]);

        // Unbounded duration (> 2.5s after last line) also clears cleanly
        var unboundedOutro = ActiveLyricSlice.Resolve(doc, lastEnd + TimeSpan.FromSeconds(3.0), TimeSpan.Zero);
        unboundedOutro.IsPostSongOutro.Should().BeTrue();

        // Neon Horizon loop: final line ends ~34.8s, total duration 36.0s -> stays visible before wrap
        var demo = new DemoSongProvider(_parser, _normalizer).GetDemoDocument();
        var nearLoopEnd = ActiveLyricSlice.Resolve(demo, TimeSpan.FromSeconds(35.8), DemoSongProvider.LoopDuration);
        nearLoopEnd.IsPostSongOutro.Should().BeFalse();
        nearLoopEnd.ActiveLineIndex.Should().Be(11);
    }

    [Fact]
    public async Task Runtime_GuardsUnsyncedNudgesAndClearsOffsetAndToastAcrossTrackSwitches()
    {
        long ticks = 100 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        var trackA = _normalizer.CreateIdentity("Song A", "Artist", "", TimeSpan.FromMinutes(3));
        var trackB = _normalizer.CreateIdentity("Song B", "Artist", "", TimeSpan.FromMinutes(3));
        var docA = _parser.ParseLrc(trackA, "[00:01.00]Synced A", LyricsSourceKind.LrcLib, "LRCLIB", false);
        var trackBGate = new TaskCompletionSource<LyricsResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var orchestrator = Substitute.For<ILyricsOrchestrator>();
        orchestrator.GetLyricsAsync(trackA, Arg.Any<CancellationToken>())
            .Returns(new LyricsResult(LyricsStatus.Synced, docA));
        orchestrator.GetLyricsAsync(trackB, Arg.Any<CancellationToken>())
            .Returns(_ => trackBGate.Task);

        await using var media = new FakeMediaSessionProvider();
        using var matcher = new StarterLexiconVisualMatcher();
        using var settingsStore = new JsonSettingsStore(Path.Combine(_tempDir, "offset-settings.json"));
        using var offsetStore = new TrackOffsetStore(Path.Combine(_tempDir, "offset-store.json"));
        using var offsetService = new TimingOffsetService(offsetStore);

        var syncedA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller = new OverlayRuntimeController(
            media,
            orchestrator,
            matcher,
            settingsStore,
            offsetService,
            new DemoSongProvider(_parser, _normalizer),
            timestampProvider: () => ticks,
            timestampFrequency: Frequency,
            utcNowProvider: () => utc);

        controller.StateChanged += (_, snap) =>
        {
            if (snap.Track?.CacheKey == trackA.CacheKey && snap.LyricsStatus == LyricsStatus.Synced)
            {
                syncedA.TrySetResult();
            }
        };

        await controller.StartAsync();

        // 1. Nudging while idle must be a safe no-op (no toast, no offset change)
        var idleNudge = await controller.NudgeTimingOffsetAsync(TimeSpan.FromMilliseconds(-500));
        idleNudge.Offset.Should().Be(TimeSpan.Zero);
        controller.SampleFrame(ticks).LastOffsetNudgeUtc.Should().BeNull();

        // 2. Load synced Track A and nudge -1000ms
        media.EmitTrackChanged(trackA);
        await syncedA.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await controller.NudgeTimingOffsetAsync(TimeSpan.FromMilliseconds(-1000));
        var snapA = controller.SampleFrame(ticks);
        snapA.TimingOffset.Offset.Should().Be(TimeSpan.FromMilliseconds(-1000));
        snapA.LastOffsetNudgeUtc.Should().Be(utc);
        snapA.OffsetGeneration.Should().Be(1);

        // 3. Switch to Track B (still Loading): previous offset & toast must clear IMMEDIATELY synchronously!
        media.EmitTrackChanged(trackB);
        var snapBLoading = controller.SampleFrame(ticks);
        snapBLoading.LyricsStatus.Should().Be(LyricsStatus.Loading);
        snapBLoading.TimingOffset.Offset.Should().Be(TimeSpan.Zero);
        snapBLoading.LastOffsetNudgeUtc.Should().BeNull();

        // 4. Nudging while Loading (unsynced) must also be guarded!
        await controller.NudgeTimingOffsetAsync(TimeSpan.FromMilliseconds(500));
        controller.SampleFrame(ticks).LastOffsetNudgeUtc.Should().BeNull();

        trackBGate.SetResult(new LyricsResult(LyricsStatus.NotFound, null));
    }

    [Fact]
    public async Task Runtime_DemoAutoShowsLyricsUniversalPlayerTogglesOsReducedMotionClampsMorphAndDisposeGuardsFullscreen()
    {
        long ticks = 100 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        await using var media = new FakeMediaSessionProvider();
        var orchestrator = Substitute.For<ILyricsOrchestrator>();
        var fullscreen = Substitute.For<IFullscreenDetector>();
        fullscreen.IsFullscreenExclusiveOrPresentation().Returns(false);

        using var matcher = new StarterLexiconVisualMatcher();
        using var settingsStore = new JsonSettingsStore(Path.Combine(_tempDir, "ux-settings.json"));
        using var offsetStore = new TrackOffsetStore(Path.Combine(_tempDir, "ux-offsets.json"));
        using var offsetService = new TimingOffsetService(offsetStore);

        var controller = new OverlayRuntimeController(
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

        // 1. Hide lyrics, then activate Demo Mode -> must automatically restore LyricsVisible = true!
        await controller.ToggleLyricsVisibilityAsync();
        controller.Settings.LyricsVisible.Should().BeFalse();

        await controller.SetDemoModeAsync(true);
        controller.Settings.LyricsVisible.Should().BeTrue();
        controller.SampleFrame(ticks).IsLyricsVisible.Should().BeTrue();

        // 2. Active 650ms Demo palette morph is in flight right now at t=0.
        // Advance 50ms, then enable OS Reduced Motion -> remaining morph must clamp to <= 150ms (finish by +200ms total)!
        ticks += (long)(0.050 * Frequency);
        controller.SetOsReducedMotion(true);
        controller.IsEffectiveReducedMotion.Should().BeTrue();
        controller.SampleFrame(ticks).IsEffectiveReducedMotion.Should().BeTrue();

        ticks += (long)(0.160 * Frequency); // Only 210ms total elapsed (< 650ms), must already be settled at DemoPalette!
        controller.SampleFrame(ticks).ActivePalette.Should().Be(DemoSongProvider.DemoPalette);

        // 3. ToggleUniversalPlayerModeAsync flips setting (defaults true in v2.3), persists, and pushes policy
        controller.Settings.UniversalPlayerMode.Should().BeTrue();
        await controller.ToggleUniversalPlayerModeAsync();
        controller.Settings.UniversalPlayerMode.Should().BeFalse();
        media.LastPolicy.Should().Be(MediaSourcePolicy.SupportedPlayersOnly);
        await controller.ToggleUniversalPlayerModeAsync();
        controller.Settings.UniversalPlayerMode.Should().BeTrue();
        media.LastPolicy.Should().Be(MediaSourcePolicy.UniversalFallback);

        // 4. PlayerClassifier recognizes classic iTunes.exe
        PlayerClassifier.ClassifyPlayer(@"C:\Program Files\iTunes\iTunes.exe", AppSettings.Default.BlockedAppIds)
            .Should().Be(MediaPlayerKind.DedicatedAudioPlayer);

        // 5. After DisposeAsync(), RefreshFullscreenState safely no-ops without firing StateChanged
        var eventsAfterDispose = 0;
        await controller.DisposeAsync();
        controller.StateChanged += (_, _) => eventsAfterDispose++;
        fullscreen.IsFullscreenExclusiveOrPresentation().Returns(true);
        controller.RefreshFullscreenState();
        eventsAfterDispose.Should().Be(0);
    }

    private static Dictionary<int, int> ParseCmapFormat12(byte[] cmap)
    {
        var numSubtables = BinaryPrimitives.ReadUInt16BigEndian(cmap.AsSpan(2, 2));
        var map = new Dictionary<int, int>(11_000);

        for (var i = 0; i < numSubtables; i++)
        {
            var subOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(cmap.AsSpan(4 + (i * 8) + 4, 4));
            var format = BinaryPrimitives.ReadUInt16BigEndian(cmap.AsSpan(subOffset, 2));
            if (format != 12)
            {
                continue;
            }

            var numGroups = (int)BinaryPrimitives.ReadUInt32BigEndian(cmap.AsSpan(subOffset + 12, 4));
            for (var g = 0; g < numGroups; g++)
            {
                var basePos = subOffset + 16 + (g * 12);
                var startChar = (int)BinaryPrimitives.ReadUInt32BigEndian(cmap.AsSpan(basePos, 4));
                var endChar = (int)BinaryPrimitives.ReadUInt32BigEndian(cmap.AsSpan(basePos + 4, 4));
                var startGlyph = (int)BinaryPrimitives.ReadUInt32BigEndian(cmap.AsSpan(basePos + 8, 4));
                for (var c = startChar; c <= endChar; c++)
                {
                    map[c] = startGlyph + (c - startChar);
                }
            }

            break;
        }

        return map;
    }

    private static Dictionary<int, string> ParsePostV2GlyphNames(byte[] post)
    {
        var version = BinaryPrimitives.ReadUInt32BigEndian(post.AsSpan(0, 4));
        version.Should().Be(0x00020000u, "FluentSystemIcons-Filled.ttf post table must be v2.0");

        var numGlyphs = BinaryPrimitives.ReadUInt16BigEndian(post.AsSpan(32, 2));
        var nameIndices = new ushort[numGlyphs];
        for (var i = 0; i < numGlyphs; i++)
        {
            nameIndices[i] = BinaryPrimitives.ReadUInt16BigEndian(post.AsSpan(34 + (i * 2), 2));
        }

        var pascalStrings = new List<string>();
        var pos = 34 + (numGlyphs * 2);
        while (pos < post.Length)
        {
            var len = post[pos++];
            if (pos + len > post.Length)
            {
                break;
            }

            pascalStrings.Add(Encoding.ASCII.GetString(post, pos, len));
            pos += len;
        }

        var map = new Dictionary<int, string>(numGlyphs);
        for (var gid = 0; gid < numGlyphs; gid++)
        {
            var idx = nameIndices[gid];
            if (idx >= 258 && (idx - 258) < pascalStrings.Count)
            {
                map[gid] = pascalStrings[idx - 258];
            }
        }

        return map;
    }

    private sealed class FakeMediaSessionProvider : IMediaSessionProvider
    {
        public MediaSessionSnapshot? CurrentSession { get; set; }
        public TimeSpan Position { get; set; }
        public MediaSourcePolicy LastPolicy { get; private set; }

        public event EventHandler<MediaSessionSnapshot?>? SessionChanged;
        public event EventHandler<TrackIdentity?>? TrackChanged;
        public event EventHandler<TimeSpan>? SeekDetected;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public TimeSpan GetInterpolatedPosition() => Position;
        public Task<AlbumArtPayload?> GetCurrentAlbumArtAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<AlbumArtPayload?>(null);

        public void UpdateSourcePolicy(
            MediaSourcePolicy policy,
            ImmutableArray<string> blockedAppIds,
            bool ignoreBrowserVideoSessions = true) =>
            LastPolicy = policy;

        public void EmitTrackChanged(TrackIdentity? track) => TrackChanged?.Invoke(this, track);

        public void EmitSessionChanged(MediaSessionSnapshot? snapshot)
        {
            CurrentSession = snapshot;
            SessionChanged?.Invoke(this, snapshot);
        }

        public void EmitSeekDetected(TimeSpan pos) => SeekDetected?.Invoke(this, pos);
        public Task<bool> TryTogglePlayPauseAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> TrySkipNextAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> TrySkipPreviousAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
