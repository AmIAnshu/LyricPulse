using AwesomeAssertions;
using LyricPulse.App;
using LyricPulse.App.Tray;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using SkiaSharp;
using Xunit;

namespace LyricPulse.Tests.Rendering;

public sealed class TrayAndLaunchTests
{
    [Fact]
    public void TrayMenuState_FormatsLiveDemoPausedIdleLoadingInstrumentalFullscreenAndErrorStates()
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
        liveMenu.HeaderPrimaryLine.Should().StartWith("Playing: ");
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
        liveMenu.CanRetryLyricsFetch.Should().BeFalse();

        // Pinned radio sync + structural equality (4 canonical v1.0 pins)
        liveMenu.PinnedStyles.Should().HaveCount(4);
        liveMenu.PinnedStyles.Single(p => p.IsActive).StyleId.Should().Be(OverlayStyleId.Codestyle);
        liveMenu.Equals(TrayMenuState.FromSnapshot(liveSnap)).Should().BeTrue();

        // 2. Demo Mode
        var demoMenu = TrayMenuState.FromSnapshot(liveSnap with { IsDemoMode = true, TimingOffset = new TimingOffsetState(null, TimeSpan.FromSeconds(1.0), false) });
        demoMenu.HeaderPrimaryLine.Should().Be("Demo: Neon Horizon");
        demoMenu.HeaderSecondaryLine.Should().Be("Demo Mode \u00B7 Synced lyrics");
        demoMenu.TimingOffsetHeader.Should().Be("Timing Offset (+1.0s)");
        demoMenu.IsDemoMode.Should().BeTrue();

        // 3. Idle (no track — tray.md section 4.1 copy, no duplicate waiting rows)
        var idleMenu = TrayMenuState.FromSnapshot(liveSnap with { Track = null, Document = null, LyricsStatus = LyricsStatus.NotFound, PlaybackState = MediaPlaybackState.Stopped, IsPlaying = false });
        idleMenu.HeaderPrimaryLine.Should().Be("No media playing");
        idleMenu.HeaderSecondaryLine.Should().Be("Waiting for Spotify or Apple Music");
        idleMenu.IconState.Should().Be(TrayIconVisualState.Idle);
        idleMenu.CanNudgeOffset.Should().BeFalse();

        // 4. Paused uses Idle monochrome icon (NOT diagonal slash!)
        var pausedMenu = TrayMenuState.FromSnapshot(liveSnap with { PlaybackState = MediaPlaybackState.Paused, IsPlaying = false });
        pausedMenu.HeaderPrimaryLine.Should().StartWith("Paused: ");
        pausedMenu.IconState.Should().Be(TrayIconVisualState.Idle);
        pausedMenu.TooltipText.Should().Contain("Paused");

        // 5. Loading & Instrumental use neutral Idle icon (NOT amber Error dot!)
        var loadingMenu = TrayMenuState.FromSnapshot(liveSnap with { Document = null, LyricsStatus = LyricsStatus.Loading });
        loadingMenu.IconState.Should().Be(TrayIconVisualState.Idle);
        loadingMenu.HeaderSecondaryLine.Should().Contain("Fetching lyrics");
        loadingMenu.TooltipText.Should().Contain("Fetching lyrics");
        loadingMenu.CanRetryLyricsFetch.Should().BeFalse();

        var instrumentalMenu = TrayMenuState.FromSnapshot(liveSnap with { Document = null, LyricsStatus = LyricsStatus.Instrumental });
        instrumentalMenu.IconState.Should().Be(TrayIconVisualState.Idle);
        instrumentalMenu.HeaderSecondaryLine.Should().Contain("Instrumental");
        instrumentalMenu.TooltipText.Should().Contain("Instrumental");
        instrumentalMenu.CanRetryLyricsFetch.Should().BeFalse();

        // 6. Hidden overlay & Fullscreen auto-hide use OverlayHidden diagonal slash + unpinned active style fallback
        var hiddenUnpinnedMenu = TrayMenuState.FromSnapshot(liveSnap with
        {
            Settings = AppSettings.Default with { LyricsVisible = false, ActiveStyle = OverlayStyleId.SpidersWeb }
        });
        hiddenUnpinnedMenu.IconState.Should().Be(TrayIconVisualState.OverlayHidden);
        hiddenUnpinnedMenu.PinnedStyles[0].Label.Should().Be("Spider's Web (Current)");
        hiddenUnpinnedMenu.PinnedStyles[0].IsActive.Should().BeTrue();

