using AwesomeAssertions;
using LyricPulse.App;
using LyricPulse.App.Tray;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using Xunit;

namespace LyricPulse.Tests.Rendering;

public sealed class TrayAndLaunchTests
{
    [Fact]
    public void TrayMenuState_FormatsLiveDemoPausedIdleAndErrorStatesWithZeroEmojisAndExactSigns()
    {
        var doc = new DemoSongProvider(new LyricsParser(), new TitleNormalizer()).GetDemoDocument();

        // 1. Live Spotify playing with long title & shifted timing offset
        var longTrack = new TrackIdentity(
            "A Remarkably Long Cinematic Track Title That Exceeds Thirty Eight Characters",
            "The Midnight Orchestra",
            "Album",
            "long track",
            "the midnight orchestra",
            TimeSpan.FromMinutes(4),
            "k1");

        var liveSnap = new OverlayPlaybackSnapshot(
            Track: longTrack,
            Document: doc,
            LyricsStatus: LyricsStatus.Synced,
            PlaybackState: MediaPlaybackState.Playing,
            PlayerKind: MediaPlayerKind.Spotify,
            SourceAppUserModelId: "Spotify.exe",
            RawPosition: TimeSpan.FromSeconds(10),
            EffectivePosition: TimeSpan.FromSeconds(10.5),
            ActiveSlice: ActiveLyricSlice.Resolve(doc, TimeSpan.FromSeconds(10)),
            ActivePalette: AlbumAccentPalette.Default,
            TargetPalette: AlbumAccentPalette.Default,
            TimingOffset: new TimingOffsetState("k1", TimeSpan.FromSeconds(-0.5), true),
            Settings: AppSettings.Default with { ActiveStyle = OverlayStyleId.Codestyle },
            IsPlaying: true,
            IsDemoMode: false,
            IsFullscreenHidden: false,
            PausedSinceUtc: null,
            LastOffsetNudgeUtc: null,
            TrackGeneration: 1,
            SeekGeneration: 0);

        var liveMenu = TrayMenuState.FromSnapshot(liveSnap);
        liveMenu.HeaderPrimaryLine.Length.Should().BeLessThanOrEqualTo(TrayMenuState.MaxHeaderChars);
        liveMenu.HeaderPrimaryLine.Should().Contain("\u2026");
        liveMenu.HeaderSecondaryLine.Should().Be("Spotify \u00B7 Synced lyrics");
        liveMenu.TooltipText.Length.Should().BeLessThanOrEqualTo(TrayMenuState.MaxTooltipChars);
        liveMenu.IconState.Should().Be(TrayIconVisualState.Playing);
        liveMenu.TimingOffsetHeader.Should().Be("Timing Offset (\u22120.5s)");
        liveMenu.OffsetOptions.Select(o => o.Label).Should().Equal(
            "\u22121.0s (Earlier)",
            "\u22120.5s (Earlier)",
            "Reset (0.0s)",
            "+0.5s (Later)",
            "+1.0s (Later)");
        liveMenu.OffsetOptions.Single(o => o.IsReset).IsEnabled.Should().BeTrue();
        liveMenu.OffsetPersistenceCaption.Should().Be("Saved per song");

        // Pinned radio sync
        liveMenu.PinnedStyles.Should().HaveCount(3);
        liveMenu.PinnedStyles.Single(p => p.IsActive).StyleId.Should().Be(OverlayStyleId.Codestyle);

        // Structural equality for identical snapshot avoids redundant NativeMenu rebuilds
        var liveMenuCopy = TrayMenuState.FromSnapshot(liveSnap);
        liveMenu.Equals(liveMenuCopy).Should().BeTrue();

        // 2. Demo Mode
        var demoMenu = TrayMenuState.FromSnapshot(liveSnap with { IsDemoMode = true, TimingOffset = new TimingOffsetState(null, TimeSpan.FromSeconds(1.0), false) });
        demoMenu.HeaderPrimaryLine.Should().Be("Demo: Neon Horizon");
        demoMenu.HeaderSecondaryLine.Should().Be("Demo Mode \u00B7 Synced lyrics");
        demoMenu.TimingOffsetHeader.Should().Be("Timing Offset (+1.0s)");
        demoMenu.IsDemoMode.Should().BeTrue();

        // 3. Idle (no track)
        var idleMenu = TrayMenuState.FromSnapshot(liveSnap with { Track = null, Document = null, LyricsStatus = LyricsStatus.NotFound, PlaybackState = MediaPlaybackState.Stopped, IsPlaying = false });
        idleMenu.HeaderPrimaryLine.Should().Be("Waiting for Spotify / Apple Music...");
        idleMenu.IconState.Should().Be(TrayIconVisualState.Idle);
        idleMenu.CanNudgeOffset.Should().BeFalse();

        // 4. Hidden overlay + unpinned active style fallback
        var hiddenUnpinnedMenu = TrayMenuState.FromSnapshot(liveSnap with
        {
            Settings = AppSettings.Default with { LyricsVisible = false, ActiveStyle = OverlayStyleId.Constellation }
        });
        hiddenUnpinnedMenu.IconState.Should().Be(TrayIconVisualState.PausedOrHidden);
        hiddenUnpinnedMenu.PinnedStyles[0].Label.Should().Be("Constellation (Current)");
        hiddenUnpinnedMenu.PinnedStyles[0].IsActive.Should().BeTrue();

        // 5. Missing lyrics on active playing track -> Error amber dot
        var errorMenu = TrayMenuState.FromSnapshot(liveSnap with { Document = null, LyricsStatus = LyricsStatus.NotFound });
        errorMenu.IconState.Should().Be(TrayIconVisualState.Error);
    }

