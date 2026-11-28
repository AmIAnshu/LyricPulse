using System.Buffers;
using System.Collections.Immutable;
using System.Numerics.Tensors;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricPulse.Core.Visuals;

/// <summary>
/// Four-layer offline semantic word-to-visual matcher:
/// Layer 1: Poetic bigram idioms (pre-stopword, TTF-verified Fluent Icons).
/// Layer 2: Exact, irregular-lemma, and suffix-stemmed <c>StarterLexicon.tsv</c> lookup.
/// Layer 3: AVX2/AVX-512 SIMD <see cref="TensorPrimitives.Dot"/> search over optional <c>LPQV</c> GloVe 100d-q8 vectors.
/// Layer 4: Song-mood fallback visual inference for mood-driven kinetic styles.
/// Gracefully runs in <see cref="VisualMatcherMode.StarterLexicon"/> mode when the optional GloVe binary is not installed.
/// </summary>
public sealed partial class GloveSemanticVisualMatcher : IWordVisualMatcher
{
    public const float DefaultSimilarityThreshold = 0.56f;
    public const string DefaultBinaryFileName = "glove-100d-q8.bin";
    public const string DefaultVocabFileName = "glove-vocab.txt";

    private readonly string? _explicitBinaryPath;
    private readonly string? _explicitVocabPath;
    private readonly float _similarityThreshold;
    private readonly ILogger<GloveSemanticVisualMatcher> _logger;
    private readonly Lock _initLock = new();

    private StarterLexiconCatalog? _catalog;
    private QuantizedEmbeddingTable? _embeddingTable;
    private ConceptCentroidIndex? _conceptIndex;
    private volatile bool _initialized;
    private volatile bool _disposed;

    public GloveSemanticVisualMatcher(
        string? binaryPath = null,
        string? vocabPath = null,
        float similarityThreshold = DefaultSimilarityThreshold,
        ILogger<GloveSemanticVisualMatcher>? logger = null)
    {
        _explicitBinaryPath = binaryPath;
        _explicitVocabPath = vocabPath;
        _similarityThreshold = Math.Clamp(similarityThreshold, 0.25f, 0.99f);
        _logger = logger ?? NullLogger<GloveSemanticVisualMatcher>.Instance;
    }

    public VisualMatcherMode Mode =>
        _conceptIndex is { ConceptCount: > 0 }
            ? VisualMatcherMode.FullSemanticGloVe
            : VisualMatcherMode.StarterLexicon;

