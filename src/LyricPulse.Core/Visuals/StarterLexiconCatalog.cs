using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Visuals;

/// <summary>
/// Immutable concept record loaded from the TTF-verified <c>StarterLexicon.tsv</c> embedded resource.
/// </summary>
public sealed record StarterLexiconEntry(
    string ConceptId,
    string IconName,
    string GlyphChar,
    string Emoji,
    ImmutableArray<string> Synonyms);

/// <summary>
/// Shared immutable catalog over <c>StarterLexicon.tsv</c> used by both <see cref="StarterLexiconVisualMatcher"/>
/// and <see cref="GloveSemanticVisualMatcher"/> so every emitted icon is guaranteed TTF-verified.
/// </summary>
public sealed class StarterLexiconCatalog
{
    public const string ResourceName = "LyricPulse.Core.Resources.StarterLexicon.tsv";

    private static readonly Lazy<StarterLexiconCatalog> Cached = new(LoadEmbeddedInternal);

    public StarterLexiconCatalog(
        ImmutableArray<StarterLexiconEntry> concepts,
        FrozenDictionary<string, StarterLexiconEntry> conceptsById,
        FrozenDictionary<string, StarterLexiconEntry> wordToConcept)
    {
        Concepts = concepts;
        ConceptsById = conceptsById;
        WordToConcept = wordToConcept;
    }

    public ImmutableArray<StarterLexiconEntry> Concepts { get; }
    public FrozenDictionary<string, StarterLexiconEntry> ConceptsById { get; }
    public FrozenDictionary<string, StarterLexiconEntry> WordToConcept { get; }

    public static StarterLexiconCatalog LoadEmbedded() => Cached.Value;

    public static WordVisualMatch CreateMatch(
        StarterLexiconEntry entry,
        VisualPreference preference,
        float confidence,
        bool isExact) =>
        preference == VisualPreference.EmojiOnly
            ? new WordVisualMatch(VisualKind.Emoji, entry.Emoji, entry.IconName, entry.ConceptId, confidence, isExact, entry.Emoji)
            : new WordVisualMatch(VisualKind.FluentIcon, entry.GlyphChar, entry.IconName, entry.ConceptId, confidence, isExact, entry.Emoji);

    public bool TryMatchExactOrStem(
        ReadOnlySpan<char> normalized,
        [NotNullWhen(true)] out StarterLexiconEntry? entry,
        out float confidence,
        out bool isExact)
    {
        var lookup = WordToConcept.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(normalized, out entry))
        {
            confidence = 1.0f;
            isExact = true;
            return true;
        }

        var tokenStr = normalized.ToString();
        if (LyricTextNormalizationData.IrregularLemmas.TryGetValue(tokenStr, out var irregularStem) &&
            WordToConcept.TryGetValue(irregularStem, out entry))
        {
            confidence = 0.96f;
            isExact = true;
            return true;
        }

        if (TryMatchBySuffixStem(tokenStr, WordToConcept, out entry))
        {
            confidence = 0.92f;
            isExact = false;
            return true;
        }

        entry = null;
        confidence = 0f;
        isExact = false;
        return false;
    }

    private static bool TryMatchBySuffixStem(
        string token,
        FrozenDictionary<string, StarterLexiconEntry> lexicon,
        [NotNullWhen(true)] out StarterLexiconEntry? entry)
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
        FrozenDictionary<string, StarterLexiconEntry> lexicon,
        [NotNullWhen(true)] out StarterLexiconEntry? entry) =>
        lexicon.TryGetValue(baseStem, out entry) ||
        lexicon.TryGetValue(baseStem + "e", out entry) ||
        (baseStem.Length >= 3 && baseStem[^1] == baseStem[^2] && lexicon.TryGetValue(baseStem[..^1], out entry));

    private static StarterLexiconCatalog LoadEmbeddedInternal()
    {
        var assembly = typeof(StarterLexiconCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded starter lexicon '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);

        var concepts = ImmutableArray.CreateBuilder<StarterLexiconEntry>(200);
        var byId = new Dictionary<string, StarterLexiconEntry>(200, StringComparer.Ordinal);
        var wordMap = new Dictionary<string, StarterLexiconEntry>(1400, StringComparer.Ordinal);
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

            var conceptId = parts[0].Trim().ToLowerInvariant();
            var iconName = parts[1].Trim();
            var emoji = parts[3].Trim();
            var glyphChar = int.TryParse(parts[2].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp)
                ? char.ConvertFromUtf32(cp)
                : emoji;

            var synList = parts[4]
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static s => s.ToLowerInvariant())
                .Where(static s => s.Length >= 2)
                .Distinct(StringComparer.Ordinal)
                .ToImmutableArray();

            var entry = new StarterLexiconEntry(conceptId, iconName, glyphChar, emoji, synList);
            concepts.Add(entry);
            byId.TryAdd(conceptId, entry);
            wordMap.TryAdd(conceptId, entry);

            foreach (var key in synList)
            {
                wordMap.TryAdd(key, entry);
            }
        }

        return new StarterLexiconCatalog(
            concepts.ToImmutable(),
            byId.ToFrozenDictionary(StringComparer.Ordinal),
            wordMap.ToFrozenDictionary(StringComparer.Ordinal));
    }
}
