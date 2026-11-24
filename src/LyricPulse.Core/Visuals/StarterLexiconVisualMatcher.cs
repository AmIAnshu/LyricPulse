using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Visuals;

/// <summary>
/// Zero-download embedded semantic word-to-icon matcher backed by StarterLexicon.tsv,
/// FrozenSet stopword filtering, contraction stripping, and controlled lyric lemmatization.
/// </summary>
public sealed partial class StarterLexiconVisualMatcher : IWordVisualMatcher
{
    public const string ResourceName = "LyricPulse.Core.Resources.StarterLexicon.tsv";

    private FrozenDictionary<string, LexiconEntry>? _lexicon;
    private bool _disposed;

    public VisualMatcherMode Mode => VisualMatcherMode.StarterLexicon;
    public bool IsReady => _lexicon is not null;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_lexicon is not null)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _lexicon = LoadEmbeddedLexicon();
        return Task.CompletedTask;
    }

    public WordVisualMatch? MatchWord(ReadOnlySpan<char> word, VisualPreference preference = VisualPreference.Balanced)
    {
        if (_disposed || word.IsEmpty)
        {
            return null;
        }

        var lexicon = _lexicon ??= LoadEmbeddedLexicon();
        Span<char> buffer = stackalloc char[48];
        if (!LyricTextNormalizationData.TryNormalizeToken(word, buffer, out var length))
        {
            return null;
        }

        var normalized = buffer[..length];
        if (LyricTextNormalizationData.Stopwords.GetAlternateLookup<ReadOnlySpan<char>>().Contains(normalized))
        {
            return null;
        }

        var lookup = lexicon.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(normalized, out var directEntry))
        {
            return CreateMatch(directEntry, preference, confidence: 1.0f, isExact: true);
        }

        var tokenStr = normalized.ToString();
        if (LyricTextNormalizationData.IrregularLemmas.TryGetValue(tokenStr, out var irregularStem) &&
            lexicon.TryGetValue(irregularStem, out var irregularEntry))
        {
            return CreateMatch(irregularEntry, preference, confidence: 0.96f, isExact: true);
        }

        if (TryMatchBySuffixStem(tokenStr, lexicon, out var stemmedEntry))
        {
            return CreateMatch(stemmedEntry, preference, confidence: 0.92f, isExact: false);
        }

        return null;
    }

    public LyricsDocument AnnotateDocument(LyricsDocument document, VisualPreference preference = VisualPreference.Balanced)
    {
        ArgumentNullException.ThrowIfNull(document);
        _lexicon ??= LoadEmbeddedLexicon();

        var updatedLines = ImmutableArray.CreateBuilder<LyricLine>(document.Lines.Length);
        foreach (var line in document.Lines)
        {
            if (line.Words.IsEmpty)
            {
                updatedLines.Add(line);
                continue;
            }

            var updatedWords = ImmutableArray.CreateBuilder<LyricWord>(line.Words.Length);
            string? previousConcept = null;

            foreach (var word in line.Words)
            {
                var match = MatchWord(word.Text, preference);
                if (match is not null && string.Equals(match.ConceptName, previousConcept, StringComparison.Ordinal))
                {
                    updatedWords.Add(word with { Visual = null });
                    continue;
                }

                previousConcept = match?.ConceptName;
                updatedWords.Add(word with { Visual = match });
            }

            updatedLines.Add(line with { Words = updatedWords.ToImmutable() });
        }

        return document with { Lines = updatedLines.ToImmutable() };
    }

    public void Dispose() => _disposed = true;

    private static bool TryMatchBySuffixStem(
        string token,
        FrozenDictionary<string, LexiconEntry> lexicon,
        [NotNullWhen(true)] out LexiconEntry? entry)
    {
        entry = null;

        if (token.Length > 4 && token.EndsWith("ies", StringComparison.Ordinal) &&
            lexicon.TryGetValue(token[..^3] + "y", out entry))
        {
            return true;
        }

        if (token.Length > 4 && token.EndsWith("ves", StringComparison.Ordinal) &&
            (lexicon.TryGetValue(token[..^3] + "f", out entry) || lexicon.TryGetValue(token[..^3] + "fe", out entry)))
        {
            return true;
        }

        if (token.Length > 5 && token.EndsWith("ing", StringComparison.Ordinal) &&
            TryMatchVerbStem(token[..^3], lexicon, out entry))
        {
            return true;
        }

        if (token.Length > 4 && token.EndsWith("ed", StringComparison.Ordinal) &&
            TryMatchVerbStem(token[..^2], lexicon, out entry))
        {
            return true;
        }

        if (token.Length > 3 && token.EndsWith("es", StringComparison.Ordinal) &&
            lexicon.TryGetValue(token[..^2], out entry))
        {
            return true;
        }

        return token.Length > 3 && token.EndsWith('s') && !token.EndsWith("ss", StringComparison.Ordinal) &&
               lexicon.TryGetValue(token[..^1], out entry);
    }

    private static bool TryMatchVerbStem(
        string baseStem,
        FrozenDictionary<string, LexiconEntry> lexicon,
        [NotNullWhen(true)] out LexiconEntry? entry) =>
        lexicon.TryGetValue(baseStem, out entry) ||
        lexicon.TryGetValue(baseStem + "e", out entry) ||
        (baseStem.Length >= 3 && baseStem[^1] == baseStem[^2] && lexicon.TryGetValue(baseStem[..^1], out entry));

    private static WordVisualMatch CreateMatch(
        LexiconEntry entry,
        VisualPreference preference,
        float confidence,
        bool isExact) =>
        preference == VisualPreference.EmojiOnly
            ? new WordVisualMatch(VisualKind.Emoji, entry.Emoji, entry.IconName, entry.ConceptId, confidence, isExact, entry.Emoji)
            : new WordVisualMatch(VisualKind.FluentIcon, entry.GlyphChar, entry.IconName, entry.ConceptId, confidence, isExact, entry.Emoji);

    private static FrozenDictionary<string, LexiconEntry> LoadEmbeddedLexicon()
    {
        var assembly = typeof(StarterLexiconVisualMatcher).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded starter lexicon '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);

        var map = new Dictionary<string, LexiconEntry>(1200, StringComparer.Ordinal);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 5)
            {
                continue;
            }

            var conceptId = parts[0].Trim();
            var iconName = parts[1].Trim();
            var emoji = parts[3].Trim();
            var glyphChar = int.TryParse(parts[2].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp)
                ? char.ConvertFromUtf32(cp)
                : emoji;

            var entry = new LexiconEntry(conceptId, iconName, glyphChar, emoji);
            map.TryAdd(conceptId.ToLowerInvariant(), entry);

            foreach (var syn in parts[4].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var key = syn.ToLowerInvariant();
                if (key.Length >= 2)
                {
                    map.TryAdd(key, entry);
                }
            }
        }

        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private sealed record LexiconEntry(string ConceptId, string IconName, string GlyphChar, string Emoji);
}
