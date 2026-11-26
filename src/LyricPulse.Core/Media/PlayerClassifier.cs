using System.Collections.Immutable;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Media;

/// <summary>
/// Classifies Windows GSMTC SourceAppUserModelId values and media metadata
/// into canonical player priority tiers and item kinds.
/// </summary>
public static class PlayerClassifier
{
    private static readonly string[] DedicatedAudioTokens =
    [
        "tidal",
        "musicbee",
        "foobar2000",
        "qobuz",
        "amazonmusic",
        "aimp",
        "vlc",
        "winamp",
        "dopamine",
        "youtubemusicdesktop",
        "music.youtube",
        "cider",
        "itunes",
        "deezer",
        "soundcloud",
        "plexamp",
        "mediamonkey",
        "bandcamp",
        "zune",
        "microsoft.media.player",
        "yandexmusic",
        "netease",
        "qqmusic"
    ];

    private static readonly string[] TitlePlatformSuffixes =
    [
        " - YouTube Music",
        " | YouTube Music",
        " • YouTube Music",
        " - YouTube",
        " | SoundCloud",
        " | Spotify",
        " - Spotify"
    ];

    private static readonly string[] BrowserTokens =
    [
        "chrome.exe",
        "msedge.exe",
        "brave.exe",
        "firefox.exe",
        "opera.exe",
        "vivaldi.exe",
        "_crx_"
    ];

    private static readonly TimeSpan MaxSongDurationForLyrics = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan MaxAdDuration = TimeSpan.FromSeconds(35);

    public static MediaPlayerKind ClassifyPlayer(string? sourceAppUserModelId, ImmutableArray<string> blockedAppIds)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return MediaPlayerKind.Unknown;
        }

        var aumid = sourceAppUserModelId.Trim();

        if (!blockedAppIds.IsDefaultOrEmpty)
        {
            foreach (var blocked in blockedAppIds)
            {
                if (!string.IsNullOrWhiteSpace(blocked) &&
                    aumid.Contains(blocked.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return MediaPlayerKind.Ignored;
                }
            }
        }

        if (IsSpotify(aumid))
        {
            return MediaPlayerKind.Spotify;
        }

        if (IsAppleMusic(aumid))
        {
            return MediaPlayerKind.AppleMusic;
        }

        foreach (var token in DedicatedAudioTokens)
        {
            if (aumid.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return MediaPlayerKind.DedicatedAudioPlayer;
            }
        }

        foreach (var token in BrowserTokens)
        {
            if (aumid.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return MediaPlayerKind.BrowserOrGeneric;
            }
        }

        return MediaPlayerKind.BrowserOrGeneric;
    }

    public static int GetPriorityWeight(MediaPlayerKind kind) => kind switch
    {
        MediaPlayerKind.Spotify => 100,
        MediaPlayerKind.AppleMusic => 90,
        MediaPlayerKind.DedicatedAudioPlayer => 60,
        MediaPlayerKind.BrowserOrGeneric => 30,
        MediaPlayerKind.Ignored => -1,
        _ => 0
    };

    public static MediaItemKind ClassifyItem(
        string? title,
        string? artist,
        string? albumTitle,
        TimeSpan endTime,
        bool isVideo)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return MediaItemKind.Unknown;
        }

        var trimmedTitle = title.Trim();
        var hasArtist = !string.IsNullOrWhiteSpace(artist);
        var hasAlbum = !string.IsNullOrWhiteSpace(albumTitle);

        if (IsSpotifyAdTitle(trimmedTitle) &&
            (!hasArtist || (!hasAlbum && endTime > TimeSpan.Zero && endTime <= MaxAdDuration)))
        {
            return MediaItemKind.Advertisement;
        }

        if (isVideo)
        {
            // Promote official music streams (YouTube Music Art Tracks / SoundCloud / full Album metadata)
            // even when Chromium reports PlaybackType == Video.
            var isTopicArtist = artist?.TrimEnd().EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase) == true;
            var isMusicPlatformTitle = trimmedTitle.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase) ||
                                       trimmedTitle.Contains("SoundCloud", StringComparison.OrdinalIgnoreCase);
            if ((hasArtist && hasAlbum && endTime <= MaxSongDurationForLyrics) || isTopicArtist || isMusicPlatformTitle)
            {
                return MediaItemKind.Music;
            }

            return MediaItemKind.Video;
        }

        if (endTime > MaxSongDurationForLyrics)
        {
            return MediaItemKind.PodcastOrLongForm;
        }

        return MediaItemKind.Music;
    }

    public static void CleanDetectedMetadata(
        string? rawTitle,
        string? rawArtist,
        out string cleanedTitle,
        out string cleanedArtist)
    {
        var title = (rawTitle ?? string.Empty).Trim();
        var artist = (rawArtist ?? string.Empty).Trim();

        foreach (var suffix in TitlePlatformSuffixes)
        {
            if (title.Length > suffix.Length && title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                title = title[..^suffix.Length].TrimEnd();
            }
        }

        const string topicSuffix = " - Topic";
        if (artist.Length > topicSuffix.Length && artist.EndsWith(topicSuffix, StringComparison.OrdinalIgnoreCase))
        {
            artist = artist[..^topicSuffix.Length].TrimEnd();
        }

        // If browser/VLC exposes "Artist - Song Title" with empty Artist, split cleanly.
        if (string.IsNullOrWhiteSpace(artist))
        {
            var dashIdx = title.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIdx >= 2 && dashIdx + 3 <= title.Length - 2)
            {
                var left = title[..dashIdx].Trim();
                var right = title[(dashIdx + 3)..].Trim();
                if (!left.StartsWith("Part ", StringComparison.OrdinalIgnoreCase) &&
                    !left.StartsWith("Pt.", StringComparison.OrdinalIgnoreCase))
                {
                    artist = left;
                    title = right;
                }
            }
        }

        cleanedTitle = title;
        cleanedArtist = artist;
    }

    private static bool IsSpotify(string aumid) =>
        aumid.Equals("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", StringComparison.OrdinalIgnoreCase) ||
        aumid.EndsWith("spotify.exe", StringComparison.OrdinalIgnoreCase) ||
        aumid.Contains("SpotifyAB.SpotifyMusic", StringComparison.OrdinalIgnoreCase);

    private static bool IsAppleMusic(string aumid) =>
        aumid.StartsWith("AppleInc.AppleMusicWin_nzyj5cx40ttqa", StringComparison.OrdinalIgnoreCase) ||
        aumid.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase);

    private static bool IsSpotifyAdTitle(string title) =>
        title.Equals("Advertisement", StringComparison.OrdinalIgnoreCase) ||
        title.Equals("Spotify", StringComparison.OrdinalIgnoreCase);
}
