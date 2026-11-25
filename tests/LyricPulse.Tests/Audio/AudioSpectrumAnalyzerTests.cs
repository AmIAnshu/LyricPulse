using System.Text.Json;
using AwesomeAssertions;
using LyricPulse.Core.Audio;
using LyricPulse.Core.Models;
using LyricPulse.Core.Serialization;
using Xunit;

namespace LyricPulse.Tests.Audio;

public sealed class AudioSpectrumAnalyzerTests
{
    [Fact]
    public void PushInterleavedFloatSamples_SeparatesBassSineFromTrebleSine()
    {
        var analyzer = new AudioSpectrumAnalyzer(sampleRateHz: 48_000);
        const int frames = 1024;
        var bassBuf = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var s = MathF.Sin((2f * MathF.PI * 65f * i) / 48_000f) * 0.8f;
            bassBuf[i * 2] = s;
            bassBuf[(i * 2) + 1] = s;
        }

        analyzer.PushInterleavedFloatSamples(bassBuf, channels: 2);
        var bassMetrics = analyzer.ComputeMetrics(0.1, isPlaying: true, partyEnergy: 1.0f);

        bassMetrics.IsLiveAudioActive.Should().BeTrue();
        bassMetrics.SafeBands.Should().HaveCount(AudioFrameMetrics.BandCount);
        bassMetrics.BassEnergy.Should().BeGreaterThan(bassMetrics.TrebleEnergy);
        bassMetrics.OverallLevel.Should().BeGreaterThan(0.05f);

        var trebleAnalyzer = new AudioSpectrumAnalyzer(sampleRateHz: 48_000);
        var trebleBuf = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var s = MathF.Sin((2f * MathF.PI * 5200f * i) / 48_000f) * 0.8f;
            trebleBuf[i * 2] = s;
            trebleBuf[(i * 2) + 1] = s;
        }

        trebleAnalyzer.PushInterleavedFloatSamples(trebleBuf, channels: 2);
        var trebleMetrics = trebleAnalyzer.ComputeMetrics(0.1, isPlaying: true, partyEnergy: 1.0f);

        trebleMetrics.IsLiveAudioActive.Should().BeTrue();
        trebleMetrics.TrebleEnergy.Should().BeGreaterThan(trebleMetrics.BassEnergy);
    }

    [Fact]
    public void PushInterleavedInt16Samples_AndSyntheticFallback_WorkDeterministically()
    {
        var analyzer = new AudioSpectrumAnalyzer(sampleRateHz: 44_100);
        var pcm16 = new short[1024 * 2];
        for (var i = 0; i < 1024; i++)
        {
            var s = (short)(Math.Sin((2.0 * Math.PI * 100.0 * i) / 44_100.0) * 18000);
            pcm16[i * 2] = s;
            pcm16[(i * 2) + 1] = s;
        }

        analyzer.PushInterleavedInt16Samples(pcm16, channels: 2);
        var live = analyzer.ComputeMetrics(0.5, isPlaying: true, partyEnergy: 1.2f);
        live.IsLiveAudioActive.Should().BeTrue();
        live.OverallLevel.Should().BeGreaterThan(0.05f);

        var playingSyn = AudioSpectrumAnalyzer.ComputeSyntheticPulse(1.25, isPlaying: true, partyEnergy: 1.0f);
        playingSyn.IsLiveAudioActive.Should().BeFalse();
        playingSyn.SafeBands.Should().HaveCount(AudioFrameMetrics.BandCount);
        playingSyn.OverallLevel.Should().BeGreaterThan(0.05f);

        var pausedSyn = AudioSpectrumAnalyzer.ComputeSyntheticPulse(1.25, isPlaying: false, partyEnergy: 1.0f);
        pausedSyn.OverallLevel.Should().Be(0f);
    }

    [Fact]
    public void AppSettings_EdgeGlowAndVisualizerDefaults_NormalizeAndRoundTripAotJson()
    {
        var raw = AppSettings.Default with
        {
            EdgeGlowMode = EdgeGlowMode.NeonRim,
            EdgeGlowIntensity = 9.0f,
            AudioVisualizerMode = AudioVisualizerMode.FluidWave,
            AudioVisualizerIntensity = -1.0f
        };

        var normalized = raw.ValidateAndNormalize();
        normalized.EdgeGlowMode.Should().Be(EdgeGlowMode.NeonRim);
        normalized.EdgeGlowIntensity.Should().Be(AppSettings.MaxGlowIntensity);
        normalized.AudioVisualizerMode.Should().Be(AudioVisualizerMode.FluidWave);
        normalized.AudioVisualizerIntensity.Should().Be(AppSettings.MinVisualizerIntensity);

        var json = JsonSerializer.Serialize(normalized, LyricPulseJsonContext.Default.AppSettings);
        var roundTripped = JsonSerializer.Deserialize(json, LyricPulseJsonContext.Default.AppSettings);
        roundTripped.Should().NotBeNull();
        roundTripped!.EdgeGlowMode.Should().Be(EdgeGlowMode.NeonRim);
        roundTripped.AudioVisualizerMode.Should().Be(AudioVisualizerMode.FluidWave);
    }
}
