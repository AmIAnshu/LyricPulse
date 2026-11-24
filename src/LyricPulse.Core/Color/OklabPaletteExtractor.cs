using System.Buffers.Binary;
using System.Security.Cryptography;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Color;

/// <summary>
/// Deterministic SHA-256 seeded OKLab K-Means++ (K=8) album-cover palette extractor
/// with monochrome C_p85 detection and role-specific OKLCH contrast clamps.
/// </summary>
public static class OklabPaletteExtractor
{
    public const int ClusterCount = 8;
    public const int MaxKMeansIterations = 12;
    public const double MonochromeChromaP85Threshold = 0.035;
    public const double MinAccent2HueSeparationDeg = 38.0;
    public const double MinAccent3HueSeparationDeg = 28.0;
    public const double MinAccentDeltaE = 0.16;

    public static AlbumAccentPalette ExtractFromRgba(
        ReadOnlySpan<byte> rgbaPixels,
        int width,
        int height,
        ReadOnlySpan<byte> seedBytes,
        string? sourceAlbumHash = null)
    {
        if (width <= 0 || height <= 0 || rgbaPixels.Length < (long)width * height * 4)
        {
            return AlbumAccentPalette.Default with { SourceAlbumHash = sourceAlbumHash };
        }

        var opaqueSamples = CollectWeightedSamples(rgbaPixels, width, height, out var allChromas);
        if (allChromas.Count == 0)
        {
            return AlbumAccentPalette.Default with { SourceAlbumHash = sourceAlbumHash };
        }

        allChromas.Sort();
        var p85Idx = Math.Clamp((int)Math.Floor((allChromas.Count - 1) * 0.85), 0, allChromas.Count - 1);
        if (allChromas[p85Idx] < MonochromeChromaP85Threshold)
        {
            return AlbumAccentPalette.MonochromeStudio with { SourceAlbumHash = sourceAlbumHash };
        }

        var samples = opaqueSamples.Where(s => s.Lab.L is >= 0.12 and <= 0.95).ToList();
        if (samples.Count == 0)
        {
            samples = opaqueSamples;
        }

        var seed = DeriveDeterministicSeed(seedBytes, rgbaPixels);
        var clusters = RunKMeansPlusPlus(samples, ClusterCount, MaxKMeansIterations, seed);
        return clusters.Count == 0
            ? AlbumAccentPalette.Default with { SourceAlbumHash = sourceAlbumHash }
            : AssignPerceptualPaletteRoles(clusters, sourceAlbumHash);
    }

    private static List<WeightedPixel> CollectWeightedSamples(
        ReadOnlySpan<byte> rgbaPixels,
        int width,
        int height,
        out List<double> chromas)
    {
        var totalPixels = width * height;
        var samples = new List<WeightedPixel>(totalPixels);
        chromas = new List<double>(totalPixels);

        var invHalfW = width > 1 ? 2.0 / (width - 1) : 0.0;
        var invHalfH = height > 1 ? 2.0 / (height - 1) : 0.0;

        for (var y = 0; y < height; y++)
        {
            var ny = (y * invHalfH) - 1.0;
            for (var x = 0; x < width; x++)
            {
                var idx = ((y * width) + x) * 4;
                var alpha = rgbaPixels[idx + 3];
                if (alpha < 128)
                {
                    continue;
                }

                var lab = OklabColorMath.SrgbToOklab(new RgbColor(rgbaPixels[idx], rgbaPixels[idx + 1], rgbaPixels[idx + 2], alpha));
                chromas.Add(Math.Sqrt((lab.A * lab.A) + (lab.B * lab.B)));

                var nx = (x * invHalfW) - 1.0;
                var radialWeight = 0.65 + (0.35 * Math.Exp(-((nx * nx) + (ny * ny)) / 0.55));
                samples.Add(new WeightedPixel(lab, radialWeight));
            }
        }

        return samples;
    }

    private static List<OklabCluster> RunKMeansPlusPlus(
        List<WeightedPixel> samples,
        int k,
        int maxIterations,
        ulong seed)
    {
        var actualK = Math.Min(k, samples.Count);
        var rng = new DeterministicPrng(seed);
        var centroids = InitializeCentroids(samples, actualK, ref rng);

        var sumL = new double[actualK];
        var sumA = new double[actualK];
        var sumB = new double[actualK];
        var sumW = new double[actualK];

        for (var iter = 0; iter < maxIterations; iter++)
        {
            Array.Clear(sumL);
            Array.Clear(sumA);
            Array.Clear(sumB);
            Array.Clear(sumW);

            AssignSamplesToCentroids(samples, centroids, sumL, sumA, sumB, sumW);

            var maxMove = UpdateCentroids(centroids, sumL, sumA, sumB, sumW);
            if (maxMove < 1e-4)
            {
                break;
            }
        }

        return RankClusters(centroids, sumW, samples.Sum(s => s.Weight));
    }

