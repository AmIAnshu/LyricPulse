using System.Collections.Immutable;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Runtime;

/// <summary>
/// Immutable O(log N)-resolved slice of active, previous, and next lyrics
/// at a given effective playback timestamp, with pre-resolved word visuals.
/// </summary>
public sealed record ActiveLyricSlice(
    int ActiveLineIndex,
    int ActiveWordIndex,
    LyricLine? PreviousLine,
    LyricLine? CurrentLine,
    LyricLine? NextLine,
    double WordProgress,
    double LineProgress,
    bool IsInInstrumentalGap,
    TimeSpan? TimeUntilNextLine)
{
    public static readonly TimeSpan InstrumentalGapThreshold = TimeSpan.FromSeconds(4.0);
    public static readonly TimeSpan PostLineLingerDuration = TimeSpan.FromSeconds(0.9);

    public static ActiveLyricSlice Empty { get; } = new(
        ActiveLineIndex: -1,
        ActiveWordIndex: -1,
        PreviousLine: null,
        CurrentLine: null,
        NextLine: null,
        WordProgress: 0.0,
        LineProgress: 0.0,
        IsInInstrumentalGap: false,
        TimeUntilNextLine: null);

    public static ActiveLyricSlice Resolve(LyricsDocument? document, TimeSpan effectivePosition)
    {
        if (document is null || document.Lines.IsEmpty)
        {
            return Empty;
        }

        if (document.TimingKind is TimingKind.None or TimingKind.PlainUnsynced)
        {
            return new ActiveLyricSlice(
                ActiveLineIndex: 0,
                ActiveWordIndex: -1,
                PreviousLine: null,
                CurrentLine: document.Lines[0],
                NextLine: document.Lines.Length > 1 ? document.Lines[1] : null,
                WordProgress: 0.0,
                LineProgress: 0.0,
                IsInInstrumentalGap: false,
                TimeUntilNextLine: null);
        }

        var lines = document.Lines;
        var pos = effectivePosition < TimeSpan.Zero ? TimeSpan.Zero : effectivePosition;

        if (pos < lines[0].StartTime)
        {
            var untilFirst = lines[0].StartTime - pos;
            return new ActiveLyricSlice(
                ActiveLineIndex: -1,
                ActiveWordIndex: -1,
                PreviousLine: null,
                CurrentLine: null,
                NextLine: lines[0],
                WordProgress: 0.0,
                LineProgress: 0.0,
                IsInInstrumentalGap: untilFirst > InstrumentalGapThreshold,
                TimeUntilNextLine: untilFirst);
        }

        var lineIdx = FindActiveLineIndex(lines, pos);
        var current = lines[lineIdx];
        var previous = lineIdx > 0 ? lines[lineIdx - 1] : null;
        var next = lineIdx + 1 < lines.Length ? lines[lineIdx + 1] : null;
        var timeUntilNext = next is not null && next.StartTime > pos
            ? next.StartTime - pos
            : (TimeSpan?)null;

        var isInstrumental = DetermineInstrumentalGap(current, next, pos);
        var lineProgress = ComputeIntervalProgress(current.StartTime, current.EndTime, pos);

        var (wordIdx, wordProgress) = ResolveActiveWord(current, pos);

        return new ActiveLyricSlice(
            ActiveLineIndex: lineIdx,
            ActiveWordIndex: wordIdx,
            PreviousLine: previous,
            CurrentLine: current,
            NextLine: next,
            WordProgress: wordProgress,
            LineProgress: lineProgress,
            IsInInstrumentalGap: isInstrumental,
            TimeUntilNextLine: timeUntilNext);
    }

    private static int FindActiveLineIndex(ImmutableArray<LyricLine> lines, TimeSpan pos)
    {
        var lo = 0;
        var hi = lines.Length - 1;
        var result = 0;

        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (lines[mid].StartTime <= pos)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return result;
    }

    private static (int WordIndex, double WordProgress) ResolveActiveWord(LyricLine line, TimeSpan pos)
    {
        var words = line.Words;
        if (line.IsInstrumentalBreak || words.IsEmpty)
        {
            return (-1, 0.0);
        }

        if (pos < words[0].StartTime)
        {
            return (0, 0.0);
        }

        var lo = 0;
        var hi = words.Length - 1;
        var idx = 0;

        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (words[mid].StartTime <= pos)
            {
                idx = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        var word = words[idx];
        var progress = ComputeIntervalProgress(word.StartTime, word.EndTime, pos);
        return (idx, progress);
    }

    private static bool DetermineInstrumentalGap(LyricLine current, LyricLine? next, TimeSpan pos)
    {
        if (current.IsInstrumentalBreak)
        {
            return true;
        }

        if (next is null)
        {
            return false;
        }

        var silenceSpan = next.StartTime - current.EndTime;
        return silenceSpan > InstrumentalGapThreshold && pos >= current.EndTime + PostLineLingerDuration;
    }

    private static double ComputeIntervalProgress(TimeSpan start, TimeSpan end, TimeSpan pos)
    {
        var spanTicks = end.Ticks - start.Ticks;
        if (spanTicks <= 0)
        {
            return pos >= start ? 1.0 : 0.0;
        }

        var elapsedTicks = pos.Ticks - start.Ticks;
        return Math.Clamp(elapsedTicks / (double)spanTicks, 0.0, 1.0);
    }
}
