using System.Collections.Immutable;

namespace LyricPulse.Core.Audio;

/// <summary>
/// Immutable per-frame FFT spectrum and energy snapshot pushed to renderers and UI telemetry.
/// </summary>
public sealed record AudioFrameMetrics(
    float BassEnergy,
    float MidEnergy,
    float TrebleEnergy,
    float OverallLevel,
    float BeatPulse,
    bool IsLiveAudioActive,
    ImmutableArray<float> SpectrumBands,
    float DropTrigger = 0f,
    float ClimaxIntensity = 0f,
    bool IsClimaxActive = false)
{
    public const int BandCount = 32;

    public static AudioFrameMetrics Silent { get; } = new(
        BassEnergy: 0f,
        MidEnergy: 0f,
        TrebleEnergy: 0f,
        OverallLevel: 0f,
        BeatPulse: 0f,
        IsLiveAudioActive: false,
        SpectrumBands: ImmutableArray.Create(new float[BandCount]),
        DropTrigger: 0f,
        ClimaxIntensity: 0f,
        IsClimaxActive: false);

    public ImmutableArray<float> SafeBands =>
        SpectrumBands.IsDefault || SpectrumBands.Length != BandCount
            ? Silent.SpectrumBands
            : SpectrumBands;
}
