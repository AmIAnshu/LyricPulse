using System.Collections.Immutable;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Visuals;

/// <summary>
/// Zero-download embedded semantic word-to-icon matcher backed by StarterLexicon.tsv,
/// FrozenSet stopword filtering, contraction stripping, and controlled lyric lemmatization.
/// </summary>
public sealed partial class StarterLexiconVisualMatcher : IWordVisualMatcher
{
    public const string ResourceName = StarterLexiconCatalog.ResourceName;

    private StarterLexiconCatalog? _catalog;
    private bool _disposed;

    public VisualMatcherMode Mode => VisualMatcherMode.StarterLexicon;
    public bool IsReady => _catalog is not null;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_catalog is not null)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _catalog = StarterLexiconCatalog.LoadEmbedded();
        return Task.CompletedTask;
    }

    public WordVisualMatch? MatchWord(ReadOnlySpan<char> word, VisualPreference preference = VisualPreference.Balanced)
    {
        if (_disposed || word.IsEmpty)
        {
            return null;
        }

        var catalog = _catalog ??= StarterLexiconCatalog.LoadEmbedded();
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

        return catalog.TryMatchExactOrStem(normalized, out var entry, out var confidence, out var isExact)
            ? StarterLexiconCatalog.CreateMatch(entry, preference, confidence, isExact)
            : null;
    }

    public ImmutableArray<WordVisualMatch?> MatchLine(
        IReadOnlyList<LyricWord> words,
        VisualPreference preference = VisualPreference.Balanced)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (_disposed || words.Count == 0)
        {
            return ImmutableArray<WordVisualMatch?>.Empty;
        }

        _catalog ??= StarterLexiconCatalog.LoadEmbedded();
        var builder = ImmutableArray.CreateBuilder<WordVisualMatch?>(words.Count);
        string? previousConcept = null;

        foreach (var word in words)
        {
            var match = MatchWord(word.Text, preference);
            if (match is not null && string.Equals(match.ConceptName, previousConcept, StringComparison.Ordinal))
            {
                builder.Add(null);
                continue;
            }

            previousConcept = match?.ConceptName;
            builder.Add(match);
        }

        return builder.ToImmutable();
    }

    public WordVisualMatch? InferTrackMoodVisual(
        TrackIdentity? track,
        LyricsDocument? document,
        VisualPreference preference = VisualPreference.Balanced)
    {
        if (_disposed)
        {
            return null;
        }

        var catalog = _catalog ??= StarterLexiconCatalog.LoadEmbedded();
        var moodConceptId = SemanticIdiomLexicon.InferTrackMoodConceptId(track, document, catalog);
        return catalog.ConceptsById.TryGetValue(moodConceptId, out var entry)
            ? StarterLexiconCatalog.CreateMatch(entry, preference, confidence: 0.60f, isExact: false)
            : null;
    }

    public LyricsDocument AnnotateDocument(LyricsDocument document, VisualPreference preference = VisualPreference.Balanced)
    {
        ArgumentNullException.ThrowIfNull(document);
        _catalog ??= StarterLexiconCatalog.LoadEmbedded();

        var updatedLines = ImmutableArray.CreateBuilder<LyricLine>(document.Lines.Length);
        foreach (var line in document.Lines)
        {
            if (line.Words.IsEmpty)
            {
                updatedLines.Add(line);
                continue;
            }

            var lineMatches = MatchLine(line.Words, preference);
            var updatedWords = ImmutableArray.CreateBuilder<LyricWord>(line.Words.Length);
            for (var i = 0; i < line.Words.Length; i++)
            {
                updatedWords.Add(line.Words[i] with { Visual = lineMatches[i] });
            }

            updatedLines.Add(line with { Words = updatedWords.ToImmutable() });
        }

        return document with { Lines = updatedLines.ToImmutable() };
    }

    public void Dispose() => _disposed = true;
}
