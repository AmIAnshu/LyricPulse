using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Lyrics;

/// <summary>
/// Compile-time [GeneratedRegex] track title and artist normalizer with NFKC cleanup,
/// 7 ordered metadata-stripping passes, empty-title safety guard, and canonical band whitelist.
/// </summary>
public sealed partial class TitleNormalizer : ITitleNormalizer
{
    public const int MaxInputChars = 512;
    private const int RegexTimeoutMs = 100;

    private static readonly HashSet<string> CanonicalCompoundArtists = new(StringComparer.OrdinalIgnoreCase)
    {
        "Simon & Garfunkel",
        "Earth, Wind & Fire",
        "Hall & Oates",
        "Daryl Hall & John Oates",
        "Florence + The Machine",
        "AC/DC",
        "Above & Beyond",
        "Mumford & Sons",
        "Tears for Fears",
        "Belle and Sebastian",
        "Crosby, Stills, Nash & Young",
        "Emerson, Lake & Palmer",
        "Peter, Paul and Mary",
        "Ike & Tina Turner",
        "Brooks & Dunn",
        "Sonny & Cher",
        "Kool & The Gang",
        "KC & The Sunshine Band",
        "Sly & The Family Stone",
        "Tom Petty & The Heartbreakers",
        "Huey Lewis & The News",
        "Bob Marley & The Wailers",
        "Nick Cave & The Bad Seeds",
        "Echo & The Bunnymen",
        "Siouxsie and the Banshees",
        "Guns N' Roses",
        "Of Monsters and Men",
        "Coheed and Cambria",
        "Tegan and Sara"
    };

    public TrackIdentity CreateIdentity(string rawTitle, string rawArtist, string albumTitle, TimeSpan duration)
    {
        var safeTitle = rawTitle ?? string.Empty;
        var safeArtist = rawArtist ?? string.Empty;
        var safeAlbum = albumTitle ?? string.Empty;
        var safeDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;

        var normalizedTitle = NormalizeTitle(safeTitle);
        var primaryArtist = ExtractPrimaryArtist(safeArtist);
        var cacheKey = ComputeCacheKey(primaryArtist, normalizedTitle, safeDuration);

        return new TrackIdentity(
            RawTitle: safeTitle.Trim(),
            RawArtist: safeArtist.Trim(),
            AlbumTitle: safeAlbum.Trim(),
            NormalizedTitle: normalizedTitle,
            PrimaryArtist: primaryArtist,
            Duration: safeDuration,
            CacheKey: cacheKey);
    }

