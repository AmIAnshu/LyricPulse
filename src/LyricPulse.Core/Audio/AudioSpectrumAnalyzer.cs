using System.Collections.Immutable;
using System.Diagnostics;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Audio;

/// <summary>
/// Thread-safe zero-dependency 1024-point Hann-windowed Radix-2 Cooley-Tukey FFT spectrum analyzer
/// transforming live PCM audio samples into 32 log-spaced frequency bands, bass/mid/treble energy,
/// spectral-flux beat pulses, and multi-timescale beat-drop / climax detection.
/// </summary>
public sealed class AudioSpectrumAnalyzer
{
    public const int FftSize = 1024;
    public const int BandCount = AudioFrameMetrics.BandCount;

    private static readonly float[] HannWindow = BuildHannWindow(FftSize);
    private static readonly int[] BitReversed = BuildBitReversedTable(FftSize);

    private readonly Lock _sync = new();
    private readonly float[] _pcmRing = new float[FftSize];
    private readonly float[] _real = new float[FftSize];
    private readonly float[] _imag = new float[FftSize];
    private readonly float[] _smoothedBands = new float[BandCount];
    private readonly float[] _prevSpectrumMag = new float[FftSize / 2];

    private int _ringWriteIdx;
    private int _sampleRateHz = 48_000;
    private float _smoothedBass;
    private float _smoothedMid;
    private float _smoothedTreble;
    private float _rollingPeak = 0.08f;
    private float _beatPulse;
    private float _prevBassFluxRef;

    private long _lastNonSilentTicks;
    private long _lastComputeTicks;
    private long _lastCacheTicks;
    private float _lastCachedEnergy = -1f;
    private bool _hasCachedMetrics;
    private AudioFrameMetrics? _cachedMetrics;

    public AudioSpectrumAnalyzer(int sampleRateHz = 48_000)
    {
        _sampleRateHz = Math.Clamp(sampleRateHz, 8_000, 192_000);
        _lastComputeTicks = Stopwatch.GetTimestamp();
    }

    public int SampleRateHz
    {
        get
        {
            lock (_sync)
            {
                return _sampleRateHz;
            }
        }
        set
        {
            lock (_sync)
            {
                _sampleRateHz = Math.Clamp(value, 8_000, 192_000);
            }
        }
    }

    public void PushInterleavedFloatSamples(ReadOnlySpan<float> interleaved, int channels)
    {
        if (interleaved.IsEmpty || channels <= 0)
        {
            return;
        }

        var frameCount = interleaved.Length / channels;
        if (frameCount <= 0)
        {
            return;
        }

        var maxAbs = 0f;
        lock (_sync)
        {
            var invChannels = 1f / channels;
            for (var f = 0; f < frameCount; f++)
            {
                var baseIdx = f * channels;
                var sum = 0f;
                for (var c = 0; c < channels; c++)
                {
                    var sample = interleaved[baseIdx + c];
                    if (float.IsFinite(sample))
                    {
                        sum += sample;
                    }
                }

                var mono = Math.Clamp(sum * invChannels, -1f, 1f);
                var abs = MathF.Abs(mono);
                if (abs > maxAbs)
                {
                    maxAbs = abs;
                }

                _pcmRing[_ringWriteIdx] = mono;
                _ringWriteIdx = (_ringWriteIdx + 1) & (FftSize - 1);
            }

            if (maxAbs > 0.0008f)
            {
                _lastNonSilentTicks = Stopwatch.GetTimestamp();
            }

            _hasCachedMetrics = false;
        }
    }

    public void PushInterleavedInt16Samples(ReadOnlySpan<short> interleaved, int channels)
    {
        if (interleaved.IsEmpty || channels <= 0)
        {
            return;
        }

        var frameCount = interleaved.Length / channels;
        if (frameCount <= 0)
        {
            return;
        }

        const float Inv32768 = 1f / 32768f;
        var maxAbs = 0f;
        lock (_sync)
        {
            var invChannels = 1f / channels;
            for (var f = 0; f < frameCount; f++)
            {
                var baseIdx = f * channels;
                var sum = 0f;
                for (var c = 0; c < channels; c++)
                {
                    sum += interleaved[baseIdx + c] * Inv32768;
                }

                var mono = Math.Clamp(sum * invChannels, -1f, 1f);
                var abs = MathF.Abs(mono);
                if (abs > maxAbs)
                {
                    maxAbs = abs;
                }

                _pcmRing[_ringWriteIdx] = mono;
                _ringWriteIdx = (_ringWriteIdx + 1) & (FftSize - 1);
            }

            if (maxAbs > 0.0008f)
            {
                _lastNonSilentTicks = Stopwatch.GetTimestamp();
            }

            _hasCachedMetrics = false;
        }
    }

