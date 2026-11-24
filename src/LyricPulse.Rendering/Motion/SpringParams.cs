namespace LyricPulse.Rendering.Motion;

/// <summary>
/// Immutable parameters for a damped harmonic spring oscillator.
/// </summary>
public readonly record struct SpringParams(float Stiffness, float Damping, float Mass = 1.0f)
{
    public const float DefaultReferenceWordDurationSec = 0.32f;
    public const float MinTempoPacing = 0.42f;
    public const float MaxTempoPacing = 1.35f;

    public float SafeMass => Math.Max(Mass, 0.01f);

    public float SafeStiffness => Math.Max(Stiffness, 0.01f);

    public float SafeDamping => Math.Max(Damping, 0.0f);

    /// <summary>Natural angular frequency omega_0 = sqrt(k / m).</summary>
    public float NaturalFrequency => MathF.Sqrt(SafeStiffness / SafeMass);

    /// <summary>Damping ratio zeta = c / (2 * sqrt(k * m)).</summary>
    public float DampingRatio => SafeDamping / (2.0f * MathF.Sqrt(SafeStiffness * SafeMass));

    /// <summary>
    /// Applies MOTION_AND_STYLES.md section 4.4 tempo-adaptive pacing compression
    /// so fast rap words (&lt; 250ms) tighten smoothly without overshoot jitter and slow ballads relax gracefully.
    /// </summary>
    public SpringParams WithTempoCompression(float wordDurationSeconds)
    {
        var safeDuration = float.IsFinite(wordDurationSeconds) && wordDurationSeconds > 0f
            ? wordDurationSeconds
            : DefaultReferenceWordDurationSec;

        var pacing = Math.Clamp(safeDuration / DefaultReferenceWordDurationSec, MinTempoPacing, MaxTempoPacing);
        var effectiveStiffness = SafeStiffness / (pacing * pacing);
        var effectiveDamping = (SafeDamping / pacing) * (pacing < 0.65f ? 1.25f : 1.0f);

        return new SpringParams(effectiveStiffness, effectiveDamping, SafeMass);
    }

    /// <summary>
    /// Clamps damping ratio to at least critically damped (zeta &gt;= 1.0) for Reduced Motion mode.
    /// </summary>
    public SpringParams WithReducedMotion()
    {
        var criticalDamping = 2.0f * MathF.Sqrt(SafeStiffness * SafeMass);
        return this with { Damping = MathF.Max(SafeDamping, criticalDamping) };
    }
}
