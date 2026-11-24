using System.Collections.Immutable;
using AwesomeAssertions;
using LyricPulse.Core.Models;
using LyricPulse.Core.Settings;
using Xunit;

namespace LyricPulse.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
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
    public async Task SaveAndLoad_RoundTripsAllAppSettingsFieldsViaAotJsonContext()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        var store = new JsonSettingsStore(path);

        AppSettings? raisedEvent = null;
        store.SettingsChanged += (_, updated) => raisedEvent = updated;

        var custom = AppSettings.Default with
        {
            ActiveStyle = OverlayStyleId.KineticWall,
            PinnedStyles = [OverlayStyleId.Fisheye, OverlayStyleId.Constellation, OverlayStyleId.KineticWall],
            FontFamily = "Bodoni Moda",
            FontScale = 1.25,
            AutoSyncColors = false,
            ManualAccent1 = new RgbColor(0x5E, 0xEA, 0xD4),
            ManualAccent2 = new RgbColor(0x81, 0x8C, 0xF8),
            ManualAccent3 = new RgbColor(0x94, 0xA3, 0xB8),
            OverlayOpacity = 0.85,
            AnchorZone = AnchorZone.TopCenter,
            SafeMarginPx = 64,
            DisabledDisplays = [@"\\.\DISPLAY2"],
            UniversalPlayerMode = true,
            IgnoreBrowserVideoSessions = false,
            EnablePerTrackSavedOffsets = true,
            PerTrackOffsetsMs = ImmutableDictionary<string, int>.Empty.Add("abc123", -500),
            ReducedMotion = true,
            HighContrastScrim = true,
            ScrimIntensity = ScrimIntensity.Strong,
            ThemeVariantMode = ThemeVariantMode.Dark,
            HideDuringFullscreen = false,
            HideDuringInstrumentals = true,
            SecondaryLineMode = SecondaryLineMode.Translation,
            LyricsVisible = false,
            VisualPreference = VisualPreference.FluentIconsOnly
        };

        await store.SaveAsync(custom);
        raisedEvent.Should().NotBeNull();
        File.Exists(path).Should().BeTrue();

        var reloadedStore = new JsonSettingsStore(path);
        var loaded = await reloadedStore.LoadAsync();

        loaded.ActiveStyle.Should().Be(OverlayStyleId.KineticWall);
        loaded.PinnedStyles.Should().Equal(OverlayStyleId.Fisheye, OverlayStyleId.Constellation, OverlayStyleId.KineticWall);
        loaded.FontFamily.Should().Be("Bodoni Moda");
        loaded.FontScale.Should().Be(1.25);
        loaded.AutoSyncColors.Should().BeFalse();
        loaded.ManualAccent1.Should().Be(new RgbColor(0x5E, 0xEA, 0xD4));
        loaded.OverlayOpacity.Should().Be(0.85);
        loaded.AnchorZone.Should().Be(AnchorZone.TopCenter);
        loaded.SafeMarginPx.Should().Be(64);
        loaded.DisabledDisplays.Should().Equal(@"\\.\DISPLAY2");
        loaded.UniversalPlayerMode.Should().BeTrue();
        loaded.IgnoreBrowserVideoSessions.Should().BeFalse();
        loaded.PerTrackOffsetsMs["abc123"].Should().Be(-500);
        loaded.ReducedMotion.Should().BeTrue();
        loaded.HighContrastScrim.Should().BeTrue();
        loaded.ScrimIntensity.Should().Be(ScrimIntensity.Strong);
        loaded.ThemeVariantMode.Should().Be(ThemeVariantMode.Dark);
        loaded.HideDuringFullscreen.Should().BeFalse();
        loaded.HideDuringInstrumentals.Should().BeTrue();
        loaded.SecondaryLineMode.Should().Be(SecondaryLineMode.Translation);
        loaded.LyricsVisible.Should().BeFalse();
        loaded.VisualPreference.Should().Be(VisualPreference.FluentIconsOnly);
        loaded.GlobalHotkeys[HotkeyAction.ToggleLyrics].Should().Be("Ctrl+Shift+L");
    }

    [Fact]
    public async Task LoadAsync_RecoversGracefullyFromMissingOrCorruptFile()
    {
        var path = Path.Combine(_tempDir, "corrupt-settings.json");
        var store = new JsonSettingsStore(path);

        // Missing file -> defaults
        var fromMissing = await store.LoadAsync();
        fromMissing.ActiveStyle.Should().Be(OverlayStyleId.Fisheye);
        fromMissing.EnablePerTrackSavedOffsets.Should().BeTrue();

        // Corrupt JSON -> recovers cleanly to defaults
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "{ broken json [[[");
        var fromCorrupt = await store.LoadAsync();
        fromCorrupt.ActiveStyle.Should().Be(OverlayStyleId.Fisheye);
        fromCorrupt.FontFamily.Should().Be("Jost");
    }

    [Fact]
    public void ValidateAndNormalize_ClampsOutOfRangeValuesAndEnforcesBodoniFloor()
    {
        var invalid = AppSettings.Default with
        {
            FontFamily = "Bodoni Moda",
            FontScale = 0.10,
            OverlayOpacity = 5.0,
            SafeMarginPx = 999
        };

        var normalized = invalid.ValidateAndNormalize();

        normalized.FontScale.Should().Be(AppSettings.MinFontScale);
        normalized.OverlayOpacity.Should().Be(AppSettings.MaxOverlayOpacity);
        normalized.SafeMarginPx.Should().Be(AppSettings.MaxSafeMarginPx);
        normalized.ComputeEffectiveFontSizeDip().Should().Be(AppSettings.BodoniModaMinDip);

        var jostMin = (AppSettings.Default with { FontFamily = "Jost", FontScale = 0.60 }).ValidateAndNormalize();
        jostMin.ComputeEffectiveFontSizeDip().Should().BeApproximately(28.8, 0.01);
    }

    [Fact]
    public async Task ResetToDefaultsAsync_RestoresAndPersistsDefaults()
    {
        var path = Path.Combine(_tempDir, "reset-settings.json");
        var store = new JsonSettingsStore(path);
        await store.SaveAsync(AppSettings.Default with { ActiveStyle = OverlayStyleId.Messages, ReducedMotion = true });

        var reset = await store.ResetToDefaultsAsync();
        reset.ActiveStyle.Should().Be(OverlayStyleId.Fisheye);
        reset.ReducedMotion.Should().BeFalse();

        var reloaded = await new JsonSettingsStore(path).LoadAsync();
        reloaded.ActiveStyle.Should().Be(OverlayStyleId.Fisheye);
    }
}
