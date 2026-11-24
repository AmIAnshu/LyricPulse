using System.Collections.Immutable;
using System.Globalization;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;

namespace LyricPulse.App.Tray;

public sealed record TrayStyleOption(
    OverlayStyleId StyleId,
    string Label,
    bool IsActive);

public sealed record TrayOffsetOption(
    string Label,
    TimeSpan? Delta,
    bool IsReset,
    bool IsEnabled);

/// <summary>
/// Pure, immutable presentation model for the Windows System Tray icon, tooltip, and NativeMenu
/// adhering strictly to <c>design-system/lyricpulse/pages/tray.md</c> (zero emojis in chrome,
/// Unicode minus U+2212 sign convention, 38-char header truncation, 96-char tooltip cap).
/// </summary>
public sealed record TrayMenuState(
    string HeaderPrimaryLine,
    string HeaderSecondaryLine,
    string TooltipText,
    TrayIconVisualState IconState,
    bool IsLyricsVisible,
    string ShowLyricsGestureHint,
    ImmutableArray<TrayStyleOption> PinnedStyles,
    string TimingOffsetHeader,
    ImmutableArray<TrayOffsetOption> OffsetOptions,
    bool CanNudgeOffset,
    string OffsetPersistenceCaption,
    bool IsDemoMode,
    bool IsReducedMotion)
{
    public const int MaxHeaderChars = 38;
    public const int MaxTooltipChars = 96;

    public static TrayMenuState FromSnapshot(OverlayPlaybackSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var settings = snapshot.Settings;
        var hasSyncedLyrics = snapshot.Document is not null && snapshot.LyricsStatus == LyricsStatus.Synced;
        var hasTrack = snapshot.Track is { IsValid: true };

        var headerPrimary = BuildHeaderPrimary(snapshot, hasTrack);
        var headerSecondary = BuildHeaderSecondary(snapshot, hasTrack, hasSyncedLyrics);
        var iconState = ResolveIconState(snapshot, hasTrack, hasSyncedLyrics);
        var tooltip = BuildTooltip(snapshot, hasTrack);

        var pinned = BuildPinnedStyleOptions(settings);
        var offsetSec = snapshot.TimingOffset.Offset.TotalSeconds;
        var hasNonZeroOffset = Math.Abs(offsetSec) >= 0.005;
        var canNudge = hasSyncedLyrics;

        var offsetHeader = FormatTimingOffsetHeader(snapshot.TimingOffset.Offset);
        var offsetOptions = ImmutableArray.Create(
            new TrayOffsetOption("\u22121.0s (Earlier)", TimeSpan.FromSeconds(-1.0), IsReset: false, IsEnabled: canNudge),
            new TrayOffsetOption("\u22120.5s (Earlier)", TimeSpan.FromSeconds(-0.5), IsReset: false, IsEnabled: canNudge),
            new TrayOffsetOption("Reset (0.0s)", Delta: null, IsReset: true, IsEnabled: canNudge && hasNonZeroOffset),
            new TrayOffsetOption("+0.5s (Later)", TimeSpan.FromSeconds(0.5), IsReset: false, IsEnabled: canNudge),
            new TrayOffsetOption("+1.0s (Later)", TimeSpan.FromSeconds(1.0), IsReset: false, IsEnabled: canNudge));

        var persistenceCaption = snapshot.IsDemoMode
            ? "Demo session (not saved)"
            : settings.EnablePerTrackSavedOffsets
                ? "Saved per song"
                : "Resets automatically on next track";

        var toggleHint = settings.GlobalHotkeys.TryGetValue(HotkeyAction.ToggleLyrics, out var gesture)
            ? gesture
            : "Ctrl+Shift+L";

        return new TrayMenuState(
            HeaderPrimaryLine: headerPrimary,
            HeaderSecondaryLine: headerSecondary,
            TooltipText: tooltip,
            IconState: iconState,
            IsLyricsVisible: settings.LyricsVisible,
            ShowLyricsGestureHint: toggleHint,
            PinnedStyles: pinned,
            TimingOffsetHeader: offsetHeader,
            OffsetOptions: offsetOptions,
            CanNudgeOffset: canNudge,
            OffsetPersistenceCaption: persistenceCaption,
            IsDemoMode: snapshot.IsDemoMode,
            IsReducedMotion: settings.ReducedMotion);
    }

    public bool Equals(TrayMenuState? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return HeaderPrimaryLine == other.HeaderPrimaryLine &&
               HeaderSecondaryLine == other.HeaderSecondaryLine &&
               TooltipText == other.TooltipText &&
               IconState == other.IconState &&
               IsLyricsVisible == other.IsLyricsVisible &&
               ShowLyricsGestureHint == other.ShowLyricsGestureHint &&
               TimingOffsetHeader == other.TimingOffsetHeader &&
               CanNudgeOffset == other.CanNudgeOffset &&
               OffsetPersistenceCaption == other.OffsetPersistenceCaption &&
               IsDemoMode == other.IsDemoMode &&
               IsReducedMotion == other.IsReducedMotion &&
               PinnedStyles.SequenceEqual(other.PinnedStyles) &&
               OffsetOptions.SequenceEqual(other.OffsetOptions);
    }

    public override int GetHashCode()
    {
        var hc = new HashCode();
        hc.Add(HeaderPrimaryLine);
        hc.Add(HeaderSecondaryLine);
        hc.Add(TooltipText);
        hc.Add(IconState);
        hc.Add(IsLyricsVisible);
        hc.Add(TimingOffsetHeader);
        hc.Add(IsDemoMode);
        hc.Add(IsReducedMotion);
        hc.Add(PinnedStyles.Length);
        return hc.ToHashCode();
    }

    public static string GetStyleDisplayName(OverlayStyleId styleId) => styleId switch
    {
        OverlayStyleId.Fisheye => "Fisheye",
        OverlayStyleId.FisheyeVisual => "Fisheye Visual",
        OverlayStyleId.Codestyle => "Codestyle",
        OverlayStyleId.CodestyleVisual => "Codestyle Visual",
        OverlayStyleId.VisualStack => "Visual Stack",
        OverlayStyleId.Constellation => "Constellation",
        OverlayStyleId.SpidersWeb => "Spider's Web",
        OverlayStyleId.NodeLine => "Node Line",
        OverlayStyleId.KineticWall => "Kinetic Wall",
        OverlayStyleId.Messages => "Messages",
        _ => styleId.ToString()
    };

    public static string FormatTimingOffsetHeader(TimeSpan offset)
    {
        var seconds = offset.TotalSeconds;
        if (Math.Abs(seconds) < 0.005)
        {
            return "Timing Offset";
        }

        var formatted = FormatSignedSeconds(seconds);
        return $"Timing Offset ({formatted})";
    }

    public static string FormatSignedSeconds(double seconds)
    {
        var absStr = Math.Abs(seconds).ToString("0.0", CultureInfo.InvariantCulture);
        return seconds < 0 ? $"\u2212{absStr}s" : $"+{absStr}s";
    }

    public static string TruncateMiddle(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        if (maxLength <= 3)
        {
            return text[..maxLength];
        }

        var headLen = (maxLength - 1) / 2;
        var tailLen = maxLength - 1 - headLen;
        return $"{text[..headLen].TrimEnd()}\u2026{text[^tailLen..].TrimStart()}";
    }

    private static string BuildHeaderPrimary(OverlayPlaybackSnapshot snapshot, bool hasTrack)
    {
        if (snapshot.IsDemoMode)
        {
            return TruncateMiddle("Demo: Neon Horizon", MaxHeaderChars);
        }

        if (!hasTrack || snapshot.Track is null)
        {
            return "Waiting for Spotify / Apple Music...";
        }

        var titleArtist = string.IsNullOrWhiteSpace(snapshot.Track.RawArtist)
            ? snapshot.Track.RawTitle
            : $"{snapshot.Track.RawTitle} \u2014 {snapshot.Track.RawArtist}";

        var raw = snapshot.PlaybackState == MediaPlaybackState.Paused
            ? $"Paused \u2014 {titleArtist}"
            : titleArtist;

        return TruncateMiddle(raw, MaxHeaderChars);
    }

    private static string BuildHeaderSecondary(OverlayPlaybackSnapshot snapshot, bool hasTrack, bool hasSyncedLyrics)
    {
        if (snapshot.IsDemoMode)
        {
            return "Demo Mode \u00B7 Synced lyrics";
        }

        if (!hasTrack)
        {
            return "Waiting for Spotify or Apple Music";
        }

        var player = FormatPlayerName(snapshot.PlayerKind);
        var status = snapshot.LyricsStatus switch
        {
            LyricsStatus.Synced => "Synced lyrics",
            LyricsStatus.Instrumental => "Instrumental track",
            LyricsStatus.PlainOnly => "Unsynced lyrics only",
            LyricsStatus.Error => "Lyrics lookup error",
            _ => hasSyncedLyrics ? "Synced lyrics" : "No synced lyrics found"
        };

        return $"{player} \u00B7 {status}";
    }

    private static TrayIconVisualState ResolveIconState(
        OverlayPlaybackSnapshot snapshot,
        bool hasTrack,
        bool hasSyncedLyrics)
    {
        if (!snapshot.Settings.LyricsVisible || snapshot.IsFullscreenHidden)
        {
            return TrayIconVisualState.PausedOrHidden;
        }

        if (snapshot.IsDemoMode)
        {
            return TrayIconVisualState.Playing;
        }

        if (!hasTrack || snapshot.PlaybackState is MediaPlaybackState.Closed or MediaPlaybackState.Stopped)
        {
            return TrayIconVisualState.Idle;
        }

        if (snapshot.PlaybackState == MediaPlaybackState.Paused)
        {
            return TrayIconVisualState.PausedOrHidden;
        }

        return hasSyncedLyrics ? TrayIconVisualState.Playing : TrayIconVisualState.Error;
    }

    private static string BuildTooltip(OverlayPlaybackSnapshot snapshot, bool hasTrack)
    {
        if (!snapshot.Settings.LyricsVisible)
        {
            return "LyricPulse \u2014 Lyrics hidden (Right-click for menu)";
        }

        var styleName = GetStyleDisplayName(snapshot.Settings.ActiveStyle);
        var offsetSec = snapshot.TimingOffset.Offset.TotalSeconds;
        var suffix = Math.Abs(offsetSec) >= 0.005 ? FormatSignedSeconds(offsetSec) : styleName;

        if (snapshot.IsDemoMode)
        {
            return TruncateEnd($"LyricPulse \u2014 Demo: Neon Horizon ({suffix})", MaxTooltipChars);
        }

        if (!hasTrack || snapshot.Track is null)
        {
            return "LyricPulse \u2014 Ready (Waiting for Spotify or Apple Music)";
        }

        var baseText = snapshot.PlaybackState == MediaPlaybackState.Paused
            ? $"LyricPulse \u2014 Paused \u00B7 {snapshot.Track.RawTitle}"
            : $"LyricPulse \u2014 {snapshot.Track.RawTitle} \u00B7 {snapshot.Track.RawArtist} ({suffix})";

        return TruncateEnd(baseText, MaxTooltipChars);
    }

    private static ImmutableArray<TrayStyleOption> BuildPinnedStyleOptions(AppSettings settings)
    {
        var pinned = settings.PinnedStyles.IsDefaultOrEmpty
            ? AppSettings.Default.PinnedStyles
            : settings.PinnedStyles;

        var builder = ImmutableArray.CreateBuilder<TrayStyleOption>(pinned.Length + 1);
        var activeInPins = false;

        foreach (var style in pinned)
        {
            var isActive = style == settings.ActiveStyle;
            if (isActive)
            {
                activeInPins = true;
            }

            builder.Add(new TrayStyleOption(style, GetStyleDisplayName(style), isActive));
        }

        if (!activeInPins)
        {
            builder.Insert(0, new TrayStyleOption(
                settings.ActiveStyle,
                $"{GetStyleDisplayName(settings.ActiveStyle)} (Current)",
                IsActive: true));
        }

        return builder.ToImmutable();
    }

    private static string FormatPlayerName(MediaPlayerKind kind) => kind switch
    {
        MediaPlayerKind.Spotify => "Spotify",
        MediaPlayerKind.AppleMusic => "Apple Music",
        MediaPlayerKind.DedicatedAudioPlayer => "Audio Player",
        MediaPlayerKind.BrowserOrGeneric => "Media Player",
        _ => "Windows Media"
    };

    private static string TruncateEnd(string text, int maxLength) =>
        text.Length <= maxLength ? text : $"{text[..(maxLength - 1)].TrimEnd()}\u2026";
}
