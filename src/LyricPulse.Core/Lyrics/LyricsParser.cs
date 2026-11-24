using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Lyrics;

/// <summary>
/// Multi-format lyrics parser supporting Standard LRC, Enhanced word-tagged LRC,
/// NetEase YRC word-level karaoke payloads, and timestamp-aligned bilingual tlyric/romalrc merging.
/// </summary>
public sealed partial class LyricsParser : ILyricsParser
{
    private const int RegexTimeoutMs = 100;
    private static readonly TimeSpan BilingualMaxDelta = TimeSpan.FromMilliseconds(300);

    public LyricsDocument ParseLrc(
        TrackIdentity track,
        string rawLrc,
        LyricsSourceKind sourceKind,
        string providerName,
        bool isFromCache,
        string? translationLrc = null,
        string? romanizedLrc = null)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (string.IsNullOrWhiteSpace(rawLrc))
        {
            return new LyricsDocument(track, sourceKind, providerName, TimingKind.None, ImmutableArray<LyricLine>.Empty, TimeSpan.Zero, isFromCache);
        }

        var embeddedOffset = ExtractLrcOffset(rawLrc);
        var entries = ExtractRawLrcEntries(rawLrc, embeddedOffset);
        if (entries.Count == 0)
        {
            return CreatePlain(track, rawLrc, sourceKind, providerName, isFromCache);
        }

        entries.Sort(static (a, b) =>
        {
            var cmp = a.StartTime.CompareTo(b.StartTime);
            return cmp != 0 ? cmp : a.Order.CompareTo(b.Order);
        });

        var hasAnyEnhanced = entries.Any(e => EnhancedWordTagRegex().IsMatch(e.RawPayload));
        var translations = ParseSimpleLrcTimestamps(translationLrc);
        var romanizations = ParseSimpleLrcTimestamps(romanizedLrc);

