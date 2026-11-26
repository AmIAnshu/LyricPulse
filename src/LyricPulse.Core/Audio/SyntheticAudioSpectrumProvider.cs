using LyricPulse.Core.Abstractions;

namespace LyricPulse.Core.Audio;

/// <summary>
/// Headless / deterministic synthetic audio spectrum provider used in smoke tests and non-WASAPI environments.
/// </summary>
public sealed partial class SyntheticAudioSpectrumProvider : IAudioSpectrumProvider
{
    private float _dropSensitivity = 1.0f;

    public void StartCapture()
    {
    }

    public void StopCapture()
    {
    }

    public void ConfigureBeatDropSensitivity(float sensitivity) =>
        _dropSensitivity = Math.Clamp(float.IsFinite(sensitivity) ? sensitivity : 1.0f, 0.50f, 1.60f);

    public AudioFrameMetrics GetCurrentMetrics(double timeSeconds, bool isPlaying, float partyEnergy) =>
        AudioSpectrumAnalyzer.ComputeSyntheticPulse(timeSeconds, isPlaying, partyEnergy, _dropSensitivity);

    public void Dispose()
    {
    }
}
