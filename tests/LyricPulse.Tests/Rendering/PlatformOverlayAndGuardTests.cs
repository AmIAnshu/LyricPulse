using System.Collections.Immutable;
using AwesomeAssertions;
using LyricPulse.App.Platform;
using LyricPulse.Core.Models;
using Xunit;

namespace LyricPulse.Tests.Rendering;

public sealed class PlatformOverlayAndGuardTests
{
    [Theory]
    [InlineData(AnchorZone.TopCenter, 1.0)]
    [InlineData(AnchorZone.MiddleCenter, 1.25)]
    [InlineData(AnchorZone.BottomThird, 1.5)]
    [InlineData(AnchorZone.BottomLeft, 2.0)]
    [InlineData(AnchorZone.BottomRight, 1.0)]
    public void OverlayBoundsCalculator_ComputesWorkingAreaPlacementAndSafeZoneAcrossAllFiveAnchorsAndDpis(
        AnchorZone zone,
        double dpiScale)
    {
        var monitor = new MonitorDescriptor(
            DisplayKey: @"\\.\DISPLAY1",
            FriendlyName: "Primary Studio Monitor",
            WorkingLeftPx: 0,
            WorkingTopPx: 0,
            WorkingWidthPx: 2560,
            WorkingHeightPx: 1400,
            EffectiveDpiScale: dpiScale,
            IsPrimary: true);

        var settings = AppSettings.Default with
        {
            AnchorZone = zone,
            SafeMarginPx = 48
        };

        var placements = OverlayBoundsCalculator.ComputePlacements([monitor], settings);
        placements.Should().HaveCount(1);

        var p = placements[0];
        p.PositionXPx.Should().Be(0);
        p.PositionYPx.Should().Be(0);
        p.WidthDip.Should().BeApproximately(2560.0 / dpiScale, 0.01);
        p.HeightDip.Should().BeApproximately(1400.0 / dpiScale, 0.01);

        var marginDip = 48.0 / dpiScale;
        p.SafeZoneDip.LeftDip.Should().BeGreaterThanOrEqualTo(marginDip - 0.01);
        p.SafeZoneDip.TopDip.Should().BeGreaterThanOrEqualTo(marginDip - 0.01);
        (p.SafeZoneDip.LeftDip + p.SafeZoneDip.WidthDip).Should().BeLessThanOrEqualTo(p.WidthDip - marginDip + 0.01);
        (p.SafeZoneDip.TopDip + p.SafeZoneDip.HeightDip).Should().BeLessThanOrEqualTo(p.HeightDip - marginDip + 0.01);
    }

    [Fact]
    public void OverlayBoundsCalculator_FiltersDisabledDisplaysAndHandlesMixedDpiMultiMonitor()
    {
        var monitors = new[]
        {
            new MonitorDescriptor(@"\\.\DISPLAY1", "Laptop 150%", 0, 0, 2880, 1720, 1.5, IsPrimary: true),
            new MonitorDescriptor(@"\\.\DISPLAY2", "External 100%", 2880, 0, 1920, 1040, 1.0, IsPrimary: false),
            new MonitorDescriptor(@"\\.\DISPLAY3", "Disabled Monitor", -1920, 0, 1920, 1040, 1.25, IsPrimary: false)
        };

        var settings = AppSettings.Default with
        {
            DisabledDisplays = [@"  \\.\display3  "],
            PerDisplayBounds = ImmutableDictionary<string, OverlayBoundsConfig>.Empty
                .Add(@"\\.\DISPLAY2", OverlayBoundsConfig.FromAnchorZone(AnchorZone.TopCenter, 64))
        };

        var placements = OverlayBoundsCalculator.ComputePlacements(monitors, settings);
        placements.Should().HaveCount(2);
        placements.Select(p => p.DisplayKey).Should().BeEquivalentTo([@"\\.\DISPLAY1", @"\\.\DISPLAY2"]);

        var primary = placements.Single(p => p.DisplayKey == @"\\.\DISPLAY1");
        primary.WidthDip.Should().BeApproximately(1920.0, 0.01);
        primary.DpiScale.Should().Be(1.5);

        var secondary = placements.Single(p => p.DisplayKey == @"\\.\DISPLAY2");
        secondary.PositionXPx.Should().Be(2880);
        secondary.WidthDip.Should().BeApproximately(1920.0, 0.01);
        secondary.BoundsConfig.AnchorZone.Should().Be(AnchorZone.TopCenter);
        secondary.BoundsConfig.SafeMarginPx.Should().Be(64);
    }

    [Fact]
    public void Win32OverlayInterop_NeverCallsSetLayeredWindowAttributesOnDirectCompositionSurfaces()
    {
        const nint directCompStyle = 0x00200000; // WS_EX_NOREDIRECTIONBITMAP
        var resultDirectComp = Win32OverlayInterop.ComputeDesiredExStyle(directCompStyle, out var shouldCallLayeredDirectComp);

        shouldCallLayeredDirectComp.Should().BeFalse("SetLayeredWindowAttributes breaks DirectComposition / WinUIComposition surfaces");
        (resultDirectComp & Win32OverlayInterop.RequiredClickThroughExStyles)
            .Should().Be(Win32OverlayInterop.RequiredClickThroughExStyles);

        var resultRedirection = Win32OverlayInterop.ComputeDesiredExStyle(0, out var shouldCallLayeredRedirection);
        shouldCallLayeredRedirection.Should().BeTrue("RedirectionSurface fallback requires SetLayeredWindowAttributes");
        (resultRedirection & Win32OverlayInterop.RequiredClickThroughExStyles)
            .Should().Be(Win32OverlayInterop.RequiredClickThroughExStyles);
    }