    public string NormalizeTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return string.Empty;
        }

        var boundedRaw = rawTitle.Length > MaxInputChars ? rawTitle[..MaxInputChars] : rawTitle;
        var unicodeCleaned = CleanupUnicodeAndTypography(boundedRaw);
        if (string.IsNullOrWhiteSpace(unicodeCleaned))
        {
            return string.Empty;
        }

        try
        {
            var current = unicodeCleaned;
            for (var pass = 0; pass < 2; pass++)
            {
                var next = ApplyStrippingRules(current);
                if (next.Equals(current, StringComparison.Ordinal))
                {
                    break;
                }

                current = next;
            }

            current = CollapseAndTrim(current);

            // Safety guard: if stripping emptied or collapsed title to <2 chars, revert to unicode-cleaned raw title.
            if (current.Length < 2)
            {
                return CollapseAndTrim(unicodeCleaned);
            }

            return current;
        }
        catch (RegexMatchTimeoutException)
        {
            return unicodeCleaned.Trim(' ', '-', '·', '/');
        }
    }

    public string ExtractPrimaryArtist(string rawArtist)
    {
        if (string.IsNullOrWhiteSpace(rawArtist))
        {
            return string.Empty;
        }

        var boundedRaw = rawArtist.Length > MaxInputChars ? rawArtist[..MaxInputChars] : rawArtist;
        var unicodeCleaned = CleanupUnicodeAndTypography(boundedRaw);
        if (string.IsNullOrWhiteSpace(unicodeCleaned))
        {
            return string.Empty;
        }

        try
        {
            var cleaned = CollapseAndTrim(unicodeCleaned);
            if (cleaned.Length == 0)
            {
                return string.Empty;
            }

            if (CanonicalCompoundArtists.Contains(cleaned))
            {
                return cleaned;
            }

            // Preserve canonical band prefix if followed by feature clause (e.g., "Simon & Garfunkel feat. X").
            var featureSplit = ArtistFeatureSplitRegex().Split(cleaned);
            if (featureSplit.Length > 1)
            {
                var head = CollapseAndTrim(featureSplit[0]);
                if (CanonicalCompoundArtists.Contains(head))
                {
                    return head;
                }

                cleaned = head;
            }

            var parts = ArtistDelimiterRegex().Split(cleaned);
            var primary = parts.Length > 0 ? CollapseAndTrim(parts[0]) : cleaned;
            return primary.Length > 0 ? primary : cleaned;
        }
        catch (RegexMatchTimeoutException)
        {
            return unicodeCleaned.Trim(' ', '-', '·', '/');
        }
    }

    public static string ComputeCacheKey(string normalizedArtist, string normalizedTitle, TimeSpan duration)
    {
        var durationSec = duration.TotalSeconds;
        var durationBucketSec = durationSec > 0 ? (int)(Math.Round(durationSec / 2.0) * 2) : 0;
        var canonical = $"{normalizedArtist.Trim().ToLowerInvariant()}|{normalizedTitle.Trim().ToLowerInvariant()}|{durationBucketSec}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash);
    }

    private static string CleanupUnicodeAndTypography(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            switch (ch)
            {
                case '\u200B':
                case '\u200C':
                case '\u200D':
                case '\uFEFF':
                    continue;
                case '\u2018':
                case '\u2019':
                    sb.Append('\'');
                    break;
                case '\u201C':
                case '\u201D':
                    sb.Append('"');
                    break;
                case '\u2013':
                case '\u2014':
                case '\u2015':
                    sb.Append('-');
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }

    private static string ApplyStrippingRules(string value)
    {
        var result = SquareBracketTagsRegex().Replace(value, " ");
        result = ParenFeatureClauseRegex().Replace(result, " ");
        result = ParenSoundtrackOriginRegex().Replace(result, " ");
        result = ParenEditionMetadataRegex().Replace(result, " ");
        result = TrailingDashEditionRegex().Replace(result, " ");
        result = InlineFeatureClauseRegex().Replace(result, " ");
        return CollapseAndTrim(result);
    }

    private static string CollapseAndTrim(string value)
    {
        var collapsed = CollapseWhitespaceRegex().Replace(value, " ");
        return collapsed.Trim(' ', '-', '·', '/');
    }

    [GeneratedRegex(@"\s*\[[^\]]{1,80}\]\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex SquareBracketTagsRegex();

    [GeneratedRegex(@"\s*\((?:feat\.?|ft\.?|featuring|with|prod\.?\s+by)\s+[^()]{1,100}\)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex ParenFeatureClauseRegex();

    [GeneratedRegex(@"\s*\((?:(?:music|song|theme|original\s+song|soundtrack)\s+)?from\s+[^()]{1,100}\)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex ParenSoundtrackOriginRegex();

    [GeneratedRegex(@"\s*\((?:[^()]{0,40}\b(?:remaster(?:ed)?|deluxe|super\s+deluxe|anniversary|expanded|collector'?s?|special\s+edition|bonus(?:\s+track)?|explicit|clean|radio\s+edit|album\s+version|single\s+version|mono|stereo|live(?:\s+(?:at|in|from)\b[^()]{0,40})?|acoustic|unplugged|demo|instrumental|karaoke|sped\s+up|slowed(?:\s*\+\s*reverb)?)\b[^()]{0,40}|\d{4}\s*(?:remaster(?:ed)?|mix|version|edition))\)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex ParenEditionMetadataRegex();

    [GeneratedRegex(@"\s+-\s+(?:\d{4}\s+)?(?:remaster(?:ed)?(?:\s+\d{4})?|remix|version|edit|mix|acoustic|live(?:\s+.{0,120})?|instrumental|radio\s+edit|single\s+version|album\s+version|deluxe\b.{0,120}|bonus\b.{0,120}|mono|stereo|explicit|clean|from\s+.{0,120}|feat\.?\s+.{0,120})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex TrailingDashEditionRegex();

    [GeneratedRegex(@"\s+(?:feat\.?|ft\.?|featuring)\s+.{1,120}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex InlineFeatureClauseRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex CollapseWhitespaceRegex();

    [GeneratedRegex(@"\s+(?:feat\.?|ft\.?|featuring)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex ArtistFeatureSplitRegex();

    [GeneratedRegex(@"\s*(?:,\s*|\s+&\s+|\s+x\s+|\s+/\s+|;\s*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex ArtistDelimiterRegex();
}
