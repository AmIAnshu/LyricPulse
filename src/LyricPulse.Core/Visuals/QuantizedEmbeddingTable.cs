using System.Buffers;
using System.Collections.Frozen;
using System.Numerics.Tensors;
using System.Text;
using LyricPulse.Core.Security;

namespace LyricPulse.Core.Visuals;

/// <summary>
/// Compact binary reader/writer and SIMD-accelerated lookup table for LPQV int8-quantized GloVe embeddings.
/// Binary format (little-endian):
/// [4B ASCII "LPQV"][uint16 Version=1][uint16 Dimensions][int32 VocabCount]
/// followed by VocabCount records of [float32 Scale][Dimensions * sbyte QuantizedValues],
/// paired with UTF-8 line-delimited <c>glove-vocab.txt</c>.
/// </summary>
public sealed class QuantizedEmbeddingTable
{
    public const ushort SupportedVersion = 1;
    public const int MaxDimensions = 4096;
    public const int MaxVocabCount = 500_000;
    public const long MaxMatrixBytes = 128L * 1024L * 1024L;
    public const int MaxVocabTokenLength = 128;
    private static readonly byte[] MagicBytes = "LPQV"u8.ToArray();

    private readonly FrozenDictionary<string, int> _wordToIndex;
    private readonly float[] _scales;
    private readonly sbyte[] _quantizedVectors;

    private QuantizedEmbeddingTable(
        ushort version,
        int dimensions,
        FrozenDictionary<string, int> wordToIndex,
        float[] scales,
        sbyte[] quantizedVectors)
    {
        Version = version;
        Dimensions = dimensions;
        VocabCount = scales.Length;
        _wordToIndex = wordToIndex;
        _scales = scales;
        _quantizedVectors = quantizedVectors;
    }

    public int Version { get; }
    public int Dimensions { get; }
    public int VocabCount { get; }

    public static void WriteToFiles(
        string binaryPath,
        string vocabPath,
        IReadOnlyList<string> words,
        ReadOnlySpan<float> vectors,
        int dimensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(vocabPath);

        var binDir = Path.GetDirectoryName(binaryPath);
        if (!string.IsNullOrEmpty(binDir))
        {
            Directory.CreateDirectory(binDir);
        }

        var vocabDir = Path.GetDirectoryName(vocabPath);
        if (!string.IsNullOrEmpty(vocabDir))
        {
            Directory.CreateDirectory(vocabDir);
        }

        using var binStream = File.Create(binaryPath);
        using var vocabWriter = new StreamWriter(vocabPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Write(binStream, vocabWriter, words, vectors, dimensions);
    }

    public static void Write(
        Stream binaryOutput,
        TextWriter vocabOutput,
        IReadOnlyList<string> words,
        ReadOnlySpan<float> vectors,
        int dimensions)
    {
        ArgumentNullException.ThrowIfNull(binaryOutput);
        ArgumentNullException.ThrowIfNull(vocabOutput);
        ArgumentNullException.ThrowIfNull(words);

        if (dimensions is < 1 or > MaxDimensions)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions, $"Dimensions must be in [1, {MaxDimensions}].");
        }

        if (words.Count > MaxVocabCount)
        {
            throw new ArgumentOutOfRangeException(nameof(words), words.Count, $"VocabCount must be <= {MaxVocabCount}.");
        }

        var expectedFloats = words.Count * dimensions;
        if (vectors.Length != expectedFloats)
        {
            throw new ArgumentException($"Expected {expectedFloats} floats for {words.Count} words x {dimensions} dims, got {vectors.Length}.", nameof(vectors));
        }

        using var writer = new BinaryWriter(binaryOutput, Encoding.UTF8, leaveOpen: true);
        writer.Write(MagicBytes);
        writer.Write(SupportedVersion);
        writer.Write((ushort)dimensions);
        writer.Write(words.Count);

