using System.Collections.Immutable;
using AwesomeAssertions;
using LyricPulse.App.Tray;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Core.Visuals;
using LyricPulse.UI.ViewModels;
using NSubstitute;
using Xunit;

namespace LyricPulse.Tests.UI;

public sealed class Phase5StageAndSettingsTests
{
    private static async Task<OverlayRuntimeController> CreateStartedControllerAsync(AppSettings? initialSettings = null)
    {
        var media = Substitute.For<IMediaSessionProvider>();
        var lyrics = Substitute.For<ILyricsOrchestrator>();
        var matcher = new GloveSemanticVisualMatcher();
        var store = Substitute.For<ISettingsStore>();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(initialSettings ?? AppSettings.Default);
        store.SaveAsync(Arg.Any<AppSettings>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var offsetStore = Substitute.For<ITrackOffsetStore>();
        offsetStore.TryGetOffsetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((TimeSpan?)null);
        var timing = new TimingOffsetService(offsetStore);
        var demo = new DemoSongProvider(new LyricsParser(), new TitleNormalizer());

        var controller = new OverlayRuntimeController(media, lyrics, matcher, store, timing, demo);
        await controller.StartAsync();
        await controller.SetDemoModeAsync(true);
        return controller;
    }

    [Fact]
    public async Task StageViewModel_InitializesTenStyles_FourPresets_AndCyclesCleanly()
    {
        await using var runtime = await CreateStartedControllerAsync();
        runtime.SetStageWindowOpen(true);

        var vm = new StageViewModel(runtime);
        vm.ApplySnapshot(runtime.SampleFrame());

        vm.StyleCards.Should().HaveCount(10);
        vm.FilteredStyleCards.Should().HaveCount(10);
        vm.StudioPresets.Should().HaveCount(4);
        vm.HotkeyRows.Should().HaveCount(5);
        vm.SelectedStyleInfo.StyleId.Should().Be(OverlayStyleId.Fisheye);
        vm.StyleCounterText.Should().Be("1 / 10");

        // Next & Previous wrap-around
        vm.NextStyleCommand.Execute(null);
        await Task.Delay(60);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.ActiveStyle.Should().Be(OverlayStyleId.FisheyeVisual);

        vm.SelectStyleCommand.Execute(OverlayStyleId.Fisheye);
        await Task.Delay(60);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.PreviousStyleCommand.Execute(null);
        await Task.Delay(60);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.ActiveStyle.Should().Be(OverlayStyleId.Messages);
    }

    [Fact]
    public async Task StageViewModel_Flyouts_FullscreenCascade_AndFiltersWork()
    {
        await using var runtime = await CreateStartedControllerAsync();
        var vm = new StageViewModel(runtime);
        vm.ApplySnapshot(runtime.SampleFrame());

        vm.ToggleFlyoutCommand.Execute(StageFlyoutKind.Aesthetics);
        vm.IsAestheticsFlyoutOpen.Should().BeTrue();
        vm.IsAnyFlyoutOpen.Should().BeTrue();

        // Esc closes flyout first, then fullscreen; and separately closes Settings drawer before fullscreen
        vm.IsFullscreen = true;
        vm.ToggleFlyoutCommand.Execute(StageFlyoutKind.Size);
        vm.TopBarOpacity.Should().Be(1.0);

        vm.ExitFullscreenCommand.Execute(null);
        vm.IsAnyFlyoutOpen.Should().BeFalse();
        vm.IsFullscreen.Should().BeTrue();

        vm.IsSettingsDrawerOpen = true;
        vm.ExitFullscreenCommand.Execute(null);
        vm.IsSettingsDrawerOpen.Should().BeFalse();
        vm.IsFullscreen.Should().BeTrue();

        vm.ExitFullscreenCommand.Execute(null);
        vm.IsFullscreen.Should().BeFalse();

        // Style filters
        vm.SetStyleFilterCommand.Execute("With Visuals");
        vm.FilteredStyleCards.Should().OnlyContain(c => c.HasVisualIcons);
        vm.SetStyleFilterCommand.Execute("Pinned");
        vm.FilteredStyleCards.Count.Should().BeGreaterThanOrEqualTo(4);
        vm.SetStyleFilterCommand.Execute("All");
        vm.FilteredStyleCards.Should().HaveCount(10);
    }

    [Fact]
    public async Task StageViewModel_DisplaysGuardrail_Hotkeys_Presets_AndTwoStepReset()
    {
        await using var runtime = await CreateStartedControllerAsync();
        var obsToggled = false;
        var vm = new StageViewModel(
            runtime,
            hotkeyValidator: (act, g) => g == "Ctrl+Shift+Z" ? (false, "In use by OBS") : (true, null),
            onObsWindowRequested: open => obsToggled = open);
        vm.ApplySnapshot(runtime.SampleFrame());

        // Monitor guardrail
        vm.UpdateDisplays([new DisplayMonitorInfo("DISP1", "Primary 4K", 3840, 2160, 1.5, true)]);
        vm.Displays.Should().ContainSingle();
        vm.AllDisplaysDisabled.Should().BeFalse();

        vm.Displays[0].IsEnabled = false;
        await Task.Delay(50);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.AllDisplaysDisabled.Should().BeTrue();

        vm.EnablePrimaryDisplayCommand.Execute(null);
        await Task.Delay(50);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.UpdateDisplays([new DisplayMonitorInfo("DISP1", "Primary 4K", 3840, 2160, 1.5, true)]);
        vm.AllDisplaysDisabled.Should().BeFalse();

        // Hotkey validation: missing modifier, duplicate, external conflict, valid
        vm.BeginHotkeyCapture(HotkeyAction.ToggleLyrics);
        vm.CommitCapturedHotkey("L", hasModifier: false).Should().BeFalse();
        vm.HotkeyRows[0].HasError.Should().BeTrue();

        vm.BeginHotkeyCapture(HotkeyAction.ToggleLyrics);
        vm.CommitCapturedHotkey("Ctrl+Shift+S", hasModifier: true).Should().BeFalse();
        vm.HotkeyRows[0].ErrorMessage.Should().Contain("Cycle Pinned Styles");

        vm.BeginHotkeyCapture(HotkeyAction.ToggleLyrics);
        vm.CommitCapturedHotkey("Ctrl+Shift+Z", hasModifier: true).Should().BeFalse();
        vm.HotkeyRows[0].ErrorMessage.Should().Be("In use by OBS");

        vm.BeginHotkeyCapture(HotkeyAction.ToggleLyrics);
        vm.CommitCapturedHotkey("Ctrl+Alt+L", hasModifier: true).Should().BeTrue();

        // OBS & Chroma key
        vm.ToggleObsWindowCommand.Execute(null);
        obsToggled.Should().BeTrue();
        vm.ObsChromaMode = ObsChromaKeyMode.Blue;
        vm.ObsChromaHex.Should().Be("#0000FF");

        // Studio preset + 2-step reset
        vm.ApplyColorPresetCommand.Execute(vm.StudioPresets[1]);
        await Task.Delay(60);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.AutoSyncColors.Should().BeFalse();
        vm.ManualHex1.Should().Be("#FEF3C7");

        vm.ArmResetCommand.Execute(null);
        vm.IsResetConfirmArmed.Should().BeTrue();
        vm.CancelResetCommand.Execute(null);
        vm.IsResetConfirmArmed.Should().BeFalse();

        vm.ArmResetCommand.Execute(null);
        vm.ConfirmResetCommand.Execute(null);
        await Task.Delay(60);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.AutoSyncColors.Should().BeTrue();
    }

    [Fact]
    public void TrayIconFactory_RendersValidMultiSizeIcoWithFiveDpiFrames()
    {
        var ico = TrayIconFactory.RenderMultiSizeIcoBytes(TrayIconVisualState.Playing, isLightTaskbar: false);
        ico.Length.Should().BeGreaterThan(1000);
        // ICONDIR: reserved=0, type=1, count=5
        BitConverter.ToUInt16(ico, 0).Should().Be(0);
        BitConverter.ToUInt16(ico, 2).Should().Be(1);
        BitConverter.ToUInt16(ico, 4).Should().Be(5);
        // Directory entry widths: 16, 20, 24, 32, 48
        ico[6].Should().Be(16);
        ico[6 + 16].Should().Be(20);
        ico[6 + 32].Should().Be(24);
        ico[6 + 48].Should().Be(32);
        ico[6 + 64].Should().Be(48);
    }

    [Theory]
    [InlineData("Ctrl+Shift+D0")]
    [InlineData("Ctrl+Alt+D9")]
    public void HotkeyGestureParser_AcceptsDigitKeys_D0ThroughD9(string gesture)
    {
        var ok = LyricPulse.App.Platform.HotkeyGestureParser.TryParse(gesture, out var parsed, out var error);
        ok.Should().BeTrue(error);
        error.Should().BeNull();
        parsed.NormalizedGesture.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task StageViewModel_Phase6DrawerModes_HotkeyClear_HealthDot_AndActiveBooleans()
    {
        await using var runtime = await CreateStartedControllerAsync();
        runtime.SetStageWindowOpen(true);

        var vm = new StageViewModel(runtime);
        vm.ApplySnapshot(runtime.SampleFrame());

        // Drawer responsive docking vs <880px overlay
        vm.IsSettingsDrawerOpen = true;
        vm.IsCompactDrawerMode = false;
        vm.IsDockedDrawerVisible.Should().BeTrue();
        vm.IsOverlayDrawerVisible.Should().BeFalse();

        vm.IsCompactDrawerMode = true;
        vm.IsDockedDrawerVisible.Should().BeFalse();
        vm.IsOverlayDrawerVisible.Should().BeTrue();

        // Recording hotkey clear
        vm.ClearCurrentRecordingHotkey().Should().BeFalse();
        vm.BeginRecordHotkeyCommand.Execute(HotkeyAction.ToggleLyrics);
        vm.ClearCurrentRecordingHotkey().Should().BeTrue();
        await Task.Delay(60);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.HotkeyRows.Single(r => r.Action == HotkeyAction.ToggleLyrics).GestureText.Should().BeEmpty();

        // Media health dot brush is populated cleanly across states
        vm.MediaHealthDotBrush.Should().NotBeNull();

        // Filter active selection booleans
        vm.IsFilterAll.Should().BeTrue();
        vm.SetStyleFilterCommand.Execute("Pinned");
        vm.IsFilterPinned.Should().BeTrue();
        vm.IsFilterAll.Should().BeFalse();

        // Theme, Scrim, and Secondary active selection booleans
        vm.SelectThemeVariantCommand.Execute(ThemeVariantMode.Light);
        await Task.Delay(60);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.IsThemeLight.Should().BeTrue();
        vm.IsThemeDark.Should().BeFalse();

        vm.SelectScrimIntensityCommand.Execute(ScrimIntensity.Strong);
        await Task.Delay(60);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.IsScrimStrong.Should().BeTrue();

        vm.SelectSecondaryLineCommand.Execute(SecondaryLineMode.Romanization);
        await Task.Delay(60);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.IsSecondaryRomanization.Should().BeTrue();

        vm.IsReducedMotionToggleEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task StageViewModel_V2VisualizerEdgeGlow_AlbumArt_AudioStatus_AndReducedMotionPreviewLabel()
    {
        await using var runtime = await CreateStartedControllerAsync();
        using var vm = new StageViewModel(runtime);
        vm.ApplySnapshot(runtime.SampleFrame());

        // Visualizer flyout opens cleanly
        vm.ToggleFlyoutCommand.Execute(StageFlyoutKind.Visualizer);
        vm.IsVisualizerFlyoutOpen.Should().BeTrue();
        vm.AudioStatusLabel.Should().Be("Beat Sync");

        // EdgeGlow modes & intensity
        vm.SelectEdgeGlowModeCommand.Execute(EdgeGlowMode.NeonRim);
        await Task.Delay(60);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.EdgeGlowMode.Should().Be(EdgeGlowMode.NeonRim);
        vm.IsEdgeGlowNeonRim.Should().BeTrue();

        vm.EdgeGlowIntensityPercent = 135;
        await Task.Delay(60);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.EdgeGlowIntensityPercent.Should().Be(135);

        // AudioVisualizer modes & intensity
        vm.SelectAudioVisualizerModeCommand.Execute(AudioVisualizerMode.FluidWave);
        await Task.Delay(60);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.AudioVisualizerMode.Should().Be(AudioVisualizerMode.FluidWave);
        vm.IsVisFluidWave.Should().BeTrue();

        vm.AudioVisualizerIntensityPercent = 120;
        await Task.Delay(60);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.AudioVisualizerIntensityPercent.Should().Be(120);

        // SelectStyleByIndexCommand updates active style by 0-based index
        vm.SelectStyleByIndexCommand.Execute(5); // Constellation
        await Task.Delay(60);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.ActiveStyle.Should().Be(OverlayStyleId.Constellation);

        // AlbumArt decode guard handles arbitrary bytes gracefully in headless tests without throwing
        var snapWithArt = vm.CurrentSnapshot with { AlbumArt = new AlbumArtPayload("hash123", new byte[] { 1, 2, 3, 4 }) };
        vm.ApplySnapshot(snapWithArt);

        // UX6-07: Reduced Motion disables preview pause button and reports clear label
        vm.ReducedMotion = true;
        await Task.Delay(60);
        vm.ApplySnapshot(runtime.SampleFrame());
        vm.IsPreviewsToggleEnabled.Should().BeFalse();
        vm.PausePreviewsLabel.Should().Be("Paused (Reduced Motion)");
    }
}