        var linesBuilder = ImmutableArray.CreateBuilder<LyricLine>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var nextStart = i + 1 < entries.Count ? entries[i + 1].StartTime : (TimeSpan?)null;
            linesBuilder.Add(BuildLrcLine(i, entry.StartTime, entry.RawPayload, embeddedOffset, nextStart, track.Duration, translations, romanizations));
        }

        var timingKind = hasAnyEnhanced ? TimingKind.WordNative : TimingKind.WordInterpolated;
        return new LyricsDocument(track, sourceKind, providerName, timingKind, linesBuilder.ToImmutable(), embeddedOffset, isFromCache);
    }

    public LyricsDocument ParseYrc(
        TrackIdentity track,
        string rawYrc,
        LyricsSourceKind sourceKind,
        string providerName,
        bool isFromCache,
        string? translationLrc = null,
        string? romanizedLrc = null)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (string.IsNullOrWhiteSpace(rawYrc))
        {
            return new LyricsDocument(track, sourceKind, providerName, TimingKind.None, ImmutableArray<LyricLine>.Empty, TimeSpan.Zero, isFromCache);
        }

        var translations = ParseSimpleLrcTimestamps(translationLrc);
        var romanizations = ParseSimpleLrcTimestamps(romanizedLrc);
        var parsedLines = ParseYrcLines(rawYrc, translations, romanizations);

        if (parsedLines.Count == 0)
        {
            return ParseLrc(track, rawYrc, sourceKind, providerName, isFromCache, translationLrc, romanizedLrc);
        }

        parsedLines.Sort(static (a, b) => a.StartTime.CompareTo(b.StartTime));
        var reindexed = parsedLines.Select((line, idx) => line with { Index = idx }).ToImmutableArray();
        return new LyricsDocument(track, sourceKind, providerName, TimingKind.WordNative, reindexed, TimeSpan.Zero, isFromCache);
    }

    public LyricsDocument CreatePlain(
        TrackIdentity track,
        string plainText,
        LyricsSourceKind sourceKind,
        string providerName,
        bool isFromCache)
    {
        ArgumentNullException.ThrowIfNull(track);

        var lines = SplitLines(plainText ?? string.Empty)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select((text, index) => new LyricLine(
                Index: index,
                RawText: text,
                StartTime: TimeSpan.Zero,
                EndTime: TimeSpan.Zero,
                Words: ImmutableArray<LyricWord>.Empty,
                IsInstrumentalBreak: IsInstrumentalText(text)))
            .ToImmutableArray();

        return new LyricsDocument(
            track,
            sourceKind,
            providerName,
            lines.IsEmpty ? TimingKind.None : TimingKind.PlainUnsynced,
            lines,
            TimeSpan.Zero,
            isFromCache);
    }

    private static List<LyricLine> ParseYrcLines(
        string rawYrc,
        List<(TimeSpan Time, string Text)> translations,
        List<(TimeSpan Time, string Text)> romanizations)
    {
        var parsedLines = new List<LyricLine>();

        foreach (var rawLine in SplitLines(rawYrc))
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('{'))
            {
                continue;
            }

            var lineMatch = YrcLineRegex().Match(trimmed);
            if (!lineMatch.Success)
            {
                continue;
            }

            var lineStart = TimeSpan.FromMilliseconds(Math.Max(0L, long.Parse(lineMatch.Groups[1].ValueSpan, CultureInfo.InvariantCulture)));
            var lineEnd = lineStart + TimeSpan.FromMilliseconds(Math.Max(0L, long.Parse(lineMatch.Groups[2].ValueSpan, CultureInfo.InvariantCulture)));
            var words = ParseYrcWords(lineMatch.Groups[3].Value, lineStart, ref lineEnd);
            var rawText = ReconstructLineText(words);
            var isInstrumental = IsInstrumentalText(rawText) || words.IsEmpty;

            parsedLines.Add(new LyricLine(
                Index: parsedLines.Count,
                RawText: rawText,
                StartTime: lineStart,
                EndTime: lineEnd < lineStart ? lineStart : lineEnd,
                Words: isInstrumental ? ImmutableArray<LyricWord>.Empty : words,
                IsInstrumentalBreak: isInstrumental,
                TranslationText: FindNearestBilingual(translations, lineStart),
                RomanizedText: FindNearestBilingual(romanizations, lineStart)));
        }

        return parsedLines;
    }

    private static LyricLine BuildLrcLine(
        int index,
        TimeSpan startTime,
        string rawPayload,
        TimeSpan embeddedOffset,
        TimeSpan? nextLineStart,
        TimeSpan trackDuration,
        List<(TimeSpan Time, string Text)> translations,
        List<(TimeSpan Time, string Text)> romanizations)
    {
        var translation = FindNearestBilingual(translations, startTime);
        var romanized = FindNearestBilingual(romanizations, startTime);
        var wordMatches = EnhancedWordTagRegex().Matches(rawPayload);

        if (wordMatches.Count > 0)
        {
            return BuildEnhancedWordLine(index, startTime, wordMatches, embeddedOffset, nextLineStart, trackDuration, translation, romanized);
        }

        var cleanText = rawPayload.Trim();
        if (IsInstrumentalText(cleanText))
        {
            var end = nextLineStart.HasValue && nextLineStart.Value > startTime ? nextLineStart.Value : startTime;
            return new LyricLine(index, cleanText, startTime, end, ImmutableArray<LyricWord>.Empty, IsInstrumentalBreak: true, translation, romanized);
        }

        var words = WordTimingInterpolator.SynthesizeWords(cleanText, startTime, nextLineStart, trackDuration, out var lineEnd);
        return new LyricLine(index, cleanText, startTime, lineEnd, words, IsInstrumentalBreak: false, translation, romanized);
    }

    private static LyricLine BuildEnhancedWordLine(
        int index,
        TimeSpan lineStart,
        MatchCollection matches,
        TimeSpan embeddedOffset,
        TimeSpan? nextLineStart,
        TimeSpan trackDuration,
        string? translation,
        string? romanized)
    {
        var rawTokens = ExtractEnhancedTokens(matches, embeddedOffset);
        if (rawTokens.Count == 0)
        {
            return new LyricLine(index, string.Empty, lineStart, lineStart, ImmutableArray<LyricWord>.Empty, IsInstrumentalBreak: true, translation, romanized);
        }

        var words = ImmutableArray.CreateBuilder<LyricWord>(rawTokens.Count);
        var cursor = lineStart;

        for (var i = 0; i < rawTokens.Count; i++)
        {
            var token = rawTokens[i];
            var wordStart = token.Start < cursor ? cursor : token.Start;
            var wordEnd = ComputeEnhancedWordEnd(token.Text, wordStart, i + 1 < rawTokens.Count ? rawTokens[i + 1].Start : null, nextLineStart, trackDuration);

            words.Add(new LyricWord(token.Text, wordStart, wordEnd, token.SpaceAfter || i < rawTokens.Count - 1));
            cursor = wordEnd;
        }

        var builtWords = words.ToImmutable();
        return new LyricLine(index, ReconstructLineText(builtWords), lineStart, builtWords[^1].EndTime, builtWords, IsInstrumentalBreak: false, translation, romanized);
    }

    private static List<(TimeSpan Start, string Text, bool SpaceAfter)> ExtractEnhancedTokens(MatchCollection matches, TimeSpan embeddedOffset)
    {
        var tokens = new List<(TimeSpan Start, string Text, bool SpaceAfter)>(matches.Count);
        foreach (Match match in matches)
        {
            var ts = ApplyEmbeddedOffset(ParseTimestampGroups(match.Groups[1], match.Groups[2], match.Groups[3]), embeddedOffset);
            var chunk = match.Groups[4].Value;
            var text = chunk.Trim();
            if (text.Length > 0)
            {
                tokens.Add((ts, text, chunk.Length > 0 && char.IsWhiteSpace(chunk[^1])));
            }
        }

        return tokens;
    }

    private static TimeSpan ComputeEnhancedWordEnd(
        string text,
        TimeSpan wordStart,
        TimeSpan? nextWordStart,
        TimeSpan? nextLineStart,
        TimeSpan trackDuration)
    {
        if (nextWordStart.HasValue)
        {
            return nextWordStart.Value > wordStart ? nextWordStart.Value : wordStart + WordTimingInterpolator.MinWordDuration;
        }

        var estimatedSec = Math.Clamp((0.35 * WordTimingInterpolator.EstimateSyllables(text)) + 0.60, 0.60, 4.0);
        var naturalEnd = wordStart + TimeSpan.FromSeconds(Math.Min(1.8, estimatedSec));
        var wordEnd = nextLineStart.HasValue && nextLineStart.Value > wordStart
            ? (nextLineStart.Value < naturalEnd ? nextLineStart.Value : naturalEnd)
            : trackDuration > wordStart
                ? (trackDuration < naturalEnd ? trackDuration : naturalEnd)
                : naturalEnd;

        return wordEnd > wordStart ? wordEnd : wordStart + WordTimingInterpolator.MinWordDuration;
    }

    private static ImmutableArray<LyricWord> ParseYrcWords(string body, TimeSpan lineStart, ref TimeSpan lineEnd)
    {
        var matches = YrcWordRegex().Matches(body);
        if (matches.Count == 0)
        {
            return ImmutableArray<LyricWord>.Empty;
        }

        var merged = MergeYrcWordTokens(matches);
        var result = ImmutableArray.CreateBuilder<LyricWord>(merged.Count);
        var cursor = lineStart;

        foreach (var item in merged)
        {
            var start = item.Start < cursor ? cursor : item.Start;
            var end = item.End <= start ? start + WordTimingInterpolator.MinWordDuration : item.End;
            result.Add(new LyricWord(item.Text, start, end, item.SpaceSeparated));
            cursor = end;
        }

        if (result.Count > 0 && result[^1].EndTime > lineEnd)
        {
            lineEnd = result[^1].EndTime;
        }

        return result.ToImmutable();
    }

    private static List<(string Text, TimeSpan Start, TimeSpan End, bool SpaceSeparated)> MergeYrcWordTokens(MatchCollection matches)
    {
        var merged = new List<(string Text, TimeSpan Start, TimeSpan End, bool SpaceSeparated)>();

        foreach (Match match in matches)
        {
            var rawChunk = match.Groups[3].Value;
            if (rawChunk.Length == 0)
            {
                continue;
            }

            var trimmed = rawChunk.Trim();
            if (trimmed.Length == 0)
            {
                if (merged.Count > 0)
                {
                    var prev = merged[^1];
                    merged[^1] = (prev.Text, prev.Start, prev.End, true);
                }

                continue;
            }

            var start = TimeSpan.FromMilliseconds(Math.Max(0L, long.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture)));
            var end = start + TimeSpan.FromMilliseconds(Math.Max(1L, long.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture)));
            var hasLeadingSpace = char.IsWhiteSpace(rawChunk[0]);
            var hasTrailingSpace = char.IsWhiteSpace(rawChunk[^1]);

            if (merged.Count > 0 &&
                !hasLeadingSpace &&
                !merged[^1].SpaceSeparated &&
                WordTimingInterpolator.IsLatinToken(merged[^1].Text) &&
                WordTimingInterpolator.IsLatinToken(trimmed))
            {
                var prev = merged[^1];
                merged[^1] = (prev.Text + trimmed, prev.Start, end > prev.End ? end : prev.End, hasTrailingSpace);
            }
            else
            {
                merged.Add((trimmed, start, end, hasTrailingSpace));
            }
        }

        return merged;
    }

    private static TimeSpan ExtractLrcOffset(string rawLrc)
    {
        var match = LrcOffsetTagRegex().Match(rawLrc);
        return match.Success && int.TryParse(match.Groups[1].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
            ? TimeSpan.FromMilliseconds(ms)
            : TimeSpan.Zero;
    }

    private static List<(TimeSpan StartTime, int Order, string RawPayload)> ExtractRawLrcEntries(string rawLrc, TimeSpan embeddedOffset)
    {
        var entries = new List<(TimeSpan StartTime, int Order, string RawPayload)>();
        var order = 0;

        foreach (var line in SplitLines(rawLrc))
        {
            var trimmed = line.Trim();
            var tagMatches = LrcLineTimestampRegex().Matches(trimmed);
            if (tagMatches.Count == 0)
            {
                continue;
            }

            var lastTag = tagMatches[^1];
            var payload = trimmed[(lastTag.Index + lastTag.Length)..].Trim();

            foreach (Match match in tagMatches)
            {
                var start = ApplyEmbeddedOffset(ParseTimestampGroups(match.Groups[1], match.Groups[2], match.Groups[3]), embeddedOffset);
                entries.Add((start, order++, payload));
            }
        }

        return entries;
    }

    private static List<(TimeSpan Time, string Text)> ParseSimpleLrcTimestamps(string? rawLrc)
    {
        var list = new List<(TimeSpan Time, string Text)>();
        if (string.IsNullOrWhiteSpace(rawLrc))
        {
            return list;
        }

        foreach (var line in SplitLines(rawLrc))
        {
            var trimmed = line.Trim();
            var matches = LrcLineTimestampRegex().Matches(trimmed);
            if (matches.Count == 0)
            {
                continue;
            }

            var last = matches[^1];
            var text = trimmed[(last.Index + last.Length)..].Trim();
            if (text.Length == 0)
            {
                continue;
            }

            foreach (Match m in matches)
            {
                list.Add((ParseTimestampGroups(m.Groups[1], m.Groups[2], m.Groups[3]), text));
            }
        }

        return list;
    }

    private static string? FindNearestBilingual(List<(TimeSpan Time, string Text)> items, TimeSpan target)
    {
        string? best = null;
        var bestDelta = BilingualMaxDelta + TimeSpan.FromTicks(1);

        foreach (var (time, text) in items)
        {
            var delta = (time - target).Duration();
            if (delta <= BilingualMaxDelta && delta < bestDelta)
            {
                bestDelta = delta;
                best = text;
            }
        }

        return best;
    }

    private static TimeSpan ParseTimestampGroups(Group minGroup, Group secGroup, Group fracGroup)
    {
        var minutes = int.Parse(minGroup.ValueSpan, CultureInfo.InvariantCulture);
        var seconds = int.Parse(secGroup.ValueSpan, CultureInfo.InvariantCulture);
        var ms = 0;

        if (fracGroup.Success && fracGroup.Length > 0)
        {
            var raw = int.Parse(fracGroup.ValueSpan, CultureInfo.InvariantCulture);
            ms = fracGroup.Length switch { 1 => raw * 100, 2 => raw * 10, _ => raw };
        }

        return new TimeSpan(0, 0, minutes, seconds, ms);
    }

    private static TimeSpan ApplyEmbeddedOffset(TimeSpan raw, TimeSpan embeddedOffset)
    {
        var adjusted = raw - embeddedOffset;
        return adjusted < TimeSpan.Zero ? TimeSpan.Zero : adjusted;
    }

    private static string ReconstructLineText(ImmutableArray<LyricWord> words)
    {
        if (words.IsEmpty)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < words.Length; i++)
        {
            sb.Append(words[i].Text);
            if (i + 1 < words.Length && words[i].IsSpaceSeparated)
            {
                sb.Append(' ');
            }
        }

        return sb.ToString();
    }

    private static bool IsInstrumentalText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return text.Trim() is "♪" or "♫" or "🎵" or "..." or "…" or "~" or "-";
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    [GeneratedRegex(@"\[offset\s*:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex LrcOffsetTagRegex();

    [GeneratedRegex(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex LrcLineTimestampRegex();

    [GeneratedRegex(@"<(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?>([^<]*)", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex EnhancedWordTagRegex();

    [GeneratedRegex(@"^\[(\d+),(\d+)\](.*)$", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex YrcLineRegex();

    [GeneratedRegex(@"\((\d+),(\d+),\d+\)([^(]*)", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex YrcWordRegex();
}
