using LyricPulse.Core.Audio;

namespace LyricPulse.Core.Abstractions;

/// <summary>
/// Provides real-time smoothed FFT spectrum bands and beat/bass energy for visualizers and Edge Glow,
/// with graceful fallback to synthetic tempo pulses when idle or headless.
/// </summary>
public interface IAudioSpectrumProvider : IDisposable
{
    void StartCapture();

    void StopCapture();

    AudioFrameMetrics GetCurrentMetrics(double timeSeconds, bool isPlaying, float partyEnergy);
}
