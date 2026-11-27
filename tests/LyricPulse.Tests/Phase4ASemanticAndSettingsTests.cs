using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using LyricPulse.Core;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Core.Serialization;
using LyricPulse.Core.Settings;
using LyricPulse.Core.Visuals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LyricPulse.Tests;

public sealed class Phase4ASemanticAndSettingsTests : IDisposable
{
    private const long Frequency = 10_000_000;
    private readonly string _tempDir;
    private readonly TitleNormalizer _normalizer = new();
    private readonly LyricsParser _parser = new();

    public Phase4ASemanticAndSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LyricPulse_P4A_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AppSettings_Phase4AStageAndAestheticDefaultsRoundTripAndClampCleanly()
    {
        var defaults = AppSettings.Default;
        defaults.StageBackdropMode.Should().Be(StageBackdropMode.AlbumArtBlur);
        defaults.ShowFollowingDot.Should().BeFalse();
        defaults.ShowColorSweepLayer.Should().BeTrue();
        defaults.PartyEnergyLevel.Should().Be(1.0f);
        defaults.FullscreenStageScale.Should().Be(1.85f);
        defaults.DesktopOverlayEnabled.Should().BeFalse();
        defaults.PinnedStyles.Should().Equal(
            OverlayStyleId.FisheyeVisual,
            OverlayStyleId.Codestyle,
            OverlayStyleId.Constellation,
            OverlayStyleId.KineticWall);

        // Clamping & invalid/removed enum/NaN defense
        var invalid = defaults with
        {
            StageBackdropMode = (StageBackdropMode)999,
            ShowFollowingDot = true,
            PartyEnergyLevel = float.NaN,
            FullscreenStageScale = 9.5f
        };
        var normalized = invalid.ValidateAndNormalize();
        normalized.StageBackdropMode.Should().Be(StageBackdropMode.AlbumArtBlur);
        normalized.ShowFollowingDot.Should().BeFalse();
        normalized.PartyEnergyLevel.Should().Be(1.0f);
        normalized.FullscreenStageScale.Should().Be(2.6f);

        // Legacy AmbientPartyMesh settings normalize cleanly to AlbumArtBlur
        var legacy = (defaults with { StageBackdropMode = StageBackdropMode.AmbientPartyMesh }).ValidateAndNormalize();
        legacy.StageBackdropMode.Should().Be(StageBackdropMode.AlbumArtBlur);

        var lowClamped = (defaults with { PartyEnergyLevel = 0.1f, FullscreenStageScale = 0.4f }).ValidateAndNormalize();
        lowClamped.PartyEnergyLevel.Should().Be(0.5f);
        lowClamped.FullscreenStageScale.Should().Be(1.2f);

        var highClamped = (defaults with { PartyEnergyLevel = 3.2f, FullscreenStageScale = float.PositiveInfinity }).ValidateAndNormalize();
        highClamped.PartyEnergyLevel.Should().Be(1.5f);
        highClamped.FullscreenStageScale.Should().Be(1.85f);

        // AOT JSON round-trip via JsonSettingsStore (also verifying existing user-saved 3-pin upgrade preservation)
        var settingsPath = Path.Combine(_tempDir, "phase4a-settings.json");
        using var store = new JsonSettingsStore(settingsPath);
        var custom = defaults with
        {
            StageBackdropMode = StageBackdropMode.PureBlack,
            ShowFollowingDot = false,
            ShowColorSweepLayer = false,
            PartyEnergyLevel = 1.35f,
            FullscreenStageScale = 2.25f,
            DesktopOverlayEnabled = true,
            PinnedStyles = [OverlayStyleId.Fisheye, OverlayStyleId.Codestyle, OverlayStyleId.VisualStack]
        };

        await store.SaveAsync(custom);
        var reloaded = await store.LoadAsync();
        reloaded.StageBackdropMode.Should().Be(StageBackdropMode.PureBlack);
        reloaded.ShowFollowingDot.Should().BeFalse();
        reloaded.ShowColorSweepLayer.Should().BeFalse();
        reloaded.PartyEnergyLevel.Should().BeApproximately(1.35f, 0.001f);
        reloaded.FullscreenStageScale.Should().BeApproximately(2.25f, 0.001f);
        reloaded.DesktopOverlayEnabled.Should().BeTrue();
        reloaded.PinnedStyles.Should().Equal(
            OverlayStyleId.Fisheye,
            OverlayStyleId.Codestyle,
            OverlayStyleId.VisualStack);

        // Verify camelCase string enum output via compact AOT JsonContext
        var compactJson = JsonSerializer.Serialize(custom, LyricPulseJsonContext.Default.AppSettings);
        compactJson.Should().Contain("\"stageBackdropMode\":\"PureBlack\"");
        compactJson.Should().Contain("\"showFollowingDot\":false");
        compactJson.Should().Contain("\"desktopOverlayEnabled\":true");
    }

