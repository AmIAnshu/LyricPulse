using System.Collections.Immutable;
using System.Text;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Lyrics;

/// <summary>
/// Proportional syllable- and character-weighted word-timing synthesizer
/// with instrumental gap capping, 70ms readability floor, and CJK tokenization.
/// </summary>
public static class WordTimingInterpolator
{
    public static readonly TimeSpan MinWordDuration = TimeSpan.FromMilliseconds(70);
    public static readonly TimeSpan DefaultFinalLineDuration = TimeSpan.FromSeconds(4.5);

    public static ImmutableArray<LyricWord> SynthesizeWords(
        string lineText,
        TimeSpan startTime,
        TimeSpan? nextLineStart,
        TimeSpan trackDuration,
        out TimeSpan lineEnd)
    {
        var tokens = TokenizeLine(lineText);
        if (tokens.Count == 0)
        {
            lineEnd = startTime;
            return ImmutableArray<LyricWord>.Empty;
        }

        var weights = ComputeTokenWeights(tokens, out var totalWeight, out var totalSyllables, out var punctPauseSec);
        var activeMs = ComputeActiveDurationMs(startTime, nextLineStart, trackDuration, totalSyllables, punctPauseSec, tokens.Count);
        var durationsMs = AllocateWordDurations(weights, totalWeight, activeMs);

        var builder = ImmutableArray.CreateBuilder<LyricWord>(tokens.Count);
        var cursor = startTime;

        for (var i = 0; i < tokens.Count; i++)
        {
            var end = cursor + TimeSpan.FromMilliseconds(Math.Max(1.0, durationsMs[i]));
            builder.Add(new LyricWord(tokens[i].Text, cursor, end, tokens[i].SpaceSeparated));
            cursor = end;
        }

        lineEnd = cursor;
        return builder.ToImmutable();
    }

    public static double EstimateSyllables(string token)
    {
        var cjkCount = 0;
        var vowelGroups = 0;
        var inVowel = false;

        foreach (var ch in token)
        {
            if (IsCjkChar(ch))
            {
                cjkCount++;
                inVowel = false;
                continue;
            }

            var lower = char.ToLowerInvariant(ch);
            var isVowel = lower is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
            if (isVowel && !inVowel)
            {
                vowelGroups++;
            }

            inVowel = isVowel;
        }

        if (cjkCount > 0)
        {
            return (1.35 * cjkCount) + vowelGroups;
        }

        var alphaOnly = token.TrimEnd('.', ',', '!', '?', ';', ':', '…', '-', '—', '"', '\'');
        if (vowelGroups > 1 && alphaOnly.EndsWith('e') && !alphaOnly.EndsWith("le", StringComparison.OrdinalIgnoreCase))
        {
            vowelGroups--;
        }

        return Math.Max(1, vowelGroups);
    }

    public static bool IsCjkChar(char ch) =>
        (ch >= '\u4E00' && ch <= '\u9FFF') ||
        (ch >= '\u3040' && ch <= '\u30FF') ||
        (ch >= '\uAC00' && ch <= '\uD7AF') ||
        (ch >= '\u3400' && ch <= '\u4DBF');

    public static bool IsLatinToken(string text)
    {
        foreach (var ch in text)
        {
            if (IsCjkChar(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static double[] ComputeTokenWeights(
        List<(string Text, bool SpaceSeparated)> tokens,
        out double totalWeight,
        out double totalSyllables,
        out double punctPauseSec)
    {
        var weights = new double[tokens.Count];
        totalWeight = 0.0;
        totalSyllables = 0.0;
        punctPauseSec = 0.0;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i].Text;
            var syl = EstimateSyllables(token);
            var punct = PunctuationWeight(token);
            totalSyllables += syl;
            punctPauseSec += punct * 0.20;
            weights[i] = 0.35 + (0.50 * syl) + (0.15 * Math.Log2(1 + token.Length)) + punct;
            totalWeight += weights[i];
        }

        return weights;
    }

    private static double ComputeActiveDurationMs(
        TimeSpan startTime,
        TimeSpan? nextLineStart,
        TimeSpan trackDuration,
        double totalSyllables,
        double punctPauseSec,
        int tokenCount)
    {
        var gap = nextLineStart.HasValue && nextLineStart.Value > startTime
            ? nextLineStart.Value - startTime
            : trackDuration > startTime
                ? trackDuration - startTime
                : DefaultFinalLineDuration;

        var maxNaturalSec = Math.Clamp(1.1 + (totalSyllables * 0.42) + punctPauseSec, 1.5, 9.0);
        var activeMs = Math.Min(gap.TotalMilliseconds * 0.96, maxNaturalSec * 1000.0);

        return activeMs < tokenCount * 10.0
            ? Math.Max(gap.TotalMilliseconds, tokenCount * 20.0)
            : activeMs;
    }

    private static double[] AllocateWordDurations(double[] weights, double totalWeight, double activeMs)
    {
        var durations = new double[weights.Length];
        var minRequiredMs = weights.Length * MinWordDuration.TotalMilliseconds;

        if (activeMs <= minRequiredMs)
        {
            for (var i = 0; i < weights.Length; i++)
            {
                durations[i] = Math.Max(10.0, activeMs * (weights[i] / totalWeight));
            }

            return durations;
        }

        var floorMs = MinWordDuration.TotalMilliseconds;
        var spareMs = activeMs - minRequiredMs;
        for (var i = 0; i < weights.Length; i++)
        {
            durations[i] = floorMs + (spareMs * (weights[i] / totalWeight));
        }

        return durations;
    }

    private static List<(string Text, bool SpaceSeparated)> TokenizeLine(string lineText)
    {
        var tokens = new List<(string Text, bool SpaceSeparated)>();
        var parts = lineText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        for (var p = 0; p < parts.Length; p++)
        {
            var part = parts[p];
            if (!ContainsCjk(part))
            {
                tokens.Add((part, p < parts.Length - 1));
                continue;
            }

            AppendMixedCjkTokens(part, p < parts.Length - 1, tokens);
        }

        return tokens;
    }

    private static void AppendMixedCjkTokens(string part, bool spaceAtEnd, List<(string Text, bool SpaceSeparated)> tokens)
    {
        var current = new StringBuilder();
        foreach (var ch in part)
        {
            if (IsCjkChar(ch))
            {
                if (current.Length > 0)
                {
                    tokens.Add((current.ToString(), false));
                    current.Clear();
                }

                tokens.Add((ch.ToString(), false));
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
        {
            tokens.Add((current.ToString(), spaceAtEnd));
        }
    }

    private static double PunctuationWeight(string token) =>
        token.Length == 0
            ? 0.0
            : token[^1] switch
            {
                ',' or ';' or ':' => 0.45,
                '.' or '!' or '?' or '…' or '—' => 0.80,
                _ => 0.0
            };

    private static bool ContainsCjk(string text)
    {
        foreach (var ch in text)
        {
            if (IsCjkChar(ch))
            {
                return true;
            }
        }

        return false;
    }
}
