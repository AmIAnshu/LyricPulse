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

    // Multi-timescale Beat Drop / Chorus Climax detector state
    private float _dropSensitivity = AppSettings.DefaultBeatDropSensitivity;
    private float _fastSubBass;
    private float _medEnergy;
    private float _slowBaseline = 0.22f;
    private float _buildupTension;
    private float _dropTrigger;
    private float _climaxIntensity;
    private float _dropCooldownSec;

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

    public float DropSensitivity
    {
        get
        {
            lock (_sync)
            {
                return _dropSensitivity;
            }
        }
        set
        {
            lock (_sync)
            {
                _dropSensitivity = float.IsFinite(value)
                    ? Math.Clamp(value, AppSettings.MinBeatDropSensitivity, AppSettings.MaxBeatDropSensitivity)
                    : AppSettings.DefaultBeatDropSensitivity;
                _hasCachedMetrics = false;
            }
        }
    }

    public void ResetBeatDropState()
    {
        lock (_sync)
        {
            _fastSubBass = 0f;
            _medEnergy = 0f;
            _slowBaseline = 0.22f;
            _buildupTension = 0f;
            _dropTrigger = 0f;
            _climaxIntensity = 0f;
            _dropCooldownSec = 0f;
            _hasCachedMetrics = false;
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
        float sens;

        lock (_sync)
        {
            sens = _dropSensitivity;
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
                _fastSubBass *= MathF.Max(0f, 1f - (dtSec * 6f));
                _medEnergy *= MathF.Max(0f, 1f - (dtSec * 1.5f));
                _buildupTension = MathF.Max(0f, _buildupTension - (dtSec * 0.45f));
                _dropTrigger = MathF.Max(0f, _dropTrigger - (dtSec / 0.48f));
                _climaxIntensity = MathF.Max(0f, _climaxIntensity - (dtSec / 3.60f));
                _dropCooldownSec = MathF.Max(0f, _dropCooldownSec - dtSec);
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

        return ComputeSyntheticPulse(timeSeconds, isPlaying, partyEnergy, sens);
    }

    public static AudioFrameMetrics ComputeSyntheticPulse(double timeSec, bool isPlaying, float partyEnergy) =>
        ComputeSyntheticPulse(timeSec, isPlaying, partyEnergy, AppSettings.DefaultBeatDropSensitivity);

    public static AudioFrameMetrics ComputeSyntheticPulse(
        double timeSec,
        bool isPlaying,
        float partyEnergy,
        float dropSensitivity)
    {
        if (!isPlaying)
        {
            return AudioFrameMetrics.Silent;
        }

        var energy = float.IsFinite(partyEnergy)
            ? Math.Clamp(partyEnergy, AppSettings.MinPartyEnergyLevel, AppSettings.MaxPartyEnergyLevel)
            : 1.0f;
        var sens = float.IsFinite(dropSensitivity)
            ? Math.Clamp(dropSensitivity, AppSettings.MinBeatDropSensitivity, AppSettings.MaxBeatDropSensitivity)
            : AppSettings.DefaultBeatDropSensitivity;

        var t = double.IsFinite(timeSec) ? timeSec : 0.0;
        var beatPhase = (t * 2.05) % 1.0; // ~123 BPM crisp groove
        var kick = MathF.Pow(MathF.Max(0f, 1f - (float)(beatPhase * 2.4)), 2.2f);
        var subWave = 0.24f + (0.28f * (0.5f + (0.5f * MathF.Sin((float)(t * 3.1)))));

        // Choreographed demo climax surges synced cleanly to phrase peaks
        var periodSec = sens >= 1.25f ? 8.0 : sens < 0.75f ? 18.0 : 12.0;
        var dropTrigger = 0f;
        var climaxIntensity = 0f;
        if (t >= 1.5)
        {
            var dropPhase = (t + (periodSec * 0.5)) % periodSec;
            if (dropPhase < 0.48)
            {
                dropTrigger = 1f - (float)(dropPhase / 0.48);
            }

            if (dropPhase < 3.40)
            {
                climaxIntensity = MathF.Pow(1f - (float)(dropPhase / 3.40), 1.35f);
            }
        }

        var climaxLift = climaxIntensity * 0.22f;
        var bass = Math.Clamp(((subWave + (kick * 0.52f)) * (0.75f + (0.25f * energy))) + climaxLift, 0.08f, 0.99f);
        var mid = Math.Clamp((0.22f + (0.22f * MathF.Sin((float)(t * 4.7 + 0.8))) + (kick * 0.18f)) * energy * 0.8f + (climaxLift * 0.6f), 0.08f, 0.90f);
        var treble = Math.Clamp((0.16f + (0.18f * MathF.Sin((float)(t * 8.9 + 2.1)))) * energy * 0.78f + (climaxLift * 0.5f), 0.05f, 0.85f);

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
            BeatPulse: Math.Clamp((kick * (0.75f + (0.25f * energy))) + (dropTrigger * 0.35f), 0f, 1f),
            IsLiveAudioActive: false,
            SpectrumBands: bands.MoveToImmutable(),
            DropTrigger: Math.Clamp(dropTrigger, 0f, 1f),
            ClimaxIntensity: Math.Clamp(climaxIntensity, 0f, 1f),
            IsClimaxActive: climaxIntensity >= 0.15f);
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
        var subBassRaw = 0f;
        var subBassBins = 0;
        var bassRaw = 0f;
        var midRaw = 0f;
        var trebleRaw = 0f;
        var bassBins = 0;
        var midBins = 0;
        var trebleBins = 0;

        for (var b = 0; b < halfBins; b++)
        {
            var mag = MathF.Sqrt((_real[b] * _real[b]) + (_imag[b] * _imag[b])) * normFactor;
            _prevSpectrumMag[b] = mag;
            if (b >= 1 && mag > framePeak)
            {
                framePeak = mag;
            }

            var freq = b * hzPerBin;
            if (freq is >= 20f and <= 115f)
            {
                subBassRaw += mag;
                subBassBins++;
            }

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

        // Smooth AGC peak so soft vocals and EDM drops both normalize cleanly into 0..1
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

        var targetSubBass = subBassBins > 0 ? Math.Clamp(MathF.Pow((subBassRaw / subBassBins) * scale * 2.1f, 0.72f), 0f, 1f) : 0f;
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

        // Multi-timescale Beat Drop / Song Climax detector (buildup scoop + sub-bass/energy-wall surge)
        var instantOverall = Math.Clamp((targetBass * 0.48f) + (targetMid * 0.32f) + (targetTreble * 0.20f), 0f, 1f);
        var subBassDelta = targetSubBass - _fastSubBass;
        _fastSubBass += subBassDelta * Math.Clamp(dtSec * 16f, 0.18f, 0.78f);
        _medEnergy += (instantOverall - _medEnergy) * Math.Clamp(dtSec * 1.15f, 0.01f, 0.22f);
        _slowBaseline = Math.Clamp(
            _slowBaseline + ((instantOverall - _slowBaseline) * Math.Clamp(dtSec * 0.14f, 0.002f, 0.04f)),
            0.12f,
            0.72f);

        var isPreDropScoop = targetSubBass < (_medEnergy * 0.82f) && targetSubBass < 0.38f;
        var isRiserBuild = targetTreble > (_slowBaseline * 1.05f) && targetTreble >= targetSubBass;
        if (isPreDropScoop || isRiserBuild)
        {
            _buildupTension = Math.Min(1f, _buildupTension + (dtSec * 0.45f));
        }
        else
        {
            _buildupTension = Math.Max(0f, _buildupTension - (dtSec * 0.22f));
        }

        _dropCooldownSec = MathF.Max(0f, _dropCooldownSec - dtSec);
        _dropTrigger = MathF.Max(0f, _dropTrigger - (dtSec / 0.48f));
        _climaxIntensity = MathF.Max(0f, _climaxIntensity - (dtSec / 3.60f));

        var sens = _dropSensitivity;
        var surgeThreshold = 0.15f / MathF.Sqrt(sens);
        var wallRatioThreshold = 1.42f - ((sens - 1.0f) * 0.24f);
        var minImpactFloor = Math.Clamp(0.45f / MathF.Pow(sens, 0.35f), 0.28f, 0.62f);

        var buildupDrop = _buildupTension >= 0.18f &&
                          subBassDelta >= surgeThreshold &&
                          targetSubBass >= minImpactFloor;
        var energyWallDrop = subBassDelta >= (surgeThreshold * 1.12f) &&
                             targetSubBass >= Math.Max(minImpactFloor, _slowBaseline * wallRatioThreshold) &&
                             instantOverall >= (_slowBaseline * 1.24f);

        if (_dropCooldownSec <= 0f && (buildupDrop || energyWallDrop))
        {
            _dropTrigger = 1.0f;
            _climaxIntensity = 1.0f;
            _buildupTension = 0f;
            _dropCooldownSec = Math.Clamp(11.5f - (sens * 3.5f), 5.5f, 13.0f);
        }

        var overall = Math.Clamp((_smoothedBass * 0.45f) + (_smoothedMid * 0.35f) + (_smoothedTreble * 0.20f), 0f, 1f);
        return new AudioFrameMetrics(
            BassEnergy: _smoothedBass,
            MidEnergy: _smoothedMid,
            TrebleEnergy: _smoothedTreble,
            OverallLevel: overall,
            BeatPulse: Math.Clamp(_beatPulse + (_dropTrigger * 0.25f), 0f, 1f),
            IsLiveAudioActive: true,
            SpectrumBands: bandBuilder.MoveToImmutable(),
            DropTrigger: _dropTrigger,
            ClimaxIntensity: _climaxIntensity,
            IsClimaxActive: _climaxIntensity >= 0.15f);
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
