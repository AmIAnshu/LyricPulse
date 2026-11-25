using System.Collections.Immutable;

namespace LyricPulse.Core.Models;

public enum LyricsSourceKind
{
    None = 0,
    LocalSidecar = 1,
    DiskCache = 2,
    LrcLib = 3,
    NetEase = 4,
    Demo = 5
}

public enum TimingKind
{
    None = 0,
    PlainUnsynced = 1,
    LineSynced = 2,
    WordInterpolated = 3,
    WordNative = 4
}

public enum LyricsStatus
{
    NotFound = 0,
    Instrumental = 1,
    PlainOnly = 2,
    Synced = 3,
    Error = 4,
    Loading = 5
}

public enum VisualKind
{
    FluentIcon = 0,
    Emoji = 1
}

public enum VisualMatcherMode
{
    StarterLexicon = 0,
    FullSemanticGloVe = 1
}

public enum VisualPreference
{
    Balanced = 0,
    FluentIconsOnly = 1,
    EmojiOnly = 2
}

public sealed record WordVisualMatch(
    VisualKind Kind,
    string Value,
    string IconName,
    string ConceptName,
    float Confidence,
    bool IsExactMatch,
    string? EmojiFallback = null);

public sealed record LyricWord(
    string Text,
    TimeSpan StartTime,
    TimeSpan EndTime,
    bool IsSpaceSeparated,
    WordVisualMatch? Visual = null);

public sealed record LyricLine(
    int Index,
    string RawText,
    TimeSpan StartTime,
    TimeSpan EndTime,
    ImmutableArray<LyricWord> Words,
    bool IsInstrumentalBreak = false,
    string? TranslationText = null,
    string? RomanizedText = null);

public sealed record LyricsDocument(
    TrackIdentity Track,
    LyricsSourceKind SourceKind,
    string ProviderName,
    TimingKind TimingKind,
    ImmutableArray<LyricLine> Lines,
    TimeSpan EmbeddedLrcOffset,
    bool IsFromCache);

public sealed record LyricsResult(
    LyricsStatus Status,
    LyricsDocument? Document,
    string? ErrorMessage = null);

public sealed record TimingOffsetState(
    string? TrackCacheKey,
    TimeSpan Offset,
    bool IsPersistedOverride)
{
    public static readonly TimeSpan MinOffset = TimeSpan.FromSeconds(-15);
    public static readonly TimeSpan MaxOffset = TimeSpan.FromSeconds(15);

    public TimingOffsetState WithNudge(TimeSpan delta)
    {
        var clampedTicks = Math.Clamp((Offset + delta).Ticks, MinOffset.Ticks, MaxOffset.Ticks);
        return this with { Offset = TimeSpan.FromTicks(clampedTicks) };
    }

    public TimingOffsetState WithReset() => this with { Offset = TimeSpan.Zero };
}