    public AudioFrameMetrics ComputeMetrics(double timeSeconds, bool isPlaying, float partyEnergy) =>
        ComputeMetrics(timeSeconds, isPlaying, partyEnergy, dtSecondsOverride: null);

    public AudioFrameMetrics ComputeMetrics(
        double timeSeconds,
        bool isPlaying,
        float partyEnergy,
        float? dtSecondsOverride)
    {
        var nowTicks = Stopwatch.GetTimestamp();
        bool useLive;
        float dtSec;

        lock (_sync)
        {
            if (!dtSecondsOverride.HasValue &&
                _hasCachedMetrics &&
                _cachedMetrics is not null &&
                MathF.Abs(_lastCachedEnergy - partyEnergy) < 0.01f &&
                Stopwatch.GetElapsedTime(_lastCacheTicks, nowTicks).TotalMilliseconds < 11.0)
            {
                return _cachedMetrics;
            }

            dtSec = dtSecondsOverride.HasValue && float.IsFinite(dtSecondsOverride.Value)
                ? Math.Clamp(dtSecondsOverride.Value, 0.001f, 0.25f)
                : Math.Clamp((float)Stopwatch.GetElapsedTime(_lastComputeTicks, nowTicks).TotalSeconds, 0.001f, 0.1f);
            _lastComputeTicks = nowTicks;

            var sinceAudioSec = _lastNonSilentTicks == 0
                ? double.MaxValue
                : Stopwatch.GetElapsedTime(_lastNonSilentTicks, nowTicks).TotalSeconds;
            useLive = sinceAudioSec <= 1.65;

            if (!useLive)
            {
                _hasCachedMetrics = false;
                for (var b = 0; b < BandCount; b++)
                {
                    _smoothedBands[b] *= MathF.Max(0f, 1f - (dtSec * 6f));
                }

                _smoothedBass *= MathF.Max(0f, 1f - (dtSec * 6f));
                _smoothedMid *= MathF.Max(0f, 1f - (dtSec * 6f));
                _smoothedTreble *= MathF.Max(0f, 1f - (dtSec * 6f));
                _beatPulse = MathF.Max(0f, _beatPulse - (dtSec * 3.5f));
            }
            else
            {
                var metrics = RunLiveFftLocked(dtSec, partyEnergy);
                _cachedMetrics = metrics;
                _lastCacheTicks = nowTicks;
                _lastCachedEnergy = partyEnergy;
                _hasCachedMetrics = true;
                return metrics;
            }
        }

        return ComputeSyntheticPulse(timeSeconds, isPlaying, partyEnergy);
    }

    public static AudioFrameMetrics ComputeSyntheticPulse(double timeSec, bool isPlaying, float partyEnergy)
    {
        if (!isPlaying)
        {
            return AudioFrameMetrics.Silent;
        }

        var energy = float.IsFinite(partyEnergy)
            ? Math.Clamp(partyEnergy, AppSettings.MinPartyEnergyLevel, AppSettings.MaxPartyEnergyLevel)
            : 1.0f;

        var t = double.IsFinite(timeSec) ? timeSec : 0.0;
        var beatPhase = (t * 2.05) % 1.0; // ~123 BPM crisp groove
        var kick = MathF.Pow(MathF.Max(0f, 1f - (float)(beatPhase * 2.4)), 2.2f);
        var subWave = 0.24f + (0.28f * (0.5f + (0.5f * MathF.Sin((float)(t * 3.1)))));

        var bass = Math.Clamp((subWave + (kick * 0.52f)) * (0.75f + (0.25f * energy)), 0.08f, 0.99f);
        var mid = Math.Clamp((0.22f + (0.22f * MathF.Sin((float)(t * 4.7 + 0.8))) + (kick * 0.18f)) * energy * 0.8f, 0.08f, 0.90f);
        var treble = Math.Clamp((0.16f + (0.18f * MathF.Sin((float)(t * 8.9 + 2.1)))) * energy * 0.78f, 0.05f, 0.85f);

        var bands = ImmutableArray.CreateBuilder<float>(BandCount);
        for (var i = 0; i < BandCount; i++)
        {
            var u = i / (float)(BandCount - 1);
            var env = MathF.Exp(-MathF.Pow((u - 0.12f) * 3.2f, 2f)) * bass
                    + MathF.Exp(-MathF.Pow((u - 0.48f) * 3.6f, 2f)) * mid
                    + MathF.Exp(-MathF.Pow((u - 0.82f) * 4.0f, 2f)) * treble;
            var ripple = 0.10f * (0.5f + (0.5f * MathF.Sin((float)(t * (3.5 + (i * 0.35)) + (i * 0.6)))));
            bands.Add(Math.Clamp(env * 0.72f + ripple * energy, 0.03f, 0.96f));
        }

        var overall = (bass * 0.45f) + (mid * 0.35f) + (treble * 0.20f);
        return new AudioFrameMetrics(
            BassEnergy: bass,
            MidEnergy: mid,
            TrebleEnergy: treble,
            OverallLevel: Math.Clamp(overall, 0f, 1f),
            BeatPulse: Math.Clamp(kick * (0.75f + (0.25f * energy)), 0f, 1f),
            IsLiveAudioActive: false,
            SpectrumBands: bands.MoveToImmutable());
    }

