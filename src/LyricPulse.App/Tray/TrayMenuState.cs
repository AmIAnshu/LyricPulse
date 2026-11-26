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
/// Unicode minus U+2212 sign convention, grapheme-safe 38-char header truncation, 96-char tooltip cap,
/// accurate Loading/Instrumental/Error/Fullscreen states, hotkey conflict visibility, and OS Reduced Motion sync).
/// </summary>
public sealed record TrayMenuState(
    string HeaderPrimaryLine,
    string HeaderSecondaryLine,
    string TooltipText,
    TrayIconVisualState IconState,
    bool IsLyricsVisible,
    bool IsDesktopOverlayEnabled,
    string? ShowLyricsGestureHint,
    string? HotkeyConflictNotice,
    ImmutableArray<TrayStyleOption> PinnedStyles,
    ImmutableArray<TrayStyleOption> AllStyles,
    string TimingOffsetHeader,
    ImmutableArray<TrayOffsetOption> OffsetOptions,
    bool CanNudgeOffset,
    string OffsetPersistenceCaption,
    bool CanRetryLyricsFetch,
    bool IsUniversalPlayerMode,
    bool IsDemoMode,
    bool IsEffectiveReducedMotion,
    bool IsOsReducedMotion,
    string ReducedMotionLabel)
{
    public const int MaxHeaderChars = 38;
    public const int MaxTooltipChars = 96;

    public static TrayMenuState FromSnapshot(
        OverlayPlaybackSnapshot snapshot,
        IReadOnlyDictionary<HotkeyAction, string>? hotkeyConflicts = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var settings = snapshot.Settings;
        var hasSyncedLyrics = snapshot.Document is not null && snapshot.LyricsStatus == LyricsStatus.Synced;
        var hasTrack = snapshot.Track is { IsValid: true };

        var headerPrimary = BuildHeaderPrimary(snapshot, hasTrack);
        var headerSecondary = BuildHeaderSecondary(snapshot, hasTrack);
        var iconState = ResolveIconState(snapshot, hasTrack, hasSyncedLyrics);

        var conflictNotice = BuildHotkeyConflictNotice(hotkeyConflicts);
        var tooltip = BuildTooltip(snapshot, hasTrack, conflictNotice is not null);

        var pinned = BuildPinnedStyleOptions(settings);
        var allStyles = Enum.GetValues<OverlayStyleId>()
            .Select(id => new TrayStyleOption(id, GetStyleDisplayName(id), id == settings.ActiveStyle))
            .ToImmutableArray();
        var offsetSec = snapshot.TimingOffset.Offset.TotalSeconds;
        var hasNonZeroOffset = Math.Abs(offsetSec) >= 0.005;
        var canNudge = hasSyncedLyrics && (snapshot.IsLyricsVisible || snapshot.IsStageWindowOpen);

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

        // Strip conflicting shortcut hint from menu label so dead hotkeys are never advertised
        var toggleHasConflict = hotkeyConflicts is not null && hotkeyConflicts.ContainsKey(HotkeyAction.ToggleLyrics);
        string? toggleHint = toggleHasConflict
            ? null
            : settings.GlobalHotkeys.TryGetValue(HotkeyAction.ToggleLyrics, out var gesture)
                ? gesture
                : "Ctrl+Shift+L";

        var canRetry = !snapshot.IsDemoMode &&
                       hasTrack &&
                       snapshot.LyricsStatus is LyricsStatus.Error or LyricsStatus.NotFound;

        var reducedLabel = snapshot.IsOsReducedMotion
            ? "Reduced Motion (Windows setting)"
            : "Reduced Motion";

        return new TrayMenuState(
            HeaderPrimaryLine: headerPrimary,
            HeaderSecondaryLine: headerSecondary,
            TooltipText: tooltip,
            IconState: iconState,
            IsLyricsVisible: settings.LyricsVisible,
            IsDesktopOverlayEnabled: settings.DesktopOverlayEnabled,
            ShowLyricsGestureHint: toggleHint,
            HotkeyConflictNotice: conflictNotice,
            PinnedStyles: pinned,
            AllStyles: allStyles,
            TimingOffsetHeader: offsetHeader,
            OffsetOptions: offsetOptions,
            CanNudgeOffset: canNudge,
            OffsetPersistenceCaption: persistenceCaption,
            CanRetryLyricsFetch: canRetry,
            IsUniversalPlayerMode: settings.UniversalPlayerMode,
            IsDemoMode: snapshot.IsDemoMode,
            IsEffectiveReducedMotion: snapshot.IsEffectiveReducedMotion,
            IsOsReducedMotion: snapshot.IsOsReducedMotion,
            ReducedMotionLabel: reducedLabel);
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
               IsDesktopOverlayEnabled == other.IsDesktopOverlayEnabled &&
               ShowLyricsGestureHint == other.ShowLyricsGestureHint &&
               HotkeyConflictNotice == other.HotkeyConflictNotice &&
               TimingOffsetHeader == other.TimingOffsetHeader &&
               CanNudgeOffset == other.CanNudgeOffset &&
               OffsetPersistenceCaption == other.OffsetPersistenceCaption &&
               CanRetryLyricsFetch == other.CanRetryLyricsFetch &&
               IsUniversalPlayerMode == other.IsUniversalPlayerMode &&
               IsDemoMode == other.IsDemoMode &&
               IsEffectiveReducedMotion == other.IsEffectiveReducedMotion &&
               IsOsReducedMotion == other.IsOsReducedMotion &&
               ReducedMotionLabel == other.ReducedMotionLabel &&
               PinnedStyles.SequenceEqual(other.PinnedStyles) &&
               AllStyles.SequenceEqual(other.AllStyles) &&
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
        hc.Add(IsDesktopOverlayEnabled);
        hc.Add(HotkeyConflictNotice);
        hc.Add(TimingOffsetHeader);
        hc.Add(CanRetryLyricsFetch);
        hc.Add(IsUniversalPlayerMode);
        hc.Add(IsDemoMode);
        hc.Add(IsEffectiveReducedMotion);
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
        OverlayStyleId.VinylGroove => "Vinyl Groove",
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

    /// <summary>Grapheme-cluster-safe middle truncation preventing broken UTF-16 surrogate pairs.</summary>
    public static string TruncateMiddle(string text, int maxElements)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var info = new StringInfo(text);
        var count = info.LengthInTextElements;
        if (count <= maxElements)
        {
            return text;
        }

        if (maxElements <= 3)
        {
            return info.SubstringByTextElements(0, maxElements);
        }

        var headLen = (maxElements - 1) / 2;
        var tailLen = maxElements - 1 - headLen;
        var head = info.SubstringByTextElements(0, headLen).TrimEnd();
        var tail = info.SubstringByTextElements(count - tailLen, tailLen).TrimStart();
        return $"{head}\u2026{tail}";
    }

    private static string TruncateEnd(string text, int maxElements)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var info = new StringInfo(text);
        if (info.LengthInTextElements <= maxElements)
        {
            return text;
        }

        return info.SubstringByTextElements(0, Math.Max(1, maxElements - 1)).TrimEnd() + "\u2026";
    }

    private static string BuildHeaderPrimary(OverlayPlaybackSnapshot snapshot, bool hasTrack)
    {
        if (snapshot.IsDemoMode)
        {
            return TruncateMiddle("Demo: Neon Horizon", MaxHeaderChars);
        }

        if (!hasTrack || snapshot.Track is null)
        {
            return "No media playing";
        }

        var titleArtist = string.IsNullOrWhiteSpace(snapshot.Track.RawArtist)
            ? snapshot.Track.RawTitle
            : $"{snapshot.Track.RawTitle} \u2014 {snapshot.Track.RawArtist}";

        var raw = snapshot.PlaybackState == MediaPlaybackState.Paused
            ? $"Paused: {titleArtist}"
            : $"Playing: {titleArtist}";

        return TruncateMiddle(raw, MaxHeaderChars);
    }

    private static string BuildHeaderSecondary(OverlayPlaybackSnapshot snapshot, bool hasTrack)
    {
        if (snapshot.IsFullscreenHidden)
        {
            return "Paused for fullscreen app";
        }

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
            LyricsStatus.Loading => "Fetching lyrics\u2026",
            LyricsStatus.Synced => "Synced lyrics",
            LyricsStatus.Instrumental => "Instrumental track",
            LyricsStatus.PlainOnly => "No synced lyrics",
            LyricsStatus.Error => "Offline / lyrics unavailable",
            _ => "No synced lyrics"
        };

        return $"{player} \u00B7 {status}";
    }

    private static TrayIconVisualState ResolveIconState(
        OverlayPlaybackSnapshot snapshot,
        bool hasTrack,
        bool hasSyncedLyrics)
    {
        // Diagonal slash strictly for Overlay Hidden / Fullscreen Auto-Hidden (NEVER for Paused!)
        if (!snapshot.Settings.LyricsVisible || snapshot.IsFullscreenHidden)
        {
            return TrayIconVisualState.OverlayHidden;
        }

        if (snapshot.IsDemoMode)
        {
            return TrayIconVisualState.Playing;
        }

        if (!hasTrack ||
            snapshot.PlaybackState is MediaPlaybackState.Closed or MediaPlaybackState.Stopped or MediaPlaybackState.Paused or MediaPlaybackState.Changing)
        {
            return TrayIconVisualState.Idle;
        }

        // Loading and Instrumental tracks use clean neutral Idle waveform (never false amber error!)
        if (snapshot.LyricsStatus is LyricsStatus.Loading or LyricsStatus.Instrumental)
        {
            return TrayIconVisualState.Idle;
        }

        return hasSyncedLyrics ? TrayIconVisualState.Playing : TrayIconVisualState.Error;
    }

    private static string BuildTooltip(OverlayPlaybackSnapshot snapshot, bool hasTrack, bool hasHotkeyConflict)
    {
        string baseText;
        if (snapshot.IsFullscreenHidden)
        {
            baseText = "LyricPulse \u2014 Paused for fullscreen app";
        }
        else if (!snapshot.Settings.LyricsVisible)
        {
            baseText = "LyricPulse \u2014 Lyrics hidden (Left-click to show)";
        }
        else
        {
            var styleName = GetStyleDisplayName(snapshot.Settings.ActiveStyle);
            var offsetSec = snapshot.TimingOffset.Offset.TotalSeconds;
            var suffix = Math.Abs(offsetSec) >= 0.005 ? FormatSignedSeconds(offsetSec) : styleName;

            if (snapshot.IsDemoMode)
            {
                baseText = $"LyricPulse \u2014 Demo: Neon Horizon ({suffix})";
            }
            else if (!hasTrack || snapshot.Track is null)
            {
                baseText = "LyricPulse \u2014 Ready (Left-click for Demo, right-click for menu)";
            }
            else if (snapshot.PlaybackState == MediaPlaybackState.Paused)
            {
                baseText = $"LyricPulse \u2014 Paused \u00B7 {snapshot.Track.RawTitle}";
            }
            else
            {
                baseText = snapshot.LyricsStatus switch
                {
                    LyricsStatus.Loading => $"LyricPulse \u2014 Fetching lyrics \u00B7 {snapshot.Track.RawTitle}",
                    LyricsStatus.Instrumental => $"LyricPulse \u2014 Instrumental \u00B7 {snapshot.Track.RawTitle}",
                    LyricsStatus.Error => $"LyricPulse \u2014 Offline / lyrics unavailable \u00B7 {snapshot.Track.RawTitle}",
                    LyricsStatus.NotFound or LyricsStatus.PlainOnly => $"LyricPulse \u2014 No synced lyrics \u00B7 {snapshot.Track.RawTitle}",
                    _ => $"LyricPulse \u2014 {snapshot.Track.RawTitle} \u00B7 {snapshot.Track.RawArtist} ({suffix})"
                };
            }
        }

        if (hasHotkeyConflict)
        {
            const string conflictSuffix = " [Shortcut conflict]";
            var bodyBudget = Math.Max(16, MaxTooltipChars - conflictSuffix.Length);
            return TruncateEnd(baseText, bodyBudget) + conflictSuffix;
        }

        return TruncateEnd(baseText, MaxTooltipChars);
    }

    private static string? BuildHotkeyConflictNotice(IReadOnlyDictionary<HotkeyAction, string>? conflicts)
    {
        if (conflicts is null || conflicts.Count == 0)
        {
            return null;
        }

        return conflicts.Count == 1
            ? TruncateEnd($"Shortcut conflict: {conflicts.Values.First()}", MaxHeaderChars + 14)
            : $"Shortcut conflicts: {conflicts.Count} hotkeys in use by another app";
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
}