    [Fact]
    public void TrayIconFactory_RendersDistinctValidPngBitmapsAcrossAllFourStatesAndBothPolarities()
    {
        var darkHashes = new HashSet<string>();
        var lightHashes = new HashSet<string>();

        foreach (var state in Enum.GetValues<TrayIconVisualState>())
        {
            var darkPng = TrayIconFactory.RenderIconPngBytes(state, isLightTaskbar: false, sizePx: 32);
            var lightPng = TrayIconFactory.RenderIconPngBytes(state, isLightTaskbar: true, sizePx: 32);

            // Verify valid PNG magic signature (89 50 4E 47)
            darkPng.Length.Should().BeGreaterThan(100);
            darkPng[0].Should().Be(0x89);
            darkPng[1].Should().Be(0x50);
            darkPng[2].Should().Be(0x4E);
            darkPng[3].Should().Be(0x47);

            lightPng.Length.Should().BeGreaterThan(100);
            lightPng[0].Should().Be(0x89);

            var darkHex = Convert.ToHexString(darkPng);
            var lightHex = Convert.ToHexString(lightPng);
            darkHex.Should().NotBe(lightHex, $"{state} icon must adapt stroke color to Light vs Dark taskbar");

            darkHashes.Add(darkHex);
            lightHashes.Add(lightHex);
        }

        darkHashes.Should().HaveCount(4, "all 4 tray states must render visually distinct icons");
        lightHashes.Should().HaveCount(4, "all 4 tray states must render visually distinct icons");
    }

    [Fact]
    public void LaunchOptions_ParsesDemoAndSmokeTestSwitchesCleanly()
    {
        LaunchOptions.Parse([]).Should().Be(new LaunchOptions(false, false));
        LaunchOptions.Parse(["--demo"]).Should().Be(new LaunchOptions(true, false));
        LaunchOptions.Parse(["-d"]).Should().Be(new LaunchOptions(true, false));
        LaunchOptions.Parse(["--smoke-test"]).Should().Be(new LaunchOptions(false, true));
        LaunchOptions.Parse(["--DEMO", "--SMOKE-TEST"]).Should().Be(new LaunchOptions(true, true));
    }

    [Fact]
    public async Task SmokeTestRunner_ExecutesEndToEndDiFontsShadersDemoAndStylesSuccessfully()
    {
        await using var provider = Program.BuildServiceProvider(forHeadlessSmokeTest: true);
        var exitCode = await SmokeTestRunner.RunAsync(provider);
        exitCode.Should().Be(0);
    }
}