    private static void AssignSamplesToCentroids(
        List<WeightedPixel> samples,
        OklabColor[] centroids,
        double[] sumL,
        double[] sumA,
        double[] sumB,
        double[] sumW)
    {
        foreach (var s in samples)
        {
            var bestCluster = 0;
            var bestDist = double.MaxValue;
            for (var c = 0; c < centroids.Length; c++)
            {
                var d = OklabColorMath.SquaredDistance(s.Lab, centroids[c]);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestCluster = c;
                }
            }

            sumL[bestCluster] += s.Lab.L * s.Weight;
            sumA[bestCluster] += s.Lab.A * s.Weight;
            sumB[bestCluster] += s.Lab.B * s.Weight;
            sumW[bestCluster] += s.Weight;
        }
    }

    private static double UpdateCentroids(
        OklabColor[] centroids,
        double[] sumL,
        double[] sumA,
        double[] sumB,
        double[] sumW)
    {
        var maxMove = 0.0;
        for (var c = 0; c < centroids.Length; c++)
        {
            if (sumW[c] <= 1e-9)
            {
                continue;
            }

            var updated = new OklabColor(sumL[c] / sumW[c], sumA[c] / sumW[c], sumB[c] / sumW[c]);
            maxMove = Math.Max(maxMove, OklabColorMath.DeltaE(centroids[c], updated));
            centroids[c] = updated;
        }

        return maxMove;
    }

    private static List<OklabCluster> RankClusters(OklabColor[] centroids, double[] sumW, double totalWeight)
    {
        var clusters = new List<OklabCluster>(centroids.Length);
        for (var c = 0; c < centroids.Length; c++)
        {
            if (sumW[c] <= 1e-9)
            {
                continue;
            }

            var share = sumW[c] / totalWeight;
            var lch = OklabColorMath.OklabToOklch(centroids[c]);
            var salience = Math.Pow(share, 0.62) * (0.30 + (2.20 * lch.C)) * (1.0 - (0.85 * Math.Abs(lch.L - 0.60)));
            clusters.Add(new OklabCluster(centroids[c], lch, share, salience));
        }

        var major = clusters.Where(c => c.Share >= 0.015).ToList();
        var active = major.Count > 0 ? major : clusters;
        active.Sort(static (a, b) => b.Salience.CompareTo(a.Salience));
        return active;
    }

    private static OklabColor[] InitializeCentroids(
        List<WeightedPixel> samples,
        int k,
        ref DeterministicPrng rng)
    {
        var centroids = new OklabColor[k];
        var minDistSq = new double[samples.Count];
        Array.Fill(minDistSq, double.MaxValue);

        centroids[0] = samples[rng.NextInt(samples.Count)].Lab;

        for (var c = 1; c < k; c++)
        {
            var prev = centroids[c - 1];
            var weightedTotal = 0.0;
            for (var i = 0; i < samples.Count; i++)
            {
                var d = OklabColorMath.SquaredDistance(samples[i].Lab, prev);
                if (d < minDistSq[i])
                {
                    minDistSq[i] = d;
                }

                weightedTotal += minDistSq[i] * samples[i].Weight;
            }

            if (weightedTotal <= 1e-12)
            {
                centroids[c] = samples[rng.NextInt(samples.Count)].Lab;
                continue;
            }

            var target = rng.NextUnitDouble() * weightedTotal;
            var accum = 0.0;
            var chosen = samples.Count - 1;
            for (var i = 0; i < samples.Count; i++)
            {
                accum += minDistSq[i] * samples[i].Weight;
                if (accum >= target)
                {
                    chosen = i;
                    break;
                }
            }

            centroids[c] = samples[chosen].Lab;
        }

        return centroids;
    }

    private static AlbumAccentPalette AssignPerceptualPaletteRoles(
        List<OklabCluster> rankedClusters,
        string? sourceAlbumHash)
    {
        var top = rankedClusters[0];
        var h1 = top.Lch.H;

        var accent1Rgb = OklabColorMath.OklchToSrgbGamutMapped(
            new OklchColor(Math.Clamp(top.Lch.L, 0.78, 0.92), Math.Clamp(top.Lch.C, 0.09, 0.22), h1));
        var accent1Lab = OklabColorMath.SrgbToOklab(accent1Rgb);

        var secondCluster = rankedClusters
            .Skip(1)
            .FirstOrDefault(c => OklabColorMath.AngularDistanceDegrees(c.Lch.H, h1) >= MinAccent2HueSeparationDeg);

        var h2 = secondCluster is not null
            ? secondCluster.Lch.H
            : h1 is >= 40.0 and <= 110.0
                ? OklabColorMath.NormalizeDegrees(h1 - 48.0)
                : OklabColorMath.NormalizeDegrees(h1 + 42.0);

        var accent2Lch = EnsureDistinctAccent2(
            accent1Lab,
            h1,
            h2,
            secondCluster?.Lch.L ?? (accent1Lab.L > 0.84 ? 0.70 : 0.74),
            secondCluster?.Lch.C ?? Math.Max(top.Lch.C, 0.16));
        var accent2Rgb = OklabColorMath.OklchToSrgbGamutMapped(accent2Lch);

        var thirdCluster = rankedClusters
            .Skip(1)
            .FirstOrDefault(c =>
                OklabColorMath.AngularDistanceDegrees(c.Lch.H, h1) >= MinAccent3HueSeparationDeg &&
                OklabColorMath.AngularDistanceDegrees(c.Lch.H, accent2Lch.H) >= 20.0);

        var h3 = thirdCluster?.Lch.H ?? OklabColorMath.NormalizeDegrees(h1 + 24.0);
        var accent3Rgb = OklabColorMath.OklchToSrgbGamutMapped(
            new OklchColor(Math.Clamp(thirdCluster?.Lch.L ?? 0.68, 0.62, 0.76), Math.Clamp(thirdCluster?.Lch.C ?? 0.07, 0.04, 0.12), h3));

        var scrimRgb = OklabColorMath.OklchToSrgbGamutMapped(
            new OklchColor(Math.Clamp(top.Lch.L * 0.25, 0.14, 0.22), Math.Clamp(top.Lch.C * 0.30, 0.015, 0.055), h1));

        return new AlbumAccentPalette(accent1Rgb, accent2Rgb, accent3Rgb, scrimRgb, IsMonochromeFallback: false, sourceAlbumHash);
    }

    private static OklchColor EnsureDistinctAccent2(OklabColor accent1Lab, double h1, double h2, double rawL2, double rawC2)
    {
        var l2 = Math.Clamp(rawL2, 0.66, 0.82);
        var c2 = Math.Clamp(rawC2, 0.13, 0.26);
        var candidate = new OklchColor(l2, c2, h2);

        if (OklabColorMath.DeltaE(accent1Lab, OklabColorMath.SrgbToOklab(OklabColorMath.OklchToSrgbGamutMapped(candidate))) >= MinAccentDeltaE)
        {
            return candidate;
        }

        var adjustedL = accent1Lab.L >= 0.82 ? 0.68 : 0.78;
        var widenedH = OklabColorMath.AngularDistanceDegrees(h1, h2) < 60.0 ? OklabColorMath.NormalizeDegrees(h1 + 120.0) : h2;
        var adjusted = new OklchColor(adjustedL, Math.Max(c2, 0.18), widenedH);

        return OklabColorMath.DeltaE(accent1Lab, OklabColorMath.SrgbToOklab(OklabColorMath.OklchToSrgbGamutMapped(adjusted))) >= MinAccentDeltaE
            ? adjusted
            : new OklchColor(0.68, 0.20, OklabColorMath.NormalizeDegrees(h1 + 165.0));
    }

    private static ulong DeriveDeterministicSeed(ReadOnlySpan<byte> seedBytes, ReadOnlySpan<byte> fallbackPixels)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(seedBytes.IsEmpty ? fallbackPixels : seedBytes, hash);
        var value = BinaryPrimitives.ReadUInt64LittleEndian(hash[..8]);
        return value == 0 ? 0x9E3779B97F4A7C15UL : value;
    }

    private readonly record struct WeightedPixel(OklabColor Lab, double Weight);

    private sealed record OklabCluster(OklabColor Lab, OklchColor Lch, double Share, double Salience);

    private struct DeterministicPrng(ulong state)
    {
        private ulong _state = state == 0 ? 0x9E3779B97F4A7C15UL : state;

        public ulong NextUInt64()
        {
            var x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        public int NextInt(int maxExclusive) => (int)(NextUInt64() % (ulong)maxExclusive);

        public double NextUnitDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));
    }
}