        var quantizedRow = new byte[dimensions];
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i]?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(word))
            {
                throw new ArgumentException($"Vocabulary entry at index {i} cannot be empty.", nameof(words));
            }

            vocabOutput.WriteLine(word);
            var row = vectors.Slice(i * dimensions, dimensions);
            WriteQuantizedRow(writer, row, quantizedRow);
        }

        writer.Flush();
        vocabOutput.Flush();
    }

    public static QuantizedEmbeddingTable LoadFromFiles(string binaryPath, string vocabPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(vocabPath);

        if (!File.Exists(binaryPath))
        {
            throw new FileNotFoundException("LPQV binary file not found.", binaryPath);
        }

        if (!File.Exists(vocabPath))
        {
            throw new FileNotFoundException("LPQV vocabulary file not found.", vocabPath);
        }

        const long maxBinaryFileBytes = MaxMatrixBytes + (16L * 1024L * 1024L);
        if (!SafeFileGuard.TryOpenSafeReadStream(binaryPath, maxBinaryFileBytes, out var binStream))
        {
            throw new InvalidDataException("LPQV binary file failed safety validation (symlink or oversized).");
        }

        using (binStream!)
        {
            if (!SafeFileGuard.TryOpenSafeReadStream(vocabPath, SafeFileGuard.MaxGloveVocabFileBytes, out var vocabStream))
            {
                throw new InvalidDataException("LPQV vocabulary file failed safety validation (symlink or oversized).");
            }

            using (vocabStream!)
            using (var vocabReader = new StreamReader(vocabStream, Encoding.UTF8))
            {
                return Load(binStream, vocabReader);
            }
        }
    }

    public static QuantizedEmbeddingTable Load(Stream binaryInput, TextReader vocabInput)
    {
        ArgumentNullException.ThrowIfNull(binaryInput);
        ArgumentNullException.ThrowIfNull(vocabInput);

        using var reader = new BinaryReader(binaryInput, Encoding.UTF8, leaveOpen: true);
        var (version, dimensions, vocabCount) = ReadAndValidateHeader(reader);
        var wordToIndex = ReadAndValidateVocab(vocabInput, vocabCount);

        var scales = new float[vocabCount];
        var quantized = new sbyte[checked(vocabCount * dimensions)];
        var rowBytes = new byte[dimensions];

        for (var i = 0; i < vocabCount; i++)
        {
            float scale;
            try
            {
                scale = reader.ReadSingle();
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException($"Truncated LPQV embedding table at record {i}.", ex);
            }

            if (!float.IsFinite(scale) || scale < 0f)
            {
                throw new InvalidDataException($"Invalid LPQV scale value '{scale}' at record {i}.");
            }

            scales[i] = scale;
            ReadExactBytes(binaryInput, rowBytes, i);
            Buffer.BlockCopy(rowBytes, 0, quantized, i * dimensions, dimensions);
        }

        return new QuantizedEmbeddingTable(version, dimensions, wordToIndex, scales, quantized);
    }

    public bool TryGetVector(ReadOnlySpan<char> word, Span<float> destination)
    {
        if (word.IsEmpty || destination.Length < Dimensions)
        {
            return false;
        }

        if (!_wordToIndex.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(word, out var index))
        {
            return false;
        }

        var scale = _scales[index];
        var target = destination[..Dimensions];
        if (scale == 0f)
        {
            target.Clear();
            return false;
        }

        var offset = index * Dimensions;
        for (var d = 0; d < Dimensions; d++)
        {
            target[d] = _quantizedVectors[offset + d] * scale;
        }

        var norm = TensorPrimitives.Norm(target);
        if (norm <= 1e-6f || !float.IsFinite(norm))
        {
            return false;
        }

        TensorPrimitives.Divide(target, norm, target);
        return true;
    }

    public bool TryComputeCosineSimilarity(ReadOnlySpan<char> wordA, ReadOnlySpan<char> wordB, out float similarity)
    {
        similarity = 0f;
        if (Dimensions <= 256)
        {
            Span<float> vecA = stackalloc float[Dimensions];
            Span<float> vecB = stackalloc float[Dimensions];
            if (!TryGetVector(wordA, vecA) || !TryGetVector(wordB, vecB))
            {
                return false;
            }

            similarity = ComputeCosineSimilarity(vecA, vecB);
            return true;
        }

        var rented = ArrayPool<float>.Shared.Rent(Dimensions * 2);
        try
        {
            var vecA = rented.AsSpan(0, Dimensions);
            var vecB = rented.AsSpan(Dimensions, Dimensions);
            if (!TryGetVector(wordA, vecA) || !TryGetVector(wordB, vecB))
            {
                return false;
            }

            similarity = ComputeCosineSimilarity(vecA, vecB);
            return true;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }
    }

    public static float ComputeCosineSimilarity(ReadOnlySpan<float> unitA, ReadOnlySpan<float> unitB) =>
        TensorPrimitives.Dot(unitA, unitB);

    private static void WriteQuantizedRow(BinaryWriter writer, ReadOnlySpan<float> row, byte[] quantizedRow)
    {
        var maxAbs = 0f;
        for (var d = 0; d < row.Length; d++)
        {
            var abs = MathF.Abs(row[d]);
            if (abs > maxAbs)
            {
                maxAbs = abs;
            }
        }

        var scale = maxAbs > 0f ? maxAbs / 127f : 0f;
        writer.Write(scale);

        if (scale == 0f)
        {
            Array.Clear(quantizedRow);
            writer.Write(quantizedRow);
            return;
        }

        var invScale = 1f / scale;
        for (var d = 0; d < row.Length; d++)
        {
            var q = (sbyte)Math.Clamp((int)MathF.Round(row[d] * invScale), -127, 127);
            quantizedRow[d] = unchecked((byte)q);
        }

        writer.Write(quantizedRow);
    }

    private static (ushort Version, int Dimensions, int VocabCount) ReadAndValidateHeader(BinaryReader reader)
    {
        var magic = reader.ReadBytes(4);
        if (magic.Length != 4 || !magic.AsSpan().SequenceEqual(MagicBytes))
        {
            throw new InvalidDataException("Invalid LPQV binary header magic.");
        }

        ushort version;
        ushort dimensions;
        int vocabCount;
        try
        {
            version = reader.ReadUInt16();
            dimensions = reader.ReadUInt16();
            vocabCount = reader.ReadInt32();
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Truncated LPQV binary header.", ex);
        }

        if (version != SupportedVersion)
        {
            throw new InvalidDataException($"Unsupported LPQV version {version}; expected {SupportedVersion}.");
        }

        if (dimensions is < 1 or > MaxDimensions)
        {
            throw new InvalidDataException($"Invalid LPQV dimensions {dimensions}.");
        }

        if (vocabCount is < 0 or > MaxVocabCount)
        {
            throw new InvalidDataException($"Invalid LPQV vocabulary count {vocabCount}.");
        }

        var matrixBytes = (long)vocabCount * dimensions;
        if (matrixBytes > MaxMatrixBytes)
        {
            throw new InvalidDataException($"LPQV embedding matrix size ({matrixBytes} bytes) exceeds maximum allowed ({MaxMatrixBytes} bytes).");
        }

        if (reader.BaseStream.CanSeek)
        {
            var requiredPayloadBytes = (long)vocabCount * (4L + dimensions);
            var remainingBytes = reader.BaseStream.Length - reader.BaseStream.Position;
            if (remainingBytes < requiredPayloadBytes)
            {
                throw new InvalidDataException(
                    $"LPQV binary stream is truncated: header requires {requiredPayloadBytes} payload bytes, only {remainingBytes} bytes remain.");
            }
        }

        return (version, dimensions, vocabCount);
    }

    private static FrozenDictionary<string, int> ReadAndValidateVocab(TextReader vocabInput, int expectedCount)
    {
        var map = new Dictionary<string, int>(Math.Min(expectedCount, 16_384), StringComparer.Ordinal);
        string? line;
        var index = 0;

        while ((line = vocabInput.ReadLine()) is not null)
        {
            if (line.Length > MaxVocabTokenLength)
            {
                throw new InvalidDataException($"Vocabulary entry at index {index} exceeds max length ({MaxVocabTokenLength}).");
            }

            if (line.Length == 0 && index == expectedCount)
            {
                break;
            }

            if (index >= expectedCount)
            {
                throw new InvalidDataException($"Vocabulary file has more entries than LPQV header VocabCount ({expectedCount}).");
            }

            var normalized = line.Trim().ToLowerInvariant();
            if (normalized.Length > 0)
            {
                map.TryAdd(normalized, index);
            }

            index++;
        }

        if (index != expectedCount)
        {
            throw new InvalidDataException($"Vocabulary count mismatch: expected {expectedCount}, found {index}.");
        }

        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static void ReadExactBytes(Stream stream, byte[] buffer, int recordIndex)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
            {
                throw new InvalidDataException($"Truncated LPQV vector payload at record {recordIndex}.");
            }

            offset += read;
        }
    }
}