    private AudioFrameMetrics RunLiveFftLocked(float dtSec, float partyEnergy)
    {
        // Copy ring chronologically with Hann window applied
        for (var i = 0; i < FftSize; i++)
        {
            var idx = (_ringWriteIdx + i) & (FftSize - 1);
            _real[i] = _pcmRing[idx] * HannWindow[i];
            _imag[i] = 0f;
        }

        // Bit-reversed permutation
        for (var i = 0; i < FftSize; i++)
        {
            var j = BitReversed[i];
            if (j > i)
            {
                (_real[i], _real[j]) = (_real[j], _real[i]);
            }
        }

        // Iterative Radix-2 Cooley-Tukey FFT
        for (var len = 2; len <= FftSize; len <<= 1)
        {
            var halfLen = len >> 1;
            var angleStep = -2f * MathF.PI / len;
            for (var start = 0; start < FftSize; start += len)
            {
                for (var k = 0; k < halfLen; k++)
                {
                    var angle = k * angleStep;
                    var wR = MathF.Cos(angle);
                    var wI = MathF.Sin(angle);

                    var even = start + k;
                    var odd = even + halfLen;
                    var tR = (wR * _real[odd]) - (wI * _imag[odd]);
                    var tI = (wR * _imag[odd]) + (wI * _real[odd]);

                    _real[odd] = _real[even] - tR;
                    _imag[odd] = _imag[even] - tI;
                    _real[even] += tR;
                    _imag[even] += tI;
                }
            }
        }

        var halfBins = FftSize / 2;
        var hzPerBin = _sampleRateHz / (float)FftSize;
        var normFactor = 2f / FftSize;

        // Compute log-spaced bands (24 Hz -> 14 kHz)
        const float minFreq = 24f;
        var maxFreq = MathF.Min(14_000f, _sampleRateHz * 0.46f);
        var ratio = MathF.Pow(maxFreq / minFreq, 1f / BandCount);

        var framePeak = 0.001f;
        float bassRaw = 0f, midRaw = 0f, trebleRaw = 0f;
        int bassBins = 0, midBins = 0, trebleBins = 0;
        for (var b = 0; b < halfBins; b++)
        {
            var mag = MathF.Sqrt((_real[b] * _real[b]) + (_imag[b] * _imag[b])) * normFactor;
            _prevSpectrumMag[b] = mag;
            if (b >= 1 && mag > framePeak)
            {
                framePeak = mag;
            }

            var freq = b * hzPerBin;
            if (freq is >= 20f and <= 170f)
            {
                bassRaw += mag;
                bassBins++;
            }
            else if (freq is > 170f and <= 2000f)
            {
                midRaw += mag;
                midBins++;
            }
            else if (freq is > 2000f and <= 12_000f)
            {
                trebleRaw += mag;
                trebleBins++;
            }
        }

        // Smooth AGC peak so soft vocals and energetic bass normalize cleanly into 0..1
        var decayRate = MathF.Exp(-dtSec * 0.85f);
        _rollingPeak = MathF.Max(0.025f, Math.Max(framePeak, _rollingPeak * decayRate));
        var scale = 1f / _rollingPeak;
        var energyGain = Math.Clamp(partyEnergy, 0.5f, 1.5f);

        var attack = Math.Clamp(dtSec * 22f, 0.25f, 0.85f);
        var release = Math.Clamp(dtSec * 8.5f, 0.08f, 0.40f);

        var bandBuilder = ImmutableArray.CreateBuilder<float>(BandCount);
        for (var i = 0; i < BandCount; i++)
        {
            var fLow = minFreq * MathF.Pow(ratio, i);
            var fHigh = minFreq * MathF.Pow(ratio, i + 1);
            var binStart = Math.Clamp((int)MathF.Floor(fLow / hzPerBin), 1, halfBins - 1);
            var binEnd = Math.Clamp((int)MathF.Ceiling(fHigh / hzPerBin), binStart, halfBins - 1);

            var peakMag = 0f;
            for (var bin = binStart; bin <= binEnd; bin++)
            {
                if (_prevSpectrumMag[bin] > peakMag)
                {
                    peakMag = _prevSpectrumMag[bin];
                }
            }

            // Gentle perceptual tilt (+35% high-freq air lift so snares/hi-hats sparkle too)
            var eqBoost = 1f + (0.35f * (i / (float)(BandCount - 1)));
            var target = Math.Clamp(MathF.Pow(peakMag * scale * eqBoost, 0.68f) * (0.85f + (0.15f * energyGain)), 0f, 1f);
            var coeff = target > _smoothedBands[i] ? attack : release;
            _smoothedBands[i] += (target - _smoothedBands[i]) * coeff;
            bandBuilder.Add(_smoothedBands[i]);
        }

        var targetBass = bassBins > 0 ? Math.Clamp(MathF.Pow((bassRaw / bassBins) * scale * 2.2f, 0.72f), 0f, 1f) : 0f;
        var targetMid = midBins > 0 ? Math.Clamp(MathF.Pow((midRaw / midBins) * scale * 2.6f, 0.75f), 0f, 1f) : 0f;
        var targetTreble = trebleBins > 0 ? Math.Clamp(MathF.Pow((trebleRaw / trebleBins) * scale * 3.1f, 0.75f), 0f, 1f) : 0f;

        _smoothedBass += (targetBass - _smoothedBass) * (targetBass > _smoothedBass ? attack : release);
        _smoothedMid += (targetMid - _smoothedMid) * (targetMid > _smoothedMid ? attack : release);
        _smoothedTreble += (targetTreble - _smoothedTreble) * (targetTreble > _smoothedTreble ? attack : release);

        // Spectral-flux bass transient BeatPulse detector
        var bassDelta = targetBass - _prevBassFluxRef;
        _prevBassFluxRef = (_prevBassFluxRef * 0.78f) + (targetBass * 0.22f);
        if (bassDelta > 0.085f)
        {
            _beatPulse = Math.Clamp(_beatPulse + (bassDelta * 2.2f * energyGain), 0f, 1f);
        }
        else
        {
            _beatPulse = MathF.Max(0f, _beatPulse - (dtSec * 3.4f));
        }

        var overall = Math.Clamp((_smoothedBass * 0.45f) + (_smoothedMid * 0.35f) + (_smoothedTreble * 0.20f), 0f, 1f);
        return new AudioFrameMetrics(
            BassEnergy: _smoothedBass,
            MidEnergy: _smoothedMid,
            TrebleEnergy: _smoothedTreble,
            OverallLevel: overall,
            BeatPulse: Math.Clamp(_beatPulse, 0f, 1f),
            IsLiveAudioActive: true,
            SpectrumBands: bandBuilder.MoveToImmutable());
    }

    private static float[] BuildHannWindow(int n)
    {
        var window = new float[n];
        for (var i = 0; i < n; i++)
        {
            window[i] = 0.5f * (1f - MathF.Cos((2f * MathF.PI * i) / (n - 1)));
        }

        return window;
    }

    private static int[] BuildBitReversedTable(int n)
    {
        var bits = (int)Math.Log2(n);
        var table = new int[n];
        for (var i = 0; i < n; i++)
        {
            var rev = 0;
            var v = i;
            for (var b = 0; b < bits; b++)
            {
                rev = (rev << 1) | (v & 1);
                v >>= 1;
            }

            table[i] = rev;
        }

        return table;
    }
}