    [Fact]
    public async Task QuantizedEmbeddingTable_LpqvBinaryRoundTripMaintainsCosineAccuracyWithin002AndRejectsCorruptHeaders()
    {
        const int dim = 100;
        var rng = new Random(42);
        var words = new[] { "alpha", "beta", "gamma", "delta" };
        var rawVectors = new float[words.Length * dim];

        // Create unit vectors with varied cosines (similar pair, orthogonal, negative)
        for (var i = 0; i < words.Length; i++)
        {
            var slice = rawVectors.AsSpan(i * dim, dim);
            double normSq = 0;
            for (var d = 0; d < dim; d++)
            {
                var v = (float)(rng.NextDouble() * 2.0 - 1.0);
                slice[d] = v;
                normSq += v * v;
            }

            var invNorm = 1.0f / (float)Math.Sqrt(normSq);
            for (var d = 0; d < dim; d++)
            {
                slice[d] *= invNorm;
            }
        }

        // Make beta closely aligned with alpha (cosine ~0.88)
        var alphaSpan = rawVectors.AsSpan(0, dim);
        var betaSpan = rawVectors.AsSpan(dim, dim);
        double betaNormSq = 0;
        for (var d = 0; d < dim; d++)
        {
            betaSpan[d] = alphaSpan[d] * 0.85f + betaSpan[d] * 0.35f;
            betaNormSq += betaSpan[d] * betaSpan[d];
        }

        var betaInv = 1.0f / (float)Math.Sqrt(betaNormSq);
        for (var d = 0; d < dim; d++)
        {
            betaSpan[d] *= betaInv;
        }

        var binPath = Path.Combine(_tempDir, "test-100d-q8.bin");
        var vocabPath = Path.Combine(_tempDir, "test-vocab.txt");
        QuantizedEmbeddingTable.WriteToFiles(binPath, vocabPath, words, rawVectors, dim);

        // Verify exact binary header magic ("LPQV"), version=1, dim=100, vocabCount=4
        var headerBytes = await File.ReadAllBytesAsync(binPath);
        Encoding.ASCII.GetString(headerBytes, 0, 4).Should().Be("LPQV");
        BitConverter.ToUInt16(headerBytes, 4).Should().Be(1);
        BitConverter.ToUInt16(headerBytes, 6).Should().Be(100);
        BitConverter.ToInt32(headerBytes, 8).Should().Be(4);
        headerBytes.Length.Should().Be(12 + words.Length * (4 + dim));

        var loaded = QuantizedEmbeddingTable.LoadFromFiles(binPath, vocabPath);
        loaded.Version.Should().Be(1);
        loaded.Dimensions.Should().Be(100);
        loaded.VocabCount.Should().Be(4);

        // Verify all pairwise quantized cosine similarities match float32 within <= 0.02
        for (var i = 0; i < words.Length; i++)
        {
            for (var j = 0; j < words.Length; j++)
            {
                var exactCos = QuantizedEmbeddingTable.ComputeCosineSimilarity(
                    rawVectors.AsSpan(i * dim, dim),
                    rawVectors.AsSpan(j * dim, dim));

                loaded.TryComputeCosineSimilarity(words[i], words[j], out var quantizedCos).Should().BeTrue();
                MathF.Abs(quantizedCos - exactCos).Should().BeLessThanOrEqualTo(
                    0.02f,
                    $"quantized cosine({words[i]}, {words[j]}) must be within 0.02 of float32");
            }
        }

        loaded.TryComputeCosineSimilarity("alpha", "missing_word", out _).Should().BeFalse();

        // Corrupt header rejection tests
        var badMagic = (byte[])headerBytes.Clone();
        badMagic[0] = (byte)'X';
        var actBadMagic = () => QuantizedEmbeddingTable.Load(new MemoryStream(badMagic), new StringReader(string.Join('\n', words)));
        actBadMagic.Should().Throw<InvalidDataException>();

        var badVersion = (byte[])headerBytes.Clone();
        BitConverter.TryWriteBytes(badVersion.AsSpan(4, 2), (ushort)99);
        var actBadVersion = () => QuantizedEmbeddingTable.Load(new MemoryStream(badVersion), new StringReader(string.Join('\n', words)));
        actBadVersion.Should().Throw<InvalidDataException>();

        var badVocabCount = () => QuantizedEmbeddingTable.Load(new MemoryStream(headerBytes), new StringReader("only_one_word\n"));
        badVocabCount.Should().Throw<InvalidDataException>();

        var truncated = headerBytes[..20];
        var actTruncated = () => QuantizedEmbeddingTable.Load(new MemoryStream(truncated), new StringReader(string.Join('\n', words)));
        actTruncated.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task GloveSemanticVisualMatcher_PrioritizesBigramIdiomsOverSingleWordsAndVerifiesAllIdiomTargets()
    {
        using var matcher = new GloveSemanticVisualMatcher(
            binaryPath: Path.Combine(_tempDir, "nonexistent.bin"),
            vocabPath: Path.Combine(_tempDir, "nonexistent.txt"));
        await matcher.InitializeAsync();

        // Graceful fallback when optional GloVe file is not installed
        matcher.IsReady.Should().BeTrue();
        matcher.Mode.Should().Be(VisualMatcherMode.StarterLexicon);

        // Verify ALL curated bigrams (at least 40) and ALL mood targets exist in StarterLexicon.tsv
        SemanticIdiomLexicon.Bigrams.Count.Should().BeGreaterThanOrEqualTo(40);
        var catalog = StarterLexiconCatalog.LoadEmbedded();
        foreach (var kvp in SemanticIdiomLexicon.Bigrams)
        {
            catalog.ConceptsById.ContainsKey(kvp.Value)
                .Should().BeTrue($"bigram '{kvp.Key}' must target a verified StarterLexicon concept '{kvp.Value}'");
        }

        foreach (var moodConcept in SemanticIdiomLexicon.AllMoodConceptIds)
        {
            catalog.ConceptsById.ContainsKey(moodConcept)
                .Should().BeTrue($"mood fallback '{moodConcept}' must exist in StarterLexicon");
        }

        // Test rich poetic bigram resolution (head word receives idiom icon; modifier does not emit competing icon)
        var paperPlane = MakeWords("fold", "a", "paper", "plane");
        var ppMatches = matcher.MatchLine(paperPlane);
        ppMatches[2].Should().BeNull("modifier 'paper' is consumed by 'paper plane' bigram instead of emitting letter icon");
        ppMatches[3].Should().NotBeNull();
        ppMatches[3]!.ConceptName.Should().Be("paper_plane");
        ppMatches[3]!.IconName.Should().Be("ic_fluent_send_24_filled");

        var brokenHeart = MakeWords("my", "broken", "heart");
        var bhMatches = matcher.MatchLine(brokenHeart);
        bhMatches[1].Should().BeNull();
        bhMatches[2]!.ConceptName.Should().Be("broken_heart");
        bhMatches[2]!.IconName.Should().Be("ic_fluent_heart_broken_24_filled");

        // Stopword-headed bigram: "on fire" must still resolve despite "on" being a stopword!
        var onFire = MakeWords("world", "on", "fire");
        var ofMatches = matcher.MatchLine(onFire);
        ofMatches[1].Should().BeNull();
        ofMatches[2]!.ConceptName.Should().Be("fire");
        ofMatches[2]!.IconName.Should().Be("ic_fluent_fire_24_filled");

        var paperCrown = MakeWords("wearing", "a", "paper", "crown");
        var pcMatches = matcher.MatchLine(paperCrown);
        pcMatches[2].Should().BeNull();
        pcMatches[3]!.ConceptName.Should().Be("crown");
        pcMatches[3]!.IconName.Should().Be("ic_fluent_crown_24_filled");

        var diamondRing = MakeWords("bright", "diamond", "ring");
        var drMatches = matcher.MatchLine(diamondRing);
        drMatches[1].Should().BeNull();
        drMatches[2]!.ConceptName.Should().Be("diamond_ring");

        var starryNight = MakeWords("under", "starry", "night");
        var snMatches = matcher.MatchLine(starryNight);
        snMatches[1].Should().BeNull();
        snMatches[2]!.ConceptName.Should().Be("star");
    }

    [Fact]
    public async Task GloveSemanticVisualMatcher_PerformsSimdVectorSearchOnLpqvTableWithThresholdAndStopwordGuard()
    {
        const int dim = 100;
        var vocab = new[]
        {
            // Seed vectors for starter concepts
            "star", "galaxy", "constellation",
            "fire", "flame", "blaze",
            "ocean", "wave", "tide",
            // Novel out-of-lexicon words + orthogonal noise + stopword
            "supercluster",
            "pyroclast",
            "pelagic",
            "xylophonewidget",
            "the"
        };

        var vectors = new float[vocab.Length * dim];
        SetBasisVector(vectors, 0, dim, axis: 0, 1.0f);   // star
        SetBasisVector(vectors, 1, dim, axis: 0, 0.98f, axis2: 1, 0.199f); // galaxy
        SetBasisVector(vectors, 2, dim, axis: 0, 0.97f, axis2: 2, 0.243f); // constellation
        SetBasisVector(vectors, 3, dim, axis: 10, 1.0f);  // fire
        SetBasisVector(vectors, 4, dim, axis: 10, 0.99f, axis2: 11, 0.141f); // flame
        SetBasisVector(vectors, 5, dim, axis: 10, 0.98f, axis2: 12, 0.199f); // blaze
        SetBasisVector(vectors, 6, dim, axis: 20, 1.0f);  // ocean
        SetBasisVector(vectors, 7, dim, axis: 20, 0.98f, axis2: 21, 0.199f); // wave
        SetBasisVector(vectors, 8, dim, axis: 20, 0.97f, axis2: 22, 0.243f); // tide

        // Novel lyric words semantically close to star (~0.88), fire (~0.84), ocean (~0.80)
        SetBasisVector(vectors, 9, dim, axis: 0, 0.88f, axis2: 50, 0.475f);   // supercluster -> star
        SetBasisVector(vectors, 10, dim, axis: 10, 0.84f, axis2: 51, 0.542f); // pyroclast -> fire
        SetBasisVector(vectors, 11, dim, axis: 20, 0.80f, axis2: 52, 0.600f); // pelagic -> ocean
        // Orthogonal word far below 0.56 threshold
        SetBasisVector(vectors, 12, dim, axis: 90, 1.0f);                     // xylophonewidget -> null
        // Stopword intentionally placed on star axis to verify stopword filter runs BEFORE GloVe
        SetBasisVector(vectors, 13, dim, axis: 0, 1.0f);                      // "the" -> MUST be null!

        var binPath = Path.Combine(_tempDir, "glove-100d-q8.bin");
        var vocabPath = Path.Combine(_tempDir, "glove-vocab.txt");
        QuantizedEmbeddingTable.WriteToFiles(binPath, vocabPath, vocab, vectors, dim);

        using var matcher = new GloveSemanticVisualMatcher(binPath, vocabPath);
        await matcher.InitializeAsync();

        matcher.Mode.Should().Be(VisualMatcherMode.FullSemanticGloVe);
        matcher.IsReady.Should().BeTrue();

        var starMatch = matcher.MatchWord("supercluster");
        starMatch.Should().NotBeNull();
        starMatch!.ConceptName.Should().Be("star");
        starMatch.IsExactMatch.Should().BeFalse();
        starMatch.Confidence.Should().BeGreaterThanOrEqualTo(0.56f);
        starMatch.IconName.Should().Be("ic_fluent_star_24_filled");

        var fireMatch = matcher.MatchWord("pyroclast", VisualPreference.EmojiOnly);
        fireMatch.Should().NotBeNull();
        fireMatch!.ConceptName.Should().Be("fire");
        fireMatch.Kind.Should().Be(VisualKind.Emoji);
        fireMatch.Value.Should().Be("🔥");

        var oceanMatch = matcher.MatchWord("pelagic");
        oceanMatch.Should().NotBeNull();
        oceanMatch!.ConceptName.Should().Be("ocean");

        matcher.MatchWord("xylophonewidget").Should().BeNull("orthogonal vector is below 0.56 cosine threshold");
        matcher.MatchWord("the").Should().BeNull("stopwords must never match via GloVe");

        // AnnotateDocument applies Layer 1 bigrams + Layer 2 lexicon + Layer 3 GloVe end-to-end
        var track = _normalizer.CreateIdentity("Pelagic Supercluster", "Band", "", TimeSpan.FromSeconds(30));
        var doc = _parser.ParseLrc(
            track,
            "[00:01.00]Watch the supercluster on fire\n[00:05.00]Fold a paper plane",
            LyricsSourceKind.LrcLib,
            "LRCLIB",
            false);

        var annotated = matcher.AnnotateDocument(doc);
        annotated.Lines[0].Words[2].Visual!.ConceptName.Should().Be("star");  // supercluster (Layer 3 GloVe)
        annotated.Lines[0].Words[3].Visual.Should().BeNull();                  // "on" (bigram modifier)
        annotated.Lines[0].Words[4].Visual!.ConceptName.Should().Be("fire");  // "fire" (Layer 1 bigram)
        annotated.Lines[1].Words[3].Visual!.ConceptName.Should().Be("paper_plane");
    }

    [Fact]
    public async Task GloveSemanticVisualMatcher_InfersTrackMoodVisualAcrossThemesAndFallsBackCleanly()
    {
        using var matcher = new GloveSemanticVisualMatcher(
            binaryPath: Path.Combine(_tempDir, "missing.bin"),
            vocabPath: Path.Combine(_tempDir, "missing.txt"));
        await matcher.InitializeAsync();

        var heartbreakTrack = _normalizer.CreateIdentity("Shattered Tears", "Solo", "Blue Rain", TimeSpan.FromMinutes(3));
        var heartbreakDoc = _parser.ParseLrc(
            heartbreakTrack,
            "[00:01.00]Broken heart in pouring rain\n[00:05.00]Crying through the lonely pain",
            LyricsSourceKind.LrcLib,
            "LRCLIB",
            false);

        var heartbreakMood = matcher.InferTrackMoodVisual(heartbreakTrack, heartbreakDoc);
        heartbreakMood.Should().NotBeNull();
        heartbreakMood!.ConceptName.Should().Be("broken_heart");

        var cosmicTrack = _normalizer.CreateIdentity("Midnight Galaxy", "Synth", "Starlight", TimeSpan.FromMinutes(3));
        var cosmicDoc = _parser.ParseLrc(
            cosmicTrack,
            "[00:01.00]Under starry night and galaxy dreams",
            LyricsSourceKind.LrcLib,
            "LRCLIB",
            false);

        var cosmicMood = matcher.InferTrackMoodVisual(cosmicTrack, cosmicDoc);
        cosmicMood.Should().NotBeNull();
        cosmicMood!.ConceptName.Should().BeOneOf("star", "night", "dream");

        // Neutral / empty document falls back cleanly to verified music icon
        var neutralMood = matcher.InferTrackMoodVisual(null, null, VisualPreference.EmojiOnly);
        neutralMood.Should().NotBeNull();
        neutralMood!.ConceptName.Should().Be("music");
        neutralMood.Kind.Should().Be(VisualKind.Emoji);
    }

    [Fact]
    public async Task OverlayRuntimeController_StageWindowVisibilityGatesTimingNudgesAndExposesStageCommands()
    {
        long ticks = 100 * Frequency;
        var utc = new DateTimeOffset(2026, 11, 25, 13, 0, 0, TimeSpan.Zero);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLyricPulseCore();
        await using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IWordVisualMatcher>().Should().BeOfType<GloveSemanticVisualMatcher>();

        await using var media = new FakeMediaProvider();
        var orchestrator = Substitute.For<ILyricsOrchestrator>();
        using var matcher = new GloveSemanticVisualMatcher(
            Path.Combine(_tempDir, "absent.bin"),
            Path.Combine(_tempDir, "absent.txt"));
        using var settingsStore = new JsonSettingsStore(Path.Combine(_tempDir, "stage-settings.json"));
        using var offsetStore = new TrackOffsetStore(Path.Combine(_tempDir, "stage-offsets.json"));
        using var offsetService = new TimingOffsetService(offsetStore);

        await using var controller = new OverlayRuntimeController(
            media,
            orchestrator,
            matcher,
            settingsStore,
            offsetService,
            new DemoSongProvider(_parser, _normalizer),
            logger: NullLogger<OverlayRuntimeController>.Instance,
            timestampProvider: () => ticks,
            timestampFrequency: Frequency,
            utcNowProvider: () => utc);

        await controller.StartAsync();
        controller.IsStageWindowOpen.Should().BeFalse();

        // Enable Demo Mode so a synced track is active + TrackMoodVisual is pre-resolved
        await controller.SetDemoModeAsync(true);
        var demoSnap = controller.SampleFrame(ticks);
        demoSnap.TrackMoodVisual.Should().NotBeNull();
        demoSnap.IsStageWindowOpen.Should().BeFalse();

        // Hide overlay lyrics while Stage window is CLOSED -> timing nudge must be blocked (no blind nudging!)
        await controller.ToggleLyricsVisibilityAsync();
        controller.SampleFrame(ticks).IsLyricsVisible.Should().BeFalse();

        var blockedNudge = await controller.NudgeTimingOffsetAsync(TimeSpan.FromMilliseconds(500));
        blockedNudge.Offset.Should().Be(TimeSpan.Zero);
        controller.SampleFrame(ticks).LastOffsetNudgeUtc.Should().BeNull();

        // Open Stage Window while overlay lyrics remain hidden -> timing nudge MUST NOW SUCCEED!
        controller.SetStageWindowOpen(true);
        controller.IsStageWindowOpen.Should().BeTrue();
        controller.SampleFrame(ticks).IsStageWindowOpen.Should().BeTrue();

        var allowedNudge = await controller.NudgeTimingOffsetAsync(TimeSpan.FromMilliseconds(500));
        allowedNudge.Offset.Should().Be(TimeSpan.FromMilliseconds(500));
        controller.SampleFrame(ticks).LastOffsetNudgeUtc.Should().Be(utc);

        // Stage & aesthetic setting commands persist cleanly
        await controller.SetStageBackdropModeAsync(StageBackdropMode.PureBlack);
        await controller.ToggleColorSweepLayerAsync();
        await controller.ToggleDesktopOverlayAsync();
        await controller.SetPartyEnergyLevelAsync(1.4f);
        await controller.SetFullscreenStageScaleAsync(2.2f);

        controller.Settings.StageBackdropMode.Should().Be(StageBackdropMode.PureBlack);
        controller.Settings.ShowFollowingDot.Should().BeFalse();
        controller.Settings.ShowColorSweepLayer.Should().BeFalse();
        controller.Settings.DesktopOverlayEnabled.Should().BeTrue();
        controller.Settings.PartyEnergyLevel.Should().BeApproximately(1.4f, 0.001f);
        controller.Settings.FullscreenStageScale.Should().BeApproximately(2.2f, 0.001f);
    }

    [Fact]
    public void QuantizedEmbeddingTable_HandlesZeroVectorsHighDimPoolAndRejectsMalformedPayloads()
    {
        // High-dimensional (> 256) exercises ArrayPool rented path + zero-vector record handling
        const int highDim = 300;
        var words = new[] { "zero", "unit_a", "unit_b" };
        var vectors = new float[words.Length * highDim];
        SetBasisVector(vectors, 1, highDim, axis: 5, 1.0f);
        SetBasisVector(vectors, 2, highDim, axis: 5, 0.9f, axis2: 12, 0.43589f);

        using var ms = new MemoryStream();
        using var sw = new StringWriter();
        QuantizedEmbeddingTable.Write(ms, sw, words, vectors, highDim);

        var bytes = ms.ToArray();
        var vocabText = sw.ToString();
        var table = QuantizedEmbeddingTable.Load(new MemoryStream(bytes), new StringReader(vocabText));

        Span<float> smallBuf = stackalloc float[4];
        table.TryGetVector("unit_a", smallBuf).Should().BeFalse();
        table.TryGetVector("", new float[highDim]).Should().BeFalse();
        table.TryGetVector("zero", new float[highDim]).Should().BeFalse();
        table.TryComputeCosineSimilarity("zero", "unit_a", out _).Should().BeFalse();
        table.TryComputeCosineSimilarity("unit_a", "unit_b", out var cos).Should().BeTrue();
        cos.Should().BeApproximately(0.9f, 0.02f);

        // Write validation guards
        var actBadDim = () => QuantizedEmbeddingTable.Write(new MemoryStream(), new StringWriter(), ["a"], new float[1], 0);
        actBadDim.Should().Throw<ArgumentOutOfRangeException>();

        var actBadLen = () => QuantizedEmbeddingTable.Write(new MemoryStream(), new StringWriter(), ["a", "b"], new float[4], 4);
        actBadLen.Should().Throw<ArgumentException>();

        var actEmptyWord = () => QuantizedEmbeddingTable.Write(new MemoryStream(), new StringWriter(), [" "], new float[4], 4);
        actEmptyWord.Should().Throw<ArgumentException>();

        // Load validation guards: truncated header, bad dims, negative vocab, extra vocab line, negative scale, truncated scale
        var actTruncHeader = () => QuantizedEmbeddingTable.Load(new MemoryStream("LPQV\x01"u8.ToArray()), new StringReader(""));
        actTruncHeader.Should().Throw<InvalidDataException>();

        var badDims = (byte[])bytes.Clone();
        BitConverter.TryWriteBytes(badDims.AsSpan(6, 2), (ushort)0);
        var actZeroDims = () => QuantizedEmbeddingTable.Load(new MemoryStream(badDims), new StringReader(vocabText));
        actZeroDims.Should().Throw<InvalidDataException>();

        var negVocab = (byte[])bytes.Clone();
        BitConverter.TryWriteBytes(negVocab.AsSpan(8, 4), -1);
        var actNegVocab = () => QuantizedEmbeddingTable.Load(new MemoryStream(negVocab), new StringReader(vocabText));
        actNegVocab.Should().Throw<InvalidDataException>();

        var actExtraVocab = () => QuantizedEmbeddingTable.Load(new MemoryStream(bytes), new StringReader(vocabText + "extra\n"));
        actExtraVocab.Should().Throw<InvalidDataException>();

        var negScale = (byte[])bytes.Clone();
        BitConverter.TryWriteBytes(negScale.AsSpan(12, 4), -0.5f);
        var actNegScale = () => QuantizedEmbeddingTable.Load(new MemoryStream(negScale), new StringReader(vocabText));
        actNegScale.Should().Throw<InvalidDataException>();

        var truncAtScale = bytes[..13];
        var actTruncScale = () => QuantizedEmbeddingTable.Load(new MemoryStream(truncAtScale), new StringReader(vocabText));
        actTruncScale.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task GloveSemanticVisualMatcher_HandlesCorruptBinaryFallbackHighDimDedupeAndStarterParity()
    {
        // 1. Corrupt binary gracefully falls back to StarterLexicon with zero exceptions
        var corruptBin = Path.Combine(_tempDir, "corrupt.bin");
        var corruptVocab = Path.Combine(_tempDir, "glove-vocab.txt");
        await File.WriteAllBytesAsync(corruptBin, "BAD!"u8.ToArray());
        await File.WriteAllTextAsync(corruptVocab, "star\n");

        using (var corruptMatcher = new GloveSemanticVisualMatcher(corruptBin))
        {
            await corruptMatcher.InitializeAsync();
            corruptMatcher.Mode.Should().Be(VisualMatcherMode.StarterLexicon);
            corruptMatcher.IsReady.Should().BeTrue();

            // Verify all 180 StarterLexicon concepts resolve cleanly through GloveSemanticVisualMatcher
            var catalog = StarterLexiconCatalog.LoadEmbedded();
            catalog.Concepts.Length.Should().Be(180);
            foreach (var entry in catalog.Concepts)
            {
                entry.Synonyms.Any(s => corruptMatcher.MatchWord(s) is not null)
                    .Should().BeTrue($"starter concept '{entry.ConceptId}' must match at least one synonym");
            }

            // Adjacent duplicate concept suppression (both single word and bigram) & empty lines
            corruptMatcher.MatchLine([]).Should().BeEmpty();
            var dedupeLine = MakeWords("fire", "flames", "on", "fire", "star");
            var dedupeMatches = corruptMatcher.MatchLine(dedupeLine);
            dedupeMatches[0]!.ConceptName.Should().Be("fire");
            dedupeMatches[1].Should().BeNull("adjacent duplicate concept 'fire' is suppressed");
            dedupeMatches[2].Should().BeNull();
            dedupeMatches[3].Should().BeNull("adjacent bigram concept 'fire' is also suppressed while previousConcept is 'fire'");
            dedupeMatches[4]!.ConceptName.Should().Be("star");

            // Empty-word line in AnnotateDocument is preserved verbatim
            var track = _normalizer.CreateIdentity("Empty Line", "Band", "", TimeSpan.FromSeconds(10));
            var docWithEmpty = new LyricsDocument(
                track,
                LyricsSourceKind.LrcLib,
                "LRCLIB",
                TimingKind.LineSynced,
                [new LyricLine(0, "", TimeSpan.Zero, TimeSpan.FromSeconds(2), ImmutableArray<LyricWord>.Empty, IsInstrumentalBreak: true)],
                TimeSpan.Zero,
                false);
            corruptMatcher.AnnotateDocument(docWithEmpty).Lines.Should().HaveCount(1);

            corruptMatcher.Dispose();
            corruptMatcher.IsReady.Should().BeFalse();
            corruptMatcher.MatchWord("star").Should().BeNull();
            corruptMatcher.MatchLine(dedupeLine).Should().BeEmpty();
            corruptMatcher.InferTrackMoodVisual(track, null).Should().BeNull();
        }

        // 2. High-dim (300d) LPQV table exercises pooled query vector path in GloveSemanticVisualMatcher
        const int highDim = 300;
        var vocab = new[] { "star", "supercluster" };
        var vecs = new float[vocab.Length * highDim];
        SetBasisVector(vecs, 0, highDim, axis: 3, 1.0f);
        SetBasisVector(vecs, 1, highDim, axis: 3, 0.9f, axis2: 9, 0.43589f);
        var bin300 = Path.Combine(_tempDir, "glove-300d.bin");
        var vocab300 = Path.Combine(_tempDir, "glove-300d.txt");
        QuantizedEmbeddingTable.WriteToFiles(bin300, vocab300, vocab, vecs, highDim);

        using (var highDimMatcher = new GloveSemanticVisualMatcher(bin300, vocab300))
        {
            await highDimMatcher.InitializeAsync();
            highDimMatcher.Mode.Should().Be(VisualMatcherMode.FullSemanticGloVe);
            highDimMatcher.MatchWord("supercluster")!.ConceptName.Should().Be("star");
            highDimMatcher.MatchWord("not_in_vocab_at_all").Should().BeNull();
        }

        // 3. Default constructor (no explicit path) probes default locations cleanly & falls back safely
        using (var defaultMatcher = new GloveSemanticVisualMatcher())
        {
            await defaultMatcher.InitializeAsync();
            defaultMatcher.IsReady.Should().BeTrue();
        }

        // 4. StarterLexiconVisualMatcher parity for MatchLine & InferTrackMoodVisual
        using (var starter = new StarterLexiconVisualMatcher())
        {
            await starter.InitializeAsync();
            starter.MatchLine([]).Should().BeEmpty();
            var lineMatches = starter.MatchLine(MakeWords("star", "stars", "ocean"));
            lineMatches[0]!.ConceptName.Should().Be("star");
            lineMatches[1].Should().BeNull();
            lineMatches[2]!.ConceptName.Should().Be("ocean");
            starter.InferTrackMoodVisual(null, null)!.ConceptName.Should().Be("music");

            starter.Dispose();
            starter.MatchLine(MakeWords("star")).Should().BeEmpty();
            starter.InferTrackMoodVisual(null, null).Should().BeNull();
        }
    }

    private static ImmutableArray<LyricWord> MakeWords(params string[] tokens) =>
        tokens
            .Select((t, i) => new LyricWord(
                t,
                TimeSpan.FromSeconds(i),
                TimeSpan.FromSeconds(i + 0.8),
                IsSpaceSeparated: true))
            .ToImmutableArray();

    private static void SetBasisVector(
        float[] buffer,
        int rowIndex,
        int dim,
        int axis,
        float value,
        int axis2 = -1,
        float value2 = 0f)
    {
        var row = buffer.AsSpan(rowIndex * dim, dim);
        row[axis] = value;
        if (axis2 >= 0)
        {
            row[axis2] = value2;
        }
    }

    private sealed class FakeMediaProvider : IMediaSessionProvider
    {
        public MediaSessionSnapshot? CurrentSession => null;
        public event EventHandler<MediaSessionSnapshot?>? SessionChanged;
        public event EventHandler<TrackIdentity?>? TrackChanged;
        public event EventHandler<TimeSpan>? SeekDetected;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public TimeSpan GetInterpolatedPosition() => TimeSpan.Zero;
        public Task<AlbumArtPayload?> GetCurrentAlbumArtAsync(CancellationToken cancellationToken = default) => Task.FromResult<AlbumArtPayload?>(null);
        public void UpdateSourcePolicy(MediaSourcePolicy policy, ImmutableArray<string> blockedAppIds, bool ignoreBrowserVideoSessions = true)
        {
            _ = SessionChanged;
            _ = TrackChanged;
            _ = SeekDetected;
        }
        public Task<bool> TryTogglePlayPauseAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> TrySkipNextAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> TrySkipPreviousAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