    public bool IsReady => _initialized && !_disposed;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        return Task.CompletedTask;
    }

    public WordVisualMatch? MatchWord(ReadOnlySpan<char> word, VisualPreference preference = VisualPreference.Balanced)
    {
        if (_disposed || word.IsEmpty)
        {
            return null;
        }

        EnsureInitialized();
        var catalog = _catalog!;
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

        // Layer 2: Exact / irregular-lemma / suffix-stemmed starter lexicon lookup
        if (catalog.TryMatchExactOrStem(normalized, out var entry, out var confidence, out var isExact))
        {
            return StarterLexiconCatalog.CreateMatch(entry, preference, confidence, isExact);
        }

        // Layer 3: Optional quantized GloVe 100d-q8 SIMD cosine search
        return TryMatchByGloVe(normalized, preference);
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

        EnsureInitialized();
        var catalog = _catalog!;
        var results = new WordVisualMatch?[words.Count];
        string? previousConcept = null;

        Span<char> firstBuf = stackalloc char[48];
        Span<char> secondBuf = stackalloc char[48];

        var i = 0;
        while (i < words.Count)
        {
            // Layer 1: Check poetic bigram idiom BEFORE stopword filtering (e.g. "on fire", "paper plane")
            if (i + 1 < words.Count &&
                LyricTextNormalizationData.TryNormalizeToken(words[i].Text, firstBuf, out var len1) &&
                LyricTextNormalizationData.TryNormalizeToken(words[i + 1].Text, secondBuf, out var len2) &&
                SemanticIdiomLexicon.TryMatchBigram(firstBuf[..len1], secondBuf[..len2], out var bigramConceptId) &&
                catalog.ConceptsById.TryGetValue(bigramConceptId, out var bigramEntry))
            {
                results[i] = null;
                if (!string.Equals(bigramEntry.ConceptId, previousConcept, StringComparison.Ordinal))
                {
                    results[i + 1] = StarterLexiconCatalog.CreateMatch(bigramEntry, preference, confidence: 1.0f, isExact: true);
                    previousConcept = bigramEntry.ConceptId;
                }

                i += 2;
                continue;
            }

            // Layers 2 & 3: Single word exact/stem/GloVe match with adjacent-concept dedupe
            var match = MatchWord(words[i].Text, preference);
            if (match is not null && string.Equals(match.ConceptName, previousConcept, StringComparison.Ordinal))
            {
                results[i] = null;
            }
            else
            {
                results[i] = match;
                previousConcept = match?.ConceptName;
            }

            i++;
        }

        return [.. results];
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

        EnsureInitialized();
        var catalog = _catalog!;
        var moodConceptId = SemanticIdiomLexicon.InferTrackMoodConceptId(track, document, catalog);
        if (!catalog.ConceptsById.TryGetValue(moodConceptId, out var entry))
        {
            return null;
        }

        var confidence = string.Equals(moodConceptId, SemanticIdiomLexicon.DefaultMoodConceptId, StringComparison.Ordinal)
            ? 0.50f
            : 0.65f;
        return StarterLexiconCatalog.CreateMatch(entry, preference, confidence, isExact: false);
    }

    public LyricsDocument AnnotateDocument(LyricsDocument document, VisualPreference preference = VisualPreference.Balanced)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureInitialized();

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

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_initLock)
        {
            if (_initialized)
            {
                return;
            }

            var catalog = StarterLexiconCatalog.LoadEmbedded();
            _catalog = catalog;

            if (TryResolveEmbeddingPaths(out var binPath, out var vocabPath))
            {
                try
                {
                    var table = QuantizedEmbeddingTable.LoadFromFiles(binPath, vocabPath);
                    _embeddingTable = table;
                    _conceptIndex = ConceptCentroidIndex.Build(table, catalog);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    _logger.LogGloveEmbeddingLoadFailed(ex, Path.GetFileName(binPath));
                    _embeddingTable = null;
                    _conceptIndex = null;
                }
            }

            _initialized = true;
        }
    }

    private bool TryResolveEmbeddingPaths(out string binaryPath, out string vocabPath)
    {
        if (!string.IsNullOrWhiteSpace(_explicitBinaryPath))
        {
            binaryPath = _explicitBinaryPath;
            vocabPath = !string.IsNullOrWhiteSpace(_explicitVocabPath)
                ? _explicitVocabPath
                : Path.Combine(Path.GetDirectoryName(binaryPath) ?? string.Empty, DefaultVocabFileName);
            return File.Exists(binaryPath) && File.Exists(vocabPath);
        }

        var localAppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LyricPulse",
            "visuals");
        if (TryProbeDirectory(localAppDataDir, out binaryPath, out vocabPath))
        {
            return true;
        }

        var appLocalDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Visuals");
        return TryProbeDirectory(appLocalDir, out binaryPath, out vocabPath);
    }

    private static bool TryProbeDirectory(string directory, out string binaryPath, out string vocabPath)
    {
        binaryPath = Path.Combine(directory, DefaultBinaryFileName);
        vocabPath = Path.Combine(directory, DefaultVocabFileName);
        return File.Exists(binaryPath) && File.Exists(vocabPath);
    }

    private WordVisualMatch? TryMatchByGloVe(ReadOnlySpan<char> normalized, VisualPreference preference)
    {
        var table = _embeddingTable;
        var index = _conceptIndex;
        if (table is null || index is null || index.ConceptCount == 0)
        {
            return null;
        }

        var dim = table.Dimensions;
        if (dim <= 256)
        {
            Span<float> query = stackalloc float[dim];
            if (!table.TryGetVector(normalized, query))
            {
                return null;
            }

            return SearchCentroids(index, query, preference);
        }

        var rented = ArrayPool<float>.Shared.Rent(dim);
        try
        {
            var query = rented.AsSpan(0, dim);
            if (!table.TryGetVector(normalized, query))
            {
                return null;
            }

            return SearchCentroids(index, query, preference);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }
    }

    private WordVisualMatch? SearchCentroids(ConceptCentroidIndex index, ReadOnlySpan<float> query, VisualPreference preference)
    {
        var bestIdx = -1;
        var bestScore = _similarityThreshold;
        var dim = index.Dimensions;
        var vectors = index.Vectors;

        for (var c = 0; c < index.ConceptCount; c++)
        {
            var score = TensorPrimitives.Dot(query, vectors.AsSpan(c * dim, dim));
            if ((bestIdx < 0 && score >= _similarityThreshold) || score > bestScore)
            {
                bestScore = score;
                bestIdx = c;
            }
        }

        if (bestIdx < 0)
        {
            return null;
        }

        var confidence = Math.Clamp(bestScore, _similarityThreshold, 0.99f);
        return StarterLexiconCatalog.CreateMatch(index.Concepts[bestIdx], preference, confidence, isExact: false);
    }

    private sealed class ConceptCentroidIndex
    {
        private ConceptCentroidIndex(int dimensions, StarterLexiconEntry[] concepts, float[] vectors)
        {
            Dimensions = dimensions;
            Concepts = concepts;
            Vectors = vectors;
        }

        public int Dimensions { get; }
        public int ConceptCount => Concepts.Length;
        public StarterLexiconEntry[] Concepts { get; }
        public float[] Vectors { get; }

        public static ConceptCentroidIndex Build(QuantizedEmbeddingTable table, StarterLexiconCatalog catalog)
        {
            var dim = table.Dimensions;
            var matchedConcepts = new List<StarterLexiconEntry>(catalog.Concepts.Length);
            var centroidValues = new List<float>(catalog.Concepts.Length * dim);
            var accumulator = new float[dim];
            var scratch = new float[dim];

            foreach (var concept in catalog.Concepts)
            {
                Array.Clear(accumulator);
                var count = 0;

                if (table.TryGetVector(concept.ConceptId, scratch))
                {
                    TensorPrimitives.MultiplyAdd(scratch, 2.0f, accumulator, accumulator);
                    count++;
                }

                foreach (var syn in concept.Synonyms)
                {
                    // Skip synonyms owned by an earlier primary concept so secondary concepts
                    // do not hijack canonical anchors (e.g. sparkles_neon hijacking "star").
                    if (catalog.WordToConcept.TryGetValue(syn, out var owner) &&
                        !ReferenceEquals(owner, concept))
                    {
                        continue;
                    }

                    if (table.TryGetVector(syn, scratch))
                    {
                        TensorPrimitives.Add(accumulator, scratch, accumulator);
                        count++;
                    }
                }

                if (count == 0)
                {
                    continue;
                }

                var norm = TensorPrimitives.Norm(accumulator);
                if (norm <= 1e-6f || !float.IsFinite(norm))
                {
                    continue;
                }

                TensorPrimitives.Divide(accumulator, norm, accumulator);
                matchedConcepts.Add(concept);
                centroidValues.AddRange(accumulator);
            }

            return new ConceptCentroidIndex(dim, [.. matchedConcepts], [.. centroidValues]);
        }
    }
}
