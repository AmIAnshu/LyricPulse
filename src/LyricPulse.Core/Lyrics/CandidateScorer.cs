using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Lyrics;

/// <summary>
/// Deterministic 0..115 candidate scorer shared by LRCLIB /api/search and NetEase search.
/// Rejects candidates scoring below 65 to prevent false-positive lyrics matches.
/// </summary>
public static class CandidateScorer
{
    public const int MinimumAcceptScore = 65;

    public static int Score(
        TrackIdentity track,
        string? candidateTitle,
        string? candidateArtist,
        double? candidateDurationSec,
        bool hasWordSync,
        bool hasLineSync,
        ITitleNormalizer normalizer)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(normalizer);

        var targetTitle = track.NormalizedTitle.Trim();
        var candTitle = normalizer.NormalizeTitle(candidateTitle ?? string.Empty).Trim();
        if (targetTitle.Length == 0 || candTitle.Length == 0)
        {
            return 0;
        }

        var titleScore = ScoreTitle(targetTitle, candTitle);
        if (titleScore < 0)
        {
            // Disqualified (< 0.50 title similarity).
            return 0;
        }

        var artistScore = ScoreArtist(track.PrimaryArtist, track.RawArtist, normalizer.ExtractPrimaryArtist(candidateArtist ?? string.Empty), candidateArtist ?? string.Empty);
        var durationScore = ScoreDuration(track.Duration.TotalSeconds, candidateDurationSec);
        var syncScore = hasWordSync ? 25 : hasLineSync ? 15 : 0;

        return Math.Clamp(titleScore + artistScore + durationScore + syncScore, 0, 115);
    }

    private static int ScoreTitle(string target, string candidate)
    {
        if (target.Equals(candidate, StringComparison.OrdinalIgnoreCase))
        {
            return 40;
        }

        var sim = ComputeCombinedSimilarity(target, candidate);
        if (sim < 0.50)
        {
            return -1;
        }

        if (sim >= 0.85)
        {
            return 32;
        }

        return sim >= 0.65 ? 20 : 0;
    }

    private static int ScoreArtist(string targetPrimary, string targetRaw, string candPrimary, string candRaw)
    {
        if (string.IsNullOrWhiteSpace(targetPrimary) && string.IsNullOrWhiteSpace(targetRaw))
        {
            return 15;
        }

        if (string.IsNullOrWhiteSpace(candPrimary) && string.IsNullOrWhiteSpace(candRaw))
        {
            return -30;
        }

        var tNorm = targetPrimary.Trim();
        var cNorm = candPrimary.Trim();

        if (tNorm.Equals(cNorm, StringComparison.OrdinalIgnoreCase) ||
            candRaw.Contains(tNorm, StringComparison.OrdinalIgnoreCase) ||
            targetRaw.Contains(cNorm, StringComparison.OrdinalIgnoreCase))
        {
            return 30;
        }

        var overlap = ComputeTokenJaccard(tNorm, cNorm);
        return overlap >= 0.50 ? 18 : -30;
    }

    private static int ScoreDuration(double targetSec, double? candidateSec)
    {
        if (targetSec <= 0 || !candidateSec.HasValue || candidateSec.Value <= 0)
        {
            return 0;
        }

        var delta = Math.Abs(targetSec - candidateSec.Value);
        if (delta <= 2.0)
        {
            return 20;
        }

        if (delta <= 5.0)
        {
            return 14;
        }

        if (delta <= 10.0)
        {
            return 6;
        }

        return delta > 20.0 ? -25 : 0;
    }

    public static double ComputeCombinedSimilarity(string a, string b)
    {
        var jaccard = ComputeTokenJaccard(a, b);
        var lev = ComputeNormalizedLevenshtein(a.ToLowerInvariant(), b.ToLowerInvariant());
        return (jaccard * 0.5) + (lev * 0.5);
    }

    private static double ComputeTokenJaccard(string a, string b)
    {
        var tokensA = Tokenize(a);
        var tokensB = Tokenize(b);
        if (tokensA.Count == 0 && tokensB.Count == 0)
        {
            return 1.0;
        }

        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return 0.0;
        }

        var intersection = tokensA.Count(tokensB.Contains);
        var union = tokensA.Count + tokensB.Count - intersection;
        return union > 0 ? intersection / (double)union : 0.0;
    }

    private static HashSet<string> Tokenize(string value)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var start = -1;
        for (var i = 0; i <= value.Length; i++)
        {
            var isAlnum = i < value.Length && char.IsLetterOrDigit(value[i]);
            if (isAlnum && start < 0)
            {
                start = i;
            }
            else if (!isAlnum && start >= 0)
            {
                set.Add(value[start..i]);
                start = -1;
            }
        }

        return set;
    }

    private static double ComputeNormalizedLevenshtein(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
        {
            return 1.0;
        }

        var maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0)
        {
            return 1.0;
        }

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return 1.0 - (prev[b.Length] / (double)maxLen);
    }
}
