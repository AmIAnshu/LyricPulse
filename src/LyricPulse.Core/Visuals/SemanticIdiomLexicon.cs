using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Visuals;

/// <summary>
/// Curated Layer-1 poetic bigram idioms and Layer-4 song-mood fallback taxonomy.
/// Every target concept ID maps strictly to a TTF-verified row in <c>StarterLexicon.tsv</c>.
/// </summary>
public static class SemanticIdiomLexicon
{
    public const string DefaultMoodConceptId = "music";

    public static FrozenDictionary<string, string> Bigrams { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["broken heart"] = "broken_heart",
            ["broken hearts"] = "broken_heart",
            ["starry night"] = "star",
            ["starry nights"] = "star",
            ["on fire"] = "fire",
            ["falling rain"] = "rain",
            ["pouring rain"] = "rain",
            ["heavy rain"] = "umbrella_rain",
            ["acid rain"] = "rain_cloud",
            ["open road"] = "car",
            ["neon lights"] = "city_night",
            ["neon light"] = "city_night",
            ["neon glow"] = "midnight_glow",
            ["shooting star"] = "sparkles_neon",
            ["shooting stars"] = "sparkles_neon",
            ["shining star"] = "compass_star",
            ["distant star"] = "telescope",
            ["paper plane"] = "paper_plane",
            ["paper planes"] = "paper_plane",
            ["paper crown"] = "crown",
            ["electric sky"] = "lightning",
            ["midnight train"] = "train",
            ["golden hour"] = "sun",
            ["wild fire"] = "fire",
            ["cold winter"] = "snow",
            ["warm summer"] = "sun",
            ["endless summer"] = "sun_beach",
            ["fast car"] = "car",
            ["fast cars"] = "car",
            ["city lights"] = "city",
            ["ocean waves"] = "ocean",
            ["ocean wave"] = "ocean",
            ["high tide"] = "ocean",
            ["deep water"] = "water",
            ["thunder storm"] = "storm",
            ["diamond ring"] = "diamond_ring",
            ["blue sky"] = "cloud",
            ["dark night"] = "night",
            ["full moon"] = "night",
            ["crescent moon"] = "night",
            ["black hole"] = "moon_eclipse",
            ["northern lights"] = "rainbow",
            ["crystal ball"] = "crystal_ball",
            ["rising sun"] = "sunrise_horizon",
            ["setting sun"] = "sun",
            ["morning light"] = "sunrise_horizon",
            ["night sky"] = "telescope_night",
            ["empty room"] = "home",
            ["locked door"] = "key",
            ["open door"] = "lock_open",
            ["love letter"] = "paper_heart",
            ["sweet dream"] = "dream",
            ["sweet dreams"] = "dream",
            ["bad dream"] = "fear",
            ["wild heart"] = "anchor_heart",
            ["cold heart"] = "crystal_ice",
            ["last dance"] = "dance",
            ["slow dance"] = "dance",
            ["magic spell"] = "magic_wand",
            ["time machine"] = "clock_retro",
            ["bright light"] = "light",
            ["secret key"] = "key_golden"
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly ImmutableArray<(string MoodConceptId, FrozenSet<string> Indicators)> MoodBuckets =
    [
        ("broken_heart", new[]
        {
            "broken_heart", "cry", "rain", "umbrella_rain", "rain_cloud", "ghost", "fear", "rose_wilted", "battery_low", "skull"
        }.ToFrozenSet(StringComparer.Ordinal)),
        ("heart", new[]
        {
            "heart", "kiss", "paper_heart", "heart_sparkle", "heart_hands", "anchor_heart", "diamond_ring", "smile", "flower"
        }.ToFrozenSet(StringComparer.Ordinal)),
        ("fire", new[]
        {
            "fire", "lightning", "bolt_energy", "fire_spark", "volcano", "flame_blue", "fireworks_pop", "dance", "heartbeat_pulse", "anger"
        }.ToFrozenSet(StringComparer.Ordinal)),
        ("star", new[]
        {
            "star", "night", "telescope_night", "sparkles_neon", "compass_star", "moon_eclipse", "rocket", "earth", "dream", "dream_cloud"
        }.ToFrozenSet(StringComparer.Ordinal)),
        ("ocean", new[]
        {
            "ocean", "water", "river", "wave_surf", "sun_beach", "peace", "cloud", "wind", "forest", "leaf"
        }.ToFrozenSet(StringComparer.Ordinal)),
        ("city", new[]
        {
            "city", "city_night", "city_bridge", "midnight_glow", "code", "code_terminal", "globe_star", "car", "train", "light"
        }.ToFrozenSet(StringComparer.Ordinal))
    ];

    public static ImmutableArray<string> AllMoodConceptIds { get; } =
        MoodBuckets.Select(static b => b.MoodConceptId).Append(DefaultMoodConceptId).ToImmutableArray();

    public static bool TryMatchBigram(
        ReadOnlySpan<char> firstNormalized,
        ReadOnlySpan<char> secondNormalized,
        [NotNullWhen(true)] out string? conceptId)
    {
        conceptId = null;
        if (firstNormalized.IsEmpty || secondNormalized.IsEmpty)
        {
            return false;
        }

        var combinedLen = firstNormalized.Length + 1 + secondNormalized.Length;
        if (combinedLen > 96)
        {
            return false;
        }

        Span<char> keyBuffer = stackalloc char[combinedLen];
        firstNormalized.CopyTo(keyBuffer);
        keyBuffer[firstNormalized.Length] = ' ';
        secondNormalized.CopyTo(keyBuffer[(firstNormalized.Length + 1)..]);

        return Bigrams.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(keyBuffer, out conceptId);
    }

    public static string InferTrackMoodConceptId(
        TrackIdentity? track,
        LyricsDocument? document,
        StarterLexiconCatalog catalog)
    {
        Span<int> scores = stackalloc int[MoodBuckets.Length];

        if (track is not null)
        {
            AccumulateTextMood(track.NormalizedTitle, catalog, scores, weight: 2);
            AccumulateTextMood(track.AlbumTitle, catalog, scores, weight: 1);
        }

        if (document is not null)
        {
            foreach (var line in document.Lines)
            {
                AccumulateLineMood(line, catalog, scores);
            }
        }

        var bestIndex = -1;
        var bestScore = 0;
        for (var i = 0; i < scores.Length; i++)
        {
            if (scores[i] > bestScore)
            {
                bestScore = scores[i];
                bestIndex = i;
            }
        }

        return bestIndex >= 0 ? MoodBuckets[bestIndex].MoodConceptId : DefaultMoodConceptId;
    }

    private static void AccumulateLineMood(LyricLine line, StarterLexiconCatalog catalog, Span<int> scores)
    {
        if (!line.Words.IsDefaultOrEmpty)
        {
            foreach (var word in line.Words)
            {
                if (word.Visual is not null)
                {
                    AddConceptScore(word.Visual.ConceptName, scores, weight: 1);
                    continue;
                }

                AccumulateTokenMood(word.Text, catalog, scores, weight: 1);
            }

            return;
        }

        AccumulateTextMood(line.RawText, catalog, scores, weight: 1);
    }

    private static void AccumulateTextMood(string? text, StarterLexiconCatalog catalog, Span<int> scores, int weight)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            AccumulateTokenMood(token, catalog, scores, weight);
        }
    }

    private static void AccumulateTokenMood(ReadOnlySpan<char> rawWord, StarterLexiconCatalog catalog, Span<int> scores, int weight)
    {
        Span<char> buffer = stackalloc char[48];
        if (!LyricTextNormalizationData.TryNormalizeToken(rawWord, buffer, out var length))
        {
            return;
        }

        var normalized = buffer[..length];
        if (LyricTextNormalizationData.Stopwords.GetAlternateLookup<ReadOnlySpan<char>>().Contains(normalized))
        {
            return;
        }

        if (catalog.TryMatchExactOrStem(normalized, out var entry, out _, out _))
        {
            AddConceptScore(entry.ConceptId, scores, weight);
        }
    }

    private static void AddConceptScore(string conceptId, Span<int> scores, int weight)
    {
        for (var i = 0; i < MoodBuckets.Length; i++)
        {
            if (MoodBuckets[i].Indicators.Contains(conceptId))
            {
                scores[i] += weight;
            }
        }
    }
}