        var fullscreenMenu = TrayMenuState.FromSnapshot(liveSnap with { IsFullscreenHidden = true });
        fullscreenMenu.IconState.Should().Be(TrayIconVisualState.OverlayHidden);
        fullscreenMenu.HeaderSecondaryLine.Should().Be("Paused for fullscreen app");
        fullscreenMenu.TooltipText.Should().Contain("Paused for fullscreen app");

        // 7. Lookup Error & NotFound -> Error amber dot + Retry enabled
        var errorMenu = TrayMenuState.FromSnapshot(liveSnap with { Document = null, LyricsStatus = LyricsStatus.Error });
        errorMenu.IconState.Should().Be(TrayIconVisualState.Error);
        errorMenu.HeaderSecondaryLine.Should().Contain("Offline / lyrics unavailable");
        errorMenu.TooltipText.Should().Contain("Offline / lyrics unavailable");
        errorMenu.CanRetryLyricsFetch.Should().BeTrue();

        // 8. Hotkey conflict strips conflicting hint and surfaces notice + OS Reduced Motion & Universal mode
        var conflicts = new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.ToggleLyrics] = "Shortcut 'Ctrl+Shift+L' is already in use by another application."
        };
        var conflictMenu = TrayMenuState.FromSnapshot(
            liveSnap with
            {
                IsOsReducedMotion = true,
                Settings = liveSnap.Settings with { UniversalPlayerMode = true }
            },
            conflicts);

        conflictMenu.ShowLyricsGestureHint.Should().BeNull();
        conflictMenu.HotkeyConflictNotice.Should().Contain("Shortcut conflict");
        conflictMenu.TooltipText.Should().Contain("[Shortcut conflict]");
        conflictMenu.IsEffectiveReducedMotion.Should().BeTrue();
        conflictMenu.IsOsReducedMotion.Should().BeTrue();
        conflictMenu.ReducedMotionLabel.Should().Contain("Windows setting");
        conflictMenu.IsUniversalPlayerMode.Should().BeTrue();

        // Grapheme-safe surrogate truncation never produces lone surrogates
        var emojiTitle = "🔥✨🌟🎵🎶🔥✨🌟🎵🎶🔥✨🌟🎵🎶🔥✨🌟🎵🎶🔥✨🌟🎵🎶";
        var truncated = TrayMenuState.TruncateMiddle(emojiTitle, 12);
        truncated.Any(char.IsSurrogate).Should().BeTrue();
        var validSurrogates = true;
        for (var i = 0; i < truncated.Length; i++)
        {
            if (char.IsHighSurrogate(truncated[i]))
            {
                if (i + 1 >= truncated.Length || !char.IsLowSurrogate(truncated[i + 1]))
                {
                    validSurrogates = false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(truncated[i]))
            {
                validSurrogates = false;
            }
        }

        validSurrogates.Should().BeTrue("TruncateMiddle must never split UTF-16 surrogate pairs");
    }

    [Fact]
    public void TrayIconFactory_RendersDistinctValidPngBitmapsAndUsesWcagLightTaskbarDotColors()
    {
        // Verify Light taskbar WCAG 3.0:1+ status dots (#15803D green, #B45309 amber)
        TrayIconFactory.ResolveStatusDotColor(TrayIconVisualState.Playing, isLightTaskbar: true)
            .Should().Be(new SKColor(0x15, 0x80, 0x3D, 0xFF));
        TrayIconFactory.ResolveStatusDotColor(TrayIconVisualState.Error, isLightTaskbar: true)
            .Should().Be(new SKColor(0xB4, 0x53, 0x09, 0xFF));

        var darkHashes = new HashSet<string>();
        var lightHashes = new HashSet<string>();

        foreach (var state in Enum.GetValues<TrayIconVisualState>())
        {
            var darkPng = TrayIconFactory.RenderIconPngBytes(state, isLightTaskbar: false, sizePx: 32);
            var lightPng = TrayIconFactory.RenderIconPngBytes(state, isLightTaskbar: true, sizePx: 32);

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