    [Fact]
    public void SingleInstanceGuard_AcquiresPrimaryAndSignalsActivationOnSecondLaunch()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $@"Local\LyricPulse.Test.Mutex.{suffix}";
        var eventName = $@"Local\LyricPulse.Test.Show.{suffix}";

        using var primary = SingleInstanceGuard.TryAcquirePrimaryOrSignalExisting(mutexName, eventName);
        primary.Should().NotBeNull();

        using var activationReceived = new ManualResetEventSlim(false);
        primary!.StartActivationListener(
            onShowRequested: () => activationReceived.Set(),
            dispatchCallback: action => action());

        // Second launch attempt on a background thread (Mutex is re-entrant on the same thread in Windows!)
        SingleInstanceGuard? secondAttemptResult = null;
        var secondThread = new Thread(() =>
        {
            secondAttemptResult = SingleInstanceGuard.TryAcquirePrimaryOrSignalExisting(mutexName, eventName);
        });
        secondThread.Start();
        secondThread.Join(TimeSpan.FromSeconds(2)).Should().BeTrue();

        secondAttemptResult.Should().BeNull("second process/thread must yield to existing primary instance");
        activationReceived.Wait(TimeSpan.FromSeconds(2))
            .Should().BeTrue("second instance must wake primary instance via ShowWindow EventWaitHandle");
    }

    [Theory]
    [InlineData("Ctrl+Shift+L", 0x4Cu, "Ctrl+Shift+L")]
    [InlineData("Ctrl+Shift+S", 0x53u, "Ctrl+Shift+S")]
    [InlineData("Ctrl+Shift+[", 0xDBu, "Ctrl+Shift+[")]
    [InlineData("Ctrl+Shift+]", 0xDDu, "Ctrl+Shift+]")]
    [InlineData("Ctrl+Shift+G", 0x47u, "Ctrl+Shift+G")]
    [InlineData("Alt+F9", 0x78u, "Alt+F9")]
    public void HotkeyGestureParser_ParsesCanonicalDefaultAndCustomShortcuts(
        string gesture,
        uint expectedVk,
        string expectedNormalized)
    {
        HotkeyGestureParser.TryParse(gesture, out var parsed, out var err).Should().BeTrue(err ?? string.Empty);
        err.Should().BeNull();
        parsed.VirtualKey.Should().Be(expectedVk);
        parsed.NormalizedGesture.Should().Be(expectedNormalized);
        (parsed.Modifiers & 0x4000u).Should().Be(0x4000u, "MOD_NOREPEAT must be enabled");
    }

    [Theory]
    [InlineData("")]
    [InlineData("L")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+InvalidKeyName")]
    public void HotkeyGestureParser_RejectsInvalidShortcutsWithClearErrors(string invalidGesture)
    {
        HotkeyGestureParser.TryParse(invalidGesture, out _, out var err).Should().BeFalse();
        err.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void FullscreenAutoHideMonitor_DetectsStateTransitionsAndNotifiesCleanly()
    {
        var fullscreenFlag = false;
        using var monitor = new FullscreenAutoHideMonitor(
            pollInterval: TimeSpan.FromMilliseconds(20),
            customProbe: () => Volatile.Read(ref fullscreenFlag));

        using var enteredFullscreen = new ManualResetEventSlim(false);
        using var exitedFullscreen = new ManualResetEventSlim(false);

        monitor.FullscreenStateChanged += (_, isFullscreen) =>
        {
            if (isFullscreen)
            {
                enteredFullscreen.Set();
            }
            else
            {
                exitedFullscreen.Set();
            }
        };

        monitor.IsFullscreenExclusiveOrPresentation().Should().BeFalse();
        monitor.Start();

        Volatile.Write(ref fullscreenFlag, true);
        enteredFullscreen.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        monitor.IsCurrentlyFullscreen.Should().BeTrue();

        Volatile.Write(ref fullscreenFlag, false);
        exitedFullscreen.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        monitor.IsCurrentlyFullscreen.Should().BeFalse();

        FullscreenAutoHideMonitor.IsFullscreenState(QueryUserNotificationState.Busy).Should().BeTrue();
        FullscreenAutoHideMonitor.IsFullscreenState(QueryUserNotificationState.RunningD3DFullScreen).Should().BeTrue();
        FullscreenAutoHideMonitor.IsFullscreenState(QueryUserNotificationState.PresentationMode).Should().BeTrue();
        FullscreenAutoHideMonitor.IsFullscreenState(QueryUserNotificationState.AcceptsNotifications).Should().BeFalse();
        FullscreenAutoHideMonitor.IsFullscreenState(QueryUserNotificationState.QuietTime).Should().BeFalse();
    }
}
