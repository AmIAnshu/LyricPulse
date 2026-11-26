using LyricPulse.Core.Abstractions;

namespace LyricPulse.Core.Audio;

/// <summary>
/// Headless / deterministic synthetic audio spectrum provider used in smoke tests and non-WASAPI environments.
/// </summary>
public sealed partial class SyntheticAudioSpectrumProvider : IAudioSpectrumProvider
{
    public void StartCapture()
    {
    }

    public void StopCapture()
    {
    }

    public AudioFrameMetrics GetCurrentMetrics(double timeSeconds, bool isPlaying, float partyEnergy) =>
        AudioSpectrumAnalyzer.ComputeSyntheticPulse(timeSeconds, isPlaying, partyEnergy);

    public void Dispose()
    {
    }
}
